using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaunchFast.Core.Env;
using LaunchFast.Core.History;
using LaunchFast.Core.Models;
using LaunchFast.Core.Parsing;
using LaunchFast.Core.Running;
using LaunchFast.Core.Stores;

namespace LaunchFast.App.ViewModels;

/// <summary>
/// Detail screen for a single project: lists lanes per platform, computes the
/// dynamic required-env set and which secrets are missing, and runs a lane with
/// live output streamed into <see cref="Run"/>. One run at a time.
/// </summary>
public partial class ProjectDetailViewModel : ObservableObject
{
    readonly Project _project;
    readonly ISecretStore _secrets;
    readonly IPtyFactory _ptyFactory;
    readonly EnvResolver _resolver;
    readonly StoreStatusProvider _storeStatus;
    readonly StoreIdentifiers _identifiers;
    readonly RunHistoryStore _history;
    readonly Func<string?>? _resolveToolPath;

    IReadOnlyList<string> _required = [];
    IReadOnlyDictionary<string, string> _fromFiles = new Dictionary<string, string>();

    DispatcherTimer? _elapsedTimer;
    DateTime _runStarted;

    // Lane + start instant captured when a run launches, used to build the audit
    // record on completion (independent of the UI-only elapsed timer).
    LaneViewModel? _activeLane;
    DateTime _activeRunStartedUtc;

    public ProjectDetailViewModel(
        Project project,
        ISecretStore secrets,
        IPtyFactory ptyFactory,
        StoreStatusProvider? storeStatus = null,
        StoreIdentifiers? identifiers = null,
        RunHistoryStore? history = null,
        Func<string?>? resolveToolPath = null)
    {
        _project = project;
        _secrets = secrets;
        _ptyFactory = ptyFactory;
        _resolver = new EnvResolver(secrets);
        _storeStatus = storeStatus ?? new StoreStatusProvider(null, null);
        _identifiers = identifiers ?? new StoreIdentifiers(null, null);
        _history = history ?? new RunHistoryStore(NoOpHistoryDir());
        _resolveToolPath = resolveToolPath;
    }

    /// <summary>
    /// A throwaway temp directory for the history store when none is injected, so
    /// the default ctor never writes into the real user history. Real composition
    /// (the shell) always passes the shared store.
    /// </summary>
    static string NoOpHistoryDir() =>
        Path.Combine(Path.GetTempPath(), "lf-history-noop", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Test convenience: wires an empty in-memory secret store and a no-op PTY
    /// factory. Use the real ctor in production composition.
    /// </summary>
    public static ProjectDetailViewModel ForTest(Project project) =>
        new(project, new EmptySecretStore(), new NullPtyFactory());

    /// <summary>Set by the shell; invoked by the Back command to return to the launcher.</summary>
    public Action? GoBack { get; set; }

    /// <summary>The secret store, exposed so the secrets dialog can write into it.</summary>
    public ISecretStore Secrets => _secrets;

    public Project Project => _project;
    public string Name => _project.Name;
    public string? Version => _project.Version;

    /// <summary>Stable id used for both Keychain lookups and env resolution.</summary>
    public string ProjectId => _project.Path;

    public ObservableCollection<LaneViewModel> IosLanes { get; } = new();
    public ObservableCollection<LaneViewModel> AndroidLanes { get; } = new();

    /// <summary>Required env vars not satisfied by files or the secret store.</summary>
    public ObservableCollection<string> MissingSecrets { get; } = new();

    public RunViewModel Run { get; } = new();

    /// <summary>
    /// Mono label shown in the terminal bar (e.g. "fastlane ios beta"). Reflects the
    /// most recently launched lane; falls back to a generic "fastlane" before any run.
    /// </summary>
    [ObservableProperty]
    private string _runningLaneLabel = "fastlane";

    /// <summary>True while a lane is running; buttons bind IsEnabled to its inverse.</summary>
    public bool IsRunning => Run.IsRunning;

    public bool HasMissingSecrets => MissingSecrets.Count > 0;

    // MVP: both platforms are gated on the SAME (union) missing-secret set for
    // simplicity. Per-platform gating could split the required set later.
    public bool CanRun => !HasMissingSecrets && !IsRunning;
    public bool CanRunIos => IosLanes.Count > 0 && !HasMissingSecrets && !IsRunning;
    public bool CanRunAndroid => AndroidLanes.Count > 0 && !HasMissingSecrets && !IsRunning;

    public void Load()
    {
        IosLanes.Clear();
        AndroidLanes.Clear();
        MissingSecrets.Clear();

        LoadLanes(_project.IosFastlaneDir, Platform.Ios, IosLanes);
        LoadLanes(_project.AndroidFastlaneDir, Platform.Android, AndroidLanes);

        // Required secrets + file-sourced values are computed by the shared Core
        // scanner: only genuine secrets gate a run; non-secret control/config vars
        // (CI, FASTLANE_ENV, locales, ...) are referenced by the Fastfile but
        // can't be supplied via the secrets dialog, so they must not be required.
        var scan = ProjectSecretScanner.Scan(_project);
        _required = scan.RequiredSecrets;
        _fromFiles = scan.FromFiles;

        var status = _resolver.Resolve(ProjectId, _required, _fromFiles);
        foreach (var key in status.Missing) MissingSecrets.Add(key);

        OnPropertyChanged(nameof(HasMissingSecrets));
        OnPropertyChanged(nameof(CanRun));
        OnPropertyChanged(nameof(CanRunIos));
        OnPropertyChanged(nameof(CanRunAndroid));

        KickOffStoreStatus();
    }

    /// <summary>
    /// Fire-and-forget store-status fetch after a load, so the UI never blocks on
    /// the network. In headless tests (no UI dispatcher loop) we skip the implicit
    /// kick-off; the test awaits <see cref="RefreshStoreStatusAsync"/> directly.
    /// </summary>
    void KickOffStoreStatus()
    {
        if (!Dispatcher.UIThread.CheckAccess()) return;
        _ = RefreshStoreStatusAsync();
    }

    /// <summary>
    /// Resolves the current store status for every lane and marshals the updates
    /// onto the UI thread. Awaitable so tests can drive it deterministically.
    /// Never throws — the provider degrades each lane to "unavailable" on failure.
    /// </summary>
    public async Task RefreshStoreStatusAsync(CancellationToken ct = default)
    {
        foreach (var lane in IosLanes.Concat(AndroidLanes))
        {
            var identifier = lane.Platform == Platform.Ios
                ? _identifiers.IosBundleId
                : _identifiers.AndroidPackageName;

            if (LaneDestination.For(lane.Lane) == Destination.None)
            {
                continue;
            }

            StoreStatus status;
            if (identifier is null)
            {
                continue;
            }

            try
            {
                status = await _storeStatus.GetAsync(identifier, lane.Lane, ct).ConfigureAwait(false);
            }
            catch
            {
                status = StoreStatus.Unavailable(LaneDestination.For(lane.Lane));
            }

            SetLaneStore(lane, status);
        }
    }

    static void SetLaneStore(LaneViewModel lane, StoreStatus status)
    {
        if (Dispatcher.UIThread.CheckAccess()) lane.Store = status;
        else Dispatcher.UIThread.Post(() => lane.Store = status);
    }

    [RelayCommand]
    async Task RefreshStoreStatus()
    {
        _storeStatus.Refresh();
        await RefreshStoreStatusAsync().ConfigureAwait(false);
    }

    static void LoadLanes(string? fastlaneDir, Platform platform,
        ObservableCollection<LaneViewModel> target)
    {
        if (fastlaneDir is null || !Directory.Exists(fastlaneDir)) return;

        var platformDir = Directory.GetParent(fastlaneDir)!.FullName;

        var fastfile = Path.Combine(fastlaneDir, "Fastfile");
        if (File.Exists(fastfile))
        {
            var text = File.ReadAllText(fastfile);
            foreach (var lane in FastfileParser.Parse(text, platform))
                target.Add(new LaneViewModel(lane, platformDir));
        }
    }

    /// <summary>
    /// Reads the project's <c>.env*</c> files (and <c>scripts/deploy-env.sh</c>)
    /// into a merged dictionary. Exposed so the composition root can resolve store
    /// credentials with the same env the run uses.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ResolveProjectEnv(string projectRoot) =>
        ProjectSecretScanner.ReadEnvFiles(projectRoot);

    [RelayCommand]
    void RunLane(LaneViewModel? lane)
    {
        if (lane is null || IsRunning) return;

        // Preflight: fastlane runs via `bundle exec`, so we need both a Gemfile
        // in the platform dir and bundler on PATH. Surface a friendly message in
        // the output panel and bail out before touching running state.
        // The user's interactive-login-shell PATH (e.g. Homebrew Ruby), so `bundle`
        // resolves exactly as it does in their terminal — not the minimal GUI PATH a
        // Finder-launched app inherits. Null under test → process PATH (unchanged).
        var toolPath = _resolveToolPath?.Invoke();

        var gemfile = Preflight.CheckGemfile(lane.PlatformDir, _project.Path);
        var bundler = Preflight.CheckTool("bundle", toolPath);
        if (!gemfile.Ok || !bundler.Ok)
        {
            var failure = !gemfile.Ok ? gemfile : bundler;
            AppendLine($"⚠ Preflight failed: {failure.Message}");
            return;
        }

        // PATH is a base default so the spawned `bundle` resolves correctly; the
        // project's env (incl. any project-supplied PATH) overlays and wins.
        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(toolPath)) env["PATH"] = toolPath;
        foreach (var (k, v) in _resolver.BuildEnv(ProjectId, _required, _fromFiles))
            env[k] = v;

        RunningLaneLabel = $"fastlane {lane.Platform.ToString().ToLowerInvariant()} {lane.Name}";

        Run.Lines.Clear();
        Run.CurrentAction = null;
        SetRunning(true);

        _activeLane = lane;
        _activeRunStartedUtc = DateTime.UtcNow;

        var handle = new LaneRunner(_ptyFactory).Run(lane.Lane, lane.PlatformDir, env, OnOutput);
        Run.Handle = handle;
        handle.Completed += OnCompleted;

        StartElapsedTimer();
    }

    void StartElapsedTimer()
    {
        // App-only: a ticking timer needs a dispatcher loop, absent in headless tests.
        if (!Dispatcher.UIThread.CheckAccess()) return;

        _runStarted = DateTime.UtcNow;
        Run.Elapsed = "0:00";
        _elapsedTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _elapsedTimer.Tick -= OnElapsedTick;
        _elapsedTimer.Tick += OnElapsedTick;
        _elapsedTimer.Start();
    }

    void OnElapsedTick(object? sender, EventArgs e)
    {
        var span = DateTime.UtcNow - _runStarted;
        Run.Elapsed = $"{(int)span.TotalMinutes}:{span.Seconds:D2}";
    }

    void StopElapsedTimer() => _elapsedTimer?.Stop();

    /// <summary>Appends a single line to the run output, marshalling to the UI thread.</summary>
    void AppendLine(string line)
    {
        if (Dispatcher.UIThread.CheckAccess()) Run.Lines.Add(line);
        else Dispatcher.UIThread.Post(() => Run.Lines.Add(line));
    }

    void OnOutput(string line)
    {
        void Append()
        {
            Run.Lines.Add(line);
            if (!string.IsNullOrWhiteSpace(line)) Run.CurrentAction = line.Trim();
        }

        // In the app, marshal to the UI thread. In headless tests the fake
        // factory raises synchronously with no dispatcher loop, so append
        // directly when we're already on (or lacking) the UI thread.
        if (Dispatcher.UIThread.CheckAccess()) Append();
        else Dispatcher.UIThread.Post(Append);
    }

    void OnCompleted(int code)
    {
        void Finish()
        {
            // Persist the audit record BEFORE clearing run state, while the output
            // and active lane are still available. Guarded so a recording failure
            // never affects the run's completion semantics.
            RecordRun(code);

            SetRunning(false);
            Run.Handle = null;
            StopElapsedTimer();
            _activeLane = null;
        }

        if (Dispatcher.UIThread.CheckAccess()) Finish();
        else Dispatcher.UIThread.Post(Finish);
    }

    /// <summary>
    /// Builds and appends a <see cref="RunRecord"/> for the just-finished run.
    /// Best-effort: any failure here is swallowed so it cannot break a run.
    /// </summary>
    void RecordRun(int code)
    {
        try
        {
            if (_activeLane is not { } lane) return;

            var lines = Run.Lines.ToList();
            var lastMeaningful = lines.LastOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim();
            var tail = string.Join("\n", lines.TakeLast(50));

            var record = new RunRecord
            {
                Platform = lane.Platform,
                LaneName = lane.Name,
                Status = code == 0 ? RunStatus.Succeeded : RunStatus.Failed,
                ExitCode = code,
                StartedUtc = _activeRunStartedUtc,
                Duration = DateTime.UtcNow - _activeRunStartedUtc,
                Trigger = "Local",
                ResultSummary = string.IsNullOrEmpty(lastMeaningful) ? $"exit {code}" : lastMeaningful,
                OutputTail = tail,
            };

            _history.Append(ProjectId, record);
        }
        catch
        {
            // Recording is never allowed to break a run.
        }
    }

    void SetRunning(bool running)
    {
        Run.IsRunning = running;
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(CanRun));
        OnPropertyChanged(nameof(CanRunIos));
        OnPropertyChanged(nameof(CanRunAndroid));
        RunLaneCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Finds the lane named <paramref name="laneName"/> on the given platform and
    /// triggers its run through the normal <see cref="RunLaneCommand"/> — so the
    /// existing preflight, secret-gating and one-run-at-a-time guards all apply.
    /// Returns false (without running) when the project has no such lane, which
    /// lets section screens disable their primary action honestly rather than
    /// faking a run. Invoked by section shells (Signing → sync_certificates,
    /// TestFlight → beta) via <see cref="ProjectShellViewModel.RunLane"/>.
    /// </summary>
    public bool TryRunLane(Platform platform, string laneName)
    {
        var lanes = platform == Platform.Ios ? IosLanes : AndroidLanes;
        var lane = lanes.FirstOrDefault(l => l.Name == laneName);
        if (lane is null) return false;

        RunLaneCommand.Execute(lane);
        return true;
    }

    /// <summary>True when a lane named <paramref name="laneName"/> exists on the platform.</summary>
    public bool HasLane(Platform platform, string laneName) =>
        (platform == Platform.Ios ? IosLanes : AndroidLanes).Any(l => l.Name == laneName);

    [RelayCommand]
    void Stop() => Run.Handle?.Stop();

    [RelayCommand]
    void Back() => GoBack?.Invoke();

    sealed class EmptySecretStore : ISecretStore
    {
        public string? Get(string projectId, string key) => null;
        public void Set(string projectId, string key, string value) { }
    }

    sealed class NullPtyFactory : IPtyFactory
    {
        public IPtyProcess Start(string command, string[] args, string cwd,
            IReadOnlyDictionary<string, string> env) => new NullProcess();

        sealed class NullProcess : IPtyProcess
        {
            public event Action<string>? OutputReceived;
            public event Action<int>? Exited;
            public void Write(string input) { }
            public void Kill() => Exited?.Invoke(0);
            public void Dispose() { _ = OutputReceived; }
        }
    }
}

using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaunchFast.Core.Models;
using LaunchFast.Core.Parsing;
using LaunchFast.Core.Scaffolding;

namespace LaunchFast.App.ViewModels.Wizard;

/// <summary>Whether the wizard installs fastlane fresh or adds to an existing setup.</summary>
public enum WizardMode { Install, AddToExisting }

/// <summary>
/// Orchestrates the fastlane setup wizard: drives the steps, validates each, builds
/// the <see cref="ScaffoldPlan"/> and applies it. In <see cref="WizardMode.Install"/>
/// it renders the full file set; in <see cref="WizardMode.AddToExisting"/> it renders
/// files for a newly-added platform and merges chosen lanes into existing Fastfiles.
/// </summary>
public sealed partial class SetupWizardViewModel : ObservableObject
{
    // iOS lanes that invoke match (directly or via sync_certificates) and therefore
    // require a match git url.
    static readonly string[] MatchLanes = ["sync_certificates", "beta", "release"];

    readonly Func<ScaffoldPlan, Task> _apply;

    SetupWizardViewModel(WizardMode mode, Project project, Func<ScaffoldPlan, Task> apply)
    {
        Mode = mode;
        Project = project;
        _apply = apply;
        Platforms.PropertyChanged += (_, _) => RecomputeNavigation();
    }

    public static SetupWizardViewModel ForInstall(Project project, Func<ScaffoldPlan, Task>? apply = null)
    {
        var vm = new SetupWizardViewModel(WizardMode.Install, project, apply ?? DefaultApply);
        var facts = ProjectFacts.Read(project.Path);
        vm.Ios.BundleId = facts.IosBundleId;
        vm.Android.Package = facts.AndroidPackage;

        // Default the platform selection to whichever platform dirs the project has;
        // if neither is present on disk, offer both.
        var hasIos = Directory.Exists(System.IO.Path.Combine(project.Path, "ios"));
        var hasAndroid = Directory.Exists(System.IO.Path.Combine(project.Path, "android"));
        vm.Platforms.Ios = hasIos || (!hasIos && !hasAndroid);
        vm.Platforms.Android = hasAndroid || (!hasIos && !hasAndroid);

        vm.Lanes.OfferIos(LaneTemplate.Available(Platform.Ios));
        vm.Lanes.OfferAndroid(LaneTemplate.Available(Platform.Android));
        vm.RecomputeNavigation();
        return vm;
    }

    public static SetupWizardViewModel ForAddToExisting(Project project, Func<ScaffoldPlan, Task>? apply = null)
    {
        var vm = new SetupWizardViewModel(WizardMode.AddToExisting, project, apply ?? DefaultApply);

        vm.Platforms.IosAlreadyPresent = project.IosFastlaneDir is not null;
        vm.Platforms.AndroidAlreadyPresent = project.AndroidFastlaneDir is not null;

        var facts = ProjectFacts.Read(project.Path);
        vm.Ios.BundleId = facts.IosBundleId;
        vm.Android.Package = facts.AndroidPackage;

        // Offer only lanes NOT already present in the existing Fastfile.
        vm.Lanes.OfferIos(LanesNotPresent(Platform.Ios, project.IosFastlaneDir));
        vm.Lanes.OfferAndroid(LanesNotPresent(Platform.Android, project.AndroidFastlaneDir));
        vm.RecomputeNavigation();
        return vm;
    }

    static IReadOnlyList<string> LanesNotPresent(Platform platform, string? fastlaneDir)
    {
        var available = LaneTemplate.Available(platform);
        if (fastlaneDir is null) return available;
        var fastfile = System.IO.Path.Combine(fastlaneDir, "Fastfile");
        if (!File.Exists(fastfile)) return available;
        var existing = FastfileParser.Parse(File.ReadAllText(fastfile), platform)
            .Select(l => l.Name).ToHashSet(StringComparer.Ordinal);
        return available.Where(l => !existing.Contains(l)).ToList();
    }

    static Task DefaultApply(ScaffoldPlan plan) => Task.CompletedTask;

    public WizardMode Mode { get; }

    public Project Project { get; }

    public WizardPlatformsStepViewModel Platforms { get; } = new();

    public WizardIosStepViewModel Ios { get; } = new();

    public WizardAndroidStepViewModel Android { get; } = new();

    public WizardLanesStepViewModel Lanes { get; } = new();

    public WizardReviewStepViewModel Review { get; } = new();

    /// <summary>The shell sets this to return to the launcher when the wizard finishes.</summary>
    public Action? Closed { get; set; }

    // ---- step navigation ----------------------------------------------------

    /// <summary>The titles of the active steps, skipping platform steps not selected.</summary>
    public IReadOnlyList<string> StepTitles
    {
        get
        {
            var titles = new List<string> { "Platforms" };
            if (Platforms.Ios) titles.Add("iOS");
            if (Platforms.Android) titles.Add("Android");
            titles.Add("Lanes");
            titles.Add("Review");
            return titles;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentStep))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    private int _stepIndex;

    /// <summary>The view-model backing the current step.</summary>
    public object CurrentStep => StepTitles[StepIndex] switch
    {
        "Platforms" => Platforms,
        "iOS" => Ios,
        "Android" => Android,
        "Lanes" => Lanes,
        _ => Review,
    };

    public bool CanGoNext => StepIndex < StepTitles.Count - 1 && CurrentStepIsValid();

    public bool CanGoBack => StepIndex > 0;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    public void Next()
    {
        if (!CanGoNext) return;

        // Leaving the Lanes step: reflect the chosen iOS lanes in the iOS step's
        // match requirement (so navigating Back to iOS re-validates correctly).
        if (StepTitles[StepIndex] == "Lanes")
            Ios.RequiresMatch = Lanes.ChosenIos.Any(MatchLanes.Contains);

        StepIndex++;
        // Entering the Review step: build the plan from the answers so far.
        if (StepTitles[StepIndex] == "Review")
            Review.Plan = BuildPlan();
        RecomputeNavigation();
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    public void Back()
    {
        if (!CanGoBack) return;
        StepIndex--;
        RecomputeNavigation();
    }

    bool CurrentStepIsValid() => CurrentStep switch
    {
        WizardPlatformsStepViewModel p => p.IsValid,
        WizardIosStepViewModel i => i.IsValid,
        WizardAndroidStepViewModel a => a.IsValid,
        WizardLanesStepViewModel l => l.IsValid,
        _ => true,   // Review step is terminal and always "valid".
    };

    bool _wired;

    void RecomputeNavigation()
    {
        if (!_wired)
        {
            _wired = true;
            Ios.PropertyChanged += (_, _) => RaiseNavigation();
            Android.PropertyChanged += (_, _) => RaiseNavigation();
        }
        RaiseNavigation();
    }

    void RaiseNavigation()
    {
        OnPropertyChanged(nameof(StepTitles));
        OnPropertyChanged(nameof(CurrentStep));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(CanGoBack));
        NextCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();
    }

    // ---- plan building ------------------------------------------------------

    /// <summary>Assembles the wizard's answers from the step view-models.</summary>
    public WizardAnswers BuildAnswers()
    {
        var dartDefines = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(Ios.ApiUrl)) dartDefines["API_URL"] = "API_URL";
        if (!string.IsNullOrWhiteSpace(Ios.ApiToken)) dartDefines["API_TOKEN"] = "API_TOKEN";

        var secrets = new List<SecretInput>();
        AddSecret(secrets, "MATCH_PASSWORD", Ios.MatchPassword);
        AddSecret(secrets, "APPLE_ID", Ios.AppleId);
        AddSecret(secrets, "MATCH_GIT_URL", Ios.MatchGitUrl);
        AddSecret(secrets, "ITC_TEAM_ID", Ios.ItcTeamId);
        AddSecret(secrets, "APP_STORE_CONNECT_API_KEY_PATH", Ios.AppStoreConnectKeyPath);
        AddSecret(secrets, "API_URL", Ios.ApiUrl);
        AddSecret(secrets, "API_TOKEN", Ios.ApiToken);
        AddSecret(secrets, "SUPPLY_JSON_KEY", Android.PlayJsonKeyPath);

        return new WizardAnswers(
            Ios: Platforms.Ios,
            Android: Platforms.Android,
            IosBundleId: Ios.BundleId,
            AppleId: Ios.AppleId,
            TeamId: Ios.TeamId,
            ItcTeamId: Ios.ItcTeamId,
            MatchGitUrl: Ios.MatchGitUrl,
            AndroidPackage: Android.Package,
            PlayJsonKeyPath: Android.PlayJsonKeyPath,
            IosLanes: Lanes.ChosenIos,
            AndroidLanes: Lanes.ChosenAndroid,
            DartDefines: dartDefines,
            Secrets: secrets);
    }

    static void AddSecret(List<SecretInput> secrets, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            secrets.Add(new SecretInput(key, value));
    }

    /// <summary>Builds the <see cref="ScaffoldPlan"/> for the current answers.</summary>
    public ScaffoldPlan BuildPlan()
    {
        var answers = BuildAnswers();
        return Mode == WizardMode.Install
            ? FastlaneScaffolder.Render(answers, Project.Path)
            : BuildAddToExistingPlan(answers);
    }

    ScaffoldPlan BuildAddToExistingPlan(WizardAnswers answers)
    {
        var files = new List<FileChange>();

        BuildPlatform(answers, files, Platform.Ios,
            selected: Platforms.Ios, alreadyPresent: Platforms.IosAlreadyPresent,
            fastlaneDir: Project.IosFastlaneDir, chosenLanes: Lanes.ChosenIos, platformKey: "ios");

        BuildPlatform(answers, files, Platform.Android,
            selected: Platforms.Android, alreadyPresent: Platforms.AndroidAlreadyPresent,
            fastlaneDir: Project.AndroidFastlaneDir, chosenLanes: Lanes.ChosenAndroid, platformKey: "android");

        var secrets = answers.Secrets.Select(s => new SecretToStore(s.Key, s.Value)).ToList();
        return new ScaffoldPlan(files, secrets);
    }

    void BuildPlatform(
        WizardAnswers answers, List<FileChange> files, Platform platform,
        bool selected, bool alreadyPresent, string? fastlaneDir,
        IReadOnlyList<string> chosenLanes, string platformKey)
    {
        if (!selected) return;

        if (!alreadyPresent)
        {
            // A NEW platform: render its full file set (Kind=Create) by scoping a
            // single-platform answers set so the scaffolder emits only this platform.
            var only = answers with
            {
                Ios = platform == Platform.Ios,
                Android = platform == Platform.Android,
            };
            foreach (var file in FastlaneScaffolder.Render(only, Project.Path).Files)
            {
                // Drop the shared .env.example so we don't clobber an existing one.
                if (file.Path.Replace('\\', '/').EndsWith(".env.example")) continue;
                files.Add(file);
            }
            return;
        }

        // ALREADY-PRESENT platform: merge each chosen lane into the existing Fastfile.
        if (chosenLanes.Count == 0 || fastlaneDir is null) return;
        var fastfilePath = System.IO.Path.Combine(fastlaneDir, "Fastfile");
        if (!File.Exists(fastfilePath)) return;

        var existing = File.ReadAllText(fastfilePath);
        foreach (var lane in chosenLanes)
        {
            var laneRuby = LaneTemplate.Render(platform, lane, answers);
            var merged = FastfileMerger.HasPlatformBlock(existing, platformKey)
                ? FastfileMerger.InsertLane(existing, laneRuby, platformKey)
                : FastfileMerger.AddPlatformBlock(existing, $"platform :{platformKey} do\n{laneRuby}\nend\n");
            existing = merged;
        }

        var kind = FastfileMerger.HasPlatformBlock(File.ReadAllText(fastfilePath), platformKey)
            ? FileChangeKind.InsertLane
            : FileChangeKind.AddPlatformBlock;

        files.Add(new FileChange(fastfilePath, OldContent: File.ReadAllText(fastfilePath), NewContent: existing, kind));
    }

    // ---- apply / cancel -----------------------------------------------------

    /// <summary>
    /// The streaming <c>bundle install</c> output shown by the apply panel. The shell
    /// wires <c>ProjectScaffoldService.Output</c> into <see cref="AppendApplyLog"/>.
    /// </summary>
    public ObservableCollection<string> ApplyLog { get; } = new();

    /// <summary>True while the plan is being applied (drives the run panel/overlay).</summary>
    [ObservableProperty]
    private bool _isApplying;

    /// <summary>Appends a line to <see cref="ApplyLog"/>, marshalling onto the UI thread.</summary>
    public void AppendApplyLog(string line)
    {
        if (Dispatcher.UIThread.CheckAccess()) ApplyLog.Add(line);
        else Dispatcher.UIThread.Post(() => ApplyLog.Add(line));
    }

    [RelayCommand]
    public async Task ApplyAsync()
    {
        IsApplying = true;
        await _apply(BuildPlan());
        Closed?.Invoke();
    }

    [RelayCommand]
    public void Cancel() => Closed?.Invoke();
}

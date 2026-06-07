using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaunchFast.Core.Env;
using LaunchFast.Core.Models;
using LaunchFast.Core.Stores;

namespace LaunchFast.App.ViewModels;

/// <summary>
/// Status of a single pre-flight / precheck row, driving its dot + pill colour.
/// </summary>
public enum CheckStatus { Pass, Warn, Fail }

/// <summary>
/// Content view-model for a project's "Release Flow" section: compose a submission
/// (platform / track / rollout) and run a pre-flight checklist, then either Submit
/// (which runs the matching real release lane on the Lanes screen) or stay put.
///
/// SEVERAL checks are REAL — computed here from disk via
/// <see cref="ProjectSecretScanner"/> and <see cref="StoreMetadataReader"/> and
/// flagged <see cref="ReleaseCheckViewModel.IsReal"/>. Two (signing certificates,
/// CI) are illustrative placeholders, clearly marked. The metadata precheck list is
/// illustrative (no real <c>precheck</c> backend yet). Submit is genuinely wired to
/// the release lane and is disabled when the lane is absent OR any real check fails;
/// the mini terminal shows a static "ready" line and never fakes a streaming run.
/// </summary>
public partial class ReleaseSectionViewModel : ObservableObject
{
    readonly Project _project;
    readonly Action<Platform, string>? _runLane;
    readonly Func<Platform, string, bool> _hasLane;
    readonly Func<DateTimeOffset> _clock;

    public ReleaseSectionViewModel(
        Project project,
        Action<Platform, string>? runLane = null,
        Func<Platform, string, bool>? hasLane = null,
        Func<DateTimeOffset>? clock = null)
    {
        _project = project;
        _runLane = runLane;
        _hasLane = hasLane ?? ((_, _) => false);
        _clock = clock ?? (() => DateTimeOffset.Now);

        Checks = new ObservableCollection<ReleaseCheckViewModel>();
        Prechecks = new ObservableCollection<ReleaseCheckViewModel>();

        // Default to iOS unless the project only has Android fastlane.
        _platform = project.IosFastlaneDir is not null || project.AndroidFastlaneDir is null
            ? Platform.Ios
            : Platform.Android;

        ReloadTracks();
        RunChecks();
    }

    public string Name => _project.Name;

    public string? Version => _project.Version;

    // ---- platform ------------------------------------------------------------

    [ObservableProperty]
    private Platform _platform;

    partial void OnPlatformChanged(Platform value)
    {
        ReloadTracks();
        RunChecks();
        OnPropertyChanged(nameof(IsIos));
        OnPropertyChanged(nameof(IsAndroid));
        OnPropertyChanged(nameof(TerminalContext));
    }

    public bool IsIosSelected
    {
        get => Platform == Platform.Ios;
        set { if (value) Platform = Platform.Ios; }
    }

    public bool IsAndroidSelected
    {
        get => Platform == Platform.Android;
        set { if (value) Platform = Platform.Android; }
    }

    public bool IsIos => Platform == Platform.Ios;
    public bool IsAndroid => Platform == Platform.Android;

    // ---- track ---------------------------------------------------------------

    public ObservableCollection<ReleaseTrackOption> Tracks { get; } = new();

    [ObservableProperty]
    private ReleaseTrackOption? _selectedTrack;

    partial void OnSelectedTrackChanged(ReleaseTrackOption? value)
    {
        foreach (var t in Tracks) t.IsSelected = t == value;
        OnPropertyChanged(nameof(CanSubmit));
        OnPropertyChanged(nameof(SubmitDisabledReason));
        OnPropertyChanged(nameof(TerminalContext));
    }

    [RelayCommand]
    void SelectTrack(ReleaseTrackOption? track)
    {
        if (track is not null) SelectedTrack = track;
    }

    void ReloadTracks()
    {
        Tracks.Clear();
        if (IsIos)
        {
            Tracks.Add(new ReleaseTrackOption("TestFlight", "beta"));
            Tracks.Add(new ReleaseTrackOption("App Store", "release"));
        }
        else
        {
            Tracks.Add(new ReleaseTrackOption("Internal", "internal"));
            Tracks.Add(new ReleaseTrackOption("Beta", "beta"));
            Tracks.Add(new ReleaseTrackOption("Production", "production"));
        }

        // iOS defaults to App Store, Android to Production (the "release" intent).
        SelectedTrack = Tracks.LastOrDefault();
    }

    // ---- version & changelog -------------------------------------------------

    public string SubmittingText
    {
        get
        {
            var (name, build) = WhatsNewSectionViewModel.ParseVersion(_project.Version);
            if (name is null) return "No version set";
            return build is null ? $"v{name}" : $"v{name} · build {build}";
        }
    }

    /// <summary>The attached changelog — the first locale's release notes (real), else placeholder.</summary>
    public string ChangelogText { get; private set; } = string.Empty;

    public bool HasRealChangelog { get; private set; }

    public string ChangelogLocale { get; private set; } = "—";

    public string ChangelogVersionText =>
        WhatsNewSectionViewModel.ParseVersion(_project.Version).Name is { } n ? $"v{n}" : "—";

    // ---- rollout (illustrative compose controls) -----------------------------

    [ObservableProperty]
    private bool _phasedRelease = true;

    [ObservableProperty]
    private bool _autoRelease;

    [ObservableProperty]
    private double _stagedRolloutPercent = 20;

    partial void OnStagedRolloutPercentChanged(double value) =>
        OnPropertyChanged(nameof(StagedRolloutText));

    public string StagedRolloutText => $"{(int)Math.Round(StagedRolloutPercent)}%";

    // ---- checks --------------------------------------------------------------

    public ObservableCollection<ReleaseCheckViewModel> Checks { get; }
    public ObservableCollection<ReleaseCheckViewModel> Prechecks { get; }

    /// <summary>
    /// "Last checked HH:mm:ss" — bumped on every <see cref="RunChecks"/> so the
    /// "Re-run checks" button always gives visible feedback, even when the disk is
    /// unchanged and the rebuilt checklist is identical.
    /// </summary>
    public string LastCheckedText { get; private set; } = "Not checked yet";

    /// <summary>Re-computes the pre-flight checklist + (illustrative) precheck list.</summary>
    [RelayCommand]
    public void RunChecks()
    {
        Checks.Clear();
        Prechecks.Clear();

        var scan = ProjectSecretScanner.Scan(_project);
        var locales = StoreMetadataReader.Locales(_project, Platform);
        var locale = locales.FirstOrDefault();

        // -- Version set (REAL) --
        var (vname, _) = WhatsNewSectionViewModel.ParseVersion(_project.Version);
        Checks.Add(vname is not null
            ? Real(CheckStatus.Pass, "Version set", $"{_project.Version} parses cleanly")
            : Real(CheckStatus.Fail, "Version set", "No parseable version in pubspec"));

        // -- Secrets present (REAL) --
        var missing = scan.RequiredSecrets
            .Where(s => !scan.FromFiles.ContainsKey(s))
            .ToList();
        if (scan.RequiredSecrets.Count == 0)
        {
            Checks.Add(Real(CheckStatus.Pass, "Secrets present", "No fastlane secrets required"));
        }
        else if (missing.Count == 0)
        {
            Checks.Add(Real(CheckStatus.Pass, "Secrets present",
                $"{scan.RequiredSecrets.Count} required secret(s) sourced from .env"));
        }
        else
        {
            Checks.Add(Real(CheckStatus.Fail, "Secrets present",
                $"Missing: {string.Join(", ", missing)}"));
        }

        // -- Signing certificates valid (illustrative) --
        Checks.Add(Illustrative(CheckStatus.Pass, "Signing certificates valid",
            "Placeholder — no signing backend wired yet"));

        // -- Metadata complete (REAL) --
        if (locale is null)
        {
            Checks.Add(Real(CheckStatus.Warn, "Metadata complete",
                "No store metadata on disk for this platform"));
        }
        else
        {
            var listing = StoreMetadataReader.ReadListing(_project, Platform, locale);
            var hasKeyFields = !string.IsNullOrWhiteSpace(listing.Name)
                && !string.IsNullOrWhiteSpace(listing.FullDescription);
            Checks.Add(hasKeyFields
                ? Real(CheckStatus.Pass, "Metadata complete",
                    $"{locale} — title + description present")
                : Real(CheckStatus.Warn, "Metadata complete",
                    $"{locale} — missing title or description"));
        }

        // -- Screenshots present (REAL) --
        if (locale is null)
        {
            Checks.Add(Real(CheckStatus.Warn, "Screenshots present",
                "No locale on disk to check"));
        }
        else
        {
            var listing = StoreMetadataReader.ReadListing(_project, Platform, locale);
            Checks.Add(listing.ScreenshotPaths.Count > 0
                ? Real(CheckStatus.Pass, "Screenshots present",
                    $"{locale} — {listing.ScreenshotPaths.Count} screenshot(s)")
                : Real(CheckStatus.Warn, "Screenshots present",
                    $"locale {locale} has none"));
        }

        // -- CI green (illustrative) --
        Checks.Add(Illustrative(CheckStatus.Pass, "CI green on main",
            "Placeholder — no CI integration wired yet"));

        // -- Metadata precheck list (illustrative — no fastlane precheck backend) --
        Prechecks.Add(Illustrative(CheckStatus.Pass, "No mentions of other platforms",
            "Placeholder — fastlane precheck not run"));
        Prechecks.Add(Illustrative(CheckStatus.Pass, "No placeholder text",
            "Placeholder — fastlane precheck not run"));
        Prechecks.Add(Illustrative(CheckStatus.Pass, "All URLs reachable",
            "Placeholder — fastlane precheck not run"));

        // Attached changelog (real if present).
        if (locale is not null)
        {
            var listing = StoreMetadataReader.ReadListing(_project, Platform, locale);
            if (!string.IsNullOrWhiteSpace(listing.ReleaseNotes))
            {
                ChangelogText = listing.ReleaseNotes!;
                HasRealChangelog = true;
                ChangelogLocale = locale;
            }
            else
            {
                ChangelogText = "No release notes on disk for this locale — add them in What's New.";
                HasRealChangelog = false;
                ChangelogLocale = locale;
            }
        }
        else
        {
            ChangelogText = "No release notes on disk — add them in What's New.";
            HasRealChangelog = false;
            ChangelogLocale = "—";
        }

        OnPropertyChanged(nameof(ChangelogText));
        OnPropertyChanged(nameof(HasRealChangelog));
        OnPropertyChanged(nameof(ChangelogLocale));
        OnPropertyChanged(nameof(ChangelogVersionText));
        OnPropertyChanged(nameof(SubmittingText));
        OnPropertyChanged(nameof(WarningCount));
        OnPropertyChanged(nameof(HasFailingCheck));
        OnPropertyChanged(nameof(CanSubmit));
        OnPropertyChanged(nameof(SubmitDisabledReason));
        OnPropertyChanged(nameof(CheckSummary));

        LastCheckedText = $"Last checked {_clock().ToLocalTime():HH:mm:ss}";
        OnPropertyChanged(nameof(LastCheckedText));
    }

    static ReleaseCheckViewModel Real(CheckStatus s, string name, string detail) =>
        new(s, name, detail, isReal: true);

    static ReleaseCheckViewModel Illustrative(CheckStatus s, string name, string detail) =>
        new(s, name, detail, isReal: false);

    /// <summary>Any REAL check failed → submission is blocked.</summary>
    public bool HasFailingCheck => Checks.Any(c => c.IsReal && c.Status == CheckStatus.Fail);

    public int WarningCount => Checks.Count(c => c.Status == CheckStatus.Warn);

    public string CheckSummary
    {
        get
        {
            var w = WarningCount;
            if (HasFailingCheck) return "Resolve failing checks before submitting.";
            return w == 0
                ? "All checks passed — ready to submit."
                : $"{w} warning{(w == 1 ? "" : "s")} won't block submission — review before submitting.";
        }
    }

    // ---- submit --------------------------------------------------------------

    /// <summary>The fastlane lane name for the selected track (e.g. "release"/"beta"/"production").</summary>
    public string? ReleaseLaneName => SelectedTrack?.LaneName;

    /// <summary>True when the matching release lane exists on the project.</summary>
    public bool HasReleaseLane =>
        ReleaseLaneName is { } lane && _hasLane(Platform, lane);

    /// <summary>Submit is allowed only when the lane exists and no REAL check fails.</summary>
    public bool CanSubmit => HasReleaseLane && !HasFailingCheck;

    public string SubmitDisabledReason
    {
        get
        {
            if (HasFailingCheck) return "A required pre-flight check is failing.";
            if (!HasReleaseLane)
                return $"No '{ReleaseLaneName}' lane in the {(IsIos ? "iOS" : "Android")} Fastfile.";
            return string.Empty;
        }
    }

    /// <summary>
    /// Submits for review by running the matching release lane on the Lanes screen.
    /// Never fakes a run; no-op when gated.
    /// </summary>
    [RelayCommand]
    void Submit()
    {
        if (!CanSubmit || ReleaseLaneName is null) return;
        _runLane?.Invoke(Platform, ReleaseLaneName);
    }

    // ---- mini terminal (static, never fakes a run) ---------------------------

    public string TerminalContext =>
        $"{(IsIos ? "ios" : "android")} · {SelectedTrack?.Title ?? "—"}";

    public string TerminalReadyLine =>
        ReleaseLaneName is { } lane
            ? $"$ launchfast release {(IsIos ? "ios" : "android")} {lane}  — ready (runs on Lanes)"
            : "$ launchfast release — no lane selected";
}

/// <summary>A release track option (label + the fastlane lane it maps to).</summary>
public sealed partial class ReleaseTrackOption : ObservableObject
{
    public ReleaseTrackOption(string title, string laneName)
    {
        Title = title;
        LaneName = laneName;
    }

    public string Title { get; }
    public string LaneName { get; }

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// One checklist row: a status, name, detail line, and whether it is computed from
/// REAL project data (vs an illustrative placeholder).
/// </summary>
public sealed class ReleaseCheckViewModel
{
    public ReleaseCheckViewModel(CheckStatus status, string name, string detail, bool isReal)
    {
        Status = status;
        Name = name;
        Detail = detail;
        IsReal = isReal;
    }

    public CheckStatus Status { get; }
    public string Name { get; }
    public string Detail { get; }
    public bool IsReal { get; }

    public bool IsPass => Status == CheckStatus.Pass;
    public bool IsWarn => Status == CheckStatus.Warn;
    public bool IsFail => Status == CheckStatus.Fail;

    public string StatusLabel => Status switch
    {
        CheckStatus.Pass => "Pass",
        CheckStatus.Warn => "Warn",
        _ => "Fail",
    };

    /// <summary>"Real" / "Illustrative" badge text.</summary>
    public string SourceBadge => IsReal ? "Real" : "Illustrative";
}

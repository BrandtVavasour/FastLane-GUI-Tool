using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaunchFast.Core.Models;

namespace LaunchFast.App.ViewModels;

/// <summary>
/// Content view-model for a project's "Build &amp; Test" section (gym + scan).
///
/// SHELL / PLACEHOLDER: a faithful themed shell. The build settings, test toggles
/// and last-run results are <b>illustrative</b> placeholder data (see
/// <see cref="IsPlaceholder"/>) shown until real gym/scan backends land. The
/// "Run tests" and "Build (gym)" actions are wired to the project's <c>test</c>
/// and <c>build</c> fastlane lanes respectively, and are disabled when those lanes
/// are absent (the sample project has neither, so both stay disabled — honest).
/// </summary>
public partial class BuildTestSectionViewModel : ObservableObject
{
    readonly Action<Platform, string>? _runLane;
    readonly Func<bool> _hasTestLane;
    readonly Func<bool> _hasBuildLane;

    public BuildTestSectionViewModel(
        Project project,
        Action<Platform, string>? runLane = null,
        Func<bool>? hasTestLane = null,
        Func<bool>? hasBuildLane = null)
    {
        _ = project; // reserved for a future real gym/scan backend
        _runLane = runLane;
        _hasTestLane = hasTestLane ?? (() => false);
        _hasBuildLane = hasBuildLane ?? (() => false);

        Schemes = new ObservableCollection<string> { "VendingTracker", "VendingTracker-Staging" };
        Configurations = new ObservableCollection<string> { "Release", "Debug" };
        TestPlans = new ObservableCollection<string> { "FullSuite", "SmokeTests" };
        Simulators = new ObservableCollection<string> { "iPhone 15 Pro (17.4)", "iPhone SE (17.4)" };

        SelectedScheme = Schemes[0];
        SelectedConfiguration = Configurations[0];
        SelectedTestPlan = TestPlans[0];
        SelectedSimulator = Simulators[0];

        BuildToggles = new ObservableCollection<BuildToggleRow>
        {
            new("Clean before build", "Wipe DerivedData first", On: true),
            new("Upload dSYMs", "Send symbols to crash reporting", On: true),
            new("Include bitcode", "Deprecated by Apple", On: false),
        };

        Results = new ObservableCollection<TestResultRow>
        {
            new("Pass", "UnitTests", "180 passed", TestResultState.Pass),
            new("Pass", "UITests", "68 passed", TestResultState.Pass),
            new("Skip", "SnapshotTests", "2 skipped", TestResultState.Skip),
        };
    }

    /// <summary>Marks this section's list data as illustrative placeholder, not live.</summary>
    public bool IsPlaceholder => true;

    // ---- subbar (placeholder) ------------------------------------------------
    public string ChipText => "gym + scan";
    public string SyncedText => "last build passed · 1m 47s";

    // ---- build settings · gym ------------------------------------------------
    public ObservableCollection<string> Schemes { get; }
    public ObservableCollection<string> Configurations { get; }

    [ObservableProperty]
    private string? _selectedScheme;

    [ObservableProperty]
    private string? _selectedConfiguration;

    [ObservableProperty]
    private ExportMethod _exportMethod = ExportMethod.AppStore;

    public bool ExportAppStore
    {
        get => ExportMethod == ExportMethod.AppStore;
        set { if (value) ExportMethod = ExportMethod.AppStore; }
    }

    public bool ExportAdHoc
    {
        get => ExportMethod == ExportMethod.AdHoc;
        set { if (value) ExportMethod = ExportMethod.AdHoc; }
    }

    public bool ExportDevelopment
    {
        get => ExportMethod == ExportMethod.Development;
        set { if (value) ExportMethod = ExportMethod.Development; }
    }

    partial void OnExportMethodChanged(ExportMethod value)
    {
        OnPropertyChanged(nameof(ExportAppStore));
        OnPropertyChanged(nameof(ExportAdHoc));
        OnPropertyChanged(nameof(ExportDevelopment));
    }

    public ObservableCollection<BuildToggleRow> BuildToggles { get; }

    public string OutputPath => "build/VendingTracker.ipa";

    // ---- tests · scan --------------------------------------------------------
    [ObservableProperty]
    private bool _runTestsBeforeRelease = true;

    public ObservableCollection<string> TestPlans { get; }
    public ObservableCollection<string> Simulators { get; }

    [ObservableProperty]
    private string? _selectedTestPlan;

    [ObservableProperty]
    private string? _selectedSimulator;

    // ---- last test run (placeholder) -----------------------------------------
    public string LastRunMeta => "1m 12s · 4m ago";
    public string PassedCount => "248";
    public string FailedCount => "0";
    public string SkippedCount => "2";

    /// <summary>Pass-ratio width fraction for the inline progress bar (0..1).</summary>
    public double PassFraction => 0.992;

    public ObservableCollection<TestResultRow> Results { get; }

    /// <summary>True when the project exposes a <c>test</c> iOS lane.</summary>
    public bool CanRunTests => _hasTestLane();

    /// <summary>True when the project exposes a <c>build</c> iOS lane.</summary>
    public bool CanBuild => _hasBuildLane();

    /// <summary>Runs the real <c>test</c> lane via the shell's lane runner.</summary>
    [RelayCommand]
    void RunTests()
    {
        if (!CanRunTests) return;
        _runLane?.Invoke(Platform.Ios, "test");
    }

    /// <summary>Runs the real <c>build</c> lane via the shell's lane runner.</summary>
    [RelayCommand]
    void Build()
    {
        if (!CanBuild) return;
        _runLane?.Invoke(Platform.Ios, "build");
    }
}

public enum ExportMethod { AppStore, AdHoc, Development }

/// <summary>Illustrative gym build-option toggle row.</summary>
public sealed partial class BuildToggleRow : ObservableObject
{
    public BuildToggleRow(string Title, string Sub, bool On)
    {
        this.Title = Title;
        this.Sub = Sub;
        _on = On;
    }

    public string Title { get; }
    public string Sub { get; }

    [ObservableProperty]
    private bool _on;
}

/// <summary>State of a placeholder test result (drives the status pill tint).</summary>
public enum TestResultState { Pass, Skip, Fail }

/// <summary>Illustrative scan test-result row for the Build &amp; Test shell.</summary>
public sealed record TestResultRow(
    string StatusText, string Name, string Count, TestResultState State)
{
    public bool IsPass => State == TestResultState.Pass;
    public bool IsSkip => State == TestResultState.Skip;
    public bool IsFail => State == TestResultState.Fail;
}

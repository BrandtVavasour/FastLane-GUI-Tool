using System.Collections.ObjectModel;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaunchFast.Core.Building;
using LaunchFast.Core.Models;

namespace LaunchFast.App.ViewModels;

/// <summary>
/// Content view-model for a project's "Build &amp; Test" section (gym + scan).
///
/// <para>The build settings (scheme/configuration/export-method/clean/bitcode/output),
/// the test settings (scheme/test-plan/devices) and the last-run results are all
/// <b>real</b>, read from <c>ios/fastlane/Gymfile</c>/<c>Scanfile</c> (falling back to
/// the Fastfile's <c>build_app</c>/<c>run_tests</c> args) and the latest JUnit report
/// on disk via <see cref="BuildTestConfigReader"/>. Honest "not configured"/empty
/// states are shown where nothing is on disk; test numbers are never fabricated.</para>
///
/// <para>The "Run tests" and "Build (gym)" actions are wired to the project's
/// <c>test</c> and <c>build</c> fastlane lanes respectively, and are disabled when
/// those lanes are absent.</para>
/// </summary>
public partial class BuildTestSectionViewModel : ObservableObject
{
    readonly Action<Platform, string>? _runLane;
    readonly Func<bool> _hasTestLane;
    readonly Func<bool> _hasBuildLane;
    readonly BuildTestConfig _config;

    public BuildTestSectionViewModel(
        Project project,
        Action<Platform, string>? runLane = null,
        Func<bool>? hasTestLane = null,
        Func<bool>? hasBuildLane = null,
        Func<Project, BuildTestConfig>? readConfig = null)
    {
        _runLane = runLane;
        _hasTestLane = hasTestLane ?? (() => false);
        _hasBuildLane = hasBuildLane ?? (() => false);
        _config = (readConfig ?? BuildTestConfigReader.Read)(project);

        Devices = new ObservableCollection<string>(_config.Test?.Devices ?? Array.Empty<string>());

        Results = new ObservableCollection<TestResultRow>();
        if (_config.LatestResults is { } r)
        {
            foreach (var s in r.Suites)
            {
                Results.Add(TestResultRow.FromSuite(s));
            }
        }
    }

    // ---- subbar --------------------------------------------------------------

    public string ChipText => "gym + scan";

    /// <summary>Honest summary of the discovered config + results state.</summary>
    public string SyncedText
    {
        get
        {
            if (_config.LatestResults is { } r)
            {
                var verdict = r.Failed > 0 ? "last run had failures" : "last run passed";
                return r.Duration is { } d
                    ? $"{verdict} · {FormatDuration(d)}"
                    : verdict;
            }

            return _config.Build is not null ? "gym configured" : "no gym/scan config";
        }
    }

    // ---- build settings · gym ------------------------------------------------

    BuildSettings? BuildCfg => _config.Build;

    /// <summary>True when build (gym) settings were discovered on disk.</summary>
    public bool HasBuildSettings => BuildCfg is not null;

    public string SchemeText => BuildCfg?.Scheme ?? "Not configured";

    public string ConfigurationText => BuildCfg?.Configuration ?? "Not configured";

    /// <summary>The discovered export method, normalised for matching the segments.</summary>
    public string? ExportMethod => BuildCfg?.ExportMethod;

    public bool ExportAppStore => MatchesExport("app-store", "app_store", "appstore");

    public bool ExportAdHoc => MatchesExport("ad-hoc", "ad_hoc", "adhoc");

    public bool ExportDevelopment => MatchesExport("development", "dev");

    bool MatchesExport(params string[] forms)
    {
        var v = BuildCfg?.ExportMethod;
        if (string.IsNullOrWhiteSpace(v))
        {
            return false;
        }

        var normalised = v.Replace("_", "-").Replace(" ", "-");
        return forms.Any(f =>
            string.Equals(normalised, f.Replace("_", "-"), StringComparison.OrdinalIgnoreCase));
    }

    public string ExportMethodText => BuildCfg?.ExportMethod ?? "Not configured";

    public string CleanText =>
        BuildCfg?.Clean switch
        {
            true => "Yes",
            false => "No",
            null => "Not set",
        };

    public string IncludeBitcodeText =>
        BuildCfg?.IncludeBitcode switch
        {
            true => "Yes",
            false => "No",
            null => "Not set",
        };

    public string OutputPath => BuildCfg?.OutputPath ?? "Not configured";

    // ---- tests · scan --------------------------------------------------------

    TestSettings? Test => _config.Test;

    /// <summary>True when test (scan) settings were discovered on disk.</summary>
    public bool HasTestSettings => Test is not null;

    public string TestSchemeText => Test?.Scheme ?? "Not configured";

    public string TestPlanText => Test?.TestPlan ?? "Not configured";

    public ObservableCollection<string> Devices { get; }

    public bool HasDevices => Devices.Count > 0;

    public string DevicesText =>
        Devices.Count > 0 ? string.Join(", ", Devices) : "Not configured";

    // ---- last test run -------------------------------------------------------

    TestResults? LatestResults => _config.LatestResults;

    /// <summary>True when a JUnit report was found and parsed from disk.</summary>
    public bool HasResults => LatestResults is not null;

    public string LastRunMeta =>
        LatestResults?.Duration is { } d ? FormatDuration(d) : string.Empty;

    public string PassedCount => (LatestResults?.Passed ?? 0).ToString();

    public string FailedCount => (LatestResults?.Failed ?? 0).ToString();

    public string SkippedCount => (LatestResults?.Skipped ?? 0).ToString();

    /// <summary>Pass-ratio width fraction for the inline progress bar (0..1).</summary>
    public double PassFraction
    {
        get
        {
            var total = LatestResults?.Total ?? 0;
            if (total == 0)
            {
                return 0;
            }

            return (double)LatestResults!.Passed / total;
        }
    }

    /// <summary>Remainder of the progress bar (failed + skipped fraction).</summary>
    public double RemainderFraction => 1 - PassFraction;

    /// <summary>Pass fraction as a star <see cref="GridLength"/> for the progress bar.</summary>
    public GridLength PassStar => new(PassFraction, GridUnitType.Star);

    /// <summary>Remainder fraction as a star <see cref="GridLength"/> for the progress bar.</summary>
    public GridLength RemainderStar => new(RemainderFraction, GridUnitType.Star);

    public ObservableCollection<TestResultRow> Results { get; }

    public string EmptyResultsText => "No test report found — run tests to generate one.";

    // ---- run wiring ----------------------------------------------------------

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

    static string FormatDuration(TimeSpan d) =>
        d.TotalMinutes >= 1
            ? $"{(int)d.TotalMinutes}m {d.Seconds:D2}s"
            : $"{d.TotalSeconds:0.#}s";
}

/// <summary>State of a parsed test-suite result (drives the status pill tint).</summary>
public enum TestResultState { Pass, Skip, Fail }

/// <summary>A per-suite scan test-result row built from a parsed JUnit suite.</summary>
public sealed record TestResultRow(
    string StatusText, string Name, string Count, TestResultState State)
{
    public bool IsPass => State == TestResultState.Pass;
    public bool IsSkip => State == TestResultState.Skip;
    public bool IsFail => State == TestResultState.Fail;

    /// <summary>Builds a display row from a parsed JUnit suite result.</summary>
    public static TestResultRow FromSuite(TestSuiteResult s)
    {
        var (status, state) = s.Failed > 0
            ? ("Fail", TestResultState.Fail)
            : s.Skipped > 0 && s.Passed == 0
                ? ("Skip", TestResultState.Skip)
                : ("Pass", TestResultState.Pass);

        var parts = new List<string>();
        if (s.Passed > 0) parts.Add($"{s.Passed} passed");
        if (s.Failed > 0) parts.Add($"{s.Failed} failed");
        if (s.Skipped > 0) parts.Add($"{s.Skipped} skipped");
        var count = parts.Count > 0 ? string.Join(" · ", parts) : "0 tests";

        return new TestResultRow(status, s.Name, count, state);
    }
}

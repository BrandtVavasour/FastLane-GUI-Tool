using System.Text.RegularExpressions;
using System.Xml.Linq;
using LaunchFast.Core.Models;

namespace LaunchFast.Core.Building;

/// <summary>
/// iOS build settings discovered for a project. Sourced from
/// <c>ios/fastlane/Gymfile</c> when present, else from a <c>build_app(...)</c>/
/// <c>gym(...)</c> call in the Fastfile. All members are null when unknown.
/// </summary>
public sealed record BuildSettings(
    string? Scheme,
    string? Configuration,
    string? ExportMethod,
    bool? Clean,
    bool? IncludeBitcode,
    string? OutputPath);

/// <summary>
/// iOS test (fastlane <c>scan</c>) settings. Sourced from <c>ios/fastlane/Scanfile</c>
/// when present, else from a <c>run_tests(...)</c>/<c>scan(...)</c> call in the
/// Fastfile. All members are null/empty when unknown.
/// </summary>
public sealed record TestSettings(
    string? Scheme,
    string? TestPlan,
    IReadOnlyList<string> Devices);

/// <summary>Per-suite result counts parsed from a JUnit report.</summary>
public sealed record TestSuiteResult(string Name, int Passed, int Failed, int Skipped);

/// <summary>
/// Aggregated results of the latest test run, parsed from a JUnit XML report on disk.
/// </summary>
public sealed record TestResults(
    int Passed,
    int Failed,
    int Skipped,
    TimeSpan? Duration,
    IReadOnlyList<TestSuiteResult> Suites)
{
    /// <summary>Total test cases across all suites (passed + failed + skipped).</summary>
    public int Total => Passed + Failed + Skipped;
}

/// <summary>
/// The iOS build (gym) + test (scan) configuration discovered on disk for a project,
/// plus the latest parsed JUnit test results when present.
/// </summary>
public sealed record BuildTestConfig(
    bool HasIos,
    BuildSettings? Build,
    TestSettings? Test,
    TestResults? LatestResults)
{
    /// <summary>An empty config for projects without an iOS fastlane dir.</summary>
    public static BuildTestConfig None { get; } = new(
        HasIos: false,
        Build: null,
        Test: null,
        LatestResults: null);
}

/// <summary>
/// Pure, file-based reader for a project's iOS fastlane <c>gym</c>/<c>scan</c> config
/// and the latest test results on disk. Total — never throws; returns
/// <see cref="BuildTestConfig.None"/> when the project has no iOS fastlane dir.
///
/// Sources (all under the iOS project, Gymfile/Scanfile taking precedence over the
/// Fastfile action args):
/// <list type="bullet">
/// <item><b>Build:</b> <c>ios/fastlane/Gymfile</c> (<c>scheme</c>, <c>configuration</c>,
/// <c>export_method</c>, <c>clean</c>, <c>output_directory</c>, <c>output_name</c>,
/// <c>include_bitcode</c>); else a <c>build_app(...)</c>/<c>gym(...)</c> call in the
/// Fastfile.</item>
/// <item><b>Test:</b> <c>ios/fastlane/Scanfile</c> (<c>scheme</c>, <c>devices</c>,
/// <c>testplan</c>/<c>test_plan</c>, <c>clean</c>); else a <c>run_tests(...)</c>/
/// <c>scan(...)</c> call in the Fastfile.</item>
/// <item><b>Results:</b> the first JUnit report found among
/// <c>ios/fastlane/test_output/report.junit</c>, <c>ios/fastlane/test_output/*.xml</c>,
/// <c>ios/test_output/report.xml</c> or <c>build/reports/*.xml</c>, parsed with
/// <see cref="System.Xml.Linq"/>. <c>.xcresult</c> bundles are skipped (need xcrun).</item>
/// </list>
/// </summary>
public static partial class BuildTestConfigReader
{
    // Each key tolerates both the Gymfile/Scanfile DSL form — key("value") /
    // key "value" — and the Fastfile action-arg form — key: "value". The
    // separator is an optional "(", ":" or whitespace before the quoted value.

    [GeneratedRegex("""\bscheme\s*[(:]?\s*["'](?<v>[^"']+)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex SchemeRegex();

    [GeneratedRegex("""\bconfiguration\s*[(:]?\s*["'](?<v>[^"']+)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex ConfigurationRegex();

    [GeneratedRegex("""\bexport_method\s*[(:]?\s*["'](?<v>[^"']+)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex ExportMethodRegex();

    [GeneratedRegex("""\boutput_directory\s*[(:]?\s*["'](?<v>[^"']+)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex OutputDirRegex();

    [GeneratedRegex("""\boutput_name\s*[(:]?\s*["'](?<v>[^"']+)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex OutputNameRegex();

    [GeneratedRegex("""\bclean\s*[(:]?\s*(?<v>true|false)\b""", RegexOptions.IgnoreCase)]
    private static partial Regex CleanRegex();

    [GeneratedRegex("""\binclude_bitcode\s*[(:]?\s*(?<v>true|false)\b""", RegexOptions.IgnoreCase)]
    private static partial Regex IncludeBitcodeRegex();

    [GeneratedRegex("""\b(?:testplan|test_plan)\s*[(:]?\s*["'](?<v>[^"']+)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex TestPlanRegex();

    // devices([ ... ]) array form (body captured, may span lines).
    [GeneratedRegex(
        """\bdevices\s*[(:]?\s*\[(?<v>[^\]]*)\]""",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex DevicesArrayRegex();

    // devices("Single Device") string form.
    [GeneratedRegex(
        """\bdevices\s*[(:]?\s*["'](?<v>[^"']+)["']""",
        RegexOptions.IgnoreCase)]
    private static partial Regex DevicesStringRegex();

    [GeneratedRegex("""["'](?<v>[^"']*)["']""")]
    private static partial Regex QuotedTokenRegex();

    // build_app(...) / gym(...) call — body up to the matching first-level close.
    [GeneratedRegex(
        """\b(?:build_app|gym)\s*\((?<v>[^)]*)\)""",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex BuildAppCallRegex();

    // run_tests(...) / scan(...) call.
    [GeneratedRegex(
        """\b(?:run_tests|scan)\s*\((?<v>[^)]*)\)""",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex RunTestsCallRegex();

    /// <summary>
    /// Reads the project's iOS gym/scan config + latest JUnit results from disk.
    /// Returns <see cref="BuildTestConfig.None"/> when there is no iOS fastlane dir.
    /// </summary>
    public static BuildTestConfig Read(Project project)
    {
        if (project.IosFastlaneDir is not { } iosFl)
        {
            return BuildTestConfig.None;
        }

        var fastfile = ReadTextOrNull(Path.Combine(iosFl, "Fastfile"));

        var build = ReadBuildSettings(iosFl, fastfile);
        var test = ReadTestSettings(iosFl, fastfile);
        var results = ReadLatestResults(project, iosFl);

        return new BuildTestConfig(
            HasIos: true,
            Build: build,
            Test: test,
            LatestResults: results);
    }

    // ---- build (gym) ---------------------------------------------------------

    static BuildSettings? ReadBuildSettings(string iosFl, string? fastfile)
    {
        var gymfile = ReadTextOrNull(Path.Combine(iosFl, "Gymfile"));

        if (gymfile is not null)
        {
            var outputDir = FindFirst(OutputDirRegex(), gymfile);
            var outputName = FindFirst(OutputNameRegex(), gymfile);
            return new BuildSettings(
                Scheme: FindFirst(SchemeRegex(), gymfile),
                Configuration: FindFirst(ConfigurationRegex(), gymfile),
                ExportMethod: FindFirst(ExportMethodRegex(), gymfile),
                Clean: FindBool(CleanRegex(), gymfile),
                IncludeBitcode: FindBool(IncludeBitcodeRegex(), gymfile),
                OutputPath: ComposeOutputPath(outputDir, outputName));
        }

        // Fall back to a build_app/gym(...) call in the Fastfile.
        if (fastfile is not null && BuildAppCallRegex().Match(fastfile) is { Success: true } m)
        {
            var args = m.Groups["v"].Value;
            var outputDir = FindFirst(OutputDirRegex(), args);
            var outputName = FindFirst(OutputNameRegex(), args);
            return new BuildSettings(
                Scheme: FindFirst(SchemeRegex(), args),
                Configuration: FindFirst(ConfigurationRegex(), args),
                ExportMethod: FindFirst(ExportMethodRegex(), args),
                Clean: FindBool(CleanRegex(), args),
                IncludeBitcode: FindBool(IncludeBitcodeRegex(), args),
                OutputPath: ComposeOutputPath(outputDir, outputName));
        }

        return null;
    }

    static string? ComposeOutputPath(string? outputDir, string? outputName)
    {
        var dir = string.IsNullOrWhiteSpace(outputDir) ? null : outputDir.Trim();
        var name = string.IsNullOrWhiteSpace(outputName) ? null : outputName.Trim();

        if (dir is null && name is null)
        {
            return null;
        }

        if (name is null)
        {
            return dir;
        }

        // output_name may or may not carry the .ipa extension; leave as-is.
        return dir is null ? name : $"{dir.TrimEnd('/')}/{name}";
    }

    // ---- test (scan) ---------------------------------------------------------

    static TestSettings? ReadTestSettings(string iosFl, string? fastfile)
    {
        var scanfile = ReadTextOrNull(Path.Combine(iosFl, "Scanfile"));

        if (scanfile is not null)
        {
            return new TestSettings(
                Scheme: FindFirst(SchemeRegex(), scanfile),
                TestPlan: FindFirst(TestPlanRegex(), scanfile),
                Devices: ExtractDevices(scanfile));
        }

        if (fastfile is not null && RunTestsCallRegex().Match(fastfile) is { Success: true } m)
        {
            var args = m.Groups["v"].Value;
            return new TestSettings(
                Scheme: FindFirst(SchemeRegex(), args),
                TestPlan: FindFirst(TestPlanRegex(), args),
                Devices: ExtractDevices(args));
        }

        return null;
    }

    static IReadOnlyList<string> ExtractDevices(string text)
    {
        var arr = DevicesArrayRegex().Match(text);
        if (arr.Success)
        {
            return Tokens(arr.Groups["v"].Value);
        }

        var str = DevicesStringRegex().Match(text);
        if (str.Success)
        {
            var v = str.Groups["v"].Value.Trim();
            return v.Length == 0 ? Array.Empty<string>() : new[] { v };
        }

        return Array.Empty<string>();
    }

    // ---- latest JUnit results ------------------------------------------------

    static TestResults? ReadLatestResults(Project project, string iosFl)
    {
        foreach (var path in ResultCandidates(project, iosFl))
        {
            var xml = ReadTextOrNull(path);
            if (xml is null)
            {
                continue;
            }

            if (TryParseJUnit(xml) is { } results)
            {
                return results;
            }
        }

        return null;
    }

    static IEnumerable<string> ResultCandidates(Project project, string iosFl)
    {
        var iosDir = Directory.GetParent(iosFl)?.FullName ?? iosFl;

        // Highest-priority explicit name first, then globbed candidates.
        var fixedPaths = new[]
        {
            Path.Combine(iosFl, "test_output", "report.junit"),
            Path.Combine(iosFl, "test_output", "report.xml"),
            Path.Combine(iosDir, "test_output", "report.xml"),
        };

        foreach (var p in fixedPaths)
        {
            if (File.Exists(p))
            {
                yield return p;
            }
        }

        // Globbed XML reports (sorted for determinism), de-duped against fixed paths.
        var globDirs = new[]
        {
            Path.Combine(iosFl, "test_output"),
            Path.Combine(iosDir, "test_output"),
            Path.Combine(project.Path, "build", "reports"),
        };

        foreach (var dir in globDirs)
        {
            foreach (var p in XmlFilesIn(dir))
            {
                yield return p;
            }
        }
    }

    static IEnumerable<string> XmlFilesIn(string dir)
    {
        if (!Directory.Exists(dir))
        {
            yield break;
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(dir, "*.xml");
        }
        catch (IOException)
        {
            yield break;
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }

        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        foreach (var f in files)
        {
            yield return f;
        }
    }

    // ---- JUnit XML parsing ---------------------------------------------------

    /// <summary>
    /// Parses a JUnit-style report into aggregate + per-suite results. Returns
    /// <c>null</c> when the document is not JUnit-shaped or cannot be parsed.
    /// </summary>
    public static TestResults? TryParseJUnit(string xml)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }

        var root = doc.Root;
        if (root is null)
        {
            return null;
        }

        // Collect every <testsuite> (whether nested under <testsuites> or a lone root).
        var suiteElements = root.Name.LocalName.Equals("testsuite", StringComparison.OrdinalIgnoreCase)
            ? new[] { root }.Concat(root.Descendants().Where(IsTestSuite))
            : root.Descendants().Where(IsTestSuite);

        var suites = new List<TestSuiteResult>();
        var totalPassed = 0;
        var totalFailed = 0;
        var totalSkipped = 0;
        double totalSeconds = 0;
        var sawDuration = false;

        foreach (var suite in suiteElements)
        {
            // A <testsuite> may itself contain nested <testsuite> children; only count
            // the leaf suites that hold <testcase>s to avoid double counting.
            var cases = suite.Elements().Where(IsTestCase).ToList();
            if (cases.Count == 0 && suite.Elements().Any(IsTestSuite))
            {
                // Aggregate container; its time is rolled up by children, skip it.
                continue;
            }

            var failed = 0;
            var skipped = 0;

            foreach (var tc in cases)
            {
                if (tc.Elements().Any(e =>
                        e.Name.LocalName.Equals("failure", StringComparison.OrdinalIgnoreCase) ||
                        e.Name.LocalName.Equals("error", StringComparison.OrdinalIgnoreCase)))
                {
                    failed++;
                }
                else if (tc.Elements().Any(e =>
                             e.Name.LocalName.Equals("skipped", StringComparison.OrdinalIgnoreCase)))
                {
                    skipped++;
                }
            }

            var total = cases.Count;
            var passed = total - failed - skipped;
            if (passed < 0)
            {
                passed = 0;
            }

            var name = (string?)suite.Attribute("name") ?? "(suite)";
            suites.Add(new TestSuiteResult(name, passed, failed, skipped));

            totalPassed += passed;
            totalFailed += failed;
            totalSkipped += skipped;

            if (TryGetSeconds(suite.Attribute("time"), out var s))
            {
                totalSeconds += s;
                sawDuration = true;
            }
        }

        if (suites.Count == 0)
        {
            return null;
        }

        // Prefer a top-level <testsuites time="..."> when present.
        TimeSpan? duration = null;
        if (root.Name.LocalName.Equals("testsuites", StringComparison.OrdinalIgnoreCase) &&
            TryGetSeconds(root.Attribute("time"), out var rootSeconds))
        {
            duration = TimeSpan.FromSeconds(rootSeconds);
        }
        else if (sawDuration)
        {
            duration = TimeSpan.FromSeconds(totalSeconds);
        }

        return new TestResults(totalPassed, totalFailed, totalSkipped, duration, suites);
    }

    static bool IsTestSuite(XElement e) =>
        e.Name.LocalName.Equals("testsuite", StringComparison.OrdinalIgnoreCase);

    static bool IsTestCase(XElement e) =>
        e.Name.LocalName.Equals("testcase", StringComparison.OrdinalIgnoreCase);

    static bool TryGetSeconds(XAttribute? attr, out double seconds)
    {
        seconds = 0;
        return attr is not null &&
            double.TryParse(
                attr.Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out seconds);
    }

    // ---- DSL helpers ---------------------------------------------------------

    static string? FindFirst(Regex regex, string text)
    {
        var m = regex.Match(text);
        if (!m.Success)
        {
            return null;
        }

        var v = m.Groups["v"].Value.Trim();
        return v.Length == 0 ? null : v;
    }

    static bool? FindBool(Regex regex, string text)
    {
        var m = regex.Match(text);
        if (!m.Success)
        {
            return null;
        }

        return m.Groups["v"].Value.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    static List<string> Tokens(string body)
    {
        var result = new List<string>();
        foreach (Match m in QuotedTokenRegex().Matches(body))
        {
            var v = m.Groups["v"].Value.Trim();
            if (v.Length > 0)
            {
                result.Add(v);
            }
        }
        return result;
    }

    static string? ReadTextOrNull(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return File.ReadAllText(path);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}

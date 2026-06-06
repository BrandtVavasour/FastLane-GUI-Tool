using LaunchFast.Core.Building;
using LaunchFast.Core.Models;

namespace LaunchFast.Core.Tests;

[TestFixture]
public sealed class BuildTestConfigReaderTests
{
    string _root = null!;
    Project _project = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "lf-bt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _project = MakeProject(_root);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* best effort */ }
    }

    static Project MakeProject(string root) => new(
        Name: "demo",
        Path: root,
        Version: "1.0.0",
        IosFastlaneDir: Path.Combine(root, "ios", "fastlane"),
        AndroidFastlaneDir: Path.Combine(root, "android", "fastlane"),
        HasMatchfile: false,
        IconPath: null);

    string IosFl => Path.Combine(_root, "ios", "fastlane");

    static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    // ---- Gymfile -------------------------------------------------------------

    [Test]
    public void Read_parses_gymfile_build_settings()
    {
        Write(Path.Combine(IosFl, "Gymfile"),
            """
            scheme("Runner")
            configuration("Release")
            export_method("app-store")
            clean(true)
            include_bitcode(false)
            output_directory("./build")
            output_name("VendingTracker.ipa")
            """);

        var cfg = BuildTestConfigReader.Read(_project);

        Assert.That(cfg.HasIos, Is.True);
        Assert.That(cfg.Build, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(cfg.Build!.Scheme, Is.EqualTo("Runner"));
            Assert.That(cfg.Build.Configuration, Is.EqualTo("Release"));
            Assert.That(cfg.Build.ExportMethod, Is.EqualTo("app-store"));
            Assert.That(cfg.Build.Clean, Is.True);
            Assert.That(cfg.Build.IncludeBitcode, Is.False);
            Assert.That(cfg.Build.OutputPath, Is.EqualTo("./build/VendingTracker.ipa"));
        });
    }

    [Test]
    public void Read_falls_back_to_fastfile_build_app_call_when_no_gymfile()
    {
        Write(Path.Combine(IosFl, "Fastfile"),
            """
            platform :ios do
              lane :build do
                build_app(
                  scheme: "Runner",
                  configuration: "Release",
                  export_method: "ad-hoc",
                  clean: true
                )
              end
            end
            """);

        var cfg = BuildTestConfigReader.Read(_project);

        Assert.That(cfg.Build, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(cfg.Build!.Scheme, Is.EqualTo("Runner"));
            Assert.That(cfg.Build.Configuration, Is.EqualTo("Release"));
            Assert.That(cfg.Build.ExportMethod, Is.EqualTo("ad-hoc"));
            Assert.That(cfg.Build.Clean, Is.True);
        });
    }

    [Test]
    public void Read_gymfile_takes_precedence_over_fastfile_build_app()
    {
        Write(Path.Combine(IosFl, "Gymfile"), "scheme(\"FromGymfile\")\n");
        Write(Path.Combine(IosFl, "Fastfile"),
            "lane :build do\n  build_app(scheme: \"FromFastfile\")\nend\n");

        var cfg = BuildTestConfigReader.Read(_project);

        Assert.That(cfg.Build!.Scheme, Is.EqualTo("FromGymfile"));
    }

    // ---- Scanfile ------------------------------------------------------------

    [Test]
    public void Read_parses_scanfile_test_settings()
    {
        Write(Path.Combine(IosFl, "Scanfile"),
            """
            scheme("RunnerTests")
            test_plan("FullSuite")
            devices([
              "iPhone 15 Pro",
              "iPhone SE (3rd generation)"
            ])
            clean(true)
            """);

        var cfg = BuildTestConfigReader.Read(_project);

        Assert.That(cfg.Test, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(cfg.Test!.Scheme, Is.EqualTo("RunnerTests"));
            Assert.That(cfg.Test.TestPlan, Is.EqualTo("FullSuite"));
            Assert.That(cfg.Test.Devices, Is.EqualTo(new[]
            {
                "iPhone 15 Pro",
                "iPhone SE (3rd generation)",
            }));
        });
    }

    [Test]
    public void Read_falls_back_to_fastfile_run_tests_call_when_no_scanfile()
    {
        Write(Path.Combine(IosFl, "Fastfile"),
            """
            lane :test do
              run_tests(
                scheme: "RunnerTests",
                devices: ["iPhone 15 Pro"]
              )
            end
            """);

        var cfg = BuildTestConfigReader.Read(_project);

        Assert.That(cfg.Test, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(cfg.Test!.Scheme, Is.EqualTo("RunnerTests"));
            Assert.That(cfg.Test.Devices, Is.EqualTo(new[] { "iPhone 15 Pro" }));
        });
    }

    // ---- JUnit results -------------------------------------------------------

    [Test]
    public void Read_parses_junit_report_into_totals_and_suites()
    {
        Write(Path.Combine(IosFl, "test_output", "report.junit"),
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <testsuites name="Tests" tests="6" failures="1" skipped="1" time="12.5">
              <testsuite name="UnitTests" tests="3" failures="0" skipped="0" time="4.0">
                <testcase classname="UnitTests" name="a" time="1.0"/>
                <testcase classname="UnitTests" name="b" time="1.5"/>
                <testcase classname="UnitTests" name="c" time="1.5"/>
              </testsuite>
              <testsuite name="UITests" tests="3" failures="1" skipped="1" time="8.5">
                <testcase classname="UITests" name="d" time="2.0"/>
                <testcase classname="UITests" name="e" time="3.0">
                  <failure message="boom">stack</failure>
                </testcase>
                <testcase classname="UITests" name="f" time="0.0">
                  <skipped/>
                </testcase>
              </testsuite>
            </testsuites>
            """);

        var cfg = BuildTestConfigReader.Read(_project);

        Assert.That(cfg.LatestResults, Is.Not.Null);
        var r = cfg.LatestResults!;
        Assert.Multiple(() =>
        {
            Assert.That(r.Passed, Is.EqualTo(4));
            Assert.That(r.Failed, Is.EqualTo(1));
            Assert.That(r.Skipped, Is.EqualTo(1));
            Assert.That(r.Total, Is.EqualTo(6));
            Assert.That(r.Duration, Is.EqualTo(TimeSpan.FromSeconds(12.5)));
            Assert.That(r.Suites, Has.Count.EqualTo(2));

            var unit = r.Suites.Single(s => s.Name == "UnitTests");
            Assert.That(unit.Passed, Is.EqualTo(3));
            Assert.That(unit.Failed, Is.EqualTo(0));

            var ui = r.Suites.Single(s => s.Name == "UITests");
            Assert.That(ui.Passed, Is.EqualTo(1));
            Assert.That(ui.Failed, Is.EqualTo(1));
            Assert.That(ui.Skipped, Is.EqualTo(1));
        });
    }

    [Test]
    public void Read_parses_lone_testsuite_root_report_xml()
    {
        Write(Path.Combine(IosFl, "test_output", "report.xml"),
            """
            <testsuite name="OnlySuite" tests="2" failures="0" skipped="0" time="3.0">
              <testcase name="x" time="1.0"/>
              <testcase name="y" time="2.0"/>
            </testsuite>
            """);

        var cfg = BuildTestConfigReader.Read(_project);

        Assert.That(cfg.LatestResults, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(cfg.LatestResults!.Passed, Is.EqualTo(2));
            Assert.That(cfg.LatestResults.Suites, Has.Count.EqualTo(1));
            Assert.That(cfg.LatestResults.Suites[0].Name, Is.EqualTo("OnlySuite"));
            Assert.That(cfg.LatestResults.Duration, Is.EqualTo(TimeSpan.FromSeconds(3.0)));
        });
    }

    // ---- empty / absent ------------------------------------------------------

    [Test]
    public void Read_with_no_config_or_report_is_empty_but_has_ios()
    {
        Directory.CreateDirectory(IosFl);

        var cfg = BuildTestConfigReader.Read(_project);

        Assert.Multiple(() =>
        {
            Assert.That(cfg.HasIos, Is.True);
            Assert.That(cfg.Build, Is.Null);
            Assert.That(cfg.Test, Is.Null);
            Assert.That(cfg.LatestResults, Is.Null);
        });
    }

    [Test]
    public void Read_returns_none_when_no_ios_fastlane_dir()
    {
        var project = _project with { IosFastlaneDir = null };

        var cfg = BuildTestConfigReader.Read(project);

        Assert.That(cfg, Is.SameAs(BuildTestConfig.None));
        Assert.That(cfg.HasIos, Is.False);
    }

    [Test]
    public void Read_ignores_malformed_xml_report()
    {
        Write(Path.Combine(IosFl, "test_output", "report.xml"), "<not-xml <<>");

        var cfg = BuildTestConfigReader.Read(_project);

        Assert.That(cfg.LatestResults, Is.Null);
    }
}

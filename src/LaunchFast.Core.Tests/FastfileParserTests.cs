using LaunchFast.Core.Models;
using LaunchFast.Core.Parsing;

namespace LaunchFast.Core.Tests;

public class FastfileParserTests
{
    [Test]
    public Task Parses_ios_public_lanes_with_descriptions()
    {
        var text = File.ReadAllText("fixtures/ios.Fastfile");
        var lanes = FastfileParser.Parse(text, Platform.Ios);
        return Verify(lanes);
    }

    [Test]
    public Task Parses_android_public_lanes()
    {
        var text = File.ReadAllText("fixtures/android.Fastfile");
        var lanes = FastfileParser.Parse(text, Platform.Android);
        return Verify(lanes);
    }

    [Test]
    public void Skips_private_lanes()
    {
        var text = File.ReadAllText("fixtures/ios.Fastfile");
        var lanes = FastfileParser.Parse(text, Platform.Ios);
        Assert.That(lanes.Select(l => l.Name),
            Does.Not.Contain("capture_screenshots_for_device"));
        Assert.That(lanes.Select(l => l.Name), Does.Contain("beta"));
    }

    // ---- ParseDetailed -------------------------------------------------------

    static LaneDetail Detail(string fixture, Platform platform, string lane)
    {
        var text = File.ReadAllText($"fixtures/{fixture}");
        var details = FastfileParser.ParseDetailed(text, platform);
        return details.Single(d => d.Lane.Name == lane);
    }

    [Test]
    public void Detailed_excludes_private_lanes_and_keeps_public_ones()
    {
        var text = File.ReadAllText("fixtures/ios.Fastfile");
        var details = FastfileParser.ParseDetailed(text, Platform.Ios);

        var names = details.Select(d => d.Lane.Name).ToList();
        Assert.That(names, Does.Contain("beta"));
        Assert.That(names, Does.Contain("release"));
        Assert.That(names, Does.Contain("sync_certificates"));
        Assert.That(names, Does.Contain("screenshots"));
        // Private lanes are excluded.
        Assert.That(names, Does.Not.Contain("capture_screenshots_for_device"));
        Assert.That(names, Does.Not.Contain("organize_screenshots_for_app_store"));
        Assert.That(names, Does.Not.Contain("extract_attachments_from_xcresult"));
    }

    [Test]
    public void Detailed_carries_descriptions()
    {
        var beta = Detail("ios.Fastfile", Platform.Ios, "beta");
        Assert.That(beta.Lane.Description, Is.EqualTo("Build and upload to TestFlight"));
        Assert.That(beta.Lane.Platform, Is.EqualTo(Platform.Ios));
    }

    [Test]
    public void Ios_beta_source_block_bounds_are_correct()
    {
        var beta = Detail("ios.Fastfile", Platform.Ios, "beta");

        // Source starts at the lane header and ends at its matching end.
        Assert.That(beta.Source, Does.StartWith("  lane :beta do"));
        Assert.That(beta.Source.TrimEnd(), Does.EndWith("end"));

        // Body content is present; bleeds neither into the previous nor next lane.
        Assert.That(beta.Source, Does.Contain("upload_to_testflight"));
        Assert.That(beta.Source, Does.Contain("Dir.chdir(\"..\") do"));
        Assert.That(beta.Source, Does.Not.Contain("upload_to_app_store"));
        Assert.That(beta.Source, Does.Not.Contain("Sync code signing"));
    }

    [Test]
    public void Ios_beta_steps_have_expected_actions_and_tools()
    {
        var beta = Detail("ios.Fastfile", Platform.Ios, "beta");
        var actions = beta.Steps.Select(s => s.Action).ToList();

        // Top-level calls: sync_certificates (bareword), then upload_to_testflight.
        Assert.That(actions, Does.Contain("sync_certificates"));
        Assert.That(actions, Does.Contain("upload_to_testflight"));

        // The sh(...) inside the nested `Dir.chdir(..) do` block is NOT a top-level step.
        Assert.That(actions, Does.Not.Contain("sh"));

        var testflight = beta.Steps.Single(s => s.Action == "upload_to_testflight");
        Assert.That(testflight.Tool, Is.EqualTo("pilot"));
        Assert.That(testflight.Params, Does.Contain("skip_waiting_for_build_processing: true"));
    }

    [Test]
    public void Ios_sync_certificates_step_maps_to_match()
    {
        var sync = Detail("ios.Fastfile", Platform.Ios, "sync_certificates");
        var match = sync.Steps.Single(s => s.Action == "match");
        Assert.That(match.Tool, Is.EqualTo("match"));
        Assert.That(match.Params, Does.Contain("type: \"appstore\""));
    }

    [Test]
    public void Ios_release_step_maps_to_deliver()
    {
        var release = Detail("ios.Fastfile", Platform.Ios, "release");
        var deliver = release.Steps.Single(s => s.Action == "upload_to_app_store");
        Assert.That(deliver.Tool, Is.EqualTo("deliver"));
    }

    [Test]
    public void Ios_screenshots_lane_with_nested_do_blocks_captures_whole_lane()
    {
        var shots = Detail("ios.Fastfile", Platform.Ios, "screenshots");

        // The lane contains an `if`, a `Dir.chdir(..) do`, and `devices.each do |..|`
        // nested block. The whole lane must be captured through its final `end`.
        Assert.That(shots.Source, Does.Contain("devices.each do |device|"));
        Assert.That(shots.Source, Does.Contain("organize_screenshots_for_app_store"));
        Assert.That(shots.Source.TrimEnd(), Does.EndWith("end"));
        // Must not bleed into the private lane that follows.
        Assert.That(shots.Source, Does.Not.Contain("private_lane :capture_screenshots_for_device"));

        // organize_screenshots_for_app_store is a top-level call (unknown tool → null).
        var organize = shots.Steps.Single(s => s.Action == "organize_screenshots_for_app_store");
        Assert.That(organize.Tool, Is.Null);
        // capture_screenshots_for_device is inside the each-block → not a top-level step.
        Assert.That(shots.Steps.Select(s => s.Action),
            Does.Not.Contain("capture_screenshots_for_device"));
    }

    [Test]
    public void Android_build_lane_is_present_with_nested_blocks()
    {
        var build = Detail("android.Fastfile", Platform.Android, "build");

        // `Dir.chdir(flutter_root) do ... end` nested block captured.
        Assert.That(build.Source, Does.Contain("Dir.chdir(flutter_root) do"));
        Assert.That(build.Source.TrimEnd(), Does.EndWith("end"));
        // `load_env_production` is a top-level assignment target → not a step.
        Assert.That(build.Steps.Select(s => s.Action), Does.Not.Contain("env"));
    }

    [Test]
    public void Android_internal_lane_steps_include_supply()
    {
        var internalLane = Detail("android.Fastfile", Platform.Android, "internal");
        var supply = internalLane.Steps.Single(s => s.Action == "upload_to_play_store");
        Assert.That(supply.Tool, Is.EqualTo("supply"));
        Assert.That(supply.Params, Does.Contain("track: \"internal\""));

        // `build` (bareword call) is also a top-level step.
        Assert.That(internalLane.Steps.Select(s => s.Action), Does.Contain("build"));
    }

    [Test]
    public void Android_excludes_def_helpers_and_private_lane()
    {
        var text = File.ReadAllText("fixtures/android.Fastfile");
        var details = FastfileParser.ParseDetailed(text, Platform.Android);
        var names = details.Select(d => d.Lane.Name).ToList();

        Assert.That(names, Does.Contain("build"));
        Assert.That(names, Does.Contain("internal"));
        Assert.That(names, Does.Contain("production"));
        // Top-level `def load_env` helpers are not lanes.
        Assert.That(names, Does.Not.Contain("load_env"));
        Assert.That(names, Does.Not.Contain("flutter_root"));
        // Private lane excluded.
        Assert.That(names, Does.Not.Contain("organize_screenshots_for_play_store"));
    }

    [Test]
    public void ParseDetailed_is_robust_to_malformed_input()
    {
        Assert.That(FastfileParser.ParseDetailed("", Platform.Ios), Is.Empty);
        // A lane with no closing `end` shouldn't throw; it captures to EOF.
        var detail = FastfileParser.ParseDetailed("lane :x do\n  build\n", Platform.Ios);
        Assert.That(detail, Has.Count.EqualTo(1));
        Assert.That(detail[0].Lane.Name, Is.EqualTo("x"));
    }
}

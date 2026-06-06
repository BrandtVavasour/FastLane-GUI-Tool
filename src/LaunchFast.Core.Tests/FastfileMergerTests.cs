using LaunchFast.Core.Scaffolding;

namespace LaunchFast.Core.Tests;

public class FastfileMergerTests
{
    const string Existing =
"""
default_platform(:ios)

platform :ios do
  lane :beta do
    build_app
  end
end
""";

    [Test]
    public void Inserts_lane_before_platform_end()
    {
        var laneRuby = "  lane :release do\n    build_app\n  end";
        var merged = FastfileMerger.InsertLane(Existing, laneRuby, "ios");
        Assert.That(merged, Does.Contain("lane :release"));
        Assert.That(merged.IndexOf("lane :release"), Is.GreaterThan(merged.IndexOf("lane :beta")));
        Assert.That(merged.IndexOf("lane :release"), Is.LessThan(merged.LastIndexOf("end")));
        Assert.That(merged.TrimEnd().EndsWith("end"), Is.True);
    }

    [Test]
    public void Adds_platform_block_when_absent()
    {
        var androidBlock = "platform :android do\n  lane :build do\n    gradle(task: \"bundleRelease\")\n  end\nend";
        var merged = FastfileMerger.AddPlatformBlock(Existing, androidBlock);
        Assert.That(merged, Does.Contain("platform :ios do"));
        Assert.That(merged, Does.Contain("platform :android do"));
    }

    [Test]
    public void Insert_returns_unchanged_when_block_missing()
    {
        var merged = FastfileMerger.InsertLane("# empty\n", "  lane :x do\n  end", "ios");
        Assert.That(merged, Does.Not.Contain("lane :x"));   // signals caller to fall back to AddPlatformBlock
        Assert.That(FastfileMerger.HasPlatformBlock("# empty\n", "ios"), Is.False);
    }

    [Test]
    public void Has_platform_block_detects_it()
    {
        Assert.That(FastfileMerger.HasPlatformBlock(Existing, "ios"), Is.True);
        Assert.That(FastfileMerger.HasPlatformBlock(Existing, "android"), Is.False);
    }

    [Test]
    public void Insert_handles_nested_do_blocks_in_existing_lane()
    {
        const string withNested =
"""
platform :ios do
  lane :beta do
    Dir.chdir("..") do
      sh("flutter", "build")
    end
  end
end
""";
        var merged = FastfileMerger.InsertLane(withNested, "  lane :release do\n  end", "ios");
        // the new lane must land inside the platform block (after the nested-do lane), before the platform's end
        Assert.That(merged, Does.Contain("lane :release"));
        Assert.That(merged.IndexOf("lane :release"), Is.GreaterThan(merged.IndexOf("Dir.chdir")));
        var lines = merged.Replace("\r\n", "\n").Split('\n');
        Assert.That(lines[^1].Trim() == "end" || lines[^2].Trim() == "end", Is.True);
    }
}

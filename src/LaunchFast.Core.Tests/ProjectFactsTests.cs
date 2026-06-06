using LaunchFast.Core.Scaffolding;

namespace LaunchFast.Core.Tests;

public class ProjectFactsTests
{
    static void TempProject(out string root)
    {
        root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "ios", "Runner.xcodeproj"));
        Directory.CreateDirectory(Path.Combine(root, "android", "app"));
        File.WriteAllText(Path.Combine(root, "pubspec.yaml"), "name: demo_app\nversion: 2.3.1+7\n");
        File.WriteAllText(Path.Combine(root, "ios", "Runner.xcodeproj", "project.pbxproj"),
            "PRODUCT_BUNDLE_IDENTIFIER = com.acme.demo;");
        File.WriteAllText(Path.Combine(root, "android", "app", "build.gradle"),
            "android { defaultConfig { applicationId \"com.acme.demo_android\" } }");
    }

    [Test]
    public void Reads_bundle_id_package_name_version()
    {
        TempProject(out var root);
        var f = ProjectFacts.Read(root);
        Assert.That(f.IosBundleId, Is.EqualTo("com.acme.demo"));
        Assert.That(f.AndroidPackage, Is.EqualTo("com.acme.demo_android"));
        Assert.That(f.AppName, Is.EqualTo("demo_app"));
        Assert.That(f.Version, Is.EqualTo("2.3.1+7"));
    }

    [Test]
    public void Missing_sources_yield_nulls()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var f = ProjectFacts.Read(root);
        Assert.That(f.IosBundleId, Is.Null);
        Assert.That(f.AndroidPackage, Is.Null);
    }

    [Test]
    public void Falls_back_to_info_plist_when_pbxproj_uses_variable()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "ios", "Runner.xcodeproj"));
        Directory.CreateDirectory(Path.Combine(root, "ios", "Runner"));
        File.WriteAllText(Path.Combine(root, "ios", "Runner.xcodeproj", "project.pbxproj"),
            "PRODUCT_BUNDLE_IDENTIFIER = $(PRODUCT_BUNDLE_IDENTIFIER);");
        File.WriteAllText(Path.Combine(root, "ios", "Runner", "Info.plist"),
            "<key>CFBundleIdentifier</key>\n<string>com.acme.fromplist</string>");
        Assert.That(ProjectFacts.Read(root).IosBundleId, Is.EqualTo("com.acme.fromplist"));
    }
}

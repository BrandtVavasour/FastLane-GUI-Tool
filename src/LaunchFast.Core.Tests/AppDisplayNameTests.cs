using LaunchFast.Core.Models;
using LaunchFast.Core.Stores;

namespace LaunchFast.Core.Tests;

[TestFixture]
public sealed class AppDisplayNameTests
{
    string _root = null!;
    Project _project = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "lf-appname-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _project = new Project(
            Name: "demo",
            Path: _root,
            Version: "1.0.0",
            IosFastlaneDir: Path.Combine(_root, "ios", "fastlane"),
            AndroidFastlaneDir: Path.Combine(_root, "android", "fastlane"),
            HasMatchfile: false,
            IconPath: null);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* best effort */ }
    }

    static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    void WritePubspec(string name) =>
        Write(Path.Combine(_root, "pubspec.yaml"), $"name: {name}\nversion: 1.0.0+1\n");

    void WriteInfoPlist(string body) =>
        Write(Path.Combine(_root, "ios", "Runner", "Info.plist"),
            "<?xml version=\"1.0\"?>\n<plist version=\"1.0\">\n<dict>\n" + body + "\n</dict>\n</plist>\n");

    void WriteManifest(string label) =>
        Write(Path.Combine(_root, "android", "app", "src", "main", "AndroidManifest.xml"),
            $"<manifest>\n  <application android:label=\"{label}\">\n  </application>\n</manifest>\n");

    void WriteStrings(string name, string value) =>
        Write(Path.Combine(_root, "android", "app", "src", "main", "res", "values", "strings.xml"),
            $"<resources>\n  <string name=\"{name}\">{value}</string>\n</resources>\n");

    // ---- iOS -----------------------------------------------------------------

    [Test]
    public void Ios_uses_literal_display_name()
    {
        WritePubspec("vending_machine_tracker");
        WriteInfoPlist(
            "<key>CFBundleDisplayName</key>\n<string>Example App</string>\n" +
            "<key>CFBundleName</key>\n<string>example</string>");

        Assert.That(AppDisplayName.Read(_project, Platform.Ios), Is.EqualTo("Example App"));
    }

    [Test]
    public void Ios_falls_through_build_variable_to_bundle_name()
    {
        WritePubspec("vending_machine_tracker");
        WriteInfoPlist(
            "<key>CFBundleDisplayName</key>\n<string>$(PRODUCT_NAME)</string>\n" +
            "<key>CFBundleName</key>\n<string>Bundle Name</string>");

        Assert.That(AppDisplayName.Read(_project, Platform.Ios), Is.EqualTo("Bundle Name"));
    }

    [Test]
    public void Ios_falls_through_to_prettified_pubspec()
    {
        WritePubspec("vending_machine_tracker");
        // No Info.plist on disk.
        Assert.That(AppDisplayName.Read(_project, Platform.Ios),
            Is.EqualTo("Vending Machine Tracker"));
    }

    [Test]
    public void Ios_falls_through_when_both_plist_values_are_variables()
    {
        WritePubspec("my_cool_app");
        WriteInfoPlist(
            "<key>CFBundleDisplayName</key>\n<string>$(PRODUCT_NAME)</string>\n" +
            "<key>CFBundleName</key>\n<string>$(PRODUCT_NAME)</string>");

        Assert.That(AppDisplayName.Read(_project, Platform.Ios), Is.EqualTo("My Cool App"));
    }

    // ---- Android -------------------------------------------------------------

    [Test]
    public void Android_uses_literal_label()
    {
        WritePubspec("vending_machine_tracker");
        WriteManifest("Example App");

        Assert.That(AppDisplayName.Read(_project, Platform.Android), Is.EqualTo("Example App"));
    }

    [Test]
    public void Android_resolves_string_reference()
    {
        WritePubspec("vending_machine_tracker");
        WriteManifest("@string/app_name");
        WriteStrings("app_name", "Resolved Name");

        Assert.That(AppDisplayName.Read(_project, Platform.Android), Is.EqualTo("Resolved Name"));
    }

    [Test]
    public void Android_falls_through_to_prettified_pubspec_when_reference_unresolved()
    {
        WritePubspec("vending_machine_tracker");
        WriteManifest("@string/app_name");
        // No strings.xml → fall through.
        Assert.That(AppDisplayName.Read(_project, Platform.Android),
            Is.EqualTo("Vending Machine Tracker"));
    }

    // ---- nothing -------------------------------------------------------------

    [Test]
    public void Returns_null_when_nothing_available()
    {
        // No pubspec, no native config.
        Assert.That(AppDisplayName.Read(_project, Platform.Ios), Is.Null);
        Assert.That(AppDisplayName.Read(_project, Platform.Android), Is.Null);
    }

    [Test]
    public void Prettify_handles_kebab_and_snake_case()
    {
        WritePubspec("some-kebab-app");
        Assert.That(AppDisplayName.Read(_project, Platform.Ios), Is.EqualTo("Some Kebab App"));
    }
}

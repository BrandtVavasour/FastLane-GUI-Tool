namespace LaunchFast.App.Tests;

public static class TestProjects
{
    /// <summary>
    /// Creates a temp Flutter project directory (pubspec.yaml with version,
    /// ios/fastlane + android/fastlane dirs, and an iOS Matchfile) and returns the root path.
    /// </summary>
    public static string MakeFlutterProject(string name = "demo")
    {
        var root = Path.Combine(Path.GetTempPath(), "lf-app-" + Guid.NewGuid().ToString("N"), name);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "pubspec.yaml"), "name: demo\nversion: 1.2.3+9\n");
        Directory.CreateDirectory(Path.Combine(root, "ios", "fastlane"));
        Directory.CreateDirectory(Path.Combine(root, "android", "fastlane"));
        File.WriteAllText(Path.Combine(root, "ios", "fastlane", "Matchfile"), "type(\"appstore\")");
        return root;
    }
}

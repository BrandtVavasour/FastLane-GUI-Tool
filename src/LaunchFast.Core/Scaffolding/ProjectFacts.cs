using System.Text.RegularExpressions;

namespace LaunchFast.Core.Scaffolding;

public sealed partial record ProjectFacts(string? IosBundleId, string? AndroidPackage, string? AppName, string? Version)
{
    public static ProjectFacts Read(string root)
    {
        string? bundle = FirstMatch(Path.Combine(root, "ios", "Runner.xcodeproj", "project.pbxproj"),
            @"PRODUCT_BUNDLE_IDENTIFIER\s*=\s*([A-Za-z0-9_.$()-]+)\s*;");
        if (bundle is null || bundle.Contains('$'))
            bundle = PlistString(Path.Combine(root, "ios", "Runner", "Info.plist"), "CFBundleIdentifier") ?? bundle;

        string? pkg = FirstMatch(Path.Combine(root, "android", "app", "build.gradle"),
            @"applicationId\s+[""']([A-Za-z0-9_.]+)[""']")
            ?? FirstMatch(Path.Combine(root, "android", "app", "build.gradle.kts"),
                @"applicationId\s*=?\s*[""']([A-Za-z0-9_.]+)[""']");

        string? name = FirstMatch(Path.Combine(root, "pubspec.yaml"), @"^name:\s*(\S+)");
        string? version = FirstMatch(Path.Combine(root, "pubspec.yaml"), @"^version:\s*(\S+)");
        return new ProjectFacts(bundle, pkg, name, version);
    }

    static string? FirstMatch(string path, string pattern)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var m = Regex.Match(File.ReadAllText(path), pattern, RegexOptions.Multiline);
            return m.Success ? m.Groups[1].Value : null;
        }
        catch { return null; }
    }

    static string? PlistString(string path, string key)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var m = Regex.Match(File.ReadAllText(path),
                $@"<key>{Regex.Escape(key)}</key>\s*<string>([^<]*)</string>");
            var v = m.Success ? m.Groups[1].Value : null;
            return string.IsNullOrWhiteSpace(v) || v.Contains("$(") ? null : v;
        }
        catch { return null; }
    }
}

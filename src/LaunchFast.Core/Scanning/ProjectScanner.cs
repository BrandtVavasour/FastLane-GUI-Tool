using System.Text.RegularExpressions;
using LaunchFast.Core.Models;

namespace LaunchFast.Core.Scanning;

public static class ProjectScanner
{
    public static Project? TryScanRoot(string root)
    {
        var iosFl = Path.Combine(root, "ios", "fastlane");
        var androidFl = Path.Combine(root, "android", "fastlane");
        bool hasIos = Directory.Exists(iosFl);
        bool hasAndroid = Directory.Exists(androidFl);
        if (!hasIos && !hasAndroid) return null;

        var version = ReadPubspecVersion(Path.Combine(root, "pubspec.yaml"));
        bool match = File.Exists(Path.Combine(iosFl, "Matchfile"));

        return new Project(
            Name: new DirectoryInfo(root).Name,
            Path: root,
            Version: version,
            IosFastlaneDir: hasIos ? iosFl : null,
            AndroidFastlaneDir: hasAndroid ? androidFl : null,
            HasMatchfile: match,
            IconPath: null);
    }

    public static IReadOnlyList<Project> ScanWorkspace(string workspaceDir)
    {
        if (!Directory.Exists(workspaceDir)) return [];
        var result = new List<Project>();
        foreach (var child in Directory.EnumerateDirectories(workspaceDir))
        {
            var p = TryScanRoot(child);
            if (p is not null) result.Add(p);
        }
        return result;
    }

    static string? ReadPubspecVersion(string pubspecPath)
    {
        if (!File.Exists(pubspecPath)) return null;
        foreach (var line in File.ReadLines(pubspecPath))
        {
            var m = Regex.Match(line, @"^version:\s*(?<v>\S+)");
            if (m.Success) return m.Groups["v"].Value;
        }
        return null;
    }
}

namespace LaunchFast.Core.Icons;

public static class IconExtractor
{
    public static string? Resolve(string projectRoot)
    {
        var candidates = new List<string>();

        var iconset = Path.Combine(projectRoot, "ios", "Runner", "Assets.xcassets", "AppIcon.appiconset");
        if (Directory.Exists(iconset))
            candidates.AddRange(Directory.EnumerateFiles(iconset, "*.png"));

        var res = Path.Combine(projectRoot, "android", "app", "src", "main", "res");
        if (Directory.Exists(res))
            candidates.AddRange(Directory.EnumerateFiles(res, "ic_launcher*.png", SearchOption.AllDirectories));

        return candidates.Count == 0
            ? null
            : candidates.MaxBy(f => new FileInfo(f).Length);
    }
}

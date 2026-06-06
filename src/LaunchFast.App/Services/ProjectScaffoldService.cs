using LaunchFast.Core.Env;
using LaunchFast.Core.Running;
using LaunchFast.Core.Scaffolding;

namespace LaunchFast.App.Services;

public sealed class ProjectScaffoldService(ISecretStore secrets, IPtyFactory pty, string projectId)
{
    public event Action<string>? Output;

    public async Task ApplyAsync(ScaffoldPlan plan, string root, CancellationToken ct = default)
    {
        foreach (var f in plan.Files)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(f.Path)!);
            await File.WriteAllTextAsync(f.Path, f.NewContent, ct);
        }

        foreach (var s in plan.Secrets)
            secrets.Set(projectId, s.Key, s.Value);

        foreach (var dir in PlatformDirs(plan, root))
            await BundleInstall(dir);
    }

    static IEnumerable<string> PlatformDirs(ScaffoldPlan plan, string root)
    {
        var sep = Path.DirectorySeparatorChar;
        var dirs = new List<string>();
        if (plan.Files.Any(f => f.Path.Contains($"{sep}ios{sep}", StringComparison.Ordinal)
                             || f.Path.Contains("/ios/", StringComparison.Ordinal)))
            dirs.Add(Path.Combine(root, "ios"));
        if (plan.Files.Any(f => f.Path.Contains($"{sep}android{sep}", StringComparison.Ordinal)
                             || f.Path.Contains("/android/", StringComparison.Ordinal)))
            dirs.Add(Path.Combine(root, "android"));
        return dirs;
    }

    Task BundleInstall(string platformDir)
    {
        var tcs = new TaskCompletionSource();
        var baseEnv = Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .Where(e => e.Key is string && e.Value is string)
            .ToDictionary(e => (string)e.Key, e => (string)e.Value!, StringComparer.Ordinal);
        var proc = pty.Start("bundle", ["install"], platformDir, baseEnv);
        proc.OutputReceived += s => Output?.Invoke(s);
        proc.Exited += code =>
        {
            if (code != 0)
                Output?.Invoke($"bundle install failed (exit {code}) in {platformDir}");
            tcs.TrySetResult();
        };
        return tcs.Task;
    }
}

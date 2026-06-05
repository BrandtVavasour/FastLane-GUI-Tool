namespace LaunchFast.Core.Running;

public sealed record PreflightResult(bool Ok, string Message);

public static class Preflight
{
    public static PreflightResult CheckGemfile(string workingDir) =>
        File.Exists(Path.Combine(workingDir, "Gemfile"))
            ? new(true, "Gemfile present")
            : new(false, "No Gemfile in this platform directory — fastlane can't run via bundler.");

    public static PreflightResult CheckTool(string tool) =>
        FindOnPath(tool) is not null
            ? new(true, $"{tool} found")
            : new(false, $"`{tool}` not found on PATH.");

    static string? FindOnPath(string tool)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':'))
        {
            var candidate = Path.Combine(dir, tool);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}

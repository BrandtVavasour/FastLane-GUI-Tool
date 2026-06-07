using System.Text.RegularExpressions;
using LaunchFast.Core.Models;

namespace LaunchFast.Core.Env;

/// <summary>
/// The required-secret set + file-sourced values for a project, computed by
/// scanning its fastlane config and reading its <c>.env*</c> files.
/// </summary>
public sealed record ProjectSecretScan(
    IReadOnlyList<string> RequiredSecrets,
    IReadOnlyDictionary<string, string> FromFiles);

/// <summary>
/// Scans a project's fastlane configuration (Fastfile/Appfile/Matchfile under the
/// iOS and Android fastlane dirs) for <c>ENV["NAME"]</c> references, filters them
/// to genuine secrets via <see cref="SecretEnvFilter"/>, and reads the project's
/// <c>.env*</c> files plus <c>scripts/deploy-env.sh</c> into a merged dictionary.
///
/// Shared by the project detail (Lanes) screen and the Secrets section so both
/// compute the same required set and file-sourced values from one place.
/// </summary>
public static partial class ProjectSecretScanner
{
    [GeneratedRegex("""ENV\[\s*['"](?<k>[A-Z0-9_]+)['"]""")]
    private static partial Regex EnvRefRegex();

    /// <summary>
    /// Computes the required secret names and file-sourced values for the project.
    /// </summary>
    public static ProjectSecretScan Scan(Project project)
    {
        var refs = new SortedSet<string>(StringComparer.Ordinal);

        CollectFastlaneRefs(project.IosFastlaneDir, refs);
        CollectFastlaneRefs(project.AndroidFastlaneDir, refs);

        var required = refs
            .Where(SecretEnvFilter.IsSecret)
            .ToList();

        var fromFiles = ReadEnvFiles(project.Path);
        return new ProjectSecretScan(required, fromFiles);
    }

    /// <summary>
    /// All <c>ENV[...]</c> names referenced under a fastlane dir's
    /// Fastfile/Appfile/Matchfile (genuine secrets and control vars alike).
    /// </summary>
    public static IReadOnlyCollection<string> CollectReferencedEnvVars(string? fastlaneDir)
    {
        var refs = new SortedSet<string>(StringComparer.Ordinal);
        CollectFastlaneRefs(fastlaneDir, refs);
        return refs;
    }

    static void CollectFastlaneRefs(string? fastlaneDir, SortedSet<string> into)
    {
        if (fastlaneDir is null || !Directory.Exists(fastlaneDir)) return;

        foreach (var name in new[] { "Fastfile", "Appfile", "Matchfile" })
        {
            var path = Path.Combine(fastlaneDir, name);
            if (File.Exists(path)) CollectEnvRefs(File.ReadAllText(path), into);
        }
    }

    static void CollectEnvRefs(string text, SortedSet<string> into)
    {
        foreach (Match m in EnvRefRegex().Matches(text))
            into.Add(m.Groups["k"].Value);
    }

    /// <summary>
    /// Reads the project's <c>.env*</c> files (and <c>scripts/deploy-env.sh</c>)
    /// into a merged dictionary. Last file wins on key collisions.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ReadEnvFiles(
        string projectRoot, Func<string, string?>? environmentLookup = null)
    {
        var merged = new Dictionary<string, EnvValue>(StringComparer.Ordinal);

        if (Directory.Exists(projectRoot))
        {
            foreach (var file in Directory.EnumerateFiles(projectRoot)
                         .Where(f => Path.GetFileName(f).StartsWith(".env", StringComparison.Ordinal))
                         .OrderBy(f => f, StringComparer.Ordinal))
            {
                Merge(merged, File.ReadAllText(file));
            }
        }

        var deployEnv = Path.Combine(projectRoot, "scripts", "deploy-env.sh");
        if (File.Exists(deployEnv)) Merge(merged, File.ReadAllText(deployEnv));

        // Expand $VAR / ${VAR} / leading ~ in expandable values the way fastlane's dotenv
        // does, so e.g. APP_STORE_CONNECT_API_KEY_PATH=$HOME/.appstoreconnect/api_key.json
        // resolves to a real path for every consumer — the run environment, store-status
        // credential discovery, and signing path lookups. Each value is expanded against
        // the raw file values first, then the process environment. Single-quoted values
        // are literal (never expanded). Pure string substitution (single pass, no
        // recursion / no shell) — see EnvExpander.
        var baseLookup = environmentLookup ?? Environment.GetEnvironmentVariable;
        var rawValues = merged.ToDictionary(kv => kv.Key, kv => kv.Value.Value, StringComparer.Ordinal);

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, ev) in merged)
        {
            result[key] = ev.Expandable
                ? EnvExpander.Expand(ev.Value,
                    name => rawValues.TryGetValue(name, out var v) ? v : baseLookup(name))
                : ev.Value;
        }

        return result;
    }

    static void Merge(Dictionary<string, EnvValue> into, string content)
    {
        foreach (var (k, v) in EnvFileReader.Parse(content)) into[k] = v;
    }
}

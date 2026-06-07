using LaunchFast.Core.Models;

namespace LaunchFast.Core.Env;

public sealed class EnvResolver(ISecretStore secrets)
{
    public EnvStatus Resolve(string projectId, IEnumerable<string> required,
        IReadOnlyDictionary<string, string> fromFiles)
    {
        var satisfied = new List<string>();
        var missing = new List<string>();
        foreach (var key in required)
        {
            if (fromFiles.ContainsKey(key) || secrets.Get(projectId, key) is not null)
                satisfied.Add(key);
            else
                missing.Add(key);
        }
        return new EnvStatus(satisfied, missing);
    }

    public IReadOnlyDictionary<string, string> BuildEnv(string projectId,
        IEnumerable<string> required, IReadOnlyDictionary<string, string> fromFiles,
        Func<string, string?>? environmentLookup = null)
    {
        var baseLookup = environmentLookup ?? Environment.GetEnvironmentVariable;

        var env = new Dictionary<string, string>(StringComparer.Ordinal);

        // File-sourced values are expanded like dotenv does ($VAR / ${VAR} / leading ~),
        // resolving against the other file values then the process environment — so e.g.
        // APP_STORE_CONNECT_API_KEY_PATH=$HOME/.appstoreconnect/api_key.json becomes a
        // real path before fastlane reads it. Keychain secrets are used verbatim (never
        // expanded, so a secret value containing '$' is preserved).
        foreach (var (k, v) in fromFiles)
        {
            env[k] = EnvExpander.Expand(v, name =>
                fromFiles.TryGetValue(name, out var fv) ? fv : baseLookup(name));
        }

        foreach (var key in required)
        {
            var s = secrets.Get(projectId, key);
            if (s is not null) env[key] = s;
        }

        return env;
    }
}

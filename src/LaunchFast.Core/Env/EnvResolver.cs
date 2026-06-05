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
        IEnumerable<string> required, IReadOnlyDictionary<string, string> fromFiles)
    {
        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (k, v) in fromFiles) env[k] = v;
        foreach (var key in required)
        {
            var s = secrets.Get(projectId, key);
            if (s is not null) env[key] = s;
        }
        return env;
    }
}

namespace LaunchFast.Core.Env;

public interface ISecretStore
{
    string? Get(string projectId, string key);
    void Set(string projectId, string key, string value);
}

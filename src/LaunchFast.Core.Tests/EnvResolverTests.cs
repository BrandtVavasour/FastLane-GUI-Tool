using LaunchFast.Core.Env;

namespace LaunchFast.Core.Tests;

public class EnvResolverTests
{
    sealed class FakeSecrets : ISecretStore
    {
        readonly Dictionary<string, string> _m = new();
        public string? Get(string p, string k) => _m.GetValueOrDefault($"{p}:{k}");
        public void Set(string p, string k, string v) => _m[$"{p}:{k}"] = v;
    }

    [Test]
    public void Missing_secret_reported_until_set()
    {
        var secrets = new FakeSecrets();
        var required = new[] { "API_URL", "MATCH_PASSWORD" };
        var fromFiles = new Dictionary<string, string> { ["API_URL"] = "https://x" };

        var resolver = new EnvResolver(secrets);
        var before = resolver.Resolve("proj", required, fromFiles);
        Assert.That(before.Missing, Is.EqualTo(new[] { "MATCH_PASSWORD" }));

        secrets.Set("proj", "MATCH_PASSWORD", "hunter2");
        var after = resolver.Resolve("proj", required, fromFiles);
        Assert.That(after.Missing, Is.Empty);
        Assert.That(after.Satisfied, Does.Contain("MATCH_PASSWORD"));
    }

    [Test]
    public void BuildEnv_merges_files_then_secrets()
    {
        var secrets = new FakeSecrets();
        secrets.Set("proj", "MATCH_PASSWORD", "s3cret");
        var resolver = new EnvResolver(secrets);
        var env = resolver.BuildEnv("proj",
            new[] { "API_URL", "MATCH_PASSWORD" },
            new Dictionary<string, string> { ["API_URL"] = "https://x" });
        Assert.That(env["API_URL"], Is.EqualTo("https://x"));
        Assert.That(env["MATCH_PASSWORD"], Is.EqualTo("s3cret"));
    }
}

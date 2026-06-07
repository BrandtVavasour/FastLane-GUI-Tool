using LaunchFast.Core.Env;

namespace LaunchFast.Core.Tests;

public class EnvExpanderTests
{
    static string? Lookup(string name) => name switch
    {
        "HOME" => "/Users/dev",
        "API_URL" => "https://api.example.com",
        _ => null,
    };

    [Test]
    public void Expands_dollar_var()
    {
        Assert.That(
            EnvExpander.Expand("$HOME/.appstoreconnect/api_key.json", Lookup),
            Is.EqualTo("/Users/dev/.appstoreconnect/api_key.json"));
    }

    [Test]
    public void Expands_braced_var()
    {
        Assert.That(
            EnvExpander.Expand("${HOME}/keys", Lookup),
            Is.EqualTo("/Users/dev/keys"));
    }

    [Test]
    public void Expands_leading_tilde()
    {
        Assert.That(
            EnvExpander.Expand("~/.appstoreconnect/api_key.json", Lookup),
            Is.EqualTo("/Users/dev/.appstoreconnect/api_key.json"));
    }

    [Test]
    public void Unknown_var_is_left_untouched()
    {
        Assert.That(
            EnvExpander.Expand("$NOPE/x", Lookup),
            Is.EqualTo("$NOPE/x"));
    }

    [Test]
    public void Value_without_refs_is_unchanged()
    {
        Assert.That(
            EnvExpander.Expand("https://api.example.com", Lookup),
            Is.EqualTo("https://api.example.com"));
    }

    [Test]
    public void Multiple_refs_expand()
    {
        Assert.That(
            EnvExpander.Expand("$HOME/$API_URL", Lookup),
            Is.EqualTo("/Users/dev/https://api.example.com"));
    }
}

using LaunchFast.Core.Env;

namespace LaunchFast.Core.Tests;

public class EnvFileReaderTests
{
    [Test]
    public void Parses_plain_and_export_and_quoted()
    {
        var content = """
            # comment
            API_URL=https://x
            export MATCH_GIT_URL="https://git"
            API_TOKEN='abc'
            """;
        var vars = EnvFileReader.Parse(content);
        Assert.That(vars["API_URL"].Value, Is.EqualTo("https://x"));
        Assert.That(vars["MATCH_GIT_URL"].Value, Is.EqualTo("https://git"));
        Assert.That(vars["API_TOKEN"].Value, Is.EqualTo("abc"));
    }

    [Test]
    public void Quoted_value_with_trailing_comment_keeps_only_the_quoted_part()
    {
        // The real-world deploy-env.sh shape that mis-parsed before.
        var vars = EnvFileReader.Parse(
            "export MATCH_PASSWORD=\"s3cret-pw\"  # note: explanation here");
        Assert.That(vars["MATCH_PASSWORD"].Value, Is.EqualTo("s3cret-pw"));
    }

    [Test]
    public void Unquoted_inline_comment_is_stripped()
    {
        var vars = EnvFileReader.Parse("API_URL=https://x   # trailing comment");
        Assert.That(vars["API_URL"].Value, Is.EqualTo("https://x"));
    }

    [Test]
    public void Hash_inside_unquoted_value_is_preserved()
    {
        // A '#' not preceded by whitespace is part of the value (e.g. a URL fragment).
        var vars = EnvFileReader.Parse("URL=https://x/page#section");
        Assert.That(vars["URL"].Value, Is.EqualTo("https://x/page#section"));
    }

    [Test]
    public void Single_quoted_values_are_not_marked_expandable()
    {
        var vars = EnvFileReader.Parse("LITERAL='$HOME/x'\nDQ=\"$HOME/x\"\nBARE=$HOME/x");
        Assert.That(vars["LITERAL"].Expandable, Is.False);
        Assert.That(vars["DQ"].Expandable, Is.True);
        Assert.That(vars["BARE"].Expandable, Is.True);
        // Parse itself does not expand — that happens in ReadEnvFiles.
        Assert.That(vars["LITERAL"].Value, Is.EqualTo("$HOME/x"));
    }
}

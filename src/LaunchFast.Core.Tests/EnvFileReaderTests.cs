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
        Assert.That(vars["API_URL"], Is.EqualTo("https://x"));
        Assert.That(vars["MATCH_GIT_URL"], Is.EqualTo("https://git"));
        Assert.That(vars["API_TOKEN"], Is.EqualTo("abc"));
    }
}

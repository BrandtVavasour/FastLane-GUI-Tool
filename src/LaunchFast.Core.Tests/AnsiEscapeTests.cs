using LaunchFast.Core.Running;

namespace LaunchFast.Core.Tests;

public class AnsiEscapeTests
{
    [Test]
    public void Strips_sgr_colour_codes()
    {
        Assert.That(
            AnsiEscape.Strip("\u001b[32m--- Step: match ---\u001b[0m"),
            Is.EqualTo("--- Step: match ---"));
    }

    [Test]
    public void Strips_cursor_and_clear_codes()
    {
        Assert.That(AnsiEscape.Strip("\u001b[2K\u001b[1Ghello"), Is.EqualTo("hello"));
    }

    [Test]
    public void Strips_osc_title_sequence()
    {
        Assert.That(AnsiEscape.Strip("\u001b]0;a title\u0007done"), Is.EqualTo("done"));
    }

    [Test]
    public void Plain_text_is_unchanged()
    {
        Assert.That(AnsiEscape.Strip("plain text 123"), Is.EqualTo("plain text 123"));
    }

    [Test]
    public void Empty_is_safe()
    {
        Assert.That(AnsiEscape.Strip(string.Empty), Is.EqualTo(string.Empty));
    }
}

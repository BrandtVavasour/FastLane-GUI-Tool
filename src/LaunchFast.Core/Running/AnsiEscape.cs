using System.Text.RegularExpressions;

namespace LaunchFast.Core.Running;

/// <summary>
/// Strips ANSI terminal escape sequences (SGR colour, cursor moves, OSC) from a line
/// of process output. fastlane and friends emit colour codes — especially over a real
/// pty — which the app's plain text view would otherwise show literally as
/// <c>\u001b[31m</c> / square boxes. Pure / total.
/// </summary>
public static partial class AnsiEscape
{
    // CSI (e.g. SGR colour "ESC[31m", cursor moves), OSC ("ESC]0;title BEL/ST"), and
    // other two-byte escape sequences. ESC = \u001b, BEL = \u0007, ST = ESC \.
    [GeneratedRegex(
        "\u001b\\[[0-9;?]*[ -/]*[@-~]" +
        "|\u001b\\][^\u0007\u001b]*(?:\u0007|\u001b\\\\)" +
        "|\u001b[@-Z\\\\-_]")]
    private static partial Regex AnsiRegex();

    public static string Strip(string text) =>
        string.IsNullOrEmpty(text) ? text : AnsiRegex().Replace(text, string.Empty);
}

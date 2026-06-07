namespace LaunchFast.Core.Env;

/// <summary>
/// A parsed <c>.env</c> value plus whether it is eligible for <c>$VAR</c> expansion.
/// Single-quoted values are literal (<see cref="Expandable"/> = false), matching how
/// dotenv / the shell treat them.
/// </summary>
public sealed record EnvValue(string Value, bool Expandable);

/// <summary>
/// Parses <c>KEY=value</c> lines from a <c>.env</c> file or a <c>deploy-env.sh</c>
/// (<c>export KEY=value</c>). Handles double/single quotes and inline <c>#</c> comments
/// the way a shell / dotenv does — so a line like
/// <c>export MATCH_PASSWORD="s3cret"  # note</c> yields <c>s3cret</c>, not
/// <c>s3cret"  # note</c>.
/// </summary>
public static class EnvFileReader
{
    public static IReadOnlyDictionary<string, EnvValue> Parse(string content)
    {
        var dict = new Dictionary<string, EnvValue>(StringComparer.Ordinal);
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith("export ")) line = line[7..].Trim();

            var eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var key = line[..eq].Trim();
            if (key.Length == 0) continue;

            dict[key] = ParseValue(line[(eq + 1)..]);
        }
        return dict;
    }

    static EnvValue ParseValue(string rawValue)
    {
        var s = rawValue.TrimStart();
        if (s.Length == 0) return new EnvValue(string.Empty, Expandable: true);

        var quote = s[0];
        if (quote is '"' or '\'')
        {
            // Quoted: value is the content up to the matching closing quote; anything
            // after it (e.g. a trailing comment) is ignored. Single quotes are literal.
            var end = s.IndexOf(quote, 1);
            var inner = end > 0 ? s[1..end] : s[1..];
            return new EnvValue(inner, Expandable: quote == '"');
        }

        // Unquoted: a '#' that starts the line or follows whitespace begins a comment
        // (so a '#' inside a value, like a URL fragment, is preserved). Trim the rest.
        var comment = CommentStart(s);
        var value = (comment >= 0 ? s[..comment] : s).TrimEnd();
        return new EnvValue(value, Expandable: true);
    }

    static int CommentStart(string s)
    {
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '#' && (i == 0 || char.IsWhiteSpace(s[i - 1])))
            {
                return i;
            }
        }
        return -1;
    }
}

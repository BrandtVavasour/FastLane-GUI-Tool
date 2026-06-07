using System.Text.RegularExpressions;

namespace LaunchFast.Core.Env;

/// <summary>
/// Expands shell-style variable references in <c>.env</c> values the way fastlane's
/// <c>dotenv</c> does, so a value like <c>$HOME/.appstoreconnect/api_key.json</c>
/// resolves to a real path before we hand it to the run process. Without this, a
/// literal <c>$HOME</c> reaches fastlane (e.g. <c>match</c>'s <c>api_key_path</c>) and
/// the file can't be found.
///
/// <para>Supports <c>$VAR</c>, <c>${VAR}</c> and a leading <c>~/</c>. Names that the
/// lookup can't resolve are left untouched (we never blank out data). Pure / total.</para>
/// </summary>
public static partial class EnvExpander
{
    [GeneratedRegex(@"\$\{(?<n>[A-Za-z_][A-Za-z0-9_]*)\}|\$(?<n2>[A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex VarRefRegex();

    /// <summary>
    /// Expands variable references in <paramref name="value"/> using
    /// <paramref name="lookup"/> (returns null for unknown names). A leading
    /// <c>~</c>/<c>~/</c> expands to <c>$HOME</c>.
    /// </summary>
    public static string Expand(string value, Func<string, string?> lookup)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var result = value;

        // Leading ~ → $HOME (shell path convention).
        if (result == "~" || result.StartsWith("~/", StringComparison.Ordinal))
        {
            var home = lookup("HOME");
            if (!string.IsNullOrEmpty(home))
            {
                result = home + result[1..];
            }
        }

        return VarRefRegex().Replace(result, m =>
        {
            var name = m.Groups["n"].Success ? m.Groups["n"].Value : m.Groups["n2"].Value;
            return lookup(name) ?? m.Value;
        });
    }
}

namespace LaunchFast.Core.Env;

/// <summary>
/// Decides whether an ENV var referenced by a fastlane config is a genuine
/// secret (something a user must supply via the secrets store) versus a
/// non-secret control/config var (CI, FASTLANE_ENV, locales, etc.) that must
/// never gate a run.
/// </summary>
public static class SecretEnvFilter
{
    // Always treated as secret when referenced.
    static readonly HashSet<string> KnownSecrets = new(StringComparer.OrdinalIgnoreCase)
    {
        "MATCH_PASSWORD",
        "MATCH_GIT_URL",
        "MATCH_KEYCHAIN_PASSWORD",
        "APPLE_ID",
        "ITC_TEAM_ID",
        "APP_STORE_CONNECT_API_KEY_PATH",
        "API_TOKEN",
        "FASTLANE_PASSWORD",
        "FASTLANE_SESSION",
    };

    // Pure control/config vars: never secrets even if a suffix/substring rule
    // would otherwise match (e.g. MATCH_KEYCHAIN_NAME ends with nothing secret,
    // but is excluded explicitly for clarity and safety).
    static readonly HashSet<string> ControlVars = new(StringComparer.OrdinalIgnoreCase)
    {
        "CI",
        "FASTLANE_ENV",
        "FLUTTER_LOCALE",
        "MATCH_KEYCHAIN_NAME",
        "LANG",
        "LC_ALL",
    };

    static readonly string[] SecretSuffixes =
    {
        "_PASSWORD", "_TOKEN", "_SECRET", "_KEY_PATH", "_KEY",
    };

    static readonly string[] SecretSubstrings =
    {
        "PASSWORD", "SECRET", "TOKEN",
    };

    /// <summary>
    /// True when <paramref name="name"/> is a genuine secret that should gate a run.
    /// </summary>
    public static bool IsSecret(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        var upper = name.ToUpperInvariant();

        if (ControlVars.Contains(upper)) return false;
        if (KnownSecrets.Contains(upper)) return true;

        foreach (var suffix in SecretSuffixes)
            if (upper.EndsWith(suffix, StringComparison.Ordinal)) return true;

        foreach (var sub in SecretSubstrings)
            if (upper.Contains(sub, StringComparison.Ordinal)) return true;

        return false;
    }
}

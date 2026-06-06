using System.Text.RegularExpressions;

namespace LaunchFast.Core.Signing;

/// <summary>
/// A certificate fingerprint extracted from <c>keytool -list -v</c> output. The
/// <see cref="Type"/> is the normalised algorithm label (<c>SHA-1</c> / <c>SHA-256</c>),
/// the <see cref="Value"/> the colon-separated hex digest. <see cref="Alias"/> is the
/// keystore entry alias the fingerprint belongs to (null when keytool did not print one).
/// </summary>
public sealed record CertFingerprint(string Type, string Value, string? Alias = null);

/// <summary>
/// Reads SHA-1 / SHA-256 certificate fingerprints from an Android keystore by shelling
/// out to <c>keytool -list -v</c>. Total — never throws.
///
/// <list type="bullet">
/// <item><see cref="ParseKeytoolOutput"/> is a pure parser over the textual
/// <c>keytool -list -v</c> report (fully unit-tested).</item>
/// <item><see cref="ReadKeystoreFingerprints"/> shells out to <c>keytool</c> via
/// <see cref="System.Diagnostics.ProcessStartInfo.ArgumentList"/> (so the store password
/// is passed as a discrete argument, never shell-interpolated or logged) and feeds the
/// captured output to the parser. It returns an empty list on any failure —
/// keytool-not-on-PATH, a missing keystore, a wrong/absent password, or a timeout. This
/// shell-out is the only run-time-only bit and is not unit-tested.</item>
/// </list>
/// </summary>
public static partial class KeystoreFingerprintReader
{
    // keytool prints an alias header earlier in each entry: `Alias name: upload`.
    [GeneratedRegex(@"^\s*Alias name:\s*(?<alias>.+?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex AliasRegex();

    // Fingerprint lines: `SHA1: A1:B2:...:90`, `SHA256: 5F:...:E0`, `MD5: ...`.
    // The label tolerates the `SHA-1` / `SHA1` spellings keytool has used over versions.
    [GeneratedRegex(
        @"^\s*(?<algo>MD5|SHA-?1|SHA-?256)\s*:\s*(?<value>(?:[0-9A-Fa-f]{2})(?::[0-9A-Fa-f]{2})+)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex FingerprintRegex();

    /// <summary>
    /// Pure parser of <c>keytool -list -v</c> output. Extracts every SHA-1 and SHA-256
    /// fingerprint (MD5 is ignored — it is not surfaced), tagging each with the most
    /// recent <c>Alias name:</c> seen above it. Returns an empty list when the text holds
    /// no parseable fingerprints (including empty / garbage input). Never throws.
    /// </summary>
    public static IReadOnlyList<CertFingerprint> ParseKeytoolOutput(string? keytoolOutput)
    {
        if (string.IsNullOrEmpty(keytoolOutput)) return Array.Empty<CertFingerprint>();

        // Build an ordered alias index so each fingerprint inherits the alias whose
        // header most recently preceded it.
        var aliases = AliasRegex().Matches(keytoolOutput)
            .Select(m => (Index: m.Index, Alias: m.Groups["alias"].Value.Trim()))
            .Where(a => a.Alias.Length > 0)
            .ToArray();

        var result = new List<CertFingerprint>();
        foreach (Match m in FingerprintRegex().Matches(keytoolOutput))
        {
            var type = NormaliseAlgorithm(m.Groups["algo"].Value);
            if (type is null) continue; // MD5 / unrecognised — skipped.

            string? alias = null;
            for (var i = aliases.Length - 1; i >= 0; i--)
            {
                if (aliases[i].Index < m.Index) { alias = aliases[i].Alias; break; }
            }

            result.Add(new CertFingerprint(type, m.Groups["value"].Value.ToUpperInvariant(), alias));
        }

        return result;
    }

    static string? NormaliseAlgorithm(string raw)
    {
        var compact = raw.Replace("-", "", StringComparison.Ordinal).ToUpperInvariant();
        return compact switch
        {
            "SHA1" => "SHA-1",
            "SHA256" => "SHA-256",
            _ => null, // MD5 and anything else are not surfaced.
        };
    }

    /// <summary>
    /// Reads the fingerprints from the keystore at <paramref name="keystorePath"/> by
    /// shelling out to <c>keytool -list -v -keystore &lt;path&gt; [-alias &lt;alias&gt;]
    /// [-storepass &lt;pw&gt;]</c>. The password is passed via
    /// <see cref="System.Diagnostics.ProcessStartInfo.ArgumentList"/> — never via a shell
    /// string and never logged. Returns an empty list on any failure (keytool absent,
    /// keystore missing, wrong/absent password, non-zero exit, timeout). Graceful — never
    /// throws.
    /// </summary>
    public static IReadOnlyList<CertFingerprint> ReadKeystoreFingerprints(
        string keystorePath, string? storePassword = null, string? alias = null)
    {
        if (string.IsNullOrWhiteSpace(keystorePath) || !File.Exists(keystorePath))
            return Array.Empty<CertFingerprint>();

        try
        {
            using var proc = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "keytool",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            var args = proc.StartInfo.ArgumentList;
            args.Add("-list");
            args.Add("-v");
            args.Add("-keystore");
            args.Add(keystorePath);
            if (!string.IsNullOrEmpty(alias))
            {
                args.Add("-alias");
                args.Add(alias);
            }
            if (!string.IsNullOrEmpty(storePassword))
            {
                // Passed as a discrete argument, not via a shell — the password is never
                // exposed on a command line a shell would parse, and is never logged.
                args.Add("-storepass");
                args.Add(storePassword);
            }

            if (!proc.Start()) return Array.Empty<CertFingerprint>();
            var output = proc.StandardOutput.ReadToEnd();
            if (!proc.WaitForExit(8000))
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return Array.Empty<CertFingerprint>();
            }

            return ParseKeytoolOutput(output);
        }
        catch
        {
            return Array.Empty<CertFingerprint>();
        }
    }
}

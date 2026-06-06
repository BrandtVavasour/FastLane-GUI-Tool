using LaunchFast.Core.Signing;

namespace LaunchFast.Core.Tests;

[TestFixture]
public sealed class KeystoreFingerprintReaderTests
{
    // A representative single-entry `keytool -list -v` report (fictional org/alias).
    const string SingleEntry =
        """
        Keystore type: PKCS12
        Keystore provider: SUN

        Your keystore contains 1 entry

        Alias name: upload
        Creation date: Jan 2, 2024
        Entry type: PrivateKeyEntry
        Certificate chain length: 1
        Certificate[1]:
        Owner: CN=Acme Robotics, OU=Mobile, O=Acme Robotics, L=Townsville, C=US
        Issuer: CN=Acme Robotics, OU=Mobile, O=Acme Robotics, L=Townsville, C=US
        Serial number: 1a2b3c4d
        Valid from: Tue Jan 02 09:00:00 UTC 2024 until: Sat Jan 02 09:00:00 UTC 2049
        Certificate fingerprints:
             MD5:  AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99
             SHA1: A1:B2:C3:D4:E5:F6:07:18:29:3A:4B:5C:6D:7E:8F:90:A1:B2:C3:D4
             SHA256: 5F:0E:1D:2C:3B:4A:59:68:77:86:95:A4:B3:C2:D1:E0:FF:0E:1D:2C:3B:4A:59:68:77:86:95:A4:B3:C2:D1:E0
        Signature algorithm name: SHA256withRSA
        Subject Public Key Algorithm: 2048-bit RSA key
        Version: 3
        """;

    [Test]
    public void Parses_sha1_and_sha256_with_alias()
    {
        var fps = KeystoreFingerprintReader.ParseKeytoolOutput(SingleEntry);

        Assert.That(fps, Has.Count.EqualTo(2), "MD5 is not surfaced");

        var sha1 = fps.Single(f => f.Type == "SHA-1");
        var sha256 = fps.Single(f => f.Type == "SHA-256");

        Assert.Multiple(() =>
        {
            Assert.That(sha1.Value, Is.EqualTo(
                "A1:B2:C3:D4:E5:F6:07:18:29:3A:4B:5C:6D:7E:8F:90:A1:B2:C3:D4"));
            Assert.That(sha1.Alias, Is.EqualTo("upload"));
            Assert.That(sha256.Value, Is.EqualTo(
                "5F:0E:1D:2C:3B:4A:59:68:77:86:95:A4:B3:C2:D1:E0:FF:0E:1D:2C:3B:4A:59:68:77:86:95:A4:B3:C2:D1:E0"));
            Assert.That(sha256.Alias, Is.EqualTo("upload"));
        });
    }

    [Test]
    public void Tolerates_hyphenated_sha_labels_and_lowercase_hex()
    {
        const string hyphenated =
            """
            Alias name: releasekey
            Certificate fingerprints:
                 SHA-1: ab:cd:ef:01:23:45:67:89:ab:cd:ef:01:23:45:67:89:ab:cd:ef:01
                 SHA-256: 11:22:33:44:55:66:77:88:99:aa:bb:cc:dd:ee:ff:00:11:22:33:44:55:66:77:88:99:aa:bb:cc:dd:ee:ff:00
            """;

        var fps = KeystoreFingerprintReader.ParseKeytoolOutput(hyphenated);

        var sha1 = fps.Single(f => f.Type == "SHA-1");
        Assert.Multiple(() =>
        {
            // Normalised to upper-case hex.
            Assert.That(sha1.Value, Is.EqualTo(
                "AB:CD:EF:01:23:45:67:89:AB:CD:EF:01:23:45:67:89:AB:CD:EF:01"));
            Assert.That(sha1.Alias, Is.EqualTo("releasekey"));
            Assert.That(fps.Any(f => f.Type == "SHA-256"), Is.True);
        });
    }

    [Test]
    public void Tags_each_fingerprint_with_its_own_entry_alias_for_multi_entry_keystores()
    {
        const string twoEntries =
            """
            Your keystore contains 2 entries

            Alias name: upload
            Certificate fingerprints:
                 SHA1: 01:01:01:01:01:01:01:01:01:01:01:01:01:01:01:01:01:01:01:01
                 SHA256: 02:02:02:02:02:02:02:02:02:02:02:02:02:02:02:02:02:02:02:02:02:02:02:02:02:02:02:02:02:02:02:02

            Alias name: appsigning
            Certificate fingerprints:
                 SHA1: 03:03:03:03:03:03:03:03:03:03:03:03:03:03:03:03:03:03:03:03
                 SHA256: 04:04:04:04:04:04:04:04:04:04:04:04:04:04:04:04:04:04:04:04:04:04:04:04:04:04:04:04:04:04:04:04
            """;

        var fps = KeystoreFingerprintReader.ParseKeytoolOutput(twoEntries);

        Assert.That(fps, Has.Count.EqualTo(4));
        var uploadSha1 = fps.Single(f => f.Type == "SHA-1" && f.Value.StartsWith("01:"));
        var appSha1 = fps.Single(f => f.Type == "SHA-1" && f.Value.StartsWith("03:"));
        Assert.Multiple(() =>
        {
            Assert.That(uploadSha1.Alias, Is.EqualTo("upload"));
            Assert.That(appSha1.Alias, Is.EqualTo("appsigning"));
        });
    }

    [Test]
    public void No_fingerprints_present_yields_empty()
    {
        const string noFingerprints =
            """
            Keystore type: PKCS12
            Alias name: upload
            Entry type: PrivateKeyEntry
            (the certificate fingerprints section is absent)
            """;

        Assert.That(KeystoreFingerprintReader.ParseKeytoolOutput(noFingerprints), Is.Empty);
    }

    [Test]
    public void Garbage_input_yields_empty()
    {
        Assert.Multiple(() =>
        {
            Assert.That(KeystoreFingerprintReader.ParseKeytoolOutput("not keytool output at all"), Is.Empty);
            Assert.That(KeystoreFingerprintReader.ParseKeytoolOutput(""), Is.Empty);
            Assert.That(KeystoreFingerprintReader.ParseKeytoolOutput(null), Is.Empty);
        });
    }

    [Test]
    public void Reader_returns_empty_for_a_nonexistent_keystore()
    {
        // The runtime shell-out short-circuits before invoking keytool for a missing
        // keystore — graceful, never throws.
        var path = Path.Combine(Path.GetTempPath(), "no-such-keystore-" + Guid.NewGuid().ToString("N") + ".jks");
        Assert.That(
            KeystoreFingerprintReader.ReadKeystoreFingerprints(path, "pw", "upload"),
            Is.Empty);
    }
}

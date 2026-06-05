using System.Security.Cryptography;
using System.Text.Json;
using LaunchFast.Core.Stores;

namespace LaunchFast.Core.Tests;

[TestFixture]
public sealed class AppStoreConnectFromKeyFileTests
{
    /// <summary>Writes a fastlane-shaped api_key.json with a freshly-generated,
    /// disposable EC P-256 key, returning its path.</summary>
    private static string WriteKeyFile()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pem = ecdsa.ExportPkcs8PrivateKeyPem();

        var json = JsonSerializer.Serialize(new
        {
            key_id = "ABC123DEFG",
            issuer_id = "11111111-2222-3333-4444-555555555555",
            key = pem,
        });

        var path = Path.Combine(Path.GetTempPath(), "asc-key-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, json);
        return path;
    }

    [Test]
    public void FromKeyFile_builds_client_that_signs_a_three_part_jwt()
    {
        var path = WriteKeyFile();
        try
        {
            using var client = AppStoreConnectClient.FromKeyFile(path);

            Assert.That(client, Is.Not.Null);
            var jwt = client!.CreateJwt();
            Assert.That(jwt.Split('.'), Has.Length.EqualTo(3));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void FromKeyFile_returns_null_for_missing_path() =>
        Assert.That(AppStoreConnectClient.FromKeyFile(
            Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid().ToString("N") + ".json")),
            Is.Null);

    [Test]
    public void FromKeyFile_returns_null_for_unparseable_file()
    {
        var path = Path.Combine(Path.GetTempPath(), "asc-bad-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, "not json at all");
        try
        {
            Assert.That(AppStoreConnectClient.FromKeyFile(path), Is.Null);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

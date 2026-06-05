using System.Text;
using System.Text.Json;
using LaunchFast.Core.Models;
using LaunchFast.Core.Stores;

namespace LaunchFast.Core.Tests;

[TestFixture]
public sealed class AppStoreConnectClientTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", name));

    [Test]
    public void MapAppStoreVersions_reports_live_and_in_review()
    {
        var status = AppStoreConnectClient.MapAppStoreVersions(Fixture("asc-appstoreversions.json"));

        Assert.Multiple(() =>
        {
            Assert.That(status.Destination, Is.EqualTo(Destination.AppStore));
            Assert.That(status.Available, Is.True);
            Assert.That(status.Line, Is.EqualTo("1.4.1 live"));
            Assert.That(status.Secondary, Is.EqualTo("1.4.2 in review"));
        });
    }

    [Test]
    public void MapTestFlight_reports_top_build()
    {
        var status = AppStoreConnectClient.MapTestFlight(Fixture("asc-builds.json"));

        Assert.Multiple(() =>
        {
            Assert.That(status.Destination, Is.EqualTo(Destination.TestFlight));
            Assert.That(status.Available, Is.True);
            Assert.That(status.Line, Does.Contain("17"));
        });
    }

    [Test]
    public void MapAppStoreVersions_returns_total_status_on_empty()
    {
        var status = AppStoreConnectClient.MapAppStoreVersions("""{"data":[]}""");

        Assert.Multiple(() =>
        {
            Assert.That(status.Available, Is.True);
            Assert.That(status.Line, Is.Null);
            Assert.That(status.Secondary, Is.Null);
        });
    }

    [Test]
    public void MapAppStoreVersions_does_not_throw_on_garbage()
    {
        var status = AppStoreConnectClient.MapAppStoreVersions("not json at all");
        Assert.That(status.Available, Is.True);
    }

    [Test]
    public void MapTestFlight_does_not_throw_on_empty()
    {
        var status = AppStoreConnectClient.MapTestFlight("""{"data":[]}""");
        Assert.That(status.Line, Is.Null);
    }

    [Test]
    public void CreateJwt_produces_three_part_token()
    {
        var client = AppStoreConnectClient.WithHandler(new StubHandler());

        var jwt = client.CreateJwt();
        var parts = jwt.Split('.');

        Assert.That(parts, Has.Length.EqualTo(3));

        var headerJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[0]));
        using var doc = JsonDocument.Parse(headerJson);
        Assert.That(doc.RootElement.GetProperty("alg").GetString(), Is.EqualTo("ES256"));
    }

    private static byte[] Base64UrlDecode(string s)
    {
        var b = s.Replace('-', '+').Replace('_', '/');
        switch (b.Length % 4)
        {
            case 2: b += "=="; break;
            case 3: b += "="; break;
        }
        return Convert.FromBase64String(b);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""{"data":[]}"""),
            });
    }
}

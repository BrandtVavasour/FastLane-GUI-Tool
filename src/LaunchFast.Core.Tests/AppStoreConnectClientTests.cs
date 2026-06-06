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
    public void MapBuildsDetailed_maps_newest_build()
    {
        var build = AppStoreConnectClient.MapBuildsDetailed(Fixture("asc-builds-detailed.json"));

        Assert.That(build, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(build!.Version, Is.EqualTo("1.4.2"));
            Assert.That(build.BuildNumber, Is.EqualTo("18"));
            Assert.That(build.ProcessingState, Is.EqualTo("VALID"));
            Assert.That(build.ExpiredCompliance, Is.False);
            Assert.That(build.ExpiresText, Does.Contain("expires in"));
            Assert.That(build.WhatsToTest, Does.Contain("onboarding"));
        });
    }

    [Test]
    public void MapBuildsDetailed_returns_null_on_empty_or_garbage()
    {
        Assert.That(AppStoreConnectClient.MapBuildsDetailed("""{"data":[]}"""), Is.Null);
        Assert.That(AppStoreConnectClient.MapBuildsDetailed("not json"), Is.Null);
    }

    [Test]
    public void MapBetaGroups_maps_internal_external_and_counts()
    {
        var groups = AppStoreConnectClient.MapBetaGroups(Fixture("asc-betagroups.json"));

        Assert.That(groups, Has.Count.EqualTo(3));

        var internalGroup = groups.Single(g => g.IsInternal);
        var external = groups.First(g => !g.IsInternal);
        var noMeta = groups.Single(g => g.Name == "Early Access");

        Assert.Multiple(() =>
        {
            Assert.That(internalGroup.TesterCount, Is.EqualTo(2));
            Assert.That(external.Name, Is.EqualTo("Beta Crew"));
            Assert.That(external.TesterCount, Is.EqualTo(3));
            Assert.That(noMeta.TesterCount, Is.EqualTo(0));
        });
    }

    [Test]
    public void MapBetaGroups_does_not_throw_on_garbage()
    {
        Assert.That(AppStoreConnectClient.MapBetaGroups("not json"), Is.Empty);
    }

    [Test]
    public void MapBetaTesters_maps_names_emails_and_normalised_states()
    {
        var testers = AppStoreConnectClient.MapBetaTesters(Fixture("asc-betatesters.json"));

        Assert.That(testers, Has.Count.EqualTo(4));
        Assert.Multiple(() =>
        {
            Assert.That(testers[0].FirstName, Is.EqualTo("Ada"));
            Assert.That(testers[0].Email, Is.EqualTo("ada@example.com"));
            Assert.That(testers[0].State, Is.EqualTo("Installed"));
            Assert.That(testers[1].State, Is.EqualTo("Accepted"));
            Assert.That(testers[2].State, Is.EqualTo("Invited"));
            // No "state" attribute → falls back to inviteType (PUBLIC_LINK).
            Assert.That(testers[3].State, Is.EqualTo("Public link"));
        });
    }

    [Test]
    public void MapBetaTesters_does_not_throw_on_garbage()
    {
        Assert.That(AppStoreConnectClient.MapBetaTesters("not json"), Is.Empty);
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

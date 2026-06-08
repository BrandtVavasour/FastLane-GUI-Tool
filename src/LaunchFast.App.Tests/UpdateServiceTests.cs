using System.Net;
using LaunchFast.App.Services;

namespace LaunchFast.App.Tests;

public class UpdateServiceTests
{
    sealed class StubHandler(HttpStatusCode code, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(code)
            {
                Content = new StringContent(body),
            });
    }

    static UpdateService Make(HttpStatusCode code, string body, string current) =>
        new(new HttpClient(new StubHandler(code, body)), currentVersion: current);

    [Test]
    public async Task Returns_release_when_newer()
    {
        var svc = Make(HttpStatusCode.OK,
            """{ "tag_name": "v0.9.0", "html_url": "https://x/releases/tag/v0.9.0" }""",
            current: "0.1.0");

        var rel = await svc.CheckAsync();

        Assert.That(rel, Is.Not.Null);
        Assert.That(rel!.TagName, Is.EqualTo("v0.9.0"));
    }

    [Test]
    public async Task Returns_null_when_current_is_latest()
    {
        var svc = Make(HttpStatusCode.OK,
            """{ "tag_name": "v0.1.0", "html_url": "https://x" }""",
            current: "0.1.0");

        Assert.That(await svc.CheckAsync(), Is.Null);
    }

    [Test]
    public async Task Returns_null_on_http_error()
    {
        var svc = Make(HttpStatusCode.NotFound, "", current: "0.1.0");
        Assert.That(await svc.CheckAsync(), Is.Null);
    }
}

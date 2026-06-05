using LaunchFast.Core.Stores;

namespace LaunchFast.Core.Tests;

[TestFixture]
public sealed class AppfileReaderTests
{
    private const string IosAppfile =
        """
        app_identifier("au.com.jabtech.vendingMachineTracker")
        apple_id(ENV["APPLE_ID"])
        itc_team_id(ENV["ITC_TEAM_ID"])
        team_id("L24Z2PF77Z")
        """;

    private const string AndroidAppfile =
        """
        json_key_file(ENV["SUPPLY_JSON_KEY"] || "/home/brandy/vending-tracker.json")
        package_name("au.com.jabtech.vending_machine_tracker")
        """;

    [Test]
    public void AppIdentifier_reads_ios_bundle_id() =>
        Assert.That(AppfileReader.AppIdentifier(IosAppfile),
            Is.EqualTo("au.com.jabtech.vendingMachineTracker"));

    [Test]
    public void PackageName_reads_android_package() =>
        Assert.That(AppfileReader.PackageName(AndroidAppfile),
            Is.EqualTo("au.com.jabtech.vending_machine_tracker"));

    [Test]
    public void JsonKeyFile_reads_literal_path() =>
        Assert.That(AppfileReader.JsonKeyFile(AndroidAppfile),
            Is.EqualTo("/home/brandy/vending-tracker.json"));

    [Test]
    public void AppIdentifier_supports_single_quotes() =>
        Assert.That(AppfileReader.AppIdentifier("app_identifier('com.example.app')"),
            Is.EqualTo("com.example.app"));

    [Test]
    public void Returns_null_when_directive_absent()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AppfileReader.AppIdentifier(AndroidAppfile), Is.Null);
            Assert.That(AppfileReader.PackageName(IosAppfile), Is.Null);
            Assert.That(AppfileReader.JsonKeyFile(IosAppfile), Is.Null);
        });
    }
}

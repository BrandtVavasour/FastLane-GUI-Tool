using LaunchFast.App.ViewModels;
using LaunchFast.Core.Models;

namespace LaunchFast.App.Tests;

public class AndroidSigningSectionViewModelTests
{
    static AndroidSigningSectionViewModel Make(
        Project project,
        FakeSecretStore? store = null,
        bool hasBuildLane = true,
        Action<Platform, string>? runLane = null,
        Func<string, string?>? env = null) =>
        new(project, store ?? new FakeSecretStore(),
            runLane, () => hasBuildLane, env ?? (_ => null));

    [Test]
    public void Surfaces_real_gradle_signing_config_from_disk()
    {
        var project = TestProjects.MakeProjectWithAndroidSigning();
        var vm = Make(project);

        Assert.That(vm.HasAndroid, Is.True);
        Assert.That(vm.HasGradleConfig, Is.True);
        Assert.That(vm.PackageName, Is.EqualTo("com.jabtech.vmt"));

        var rows = vm.GradleRows.ToDictionary(r => r.Key, r => r.Value);
        Assert.Multiple(() =>
        {
            // storeFile/keyAlias reference key.properties entries → captured key names.
            Assert.That(rows["storeFile"], Is.EqualTo("storeFile"));
            Assert.That(rows["keyAlias"], Is.EqualTo("keyAlias"));
            Assert.That(rows["storeType"], Is.EqualTo("PKCS12"));
            Assert.That(rows["signingConfig release"], Is.EqualTo("applied"));
        });

        // key.properties presence is real and names the declared keys.
        Assert.That(vm.HasKeyProperties, Is.True);
        Assert.That(vm.KeyPropertiesText, Does.Contain("storePassword"));
    }

    [Test]
    public void Credential_presence_reflects_the_secret_store_and_source()
    {
        var project = TestProjects.MakeProjectWithAndroidSigning();
        var store = new FakeSecretStore();
        store.Set(project.Path, "KEYSTORE_PASSWORD", "kpw");
        // KEY_PASSWORD intentionally missing; PLAY_JSON_KEY via env (CI secret).

        var vm = Make(project, store,
            env: name => name == "PLAY_JSON_KEY" ? "/play.json" : null);

        var cred = vm.Credentials.ToDictionary(c => c.Name);

        Assert.Multiple(() =>
        {
            Assert.That(cred["KEYSTORE_PASSWORD"].IsSet, Is.True);
            Assert.That(cred["KEYSTORE_PASSWORD"].SourceText, Is.EqualTo("Keychain"));

            Assert.That(cred["KEY_PASSWORD"].IsMissing, Is.True);
            Assert.That(cred["KEY_PASSWORD"].StatusText, Is.EqualTo("Missing"));

            Assert.That(cred["PLAY_JSON_KEY"].IsSet, Is.True);
            Assert.That(cred["PLAY_JSON_KEY"].SourceText, Is.EqualTo("CI secret"));
        });
    }

    [Test]
    public void Play_key_resolves_under_alternative_env_var_name()
    {
        var project = TestProjects.MakeProjectWithAndroidSigning();
        var vm = Make(project,
            env: name => name == "SUPPLY_JSON_KEY" ? "/supply.json" : null);

        var play = vm.Credentials.Single(c => c.IsSet && c.Description.Contains("Play"));
        Assert.That(play.Name, Is.EqualTo("SUPPLY_JSON_KEY"));
        Assert.That(play.SourceText, Is.EqualTo("CI secret"));
    }

    [Test]
    public void Build_aab_runs_the_android_build_lane_when_present()
    {
        var project = TestProjects.MakeProjectWithAndroidSigning(withBuildLane: true);
        Platform? ranPlatform = null;
        string? ranLane = null;

        var vm = Make(project, hasBuildLane: true,
            runLane: (p, l) => { ranPlatform = p; ranLane = l; });

        Assert.That(vm.CanBuildAab, Is.True);
        vm.BuildAabCommand.Execute(null);

        Assert.That(ranPlatform, Is.EqualTo(Platform.Android));
        Assert.That(ranLane, Is.EqualTo("build"));
    }

    [Test]
    public void Build_aab_disabled_and_inert_without_a_build_lane()
    {
        var project = TestProjects.MakeProjectWithAndroidSigning(withBuildLane: false);
        var ran = false;

        var vm = Make(project, hasBuildLane: false, runLane: (_, _) => ran = true);

        Assert.That(vm.CanBuildAab, Is.False);
        vm.BuildAabCommand.Execute(null);
        Assert.That(ran, Is.False);
    }

    [Test]
    public void Empty_state_for_a_project_without_android()
    {
        // iOS-only project (no android dir).
        var root = TestProjects.MakeFlutterProject();
        Directory.Delete(Path.Combine(root, "android"), recursive: true);
        var project = LaunchFast.Core.Scanning.ProjectScanner.TryScanRoot(root)!;

        var vm = Make(project);

        Assert.Multiple(() =>
        {
            Assert.That(vm.HasAndroid, Is.False);
            Assert.That(vm.HasNoAndroid, Is.True);
            Assert.That(vm.HasGradleConfig, Is.False);
        });
    }

    [Test]
    public void Illustrative_blocks_are_flagged_and_populated()
    {
        var project = TestProjects.MakeProjectWithAndroidSigning();
        var vm = Make(project);

        Assert.Multiple(() =>
        {
            Assert.That(vm.IsPlaceholder, Is.True);
            Assert.That(vm.SigningKeys, Has.Count.EqualTo(2));
            Assert.That(vm.Fingerprints, Has.Count.EqualTo(3));
        });
    }
}

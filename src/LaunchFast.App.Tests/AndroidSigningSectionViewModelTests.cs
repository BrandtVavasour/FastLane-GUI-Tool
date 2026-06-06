using LaunchFast.App.ViewModels;
using LaunchFast.Core.Models;
using LaunchFast.Core.Signing;

namespace LaunchFast.App.Tests;

public class AndroidSigningSectionViewModelTests
{
    static AndroidSigningSectionViewModel Make(
        Project project,
        FakeSecretStore? store = null,
        bool hasBuildLane = true,
        Action<Platform, string>? runLane = null,
        Func<string, string?>? env = null,
        AndroidSigningSectionViewModel.FingerprintsSupplier? fingerprints = null) =>
        new(project, store ?? new FakeSecretStore(),
            runLane, () => hasBuildLane, env ?? (_ => null), fingerprints);

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
        });
    }

    [Test]
    public void Real_fingerprints_from_the_keystore_populate_the_panel()
    {
        // A resolvable keystore on disk + an injected supplier returning parsed
        // fingerprints (keytool is never invoked in tests).
        var project = TestProjects.MakeProjectWithAndroidSigning(withKeystoreFile: true);

        string? gotPath = null, gotPass = null, gotAlias = null;
        var vm = Make(project, fingerprints: (path, pass, alias) =>
        {
            gotPath = path; gotPass = pass; gotAlias = alias;
            return new[]
            {
                new CertFingerprint("SHA-1",
                    "A1:B2:C3:D4:E5:F6:07:18:29:3A:4B:5C:6D:7E:8F:90:A1:B2:C3:D4", "upload"),
                new CertFingerprint("SHA-256",
                    "5F:0E:1D:2C:3B:4A:59:68:77:86:95:A4:B3:C2:D1:E0:FF:0E:1D:2C:3B:4A:59:68:77:86:95:A4:B3:C2:D1:E0", "upload"),
            };
        });

        Assert.Multiple(() =>
        {
            Assert.That(vm.HasFingerprints, Is.True);
            Assert.That(vm.HasNoFingerprints, Is.False);
            Assert.That(vm.Fingerprints, Has.Count.EqualTo(2));

            var sha1 = vm.Fingerprints.Single(f => f.Algorithm == "SHA-1");
            Assert.That(sha1.Value, Does.StartWith("A1:B2:C3"));
            Assert.That(sha1.Label, Is.EqualTo("upload"));
            Assert.That(sha1.IsAccent, Is.False);

            var sha256 = vm.Fingerprints.Single(f => f.Algorithm == "SHA-256");
            Assert.That(sha256.IsAccent, Is.True);

            // The resolved keystore path + password (from key.properties) reach the reader.
            Assert.That(gotPath, Does.EndWith("upload-keystore.jks"));
            Assert.That(gotPass, Is.EqualTo("x")); // key.properties storePassword=x
            Assert.That(gotAlias, Is.EqualTo("upload"));
        });
    }

    [Test]
    public void Honest_empty_state_when_no_keystore_resolves()
    {
        // No keystore file on disk → ResolveKeystoreLocation yields None; the supplier is
        // never consulted and the panel shows the honest empty state.
        var project = TestProjects.MakeProjectWithAndroidSigning(withKeystoreFile: false);
        var consulted = false;
        var vm = Make(project, fingerprints: (_, _, _) =>
        {
            consulted = true;
            return Array.Empty<CertFingerprint>();
        });

        Assert.Multiple(() =>
        {
            Assert.That(vm.HasFingerprints, Is.False);
            Assert.That(vm.HasNoFingerprints, Is.True);
            Assert.That(vm.Fingerprints, Is.Empty);
            Assert.That(consulted, Is.False);
            Assert.That(vm.FingerprintsEmptyText, Is.Not.Empty);
        });
    }

    [Test]
    public void Honest_empty_state_when_keytool_returns_nothing()
    {
        var project = TestProjects.MakeProjectWithAndroidSigning(withKeystoreFile: true);
        var vm = Make(project, fingerprints: (_, _, _) => Array.Empty<CertFingerprint>());

        Assert.Multiple(() =>
        {
            Assert.That(vm.HasFingerprints, Is.False);
            Assert.That(vm.HasNoFingerprints, Is.True);
            Assert.That(vm.FingerprintsEmptyText, Does.Contain("keytool"));
        });
    }

    [Test]
    public void Verify_keystore_re_reads_the_fingerprints()
    {
        var project = TestProjects.MakeProjectWithAndroidSigning(withKeystoreFile: true);
        var calls = 0;
        var vm = Make(project, fingerprints: (_, _, _) =>
        {
            calls++;
            // Empty on the first (constructor) read, populated on the re-read.
            return calls == 1
                ? Array.Empty<CertFingerprint>()
                : new[] { new CertFingerprint("SHA-1", "AA:BB:CC", "upload") };
        });

        Assert.That(vm.HasFingerprints, Is.False);

        vm.VerifyKeystoreCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(vm.HasFingerprints, Is.True);
            Assert.That(vm.Fingerprints.Single().Value, Is.EqualTo("AA:BB:CC"));
        });
    }
}

using LaunchFast.App.ViewModels;
using LaunchFast.Core.Models;
using LaunchFast.Core.Signing;

namespace LaunchFast.App.Tests;

public class SigningSectionViewModelTests
{
    static SigningSectionViewModel Make(
        Project project,
        string profilesDir,
        FakeSecretStore? store = null,
        IosSigningReader? reader = null,
        bool hasSyncLane = true,
        Action<Platform, string>? runLane = null,
        Func<string, string?>? env = null,
        Func<DateTimeOffset>? now = null) =>
        new(project,
            secrets: store ?? new FakeSecretStore(),
            reader: reader ?? new IosSigningReader(() => null),
            profilesDir: profilesDir,
            runLane: runLane,
            hasSyncLane: () => hasSyncLane,
            readProcessEnv: env ?? (_ => null),
            now: now ?? (() => DateTimeOffset.UtcNow));

    [Test]
    public void Surfaces_real_match_storage_from_the_matchfile()
    {
        var (project, profilesDir) = TestProjects.MakeProjectWithIosSigning();
        var vm = Make(project, profilesDir);

        Assert.Multiple(() =>
        {
            Assert.That(vm.HasMatch, Is.True);
            Assert.That(vm.MatchRepo, Is.EqualTo("git@github.com:jabtech/certificates.git"));
            Assert.That(vm.MatchBranch, Is.EqualTo("main"));
            Assert.That(vm.MatchStorage, Is.EqualTo("git (encrypted)"));
            Assert.That(vm.MatchType, Is.EqualTo("appstore"));
            Assert.That(vm.IsGitBacked, Is.True);
            Assert.That(vm.BundleId, Is.EqualTo("com.jabtech.vmt"));
        });
    }

    [Test]
    public void Surfaces_real_provisioning_profiles_from_the_profiles_dir()
    {
        var (project, profilesDir) = TestProjects.MakeProjectWithIosSigning(expiringSoon: true);
        var vm = Make(project, profilesDir);

        Assert.That(vm.HasProfiles, Is.True);
        Assert.That(vm.Profiles, Has.Count.EqualTo(2));

        var appStore = vm.Profiles.Single(p => p.Title.Contains("AppStore"));
        Assert.That(appStore.IsOk, Is.True);
        Assert.That(appStore.StatusText, Is.EqualTo("Valid"));
        Assert.That(appStore.Sub, Does.Contain("com.jabtech.vmt"));

        var adHoc = vm.Profiles.Single(p => p.Title.Contains("AdHoc"));
        Assert.That(adHoc.IsWarn, Is.True);
        Assert.That(adHoc.StatusText, Is.EqualTo("Expires soon"));
        Assert.That(adHoc.Sub, Does.Contain("24 devices"));

        // Registered-device count derived from the profiles.
        Assert.That(vm.RegisteredDevicesText, Is.EqualTo("24"));
    }

    [Test]
    public void Expired_profile_renders_as_bad()
    {
        var (project, profilesDir) = TestProjects.MakeProjectWithIosSigning();
        // A "now" far past the (year-2099) expiry would still be valid; instead push
        // now beyond it.
        var vm = Make(project, profilesDir,
            now: () => new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var row = vm.Profiles.Single();
        Assert.That(row.IsBad, Is.True);
        Assert.That(row.StatusText, Is.EqualTo("Expired"));
        Assert.That(row.ExpiresMeta, Does.Contain("expired"));
    }

    [Test]
    public void Certificates_come_from_the_injected_security_output()
    {
        var (project, profilesDir) = TestProjects.MakeProjectWithIosSigning();
        var reader = new IosSigningReader(() =>
            "  1) A1B2C3D4E5F60718293A4B5C6D7E8F90A1B2C3D4 \"Apple Distribution: JAB Technologies (7F8G9H)\"\n" +
            "  2) 0011223344556677889900AABBCCDDEEFF001122 \"Apple Development: Dev (ABCDEF)\"");

        var vm = Make(project, profilesDir, reader: reader);

        Assert.That(vm.HasCertificates, Is.True);
        Assert.That(vm.Certificates, Has.Count.EqualTo(2));
        Assert.That(vm.Certificates[0].Title, Does.StartWith("Apple Distribution"));
        Assert.That(vm.Certificates[0].Sub, Does.Contain("Distribution"));
    }

    [Test]
    public void Empty_certificate_state_when_security_unavailable()
    {
        var (project, profilesDir) = TestProjects.MakeProjectWithIosSigning();
        var vm = Make(project, profilesDir, reader: new IosSigningReader(() => null));

        Assert.That(vm.HasCertificates, Is.False);
        Assert.That(vm.HasNoCertificates, Is.True);
        Assert.That(vm.Certificates, Is.Empty);
    }

    [Test]
    public void Match_credentials_reflect_the_secret_store_and_env()
    {
        var (project, profilesDir) = TestProjects.MakeProjectWithIosSigning();
        var store = new FakeSecretStore();
        store.Set(project.Path, "MATCH_PASSWORD", "pw");
        // MATCH_GIT_URL via CI env.

        var vm = Make(project, profilesDir, store,
            env: name => name == "MATCH_GIT_URL" ? "git@x" : null);

        var cred = vm.Credentials.ToDictionary(c => c.Name);
        Assert.Multiple(() =>
        {
            Assert.That(cred["MATCH_PASSWORD"].IsSet, Is.True);
            Assert.That(cred["MATCH_PASSWORD"].SourceText, Is.EqualTo("Keychain"));
            Assert.That(cred["MATCH_GIT_URL"].IsSet, Is.True);
            Assert.That(cred["MATCH_GIT_URL"].SourceText, Is.EqualTo("CI secret"));
        });
    }

    [Test]
    public void Empty_states_for_a_project_without_match_or_profiles()
    {
        // Project with no Matchfile and an empty profiles dir.
        var root = TestProjects.MakeFlutterProject("nomatch");
        File.Delete(Path.Combine(root, "ios", "fastlane", "Matchfile"));
        var project = LaunchFast.Core.Scanning.ProjectScanner.TryScanRoot(root)!;
        var emptyProfiles = Path.Combine(root, "profiles");
        Directory.CreateDirectory(emptyProfiles);

        var vm = Make(project, emptyProfiles);

        Assert.Multiple(() =>
        {
            Assert.That(vm.HasMatch, Is.False);
            Assert.That(vm.HasNoMatch, Is.True);
            Assert.That(vm.HasProfiles, Is.False);
            Assert.That(vm.HasNoProfiles, Is.True);
            Assert.That(vm.HasNoCertificates, Is.True);
            Assert.That(vm.RegisteredDevicesText, Is.EqualTo("—"));
        });
    }

    [Test]
    public void Refresh_command_re_reads_without_throwing()
    {
        var (project, profilesDir) = TestProjects.MakeProjectWithIosSigning();
        var vm = Make(project, profilesDir);

        Assert.DoesNotThrow(() => vm.RefreshCommand.Execute(null));
        Assert.That(vm.HasProfiles, Is.True);
    }

    [Test]
    public void CanRunMatch_reflects_whether_the_sync_certificates_lane_exists()
    {
        var (project, profilesDir) = TestProjects.MakeProjectWithIosSigning();

        Assert.That(Make(project, profilesDir, hasSyncLane: true).CanRunMatch, Is.True);
        Assert.That(Make(project, profilesDir, hasSyncLane: false).CanRunMatch, Is.False);
    }

    [Test]
    public void RunMatch_invokes_the_run_delegate_with_sync_certificates()
    {
        var (project, profilesDir) = TestProjects.MakeProjectWithIosSigning();
        (Platform Platform, string Lane)? called = null;

        var vm = Make(project, profilesDir,
            runLane: (p, l) => called = (p, l), hasSyncLane: true);

        Assert.DoesNotThrow(() => vm.RunMatchCommand.Execute(null));

        Assert.That(called, Is.Not.Null);
        Assert.That(called!.Value.Platform, Is.EqualTo(Platform.Ios));
        Assert.That(called.Value.Lane, Is.EqualTo("sync_certificates"));
    }

    [Test]
    public void RunMatch_is_a_no_op_when_the_lane_is_absent()
    {
        var (project, profilesDir) = TestProjects.MakeProjectWithIosSigning();
        var calls = 0;

        var vm = Make(project, profilesDir,
            runLane: (_, _) => calls++, hasSyncLane: false);

        vm.RunMatchCommand.Execute(null);
        Assert.That(calls, Is.EqualTo(0));
    }
}

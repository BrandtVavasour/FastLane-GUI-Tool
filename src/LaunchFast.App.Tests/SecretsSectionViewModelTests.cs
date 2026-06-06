using LaunchFast.App.ViewModels;

namespace LaunchFast.App.Tests;

public class SecretsSectionViewModelTests
{
    // No ambient process env in tests, so CI-env never shadows files/keychain.
    static SecretsSectionViewModel MakeVm(FakeSecretStore store)
    {
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
        return new SecretsSectionViewModel(project, store, _ => null);
    }

    static SecretRowViewModel Row(SecretsSectionViewModel vm, string name) =>
        vm.Secrets.Single(r => r.Name == name);

    [Test]
    public void Referenced_secrets_all_appear()
    {
        var vm = MakeVm(new FakeSecretStore());

        var names = vm.Secrets.Select(r => r.Name).ToList();

        // Genuine secrets referenced by the project's fastlane config.
        Assert.That(names, Does.Contain("APPLE_ID"));
        Assert.That(names, Does.Contain("MATCH_GIT_URL"));
        Assert.That(names, Does.Contain("API_TOKEN"));

        // Control/config vars are never surfaced as secrets.
        Assert.That(names, Does.Not.Contain("CI"));
        Assert.That(names, Does.Not.Contain("FASTLANE_ENV"));
    }

    [Test]
    public void Value_present_in_env_file_is_set_with_env_source()
    {
        var vm = MakeVm(new FakeSecretStore());

        // API_TOKEN is in .env.production (TestProjects fixture).
        var row = Row(vm, "API_TOKEN");
        Assert.That(row.IsSet, Is.True);
        Assert.That(row.Source, Is.EqualTo(SecretSource.EnvFile));
        Assert.That(row.SourceText, Is.EqualTo(".env"));
    }

    [Test]
    public void Known_secret_absent_everywhere_is_missing_with_dash_source()
    {
        var vm = MakeVm(new FakeSecretStore());

        // APPLE_ID is referenced (Appfile) but not in any file or the keychain.
        var row = Row(vm, "APPLE_ID");
        Assert.That(row.IsMissing, Is.True);
        Assert.That(row.Source, Is.EqualTo(SecretSource.None));
        Assert.That(row.SourceText, Is.EqualTo("—"));
        Assert.That(row.Display, Is.EqualTo("—"));
    }

    [Test]
    public void Setting_a_secret_flips_it_to_set_from_keychain_and_decrements_missing()
    {
        var store = new FakeSecretStore();
        var vm = MakeVm(store);

        var before = vm.MissingCount;
        Assert.That(Row(vm, "APPLE_ID").IsMissing, Is.True);

        vm.SetSecret("APPLE_ID", "dev@jabtech.io");

        var after = Row(vm, "APPLE_ID");
        Assert.That(after.IsSet, Is.True);
        Assert.That(after.Source, Is.EqualTo(SecretSource.Keychain));
        Assert.That(after.SourceText, Is.EqualTo("Keychain"));
        Assert.That(vm.MissingCount, Is.EqualTo(before - 1));
    }

    [Test]
    public void Process_env_wins_over_files_and_keychain()
    {
        var store = new FakeSecretStore();
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
        store.Set(project.Path, "API_TOKEN", "from-keychain");

        // API_TOKEN is in the env file AND the keychain, but CI env takes precedence.
        var vm = new SecretsSectionViewModel(project, store,
            name => name == "API_TOKEN" ? "from-ci" : null);

        var row = vm.Secrets.Single(r => r.Name == "API_TOKEN");
        Assert.That(row.Source, Is.EqualTo(SecretSource.CiEnv));
        Assert.That(row.SourceText, Is.EqualTo("CI env"));
    }

    [Test]
    public void Reveal_all_shows_the_real_value_for_set_rows()
    {
        var store = new FakeSecretStore();
        var vm = MakeVm(store);

        var row = Row(vm, "API_TOKEN"); // value tok123 from .env.production
        Assert.That(row.Display, Is.EqualTo("••••••••"));

        vm.RevealAll = true;
        Assert.That(row.Display, Is.EqualTo("tok123"));
    }
}

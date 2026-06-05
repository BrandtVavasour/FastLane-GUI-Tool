using LaunchFast.Core.Env;

namespace LaunchFast.Core.Tests;

public class SecretEnvFilterTests
{
    [TestCase("MATCH_PASSWORD")]
    [TestCase("APPLE_ID")]
    [TestCase("APP_STORE_CONNECT_API_KEY_PATH")]
    [TestCase("SCREENSHOT_PASSWORD")]
    [TestCase("SOMETHING_TOKEN")]
    public void Genuine_secrets_are_secret(string name) =>
        Assert.That(SecretEnvFilter.IsSecret(name), Is.True);

    [TestCase("CI")]
    [TestCase("FASTLANE_ENV")]
    [TestCase("FLUTTER_LOCALE")]
    [TestCase("MATCH_KEYCHAIN_NAME")]
    [TestCase("API_URL")]
    public void Control_and_config_vars_are_not_secret(string name) =>
        Assert.That(SecretEnvFilter.IsSecret(name), Is.False);

    [Test]
    public void Keychain_password_is_secret_but_keychain_name_is_not()
    {
        Assert.That(SecretEnvFilter.IsSecret("MATCH_KEYCHAIN_PASSWORD"), Is.True);
        Assert.That(SecretEnvFilter.IsSecret("MATCH_KEYCHAIN_NAME"), Is.False);
    }
}

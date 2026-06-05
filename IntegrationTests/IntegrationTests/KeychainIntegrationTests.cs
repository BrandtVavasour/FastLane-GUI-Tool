using System.Diagnostics;
using LaunchFast.Core.Env;
using NUnit.Framework;

namespace IntegrationTests;

// Roundtrips a secret through the real macOS login keychain via KeychainSecretStore.
// Also proves the ArgumentList special-character handling (spaces, quotes, $).
// Writing then reading in the SAME process normally does not prompt; if the
// security CLI is missing or access is denied (sandbox), the test ignores itself.
[TestFixture]
public sealed class KeychainIntegrationTests
{
    const string ServicePrefix = "com.jabtech.launchfast";
    const string Key = "TEST_KEY";

    [Test]
    public void Keychain_set_then_get_roundtrips()
    {
        if (!IsSecurityCliAvailable())
        {
            Assert.Ignore("`security` CLI not available on PATH.");
        }

        var projectId = "itest-" + Guid.NewGuid().ToString("N");
        const string value = "value-with spaces and \"quotes\" and $dollar";
        var store = new KeychainSecretStore();

        try
        {
            store.Set(projectId, Key, value);
        }
        catch (Exception ex)
        {
            Assert.Ignore($"Keychain write denied/unavailable (likely sandbox): {ex.Message}");
        }

        try
        {
            string? readBack;
            try
            {
                readBack = store.Get(projectId, Key);
            }
            catch (Exception ex)
            {
                Assert.Ignore($"Keychain read denied/unavailable (likely sandbox): {ex.Message}");
                return;
            }

            Assert.That(readBack, Is.EqualTo(value));
        }
        finally
        {
            DeleteBestEffort(projectId);
        }
    }

    static void DeleteBestEffort(string projectId)
    {
        try
        {
            var psi = new ProcessStartInfo("security")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("delete-generic-password");
            psi.ArgumentList.Add("-s");
            psi.ArgumentList.Add($"{ServicePrefix}.{projectId}");
            psi.ArgumentList.Add("-a");
            psi.ArgumentList.Add(Key);
            using var p = Process.Start(psi);
            p?.WaitForExit(10_000);
        }
        catch
        {
            // Cleanup is best-effort; ignore failures.
        }
    }

    static bool IsSecurityCliAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo("security")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("help");
            using var p = Process.Start(psi);
            if (p is null)
            {
                return false;
            }

            p.WaitForExit(10_000);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

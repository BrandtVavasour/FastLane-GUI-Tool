using System.Diagnostics;

namespace LaunchFast.Core.Env;

public sealed class KeychainSecretStore : ISecretStore
{
    const string ServicePrefix = "com.jabtech.launchfast";

    public string? Get(string projectId, string key)
    {
        var psi = new ProcessStartInfo("security",
            $"find-generic-password -s \"{ServicePrefix}.{projectId}\" -a \"{key}\" -w")
        { RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = Process.Start(psi)!;
        var outp = p.StandardOutput.ReadToEnd().TrimEnd('\n');
        p.WaitForExit();
        return p.ExitCode == 0 ? outp : null;
    }

    public void Set(string projectId, string key, string value)
    {
        var psi = new ProcessStartInfo("security",
            $"add-generic-password -U -s \"{ServicePrefix}.{projectId}\" -a \"{key}\" -w \"{value}\"")
        { RedirectStandardError = true };
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException("Keychain write failed");
    }
}

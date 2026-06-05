using System.Diagnostics;

namespace LaunchFast.Core.Env;

public sealed class KeychainSecretStore : ISecretStore
{
    const string ServicePrefix = "com.jabtech.launchfast";

    public string? Get(string projectId, string key)
    {
        var psi = new ProcessStartInfo("security")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("find-generic-password");
        psi.ArgumentList.Add("-s"); psi.ArgumentList.Add($"{ServicePrefix}.{projectId}");
        psi.ArgumentList.Add("-a"); psi.ArgumentList.Add(key);
        psi.ArgumentList.Add("-w");
        using var p = Process.Start(psi)!;
        var outp = p.StandardOutput.ReadToEnd().TrimEnd('\n');
        p.WaitForExit();
        return p.ExitCode == 0 ? outp : null;
    }

    public void Set(string projectId, string key, string value)
    {
        var psi = new ProcessStartInfo("security")
        {
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("add-generic-password");
        psi.ArgumentList.Add("-U");
        psi.ArgumentList.Add("-s"); psi.ArgumentList.Add($"{ServicePrefix}.{projectId}");
        psi.ArgumentList.Add("-a"); psi.ArgumentList.Add(key);
        psi.ArgumentList.Add("-w"); psi.ArgumentList.Add(value);
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException("Keychain write failed");
    }
}

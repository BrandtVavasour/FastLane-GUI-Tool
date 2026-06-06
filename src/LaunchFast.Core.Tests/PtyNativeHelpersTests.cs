
using LaunchFast.Core.Running;

namespace LaunchFast.Core.Tests;

public class PtyNativeHelpersTests
{
    [Test]
    public void DecodeWaitStatus_normal_exit_returns_exit_code()
    {
        // WIFEXITED with WEXITSTATUS == 7: exit code in bits 8-15, low byte zero.
        var status = 7 << 8;

        Assert.That(PtyNativeHelpers.DecodeWaitStatus(status), Is.EqualTo(7));
    }

    [Test]
    public void DecodeWaitStatus_zero_exit_returns_zero()
    {
        Assert.That(PtyNativeHelpers.DecodeWaitStatus(0), Is.EqualTo(0));
    }

    [Test]
    public void DecodeWaitStatus_signalled_returns_128_plus_signal()
    {
        // Terminated by SIGKILL (9): low 7 bits hold the signal, no exit byte.
        Assert.That(PtyNativeHelpers.DecodeWaitStatus(9), Is.EqualTo(128 + 9));
    }

    [Test]
    public void BuildArgv_places_command_first_then_args()
    {
        var argv = PtyNativeHelpers.BuildArgv("/bin/sh", ["-c", "echo hi"]);

        Assert.That(argv, Is.EqualTo(new[] { "/bin/sh", "-c", "echo hi" }));
    }

    [Test]
    public void BuildArgv_with_no_args_is_just_the_command()
    {
        var argv = PtyNativeHelpers.BuildArgv("/bin/ls", []);

        Assert.That(argv, Is.EqualTo(new[] { "/bin/ls" }));
    }

    [Test]
    public void BuildEnvp_merges_overrides_over_base_and_formats_key_value()
    {
        var baseEnv = new Dictionary<string, string>
        {
            ["PATH"] = "/usr/bin",
            ["FOO"] = "base",
        };
        var overrides = new Dictionary<string, string>
        {
            ["FOO"] = "override",
            ["LF_PROBE"] = "hello",
        };

        var envp = PtyNativeHelpers.BuildEnvp(baseEnv, overrides);

        Assert.That(envp, Does.Contain("PATH=/usr/bin"));
        Assert.That(envp, Does.Contain("FOO=override"));
        Assert.That(envp, Does.Contain("LF_PROBE=hello"));
        Assert.That(envp, Does.Not.Contain("FOO=base"));
        // One entry per distinct key (FOO collapsed).
        Assert.That(envp, Has.Length.EqualTo(3));
    }
}

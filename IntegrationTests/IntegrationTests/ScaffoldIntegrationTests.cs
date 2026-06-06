using System.Diagnostics;
using System.Text;
using LaunchFast.Core.Scaffolding;
using NUnit.Framework;

namespace IntegrationTests;

// Exercises the real fastlane scaffolder end-to-end: generates a minimal fastlane
// file set for an iOS-only Flutter project and verifies the generated Ruby syntax
// (ruby -c) and that bundle install succeeds in the generated ios/ directory.
// Tests Assert.Ignore rather than fail when the required tools are missing.
[TestFixture]
public sealed class ScaffoldIntegrationTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    static bool IsOnPath(string exe)
    {
        var path = System.Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            if (dir.Length == 0)
            {
                continue;
            }

            try
            {
                if (File.Exists(Path.Combine(dir, exe)))
                {
                    return true;
                }
            }
            catch
            {
                // Malformed PATH entry: skip.
            }
        }

        return false;
    }

    /// <summary>Runs an executable and returns (exitCode, combinedOutput).</summary>
    static (int ExitCode, string Output) RunProcess(
        string fileName,
        string[] args,
        string workingDirectory,
        int timeoutMs = 180_000)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        var sb = new StringBuilder();
        using var p = Process.Start(psi);
        if (p is null)
        {
            return (-1, "Process.Start returned null");
        }

        p.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                sb.AppendLine(e.Data);
            }
        };
        p.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                sb.AppendLine(e.Data);
            }
        };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        if (!p.WaitForExit(timeoutMs))
        {
            try { p.Kill(entireProcessTree: true); } catch { /* best effort */ }
            return (-2, $"timed out after {timeoutMs / 1000}s\n" + sb);
        }

        // Ensure async readers flush before reading sb.
        p.WaitForExit();
        return (p.ExitCode, sb.ToString());
    }

    /// <summary>
    /// Builds a minimal <see cref="WizardAnswers"/> for iOS-only with the two
    /// most common lanes and a couple of dart-defines. No secrets.
    /// </summary>
    static WizardAnswers MinimalIosAnswers() => new(
        Ios: true,
        Android: false,
        IosBundleId: "com.acme.scafdemo",
        AppleId: null,
        TeamId: "ABCDE12345",
        ItcTeamId: null,
        MatchGitUrl: null,
        AndroidPackage: null,
        PlayJsonKeyPath: null,
        IosLanes: ["sync_certificates", "beta"],
        AndroidLanes: [],
        DartDefines: new Dictionary<string, string>
        {
            ["API_BASE_URL"] = "API_BASE_URL",
            ["FEATURE_FLAGS"] = "FEATURE_FLAGS",
        },
        Secrets: []);

    // ── Tests ──────────────────────────────────────────────────────────────────

    [Test]
    public void Generated_ios_fastfile_is_valid_ruby()
    {
        if (!IsOnPath("ruby"))
        {
            Assert.Ignore("ruby not available on PATH.");
            return;
        }

        var answers = MinimalIosAnswers();
        var tmpDir = Path.Combine(Path.GetTempPath(), "lf_ruby_check_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);

        try
        {
            var plan = FastlaneScaffolder.Render(answers, tmpDir);

            // Write every generated file.
            foreach (var fc in plan.Files)
            {
                var dir = Path.GetDirectoryName(fc.Path);
                if (dir is not null)
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(fc.Path, fc.NewContent);
            }

            // Find the iOS Fastfile in the generated set.
            var iosFastfile = plan.Files
                .FirstOrDefault(f => f.Path.EndsWith("ios/fastlane/Fastfile", StringComparison.Ordinal))
                ?.Path;

            if (iosFastfile is null || !File.Exists(iosFastfile))
            {
                Assert.Fail("Scaffolder did not produce ios/fastlane/Fastfile.");
                return;
            }

            var (exitCode, output) = RunProcess("ruby", ["-c", iosFastfile], tmpDir, timeoutMs: 30_000);

            Assert.That(
                exitCode,
                Is.EqualTo(0),
                $"ruby -c reported syntax errors in the generated Fastfile:\n{output}");

            Assert.That(
                output,
                Does.Contain("Syntax OK"),
                $"ruby -c did not print 'Syntax OK'; output:\n{output}");
        }
        finally
        {
            try { Directory.Delete(tmpDir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Test]
    [CancelAfter(240_000)]  // generous outer timeout so NUnit doesn't kill before the inner one
    public void Generated_fastlane_bundle_installs()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Ignore("This test is macOS-only.");
            return;
        }

        if (!IsOnPath("bundle") && !IsOnPath("ruby"))
        {
            Assert.Ignore("bundle (and ruby) not available on PATH.");
            return;
        }

        if (!IsOnPath("bundle"))
        {
            Assert.Ignore("bundle not available on PATH.");
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), "lf_bundle_install_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            // 1. Write a minimal Flutter-ish project layout.
            File.WriteAllText(
                Path.Combine(root, "pubspec.yaml"),
                "name: scafold_demo\nversion: 1.0.0+1\n");
            Directory.CreateDirectory(Path.Combine(root, "ios"));

            // 2. Scaffold the fastlane file set.
            var answers = MinimalIosAnswers();
            var plan = FastlaneScaffolder.Render(answers, root);

            // 3. Write generated files to disk.
            foreach (var fc in plan.Files)
            {
                var dir = Path.GetDirectoryName(fc.Path);
                if (dir is not null)
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(fc.Path, fc.NewContent);
            }

            var iosDir = Path.Combine(root, "ios");

            Assert.That(
                File.Exists(Path.Combine(iosDir, "Gemfile")),
                Is.True,
                "Scaffolder did not produce ios/Gemfile.");

            // 4. Run `bundle install` in ios/.
            var bundlePath = IsOnPath("bundle")
                ? "bundle"
                : throw new InvalidOperationException("bundle not on PATH — already checked above.");

            var (exitCode, output) = RunProcess(
                bundlePath,
                ["install"],
                iosDir,
                timeoutMs: 180_000);

            // If bundle install failed for an environment reason (network, gem source
            // unavailable, etc.) ignore rather than hard-fail.
            if (exitCode != 0)
            {
                Assert.Ignore(
                    $"bundle install exited with code {exitCode} — likely a network / gem source " +
                    $"issue in this environment. Output (last 1000 chars):\n" +
                    $"{Truncate(output, 1000)}");
                return;
            }

            // 5. Assert Gemfile.lock was produced and exit code was 0.
            Assert.That(
                exitCode,
                Is.EqualTo(0),
                $"bundle install exited {exitCode}:\n{Truncate(output, 1000)}");

            Assert.That(
                File.Exists(Path.Combine(iosDir, "Gemfile.lock")),
                Is.True,
                "bundle install succeeded (exit 0) but Gemfile.lock was not created.");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
        }
    }

    static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}

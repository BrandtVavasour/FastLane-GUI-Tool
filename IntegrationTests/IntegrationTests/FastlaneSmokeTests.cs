using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using LaunchFast.Core.Models;
using LaunchFast.Core.Parsing;
using NUnit.Framework;

namespace IntegrationTests;

// Smoke test that guards the static FastfileParser against drift versus what the
// real fastlane CLI reports. Requires the reference project and fastlane on PATH;
// otherwise the test ignores itself rather than failing.
[TestFixture]
public sealed class FastlaneSmokeTests
{
    const string IosDir =
        "/Users/brandtvavasour/Documents/JABTech/VendingMachine/vending_machine_tracker-UI/ios";

    [Test]
    public void Parser_matches_real_fastlane_lanes()
    {
        if (!Directory.Exists(IosDir))
        {
            Assert.Ignore($"Reference iOS dir not present: {IosDir}");
        }

        var fastfile = Path.Combine(IosDir, "fastlane", "Fastfile");
        if (!File.Exists(fastfile))
        {
            Assert.Ignore($"Reference Fastfile not present: {fastfile}");
        }

        if (!TryRunFastlaneLanes(IosDir, out var stdout, out var reason))
        {
            Assert.Ignore($"Could not run fastlane lanes: {reason}");
        }

        var cliLanes = ExtractCliLaneNames(stdout);
        if (cliLanes.Count == 0)
        {
            Assert.Ignore(
                "fastlane lanes produced no recognizable lane names; nothing to compare.");
        }

        var parserLanes = FastfileParser
            .Parse(File.ReadAllText(fastfile), Platform.Ios)
            .Select(l => l.Name)
            .ToHashSet(StringComparer.Ordinal);

        // The parser must be a superset of (i.e. recognize) every public lane the
        // fastlane CLI lists. Private lanes are intentionally excluded by both.
        var missing = cliLanes.Where(name => !parserLanes.Contains(name)).ToList();

        Assert.That(
            missing,
            Is.Empty,
            $"Parser missed public lanes that fastlane reports: {string.Join(", ", missing)}. "
                + $"Parser saw: {string.Join(", ", parserLanes)}");
    }

    static bool TryRunFastlaneLanes(string cwd, out string stdout, out string reason)
    {
        stdout = string.Empty;
        reason = string.Empty;

        // Prefer `bundle exec fastlane` when a Gemfile is present, else fall back.
        var usesBundle = File.Exists(Path.Combine(cwd, "Gemfile"))
            && IsOnPath("bundle");

        string fileName;
        string[] args;
        if (usesBundle)
        {
            fileName = "bundle";
            args = ["exec", "fastlane", "lanes"];
        }
        else if (IsOnPath("fastlane"))
        {
            fileName = "fastlane";
            args = ["lanes"];
        }
        else
        {
            reason = "neither `bundle` (with Gemfile) nor `fastlane` is available on PATH";
            return false;
        }

        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        // fastlane can be slow; allow a generous timeout.
        psi.Environment["FASTLANE_SKIP_UPDATE_CHECK"] = "1";
        psi.Environment["FASTLANE_DISABLE_COLORS"] = "1";

        var sb = new StringBuilder();
        try
        {
            using var p = Process.Start(psi);
            if (p is null)
            {
                reason = "Process.Start returned null";
                return false;
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

            if (!p.WaitForExit(180_000))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* best effort */ }
                reason = "timed out after 180s";
                return false;
            }

            // Ensure async readers flush.
            p.WaitForExit();
            stdout = sb.ToString();

            if (p.ExitCode != 0)
            {
                reason = $"exit code {p.ExitCode}; output: {Truncate(stdout, 500)}";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            return false;
        }
    }

    // fastlane `lanes` prints lines like:
    //   ----- fastlane ios sync_certificates
    //   ----- fastlane android beta
    // (older/other versions may print "ios sync_certificates" without the prefix).
    // We pull the lane token following the platform from lines that name a lane.
    // This is intentionally pragmatic.
    static IReadOnlySet<string> ExtractCliLaneNames(string output)
    {
        var lanes = new HashSet<string>(StringComparer.Ordinal);
        var rx = new Regex(
            @"(?:fastlane\s+)?(?:ios|android|mac)\s+(?<name>[a-z0-9_]+)\s*$",
            RegexOptions.IgnoreCase);

        foreach (var raw in output.Split('\n'))
        {
            // Strip any leftover ANSI escape sequences.
            var line = Regex.Replace(raw.TrimEnd('\r'), "\\[[0-9;]*m", string.Empty);
            var m = rx.Match(line);
            if (m.Success)
            {
                lanes.Add(m.Groups["name"].Value);
            }
        }

        return lanes;
    }

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

    static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}

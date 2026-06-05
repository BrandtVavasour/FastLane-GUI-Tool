using LaunchFast.Core.Models;
using LaunchFast.Core.Running;

namespace LaunchFast.Core.Tests;

public class LaneRunnerTests
{
    sealed class FakePty : IPtyProcess
    {
        public event Action<string>? OutputReceived;
        public event Action<int>? Exited;
        public List<string> Writes = new();
        public void Emit(string s) => OutputReceived?.Invoke(s);
        public void Finish(int code) => Exited?.Invoke(code);
        public void Write(string input) => Writes.Add(input);
        public void Kill() => Exited?.Invoke(130);
        public void Dispose() { }
    }

    sealed class FakeFactory : IPtyFactory
    {
        public FakePty Last = null!;
        public string? Cmd; public string[]? Args; public string? Cwd;
        public IPtyProcess Start(string c, string[] a, string cwd, IReadOnlyDictionary<string, string> e)
        { Cmd = c; Args = a; Cwd = cwd; return Last = new FakePty(); }
    }

    [Test]
    public void Runs_bundle_exec_fastlane_in_platform_dir_and_streams()
    {
        var factory = new FakeFactory();
        var runner = new LaneRunner(factory);
        var log = new List<string>();

        var lane = new Lane("beta", "TestFlight", Platform.Ios);
        var handle = runner.Run(lane, platformDir: "/proj/ios",
            env: new Dictionary<string, string>(), onOutput: log.Add);

        Assert.That(factory.Cmd, Is.EqualTo("bundle"));
        Assert.That(factory.Args, Is.EqualTo(new[] { "exec", "fastlane", "ios", "beta" }));
        Assert.That(factory.Cwd, Is.EqualTo("/proj/ios"));

        factory.Last.Emit("Running…");
        Assert.That(log, Does.Contain("Running…"));
    }

    [Test]
    public void Android_lane_uses_android_platform_arg()
    {
        var factory = new FakeFactory();
        var runner = new LaneRunner(factory);
        runner.Run(new Lane("internal", "", Platform.Android), "/proj/android",
            new Dictionary<string, string>(), _ => { });
        Assert.That(factory.Args, Is.EqualTo(new[] { "exec", "fastlane", "android", "internal" }));
    }

    [Test]
    public void Stop_triggers_completed()
    {
        var factory = new FakeFactory();
        var runner = new LaneRunner(factory);
        int? exit = null;
        var handle = runner.Run(new Lane("beta", "", Platform.Ios), "/p",
            new Dictionary<string, string>(), _ => { });
        handle.Completed += c => exit = c;
        handle.Stop();
        Assert.That(exit, Is.EqualTo(130));
    }
}

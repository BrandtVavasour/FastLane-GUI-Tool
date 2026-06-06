using LaunchFast.Core.Env;
using LaunchFast.Core.Models;
using LaunchFast.Core.Running;
using LaunchFast.Core.Stores;

namespace LaunchFast.App.Tests;

/// <summary>Fake App Store Connect client returning a canned status / TestFlight info.</summary>
public sealed class FakeAscClient(StoreStatus status, TestFlightInfo? testFlight = null) : IAppStoreConnectClient
{
    public Task<StoreStatus> GetStatusAsync(string bundleId, Destination destination, CancellationToken ct = default) =>
        Task.FromResult(status with { Destination = destination });

    public Task<TestFlightInfo> GetTestFlightAsync(string bundleId, CancellationToken ct = default) =>
        Task.FromResult(testFlight ?? TestFlightInfo.Empty);
}

/// <summary>Fake Play client returning a canned status per call.</summary>
public sealed class FakePlayClient(StoreStatus status) : IPlayStoreClient
{
    public Task<StoreStatus> GetStatusAsync(string packageName, Destination destination, CancellationToken ct = default) =>
        Task.FromResult(status with { Destination = destination });
}

/// <summary>In-memory <see cref="ISecretStore"/> for tests — no Keychain.</summary>
public sealed class FakeSecretStore : ISecretStore
{
    readonly Dictionary<string, string> _values = new();

    static string Key(string projectId, string key) => projectId + "\0" + key;

    public string? Get(string projectId, string key) =>
        _values.TryGetValue(Key(projectId, key), out var v) ? v : null;

    public void Set(string projectId, string key, string value) =>
        _values[Key(projectId, key)] = value;

    /// <summary>Pre-seed every key in <paramref name="keys"/> for the given project.</summary>
    public FakeSecretStore Satisfy(string projectId, IEnumerable<string> keys, string value = "x")
    {
        foreach (var k in keys) Set(projectId, k, value);
        return this;
    }
}

/// <summary>
/// Fake <see cref="IPtyFactory"/> that records the launched (command, args, cwd)
/// and lets the test emit output and finish the process.
/// </summary>
public sealed class RecordingPtyFactory : IPtyFactory
{
    public string? Command { get; private set; }
    public string[]? Args { get; private set; }
    public string? Cwd { get; private set; }
    public IReadOnlyDictionary<string, string>? Env { get; private set; }
    public FakeProcess? Last { get; private set; }

    public IPtyProcess Start(string command, string[] args, string cwd,
        IReadOnlyDictionary<string, string> env)
    {
        Command = command;
        Args = args;
        Cwd = cwd;
        Env = env;
        Last = new FakeProcess();
        return Last;
    }

    public void Emit(string line) => Last!.Emit(line);
    public void Finish(int code) => Last!.Finish(code);
}

public sealed class FakeProcess : IPtyProcess
{
    public event Action<string>? OutputReceived;
    public event Action<int>? Exited;
    public bool Killed { get; private set; }
    public string? LastInput { get; private set; }

    public void Emit(string line) => OutputReceived?.Invoke(line);
    public void Finish(int code) => Exited?.Invoke(code);
    public void Write(string input) => LastInput = input;
    public void Kill() { Killed = true; Exited?.Invoke(-1); }
    public void Dispose() { }
}

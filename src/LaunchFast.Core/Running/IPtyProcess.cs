namespace LaunchFast.Core.Running;

public interface IPtyProcess : IDisposable
{
    event Action<string> OutputReceived;
    event Action<int> Exited;
    void Write(string input);     // for interactive prompts (2FA, passphrase)
    void Kill();
}

public interface IPtyFactory
{
    IPtyProcess Start(string command, string[] args, string cwd,
        IReadOnlyDictionary<string, string> env);
}

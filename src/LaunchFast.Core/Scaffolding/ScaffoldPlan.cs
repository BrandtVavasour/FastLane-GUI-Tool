namespace LaunchFast.Core.Scaffolding;

public enum FileChangeKind { Create, InsertLane, AddPlatformBlock, AppendEnv }

public sealed record FileChange(string Path, string OldContent, string NewContent, FileChangeKind Kind);

public sealed record SecretToStore(string Key, string Value);

public sealed record ScaffoldPlan(IReadOnlyList<FileChange> Files, IReadOnlyList<SecretToStore> Secrets);

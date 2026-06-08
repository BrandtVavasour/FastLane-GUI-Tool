namespace LaunchFast.Core.Updates;

/// <summary>A published GitHub release: its tag and the human-facing release page.</summary>
public sealed record ReleaseInfo(string TagName, string HtmlUrl);

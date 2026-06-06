namespace LaunchFast.Core.Models;

public sealed record Project(
    string Name,
    string Path,
    string? Version,
    string? IosFastlaneDir,
    string? AndroidFastlaneDir,
    bool HasMatchfile,
    string? IconPath)
{
    public bool HasFastlane => IosFastlaneDir is not null || AndroidFastlaneDir is not null;
}

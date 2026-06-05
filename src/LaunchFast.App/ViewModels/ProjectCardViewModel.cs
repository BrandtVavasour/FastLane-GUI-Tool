using LaunchFast.Core.Models;

namespace LaunchFast.App.ViewModels;

public sealed class ProjectCardViewModel(Project project)
{
    public Project Project => project;
    public string Name => project.Name;
    public string? Version => project.Version;
    public string? IconPath => project.IconPath;
    public bool HasIos => project.IosFastlaneDir is not null;
    public bool HasAndroid => project.AndroidFastlaneDir is not null;
    public bool HasMatch => project.HasMatchfile;
}

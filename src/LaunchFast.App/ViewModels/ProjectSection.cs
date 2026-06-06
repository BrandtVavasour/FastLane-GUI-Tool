using CommunityToolkit.Mvvm.ComponentModel;

namespace LaunchFast.App.ViewModels;

/// <summary>The set of per-project sections shown in the shell sidebar.</summary>
public enum ProjectSection
{
    Lanes,
    Signing,
    Secrets,
    TestFlight,
    Screenshots,
    BuildTest,
}

/// <summary>
/// Sidebar item describing one <see cref="ProjectSection"/>: its key, the display
/// title, and an icon glyph hint. <see cref="IsSelected"/> drives the selected
/// highlight in the nav list.
/// </summary>
public partial class ProjectSectionViewModel(ProjectSection section, string title, string glyph) : ObservableObject
{
    public ProjectSection Section { get; } = section;
    public string Title { get; } = title;
    public string Glyph { get; } = glyph;

    [ObservableProperty]
    private bool _isSelected;
}

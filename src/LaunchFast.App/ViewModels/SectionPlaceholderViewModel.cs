namespace LaunchFast.App.ViewModels;

/// <summary>
/// Lightweight stand-in content view-model for a project section that does not
/// yet have a real view. Carries the section title plus a short "coming up" note.
/// These are swapped out for real section view-models in later tasks; the shell
/// maps a section → a content VM and the view maps a content VM → a View, so
/// replacing this is a localized change.
/// </summary>
public sealed class SectionPlaceholderViewModel(string title, string subtitle)
{
    public string Title { get; } = title;
    public string Subtitle { get; } = subtitle;
}

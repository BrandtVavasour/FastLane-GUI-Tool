using LaunchFast.App.ViewModels;
using LaunchFast.Core.Stores;

namespace LaunchFast.App.Tests;

public class ProjectShellViewModelTests
{
    static ProjectShellViewModel MakeShell(out bool[] wentBack)
    {
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
        var back = new bool[1];
        var shell = new ProjectShellViewModel(
            project,
            new FakeSecretStore(),
            new RecordingPtyFactory(),
            new StoreStatusProvider(null, null),
            new StoreIdentifiers(null, null))
        {
            GoBack = () => back[0] = true,
        };
        wentBack = back;
        return shell;
    }

    [Test]
    public void Default_section_is_Lanes_with_detail_content()
    {
        var shell = MakeShell(out _);

        Assert.That(shell.SelectedSection, Is.EqualTo(ProjectSection.Lanes));
        Assert.That(shell.CurrentContent, Is.InstanceOf<ProjectDetailViewModel>());

        // The Lanes content is loaded (lanes populated) like OpenDetail builds it.
        var detail = (ProjectDetailViewModel)shell.CurrentContent!;
        Assert.That(detail.IosLanes, Is.Not.Empty);

        // Lanes nav item is the selected one.
        var lanes = shell.Sections.Single(s => s.Section == ProjectSection.Lanes);
        Assert.That(lanes.IsSelected, Is.True);
    }

    [Test]
    public void SelectSection_Secrets_swaps_to_placeholder()
    {
        var shell = MakeShell(out _);

        shell.SelectSectionCommand.Execute(ProjectSection.Secrets);

        Assert.That(shell.SelectedSection, Is.EqualTo(ProjectSection.Secrets));
        Assert.That(shell.CurrentContent, Is.InstanceOf<SectionPlaceholderViewModel>());
        Assert.That(((SectionPlaceholderViewModel)shell.CurrentContent!).Title, Is.EqualTo("Secrets"));

        Assert.That(shell.Sections.Single(s => s.Section == ProjectSection.Secrets).IsSelected, Is.True);
        Assert.That(shell.Sections.Single(s => s.Section == ProjectSection.Lanes).IsSelected, Is.False);
    }

    [Test]
    public void Switching_back_to_Lanes_keeps_the_same_cached_content()
    {
        var shell = MakeShell(out _);
        var firstLanes = shell.CurrentContent;

        shell.SelectSectionCommand.Execute(ProjectSection.Secrets);
        shell.SelectSectionCommand.Execute(ProjectSection.Lanes);

        Assert.That(shell.CurrentContent, Is.SameAs(firstLanes));
    }

    [Test]
    public void BackCommand_invokes_go_home_callback()
    {
        var shell = MakeShell(out var wentBack);

        shell.BackCommand.Execute(null);

        Assert.That(wentBack[0], Is.True);
    }
}

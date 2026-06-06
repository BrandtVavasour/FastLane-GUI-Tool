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
    public void SelectSection_Secrets_swaps_to_secrets_section()
    {
        var shell = MakeShell(out _);

        shell.SelectSectionCommand.Execute(ProjectSection.Secrets);

        Assert.That(shell.SelectedSection, Is.EqualTo(ProjectSection.Secrets));
        Assert.That(shell.CurrentContent, Is.InstanceOf<SecretsSectionViewModel>());
        Assert.That(((SecretsSectionViewModel)shell.CurrentContent!).Secrets, Is.Not.Empty);

        Assert.That(shell.Sections.Single(s => s.Section == ProjectSection.Secrets).IsSelected, Is.True);
        Assert.That(shell.Sections.Single(s => s.Section == ProjectSection.Lanes).IsSelected, Is.False);
    }

    [Test]
    public void SelectSection_Signing_and_TestFlight_swap_to_real_section_vms()
    {
        var shell = MakeShell(out _);

        shell.SelectSectionCommand.Execute(ProjectSection.Signing);
        Assert.That(shell.CurrentContent, Is.InstanceOf<SigningSectionViewModel>());
        Assert.That(((SigningSectionViewModel)shell.CurrentContent!).Certificates, Is.Not.Empty);

        shell.SelectSectionCommand.Execute(ProjectSection.TestFlight);
        Assert.That(shell.CurrentContent, Is.InstanceOf<TestFlightSectionViewModel>());
        Assert.That(((TestFlightSectionViewModel)shell.CurrentContent!).Testers, Is.Not.Empty);

        shell.SelectSectionCommand.Execute(ProjectSection.Screenshots);
        Assert.That(shell.CurrentContent, Is.InstanceOf<ScreenshotsSectionViewModel>());
        Assert.That(((ScreenshotsSectionViewModel)shell.CurrentContent!).Devices, Is.Not.Empty);

        shell.SelectSectionCommand.Execute(ProjectSection.BuildTest);
        Assert.That(shell.CurrentContent, Is.InstanceOf<BuildTestSectionViewModel>());
        Assert.That(((BuildTestSectionViewModel)shell.CurrentContent!).Results, Is.Not.Empty);
    }

    [Test]
    public void Store_and_release_sections_resolve_to_listing_and_placeholders()
    {
        var shell = MakeShell(out _);

        shell.SelectSectionCommand.Execute(ProjectSection.StoreListing);
        Assert.That(shell.CurrentContent, Is.InstanceOf<StoreListingSectionViewModel>());

        shell.SelectSectionCommand.Execute(ProjectSection.WhatsNew);
        Assert.That(shell.CurrentContent, Is.InstanceOf<SectionPlaceholderViewModel>());

        shell.SelectSectionCommand.Execute(ProjectSection.Release);
        Assert.That(shell.CurrentContent, Is.InstanceOf<SectionPlaceholderViewModel>());

        // All three new sidebar entries exist.
        Assert.That(shell.Sections.Any(s => s.Section == ProjectSection.StoreListing), Is.True);
        Assert.That(shell.Sections.Any(s => s.Section == ProjectSection.WhatsNew), Is.True);
        Assert.That(shell.Sections.Any(s => s.Section == ProjectSection.Release), Is.True);
    }

    [Test]
    public void Screenshots_and_BuildTest_run_actions_reflect_lane_presence_for_real_fastfiles()
    {
        var shell = MakeShell(out _);

        // The fixture iOS Fastfile defines a `screenshots` lane → Run snapshot enabled.
        shell.SelectSectionCommand.Execute(ProjectSection.Screenshots);
        var shots = (ScreenshotsSectionViewModel)shell.CurrentContent!;
        Assert.That(shots.CanRunSnapshot, Is.True);

        // The fixture has no `test`/`build` iOS lanes → both Build & Test actions disabled.
        shell.SelectSectionCommand.Execute(ProjectSection.BuildTest);
        var bt = (BuildTestSectionViewModel)shell.CurrentContent!;
        Assert.That(bt.CanRunTests, Is.False);
        Assert.That(bt.CanBuild, Is.False);
    }

    [Test]
    public void Section_run_actions_reflect_lane_presence_for_real_fastfiles()
    {
        var shell = MakeShell(out _);

        shell.SelectSectionCommand.Execute(ProjectSection.Signing);
        var signing = (SigningSectionViewModel)shell.CurrentContent!;
        // The fixture iOS Fastfile defines sync_certificates → action enabled.
        Assert.That(signing.CanRunMatch, Is.True);

        shell.SelectSectionCommand.Execute(ProjectSection.TestFlight);
        var tf = (TestFlightSectionViewModel)shell.CurrentContent!;
        // The fixture iOS Fastfile defines beta → action enabled.
        Assert.That(tf.CanDistribute, Is.True);
    }

    [Test]
    public void RunLane_switches_to_Lanes_so_the_live_run_panel_shows()
    {
        var shell = MakeShell(out _);
        shell.SelectSectionCommand.Execute(ProjectSection.Signing);

        // Invoking the wired action (no secrets satisfied → run is gated, but the
        // section navigation still flips to Lanes so the user sees the run panel).
        shell.RunLane(LaunchFast.Core.Models.Platform.Ios, "sync_certificates");

        Assert.That(shell.SelectedSection, Is.EqualTo(ProjectSection.Lanes));
        Assert.That(shell.CurrentContent, Is.InstanceOf<ProjectDetailViewModel>());
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

using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using LaunchFast.App.ViewModels;
using LaunchFast.App.Views;
using LaunchFast.Core.Scanning;

namespace LaunchFast.App.Tests;

/// <summary>
/// Rendered-frame snapshot coverage for every built view. Each view is hosted in a
/// headless window with the real Skia backend, laid out with deterministic fixture
/// data, and its frame captured. Assertions are deliberately robust (sized,
/// non-empty, real drawn content) rather than pixel-exact — see
/// <see cref="SnapshotHarness"/>. Every test runs against both the Light and Dark
/// theme variants via NUnit <see cref="ThemeCase"/> sources, and emits a PNG to the
/// test output's <c>snapshots/</c> directory for manual inspection.
/// </summary>
public class ViewSnapshotTests
{
    // Theme variants exercised by every snapshot. NUnit's [AvaloniaTest] cannot be
    // combined with [TestCaseSource] for variant fan-out, so each test calls
    // ForEachTheme to render both Light and Dark inside one Avalonia test run.
    static readonly (string Suffix, ThemeVariant Variant)[] Themes =
    {
        ("light", ThemeVariant.Light),
        ("dark", ThemeVariant.Dark),
    };

    static void ForEachTheme(string name, Func<Control> makeView)
    {
        foreach (var (_, variant) in Themes)
        {
            var frame = SnapshotHarness.Render(name, makeView(), variant);

            Assert.That(frame, Is.Not.Null, $"{name}/{variant}: null frame");
            Assert.That(frame.Size.Width, Is.GreaterThan(0), $"{name}/{variant}: zero width");
            Assert.That(frame.Size.Height, Is.GreaterThan(0), $"{name}/{variant}: zero height");
            Assert.That(SnapshotHarness.HasDrawnContent(frame), Is.True,
                $"{name}/{variant}: frame had no drawn content (flat fill only).");
        }
    }

    [AvaloniaTest]
    public void LauncherView_snapshot()
    {
        // Two fixture project cards so the launcher grid has real content to draw.
        var store = NewStore(out _);
        store.AddRecent(TestProjects.MakeFlutterProjectWithRealFastfiles("alpha").Path);
        store.AddRecent(TestProjects.MakeFlutterProjectWithRealFastfiles("bravo").Path);

        ForEachTheme("LauncherView", () =>
        {
            var vm = new LauncherViewModel(store);
            vm.Load();
            Assert.That(vm.Cards, Has.Count.GreaterThanOrEqualTo(2));
            return new LauncherView { DataContext = vm };
        });
    }

    [AvaloniaTest]
    public void ProjectDetailView_lanes_snapshot()
    {
        ForEachTheme("ProjectDetailView", () =>
        {
            var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
            var vm = new ProjectDetailViewModel(project, new FakeSecretStore(), new RecordingPtyFactory());
            vm.Load();
            return new ProjectDetailView { DataContext = vm };
        });
    }

    [AvaloniaTest]
    public void FastfileSectionView_snapshot()
    {
        ForEachTheme("FastfileSectionView", () =>
        {
            var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
            var vm = new FastfileSectionViewModel(project, runLane: (_, _) => { });
            return new FastfileSectionView { DataContext = vm };
        });
    }

    [AvaloniaTest]
    public void FastfileSectionView_source_view_snapshot()
    {
        ForEachTheme("FastfileSectionViewSource", () =>
        {
            var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
            var vm = new FastfileSectionViewModel(project, runLane: (_, _) => { })
            {
                View = FastfileView.Source,
            };
            return new FastfileSectionView { DataContext = vm };
        });
    }

    [AvaloniaTest]
    public void SecretsSectionView_snapshot()
    {
        ForEachTheme("SecretsSectionView", () =>
        {
            var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
            var vm = new SecretsSectionViewModel(project, new FakeSecretStore(), _ => null);
            return new SecretsSectionView { DataContext = vm };
        });
    }

    [AvaloniaTest]
    public void SigningSectionView_snapshot()
    {
        ForEachTheme("SigningSectionView", () =>
        {
            var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
            var vm = new SigningSectionViewModel(project, hasSyncLane: () => true);
            return new SigningSectionView { DataContext = vm };
        });
    }

    [AvaloniaTest]
    public void TestFlightSectionView_snapshot()
    {
        ForEachTheme("TestFlightSectionView", () =>
        {
            var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
            var vm = new TestFlightSectionViewModel(project, hasBetaLane: () => true);
            return new TestFlightSectionView { DataContext = vm };
        });
    }

    [AvaloniaTest]
    public void ScreenshotsSectionView_snapshot()
    {
        ForEachTheme("ScreenshotsSectionView", () =>
        {
            var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
            var vm = new ScreenshotsSectionViewModel(project, hasScreenshotsLane: () => true);
            return new ScreenshotsSectionView { DataContext = vm };
        });
    }

    [AvaloniaTest]
    public void BuildTestSectionView_snapshot()
    {
        ForEachTheme("BuildTestSectionView", () =>
        {
            var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
            var vm = new BuildTestSectionViewModel(project,
                hasTestLane: () => true, hasBuildLane: () => true);
            return new BuildTestSectionView { DataContext = vm };
        });
    }

    [AvaloniaTest]
    public void StoreListingSectionView_snapshot()
    {
        ForEachTheme("StoreListingSectionView", () =>
        {
            var project = TestProjects.MakeProjectWithStoreMetadata();
            var vm = new StoreListingSectionViewModel(project);
            return new StoreListingSectionView { DataContext = vm };
        });
    }

    [AvaloniaTest]
    public void WhatsNewSectionView_snapshot()
    {
        ForEachTheme("WhatsNewSectionView", () =>
        {
            var project = TestProjects.MakeProjectWithStoreMetadata();
            var vm = new WhatsNewSectionViewModel(project);
            return new WhatsNewSectionView { DataContext = vm };
        });
    }

    [AvaloniaTest]
    public void ReleaseSectionView_snapshot()
    {
        ForEachTheme("ReleaseSectionView", () =>
        {
            var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
            var vm = new ReleaseSectionViewModel(project,
                runLane: (_, _) => { }, hasLane: (_, _) => true);
            return new ReleaseSectionView { DataContext = vm };
        });
    }

    [AvaloniaTest]
    public void SecretsDialog_snapshot()
    {
        // SecretsDialog is itself a Window, so it is rendered directly rather than
        // hosted inside the snapshot harness's wrapper window.
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
        var keys = new[] { "APPLE_ID", "MATCH_GIT_URL", "FASTLANE_TEAM_ID" };

        foreach (var (_, variant) in Themes)
        {
            var vm = new SecretsDialogViewModel(new FakeSecretStore(), project.Path, keys);
            var dialog = new SecretsDialog
            {
                DataContext = vm,
                RequestedThemeVariant = variant,
            };
            var frame = SnapshotHarness.RenderWindow("SecretsDialog", dialog, variant);

            Assert.That(frame, Is.Not.Null, $"SecretsDialog/{variant}: null frame");
            Assert.That(frame.Size.Width, Is.GreaterThan(0));
            Assert.That(frame.Size.Height, Is.GreaterThan(0));
            Assert.That(SnapshotHarness.HasDrawnContent(frame), Is.True,
                $"SecretsDialog/{variant}: frame had no drawn content.");
        }
    }

    static ProjectStore NewStore(out string path)
    {
        path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        return new ProjectStore(path);
    }
}

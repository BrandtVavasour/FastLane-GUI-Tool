using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using LaunchFast.App.ViewModels;
using LaunchFast.App.Views;
using LaunchFast.Core.Models;
using LaunchFast.Core.Scanning;
using LaunchFast.Core.Stores;

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
            var (project, profilesDir) = TestProjects.MakeProjectWithIosSigning(expiringSoon: true);
            var store = new FakeSecretStore();
            store.Set(project.Path, "MATCH_PASSWORD", "x");
            var reader = new LaunchFast.Core.Signing.IosSigningReader(() =>
                "  1) A1B2C3D4E5F60718293A4B5C6D7E8F90A1B2C3D4 \"Apple Distribution: JAB Technologies (7F8G9H)\"\n" +
                "  2) 0011223344556677889900AABBCCDDEEFF001122 \"Apple Development: Dev (ABCDEF)\"");
            var vm = new SigningSectionViewModel(project, store, reader, profilesDir,
                hasSyncLane: () => true,
                readProcessEnv: name => name == "MATCH_GIT_URL" ? "git@x" : null);
            return new SigningSectionView { DataContext = vm };
        });
    }

    [AvaloniaTest]
    public void TestFlightSectionView_snapshot()
    {
        ForEachTheme("TestFlightSectionView", () =>
        {
            var (project, _) = TestProjects.MakeProjectWithIosSigning();
            var asc = new FakeAscClient(
                new StoreStatus(Destination.TestFlight, true, null, null),
                new TestFlightInfo(
                    new BuildInfo("1.4.2", "18", "VALID", false, "expires in 90 days",
                        "Focus testing on the new onboarding flow."),
                    [
                        new BetaGroup("App Store Connect Users", true, 2),
                        new BetaGroup("Beta Crew", false, 3),
                    ],
                    [
                        new BetaTester("Ada", "Lovelace", "ada@example.com", "Installed", "App Store Connect Users"),
                        new BetaTester("Grace", "Hopper", "grace@example.com", "Accepted", "Beta Crew"),
                        new BetaTester("Alan", "Turing", "alan@example.com", "Invited", "Beta Crew"),
                    ]));
            var vm = new TestFlightSectionViewModel(project, asc, hasBetaLane: () => true);
            vm.LoadAsync().GetAwaiter().GetResult();
            return new TestFlightSectionView { DataContext = vm };
        });
    }

    [AvaloniaTest]
    public void ScreenshotsSectionView_snapshot()
    {
        ForEachTheme("ScreenshotsSectionView", () =>
        {
            var project = TestProjects.MakeProjectWithSnapshotConfig();
            var vm = new ScreenshotsSectionViewModel(project, hasScreenshotsLane: () => true);
            return new ScreenshotsSectionView { DataContext = vm };
        });
    }

    [AvaloniaTest]
    public void BuildTestSectionView_snapshot()
    {
        ForEachTheme("BuildTestSectionView", () =>
        {
            var project = TestProjects.MakeProjectWithBuildTestConfig();
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
    public void RunHistorySectionView_snapshot()
    {
        ForEachTheme("RunHistorySectionView", () =>
        {
            var now = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
            var store = new LaunchFast.Core.History.RunHistoryStore(
                Path.Combine(Path.GetTempPath(), "lf-historysnap-" + Guid.NewGuid().ToString("N")));
            const string proj = "/projects/snapshot";

            store.Append(proj, new LaunchFast.Core.History.RunRecord
            {
                Platform = LaunchFast.Core.Models.Platform.Ios,
                LaneName = "beta",
                Status = LaunchFast.Core.History.RunStatus.Succeeded,
                StartedUtc = now.AddMinutes(-12),
                Duration = TimeSpan.FromSeconds(107),
                ResultSummary = "1.4.2 (18) → TestFlight · submitted for review",
                OutputTail = "▸ build_app\nBUILD SUCCEEDED\n▸ upload_to_testflight",
            });
            store.Append(proj, new LaunchFast.Core.History.RunRecord
            {
                Platform = LaunchFast.Core.Models.Platform.Android,
                LaneName = "release",
                Status = LaunchFast.Core.History.RunStatus.Failed,
                ExitCode = 1,
                StartedUtc = now.AddHours(-2),
                Duration = TimeSpan.FromSeconds(38),
                ResultSummary = "Failed · supply: 403 from Google Play API",
                OutputTail = "▸ gradle bundleRelease\nBUILD SUCCESSFUL in 32s\n✗ 403 permission denied",
            });

            var vm = new RunHistorySectionViewModel(store, proj,
                runLane: (_, _) => { }, nowUtc: () => now);
            // Expand a row so the mini-terminal detail renders in the snapshot.
            vm.ToggleRowCommand.Execute(vm.Rows[1]);
            return new RunHistorySectionView { DataContext = vm };
        });
    }

    [AvaloniaTest]
    public void RunHistorySectionView_empty_snapshot()
    {
        ForEachTheme("RunHistorySectionViewEmpty", () =>
        {
            var store = new LaunchFast.Core.History.RunHistoryStore(
                Path.Combine(Path.GetTempPath(), "lf-historysnap-" + Guid.NewGuid().ToString("N")));
            var vm = new RunHistorySectionViewModel(store, "/projects/empty",
                nowUtc: () => DateTime.UtcNow);
            return new RunHistorySectionView { DataContext = vm };
        });
    }

    [AvaloniaTest]
    public void AndroidSigningSectionView_snapshot()
    {
        ForEachTheme("AndroidSigningSectionView", () =>
        {
            var project = TestProjects.MakeProjectWithAndroidSigning();
            var store = new FakeSecretStore();
            store.Set(project.Path, "KEYSTORE_PASSWORD", "x");
            var vm = new AndroidSigningSectionViewModel(
                project, store, runLane: (_, _) => { },
                hasBuildLane: () => true,
                readProcessEnv: name => name == "PLAY_JSON_KEY" ? "/play.json" : null);
            return new AndroidSigningSectionView { DataContext = vm };
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

    [AvaloniaTest]
    public void SetupWizardView_review_snapshot()
    {
        ForEachTheme("SetupWizardView", () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "lf-wizsnap-" + Guid.NewGuid().ToString("N"), "demo");
            Directory.CreateDirectory(Path.Combine(root, "ios"));
            Directory.CreateDirectory(Path.Combine(root, "android"));
            File.WriteAllText(Path.Combine(root, "pubspec.yaml"), "name: demo\nversion: 1.0.0+1\n");
            var project = LaunchFast.Core.Scanning.ProjectScanner.TryScanRoot(root)!;

            var vm = LaunchFast.App.ViewModels.Wizard.SetupWizardViewModel.ForInstall(project);
            vm.Platforms.Ios = true;
            vm.Platforms.Android = false;
            vm.Next();                                  // iOS
            vm.Ios.BundleId = "com.acme.demo";
            vm.Ios.TeamId = "ABCDE12345";
            vm.Next();                                  // Lanes
            vm.Next();                                  // Review (builds the plan)
            Assert.That(vm.Review.Files, Is.Not.Empty);
            return new LaunchFast.App.Views.SetupWizardView { DataContext = vm };
        });
    }

    static ProjectStore NewStore(out string path)
    {
        path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        return new ProjectStore(path);
    }
}

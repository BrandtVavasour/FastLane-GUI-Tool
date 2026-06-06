using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaunchFast.Core.Models;

namespace LaunchFast.App.ViewModels;

/// <summary>
/// Content view-model for a project's "Screenshots" section (snapshot + frameit).
///
/// SHELL / PLACEHOLDER: a faithful themed shell. The device list, language chips,
/// capture settings and frameit preview are <b>illustrative</b> placeholder data
/// (see <see cref="IsPlaceholder"/>) shown until a real snapshot backend lands.
/// The only genuinely wired action is "Run snapshot", which triggers the project's
/// real <c>screenshots</c> fastlane lane (disabled when that lane is absent).
/// </summary>
public partial class ScreenshotsSectionViewModel : ObservableObject
{
    readonly Action<Platform, string>? _runLane;
    readonly Func<bool> _hasScreenshotsLane;

    public ScreenshotsSectionViewModel(
        Project project,
        Action<Platform, string>? runLane = null,
        Func<bool>? hasScreenshotsLane = null)
    {
        _ = project; // reserved for a future real snapshot backend
        _runLane = runLane;
        _hasScreenshotsLane = hasScreenshotsLane ?? (() => false);

        Devices = new ObservableCollection<SnapshotDeviceRow>
        {
            new("iPhone 6.9″", "iPhone 15 Pro Max · 1290×2796", On: true),
            new("iPhone 6.5″", "iPhone 11 Pro Max · 1242×2688", On: true),
            new("iPhone 5.5″", "iPhone 8 Plus · 1242×2208", On: false),
            new("iPad Pro 13″", "iPad Pro M4 · 2064×2752", On: true),
            new("iPad Pro 11″", "iPad Pro M4 · 1668×2420", On: false),
        };

        Languages = new ObservableCollection<LanguageChip>
        {
            new("en-US"),
            new("ja"),
        };

        Schemes = new ObservableCollection<string>
        {
            "VendingTrackerUITests",
            "SnapshotTests",
        };

        Backgrounds = new ObservableCollection<SwatchOption>
        {
            new("#1E8E64", Selected: true),
            new("#0A84FF", Selected: false),
            new("#111111", Selected: false),
            new("#FFFFFF", Selected: false),
        };

        SelectedScheme = Schemes[0];
    }

    /// <summary>Marks this section's list data as illustrative placeholder, not live.</summary>
    public bool IsPlaceholder => true;

    // ---- subbar (placeholder) ------------------------------------------------
    public string ChipText => "snapshot + frameit";
    public string SyncedText => "last run 3d ago";

    // ---- left column collections ---------------------------------------------
    public ObservableCollection<SnapshotDeviceRow> Devices { get; }
    public string DevicesSelectedText => $"{Devices.Count(d => d.On)} selected";

    public ObservableCollection<LanguageChip> Languages { get; }
    public ObservableCollection<string> Schemes { get; }

    [ObservableProperty]
    private string? _selectedScheme;

    public string LaunchArguments => "-FASTLANE_SNAPSHOT YES -ui_testing";

    // ---- frameit (placeholder) -----------------------------------------------
    [ObservableProperty]
    private bool _frameScreenshots = true;

    public ObservableCollection<SwatchOption> Backgrounds { get; }

    public string FrameTitle => "Track every machine in real time";

    /// <summary>Selected background colour (drives the right-pane framed preview).</summary>
    public string SelectedBackground =>
        Backgrounds.FirstOrDefault(b => b.Selected)?.Hex ?? "#1E8E64";

    /// <summary>Single-select the given background swatch.</summary>
    [RelayCommand]
    void SelectBackground(SwatchOption? option)
    {
        if (option is null) return;
        foreach (var b in Backgrounds)
            b.Selected = b == option;
        OnPropertyChanged(nameof(SelectedBackground));
    }

    // ---- right column (placeholder big-number / preview) ---------------------
    public string QueuedCount => "30";
    public string QueuedCaption => "screenshots queued\n5 screens × 3 devices × 2 languages";
    public string PreviewNote => "Live preview · iPhone 6.9″ · en-US";

    /// <summary>True when the project exposes the <c>screenshots</c> iOS lane.</summary>
    public bool CanRunSnapshot => _hasScreenshotsLane();

    /// <summary>Runs the real <c>screenshots</c> lane via the shell's lane runner.</summary>
    [RelayCommand]
    void RunSnapshot()
    {
        if (!CanRunSnapshot) return;
        _runLane?.Invoke(Platform.Ios, "screenshots");
    }
}

/// <summary>Illustrative device-selection row for the Screenshots shell.</summary>
public sealed partial class SnapshotDeviceRow : ObservableObject
{
    public SnapshotDeviceRow(string Name, string Sub, bool On)
    {
        this.Name = Name;
        this.Sub = Sub;
        _on = On;
    }

    public string Name { get; }
    public string Sub { get; }

    [ObservableProperty]
    private bool _on;
}

/// <summary>Illustrative language chip for the Screenshots shell.</summary>
public sealed record LanguageChip(string Code);

/// <summary>Illustrative background-colour swatch for the frameit preview.</summary>
public sealed partial class SwatchOption : ObservableObject
{
    public SwatchOption(string Hex, bool Selected)
    {
        this.Hex = Hex;
        _selected = Selected;
    }

    public string Hex { get; }

    [ObservableProperty]
    private bool _selected;
}

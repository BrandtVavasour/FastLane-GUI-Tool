using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaunchFast.Core.Models;
using LaunchFast.Core.Screenshots;

namespace LaunchFast.App.ViewModels;

/// <summary>
/// Content view-model for a project's "Screenshots" section (fastlane snapshot +
/// frameit).
///
/// <para>The device list, language chips, capture scheme/launch-arguments, frameit
/// flag/title/background and the captured-screenshots gallery are all <b>real</b>,
/// read from <c>ios/fastlane/Snapfile</c>, <c>Framefile(.json)</c> and the captured
/// PNGs on disk via <see cref="SnapshotConfigReader"/>. The only remaining
/// illustrative element is the framed-preview mock in the right pane
/// (see <see cref="PreviewIsIllustrative"/>).</para>
///
/// <para>When no Snapfile is present, devices are shown all-off with a note and the
/// languages/gallery are derived from the captured screenshots on disk.</para>
///
/// "Run snapshot" triggers the project's real <c>screenshots</c> fastlane lane
/// (disabled when that lane is absent).
/// </summary>
public partial class ScreenshotsSectionViewModel : ObservableObject
{
    /// <summary>
    /// A standard superset of iOS device classes surfaced as toggles. A device is
    /// "on" when it appears in the Snapfile (matched against these display names).
    /// </summary>
    static readonly (string Name, string Sub, string[] Match)[] StandardDevices =
    {
        ("iPhone 6.9″", "iPhone 16 Pro Max · 1320×2868",
            new[] { "16 Pro Max", "15 Pro Max", "6.9" }),
        ("iPhone 6.5″", "iPhone 11 Pro Max · 1242×2688",
            new[] { "11 Pro Max", "XS Max", "6.5" }),
        ("iPhone 5.5″", "iPhone 8 Plus · 1242×2208",
            new[] { "8 Plus", "7 Plus", "5.5" }),
        ("iPad Pro 13″", "iPad Pro (12.9/13-inch) · 2064×2752",
            new[] { "iPad Pro (12.9", "iPad Pro 13", "iPad Pro (13" }),
        ("iPad Pro 11″", "iPad Pro (11-inch) · 1668×2420",
            new[] { "iPad Pro (11", "iPad Pro 11" }),
    };

    readonly Action<Platform, string>? _runLane;
    readonly Func<bool> _hasScreenshotsLane;
    readonly SnapshotConfig _config;

    public ScreenshotsSectionViewModel(
        Project project,
        Action<Platform, string>? runLane = null,
        Func<bool>? hasScreenshotsLane = null,
        Func<Project, SnapshotConfig>? readConfig = null)
    {
        _runLane = runLane;
        _hasScreenshotsLane = hasScreenshotsLane ?? (() => false);
        _config = (readConfig ?? SnapshotConfigReader.Read)(project);

        Devices = new ObservableCollection<SnapshotDeviceRow>(BuildDevices(_config));
        Languages = new ObservableCollection<LanguageChip>(
            _config.Languages.Select(l => new LanguageChip(l)));

        CapturedLocales = new ObservableCollection<string>(
            _config.Captured
                .Select(g => g.Locale)
                .Where(l => l.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase));

        Screenshots = new ObservableCollection<string>();
        SelectedLocale = CapturedLocales.FirstOrDefault();
        RefreshGallery();
    }

    static IEnumerable<SnapshotDeviceRow> BuildDevices(SnapshotConfig config)
    {
        foreach (var (name, sub, match) in StandardDevices)
        {
            var on = config.Devices.Any(d =>
                match.Any(m => d.Contains(m, StringComparison.OrdinalIgnoreCase)));
            yield return new SnapshotDeviceRow(name, sub, on);
        }
    }

    // ---- subbar --------------------------------------------------------------

    public string ChipText => _config.FrameitEnabled ? "snapshot + frameit" : "snapshot";

    /// <summary>Honest summary of the discovered config state.</summary>
    public string SyncedText =>
        _config.HasSnapfile ? "Snapfile configured" : "no Snapfile";

    // ---- devices -------------------------------------------------------------

    public ObservableCollection<SnapshotDeviceRow> Devices { get; }

    public int SelectedDeviceCount => Devices.Count(d => d.On);

    public string DevicesSelectedText => $"{SelectedDeviceCount} selected";

    /// <summary>True when the project has no Snapfile (so devices are unconfigured).</summary>
    public bool NoSnapfile => !_config.HasSnapfile;

    public string DevicesNote =>
        _config.HasSnapfile
            ? "Toggles reflect the Snapfile device list."
            : "No Snapfile — devices not configured.";

    // ---- languages -----------------------------------------------------------

    public ObservableCollection<LanguageChip> Languages { get; }

    public bool HasLanguages => Languages.Count > 0;

    public string LanguagesNote =>
        _config.HasSnapfile
            ? "From Snapfile."
            : Languages.Count > 0
                ? "Derived from captured screenshots."
                : "No languages configured or captured.";

    // ---- capture (scheme + launch args) --------------------------------------

    public string? Scheme => _config.Scheme;

    public string SchemeText => _config.Scheme ?? "Not set in Snapfile";

    public string LaunchArguments => _config.LaunchArguments ?? string.Empty;

    public string LaunchArgumentsText =>
        string.IsNullOrEmpty(_config.LaunchArguments)
            ? "Not set in Snapfile"
            : _config.LaunchArguments;

    // ---- frameit -------------------------------------------------------------

    public bool FrameitEnabled => _config.FrameitEnabled;

    public string FrameitText =>
        _config.FrameitEnabled ? "Enabled (Framefile present)" : "Not configured";

    public string FrameTitle => _config.FrameTitle ?? string.Empty;

    public bool HasFrameTitle => !string.IsNullOrEmpty(_config.FrameTitle);

    public string FrameBackground => _config.FrameBackground ?? string.Empty;

    public bool HasFrameBackground => !string.IsNullOrEmpty(_config.FrameBackground);

    // ---- captured gallery ----------------------------------------------------

    public ObservableCollection<string> CapturedLocales { get; }

    public bool HasMultipleLocales => CapturedLocales.Count > 1;

    [ObservableProperty]
    private string? _selectedLocale;

    partial void OnSelectedLocaleChanged(string? value) => RefreshGallery();

    /// <summary>The PNG paths for the selected locale (or all locales when none selected).</summary>
    public ObservableCollection<string> Screenshots { get; }

    public bool HasScreenshots => Screenshots.Count > 0;

    /// <summary>Honest "N of M" / "N captured" count for the gallery header.</summary>
    public string CapturedCountText
    {
        get
        {
            var total = _config.CapturedCount;
            if (total == 0)
            {
                return "No screenshots captured yet";
            }

            if (SelectedLocale is { } locale && CapturedLocales.Count > 1)
            {
                return $"{Screenshots.Count} of {total} captured · {locale}";
            }

            return $"{total} captured";
        }
    }

    public string EmptyGalleryText =>
        "Run the snapshot lane to capture screenshots — none on disk yet.";

    void RefreshGallery()
    {
        Screenshots.Clear();

        IEnumerable<string> paths = SelectedLocale is { } locale
            ? _config.Captured
                .Where(g => string.Equals(g.Locale, locale, StringComparison.OrdinalIgnoreCase))
                .SelectMany(g => g.Paths)
            : _config.Captured.SelectMany(g => g.Paths);

        foreach (var path in paths)
        {
            Screenshots.Add(path);
        }

        OnPropertyChanged(nameof(HasScreenshots));
        OnPropertyChanged(nameof(CapturedCountText));
    }

    /// <summary>Select a captured-screenshot locale to show in the gallery.</summary>
    [RelayCommand]
    void SelectLocale(string? locale)
    {
        if (locale is not null)
        {
            SelectedLocale = locale;
        }
    }

    // ---- right column: honest queued summary + illustrative preview ----------

    /// <summary>
    /// The captured count, or — when nothing is captured — the number of locales
    /// configured. Honest: we don't know the screens-per-run count.
    /// </summary>
    public string QueuedCount =>
        _config.CapturedCount > 0
            ? _config.CapturedCount.ToString()
            : Languages.Count.ToString();

    /// <summary>
    /// Honest caption: when shots exist, "captured"; otherwise the configured matrix
    /// dimensions (languages × devices) without fabricating a screens-per-run number.
    /// </summary>
    public string QueuedCaption
    {
        get
        {
            if (_config.CapturedCount > 0)
            {
                return $"screenshots captured\n{CapturedLocales.Count} locale(s) on disk";
            }

            var langs = Languages.Count;
            var devices = SelectedDeviceCount;
            return devices > 0
                ? $"languages configured\n{langs} languages × {devices} devices"
                : $"languages configured\n{langs} languages";
        }
    }

    /// <summary>The framed-preview mock is illustrative, not generated from real frameit.</summary>
    public bool PreviewIsIllustrative => true;

    public string PreviewNote =>
        HasFrameTitle
            ? $"Illustrative frameit preview · title: {FrameTitle}"
            : "Illustrative frameit preview";

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

/// <summary>A device-selection row: on = present in the Snapfile device list.</summary>
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

/// <summary>A configured/captured language chip for the Screenshots section.</summary>
public sealed record LanguageChip(string Code);

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaunchFast.Core.Models;
using LaunchFast.Core.Stores;

namespace LaunchFast.App.ViewModels;

/// <summary>
/// Content view-model for a project's "Store Listing" section. Shows REAL on-disk
/// fastlane store metadata: for the selected <see cref="Platform"/> + locale it
/// reads the deliver (iOS) / supply (Android) text fields and screenshot PNGs via
/// <see cref="StoreMetadataReader"/>, surfacing each text field with a live char
/// count against the store's limit (and an over-limit flag).
///
/// Read-only for this pass — editing/Save is a follow-up. When no metadata exists on
/// disk for the selected platform, <see cref="IsEmpty"/> drives an empty state.
/// </summary>
public partial class StoreListingSectionViewModel : ObservableObject
{
    readonly Project _project;

    public StoreListingSectionViewModel(Project project)
    {
        _project = project;

        Platforms = new ObservableCollection<PlatformOption>
        {
            new(Platform.Ios, "App Store · iOS"),
            new(Platform.Android, "Google Play · Android"),
        };

        // Default to iOS when it has metadata; otherwise fall back to whichever
        // platform has any on-disk locales (iOS first).
        _platform = HasMetadata(Platform.Ios) || !HasMetadata(Platform.Android)
            ? Platform.Ios
            : Platform.Android;

        Fields = new ObservableCollection<StoreFieldViewModel>();
        Devices = new ObservableCollection<DeviceOption>();
        Locales = new ObservableCollection<string>();
        Screenshots = new ObservableCollection<string>();

        ReloadLocales();
        Reload();
    }

    /// <summary>The project name, shown as the subbar title.</summary>
    public string Name => _project.Name;

    // ---- platform ------------------------------------------------------------

    public ObservableCollection<PlatformOption> Platforms { get; }

    [ObservableProperty]
    private Platform _platform;

    partial void OnPlatformChanged(Platform value)
    {
        ReloadLocales();
        ReloadDevices();
        Reload();
        OnPropertyChanged(nameof(IsIos));
        OnPropertyChanged(nameof(PlatformMetaTitle));
    }

    /// <summary>Two-way helpers so the segmented control can bind per-option.</summary>
    public bool IsIosSelected
    {
        get => Platform == Platform.Ios;
        set { if (value) Platform = Platform.Ios; }
    }

    public bool IsAndroidSelected
    {
        get => Platform == Platform.Android;
        set { if (value) Platform = Platform.Android; }
    }

    public bool IsIos => Platform == Platform.Ios;

    // ---- locale --------------------------------------------------------------

    public ObservableCollection<string> Locales { get; }

    [ObservableProperty]
    private string? _selectedLocale;

    partial void OnSelectedLocaleChanged(string? value)
    {
        Reload();
        OnPropertyChanged(nameof(PlatformMetaTitle));
    }

    /// <summary>"7 of 39 localized" style note (M is illustrative store-supported count).</summary>
    public string LocalizedNote => $"{Locales.Count} localized";

    public string LastSyncedText => "Read from fastlane metadata on disk";

    // ---- devices (screenshot segmented control) ------------------------------

    public ObservableCollection<DeviceOption> Devices { get; }

    [ObservableProperty]
    private DeviceOption? _selectedDevice;

    partial void OnSelectedDeviceChanged(DeviceOption? value)
    {
        foreach (var d in Devices)
        {
            d.IsSelected = d == value;
        }
        Reload();
        OnPropertyChanged(nameof(ScreenshotCountText));
    }

    /// <summary>Selects a screenshot device (drives the segmented control).</summary>
    [RelayCommand]
    void SelectDevice(DeviceOption? device)
    {
        if (device is not null)
        {
            SelectedDevice = device;
        }
    }

    // ---- content -------------------------------------------------------------

    public ObservableCollection<StoreFieldViewModel> Fields { get; }

    public ObservableCollection<string> Screenshots { get; }

    [ObservableProperty]
    private bool _isEmpty;

    public string EmptyStateText =>
        "No store metadata found under fastlane/metadata — run deliver/supply or add it.";

    public string PlatformMetaTitle =>
        $"{(IsIos ? "App Store metadata" : "Google Play listing")} · {SelectedLocale ?? "—"}";

    public string ScreenshotCountText
    {
        get
        {
            var label = SelectedDevice?.Title ?? "Screenshots";
            return $"{label} · {Screenshots.Count} uploaded";
        }
    }

    public string ScreenshotsEmptyText =>
        SelectedLocale is null
            ? "No locale on disk"
            : "No screenshots on disk for this locale/device.";

    public bool HasScreenshots => Screenshots.Count > 0;

    // ---- reload --------------------------------------------------------------

    bool HasMetadata(Platform platform) =>
        StoreMetadataReader.Locales(_project, platform).Count > 0;

    void ReloadLocales()
    {
        var locales = StoreMetadataReader.Locales(_project, Platform);

        Locales.Clear();
        foreach (var l in locales)
        {
            Locales.Add(l);
        }

        // Preserve the current locale when still available, else pick the first.
        if (SelectedLocale is null || !Locales.Contains(SelectedLocale))
        {
            SelectedLocale = Locales.FirstOrDefault();
        }

        OnPropertyChanged(nameof(LocalizedNote));
    }

    void ReloadDevices()
    {
        Devices.Clear();
        if (IsIos)
        {
            Devices.Add(new DeviceOption("iPhone", "iPhone"));
            Devices.Add(new DeviceOption("iPad", "iPad"));
        }
        else
        {
            Devices.Add(new DeviceOption("Phone", "Android phone"));
            Devices.Add(new DeviceOption("Tablet", "Android tablet"));
        }
        SelectedDevice = Devices.FirstOrDefault();
    }

    /// <summary>Re-reads the listing for the current platform/locale and rebuilds fields.</summary>
    void Reload()
    {
        Fields.Clear();
        Screenshots.Clear();

        var locale = SelectedLocale;
        if (locale is null)
        {
            IsEmpty = true;
            RaiseDerived();
            return;
        }

        var listing = StoreMetadataReader.ReadListing(_project, Platform, locale);

        foreach (var field in BuildFields(listing))
        {
            Fields.Add(field);
        }

        foreach (var path in listing.ScreenshotPaths)
        {
            Screenshots.Add(path);
        }

        // Empty when this platform has no locale folders at all on disk.
        IsEmpty = Locales.Count == 0;
        RaiseDerived();
    }

    void RaiseDerived()
    {
        OnPropertyChanged(nameof(PlatformMetaTitle));
        OnPropertyChanged(nameof(ScreenshotCountText));
        OnPropertyChanged(nameof(HasScreenshots));
    }

    static IEnumerable<StoreFieldViewModel> BuildFields(StoreListing l)
    {
        if (l.Platform == Platform.Ios)
        {
            yield return new StoreFieldViewModel("App name", null, l.Name, StoreFieldLimits.AppStoreName);
            yield return new StoreFieldViewModel("Subtitle", "iOS", l.Subtitle, StoreFieldLimits.AppStoreSubtitle);
            yield return new StoreFieldViewModel("Promotional text", "iOS", l.PromotionalText, StoreFieldLimits.AppStorePromotionalText, multiline: true);
            yield return new StoreFieldViewModel("Keywords", "iOS", l.Keywords, StoreFieldLimits.AppStoreKeywords, multiline: true);
            yield return new StoreFieldViewModel("Full description", null, l.FullDescription, StoreFieldLimits.AppStoreDescription, multiline: true);
            yield return new StoreFieldViewModel("Marketing URL", null, l.MarketingUrl, counted: false);
            yield return new StoreFieldViewModel("Support URL", null, l.SupportUrl, counted: false);
            yield return new StoreFieldViewModel("Privacy Policy URL", null, l.PrivacyUrl, counted: false);
        }
        else
        {
            yield return new StoreFieldViewModel("Title", null, l.Name, StoreFieldLimits.PlayTitle);
            yield return new StoreFieldViewModel("Short description", "Android", l.ShortDescription, StoreFieldLimits.PlayShortDescription, multiline: true);
            yield return new StoreFieldViewModel("Full description", null, l.FullDescription, StoreFieldLimits.PlayFullDescription, multiline: true);
            yield return new StoreFieldViewModel("Promo video URL", "Android", l.VideoUrl, counted: false);
        }
    }
}

/// <summary>Platform option for the listing's segmented control.</summary>
public sealed record PlatformOption(Platform Platform, string Title);

/// <summary>A screenshot-device option for the gallery's segmented control.</summary>
public sealed partial class DeviceOption : ObservableObject
{
    public DeviceOption(string key, string title)
    {
        Key = key;
        Title = title;
    }

    public string Key { get; }
    public string Title { get; }

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// One metadata field row: label, optional platform badge ("iOS"/"Android"), the
/// on-disk value (may be null/empty), and — for counted fields — its length against
/// the store limit with warn / over-limit flags.
/// </summary>
public sealed class StoreFieldViewModel
{
    public StoreFieldViewModel(
        string label, string? badge, string? value, int max = 0,
        bool multiline = false, bool counted = true)
    {
        Label = label;
        Badge = badge;
        Value = value ?? string.Empty;
        Max = max;
        Multiline = multiline;
        Counted = counted && max > 0;
    }

    public string Label { get; }
    public string? Badge { get; }
    public bool HasBadge => !string.IsNullOrEmpty(Badge);
    public string Value { get; }
    public bool IsBlank => Value.Length == 0;
    public int Max { get; }
    public bool Multiline { get; }
    public bool Counted { get; }

    public int Length => Value.Length;

    /// <summary>"123 / 170" counter text (empty when the field isn't counted).</summary>
    public string CounterText => Counted ? $"{Length} / {Max}" : string.Empty;

    public bool IsOverLimit => Counted && Length > Max;

    /// <summary>Near the limit (≥ 88%) but not yet over — drives an amber counter.</summary>
    public bool IsNearLimit => Counted && !IsOverLimit && Length >= Max * 0.88;
}

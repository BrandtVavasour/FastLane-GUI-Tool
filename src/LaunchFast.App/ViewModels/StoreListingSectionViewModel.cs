using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaunchFast.Core.Models;
using LaunchFast.Core.Screenshots;
using LaunchFast.Core.Stores;

namespace LaunchFast.App.ViewModels;

/// <summary>
/// Content view-model for a project's "Store Listing" section. Shows REAL on-disk
/// fastlane store metadata: for the selected <see cref="Platform"/> + locale it
/// reads the deliver (iOS) / supply (Android) text fields and screenshot PNGs via
/// <see cref="StoreMetadataReader"/>, surfacing each text field with a live char
/// count against the store's limit (and an over-limit flag).
///
/// EDITABLE: each field's text is two-way bound; editing flips <see cref="IsDirty"/>.
/// <see cref="SaveCommand"/> writes the current values to the real deliver/supply
/// <c>.txt</c> files via <see cref="StoreMetadataWriter"/> (allowed even when
/// over-limit — the over-limit state is shown but does not block Save), then re-reads
/// to refresh the baseline. <see cref="DiscardCommand"/> re-reads from disk to revert.
/// Switching platform/locale auto-discards any unsaved edits (kept simple). When no
/// metadata exists on disk for the selected platform, <see cref="IsEmpty"/> drives an
/// empty state.
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
        ReloadDevices();
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
        RefreshScreenshots();
        OnPropertyChanged(nameof(ScreenshotCountText));
        OnPropertyChanged(nameof(HasScreenshots));
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

    /// <summary>All of the current locale's screenshot paths, before device filtering.</summary>
    readonly List<string> _allShots = [];

    [ObservableProperty]
    private bool _isEmpty;

    /// <summary>True when any field differs from the last-loaded on-disk snapshot.</summary>
    [ObservableProperty]
    private bool _isDirty;

    partial void OnIsDirtyChanged(bool value)
    {
        SaveCommand.NotifyCanExecuteChanged();
        DiscardCommand.NotifyCanExecuteChanged();
    }

    /// <summary>True when any field's value currently exceeds its store limit.</summary>
    public bool HasOverLimit => Fields.Any(f => f.IsOverLimit);

    /// <summary>Status / error line shown under the toolbar after a save attempt.</summary>
    [ObservableProperty]
    private string? _saveStatus;

    [ObservableProperty]
    private bool _saveFailed;

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
        foreach (var existing in Fields)
        {
            existing.Changed -= OnFieldChanged;
        }

        Fields.Clear();
        Screenshots.Clear();
        _allShots.Clear();

        var locale = SelectedLocale;
        if (locale is null)
        {
            IsEmpty = true;
            IsDirty = false;
            RaiseDerived();
            return;
        }

        var listing = StoreMetadataReader.ReadListing(_project, Platform, locale);

        foreach (var field in BuildFields(listing))
        {
            field.Changed += OnFieldChanged;
            Fields.Add(field);
        }

        _allShots.AddRange(listing.ScreenshotPaths);
        RefreshScreenshots();

        // Empty when this platform has no locale folders at all on disk.
        IsEmpty = Locales.Count == 0;
        // A fresh load is the clean baseline.
        IsDirty = false;
        SaveStatus = null;
        SaveFailed = false;
        RaiseDerived();
    }

    /// <summary>
    /// Rebuilds <see cref="Screenshots"/> from <see cref="_allShots"/>, keeping only the
    /// paths in the selected device class (all paths when no device is selected).
    /// </summary>
    void RefreshScreenshots()
    {
        Screenshots.Clear();

        var key = SelectedDevice?.Key;
        foreach (var path in _allShots)
        {
            if (key is null || ScreenshotDevice.InClass(path, key))
            {
                Screenshots.Add(path);
            }
        }
    }

    /// <summary>A field's text or limit-state changed — recompute dirty + over-limit.</summary>
    void OnFieldChanged()
    {
        IsDirty = Fields.Any(f => f.IsDirty);
        OnPropertyChanged(nameof(HasOverLimit));
    }

    // ---- save / discard ------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanSave))]
    void Save()
    {
        var locale = SelectedLocale;
        if (locale is null)
        {
            return;
        }

        try
        {
            StoreMetadataWriter.WriteListing(_project, Platform, locale, BuildListing(locale));
            SaveFailed = false;
            SaveStatus = HasOverLimit
                ? "Saved to fastlane metadata (some fields exceed the store limit)."
                : "Saved to fastlane metadata.";
            // Re-read so the on-disk values become the new clean baseline.
            Reload();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SaveFailed = true;
            SaveStatus = $"Save failed: {ex.Message}";
        }
    }

    bool CanSave() => IsDirty;

    [RelayCommand(CanExecute = nameof(CanDiscard))]
    void Discard() => Reload();

    bool CanDiscard() => IsDirty;

    /// <summary>Builds a <see cref="StoreListing"/> from the current field values.</summary>
    StoreListing BuildListing(string locale)
    {
        string? Field(string label) =>
            Fields.FirstOrDefault(f => f.Label == label)?.Value ?? string.Empty;

        return Platform == Platform.Ios
            ? new StoreListing(
                Platform.Ios, locale,
                Name: Field("App name"),
                Subtitle: Field("Subtitle"),
                ShortDescription: null,
                PromotionalText: Field("Promotional text"),
                Keywords: Field("Keywords"),
                FullDescription: Field("Full description"),
                ReleaseNotes: null, // owned by the What's New section
                MarketingUrl: Field("Marketing URL"),
                SupportUrl: Field("Support URL"),
                PrivacyUrl: Field("Privacy Policy URL"),
                VideoUrl: null,
                ScreenshotPaths: Array.Empty<string>())
            : new StoreListing(
                Platform.Android, locale,
                Name: Field("Title"),
                Subtitle: null,
                ShortDescription: Field("Short description"),
                PromotionalText: null,
                Keywords: null,
                FullDescription: Field("Full description"),
                ReleaseNotes: null, // owned by the What's New section
                MarketingUrl: null,
                SupportUrl: null,
                PrivacyUrl: null,
                VideoUrl: Field("Promo video URL"),
                ScreenshotPaths: Array.Empty<string>());
    }

    void RaiseDerived()
    {
        OnPropertyChanged(nameof(PlatformMetaTitle));
        OnPropertyChanged(nameof(ScreenshotCountText));
        OnPropertyChanged(nameof(HasScreenshots));
        OnPropertyChanged(nameof(HasOverLimit));
    }

    IEnumerable<StoreFieldViewModel> BuildFields(StoreListing l)
    {
        // The store name is often not synced to disk (managed in App Store Connect /
        // Play Console), so fall back to the native/pubspec app name for display so the
        // field isn't blank on load. The fallback becomes the field's baseline too, so
        // it does NOT mark the field dirty.
        var nameFallback = l.Name ?? AppDisplayName.Read(_project, l.Platform);

        if (l.Platform == Platform.Ios)
        {
            yield return new StoreFieldViewModel("App name", null, nameFallback, StoreFieldLimits.AppStoreName);
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
            yield return new StoreFieldViewModel("Title", null, nameFallback, StoreFieldLimits.PlayTitle);
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
/// editable value, and — for counted fields — its length against the store limit with
/// warn / over-limit flags. <see cref="Value"/> is two-way bound; editing recomputes
/// the counter/over-limit state, sets <see cref="IsDirty"/> (vs the loaded baseline),
/// and raises <see cref="Changed"/> so the parent can recompute its own dirty signal.
/// </summary>
public sealed partial class StoreFieldViewModel : ObservableObject
{
    readonly string _baseline;

    public StoreFieldViewModel(
        string label, string? badge, string? value, int max = 0,
        bool multiline = false, bool counted = true)
    {
        Label = label;
        Badge = badge;
        _baseline = value ?? string.Empty;
        _value = _baseline;
        Max = max;
        Multiline = multiline;
        Counted = counted && max > 0;
    }

    /// <summary>Raised whenever the field's value changes (drives parent dirty state).</summary>
    public event Action? Changed;

    public string Label { get; }
    public string? Badge { get; }
    public bool HasBadge => !string.IsNullOrEmpty(Badge);

    [ObservableProperty]
    private string _value;

    partial void OnValueChanged(string value)
    {
        OnPropertyChanged(nameof(IsBlank));
        OnPropertyChanged(nameof(Length));
        OnPropertyChanged(nameof(CounterText));
        OnPropertyChanged(nameof(IsOverLimit));
        OnPropertyChanged(nameof(IsNearLimit));
        OnPropertyChanged(nameof(IsDirty));
        Changed?.Invoke();
    }

    public bool IsBlank => Value.Length == 0;
    public int Max { get; }
    public bool Multiline { get; }
    public bool Counted { get; }

    /// <summary>True when the value has been edited away from the loaded baseline.</summary>
    public bool IsDirty => !string.Equals(Value, _baseline, StringComparison.Ordinal);

    public int Length => Value.Length;

    /// <summary>"123 / 170" counter text (empty when the field isn't counted).</summary>
    public string CounterText => Counted ? $"{Length} / {Max}" : string.Empty;

    public bool IsOverLimit => Counted && Length > Max;

    /// <summary>Near the limit (≥ 88%) but not yet over — drives an amber counter.</summary>
    public bool IsNearLimit => Counted && !IsOverLimit && Length >= Max * 0.88;
}

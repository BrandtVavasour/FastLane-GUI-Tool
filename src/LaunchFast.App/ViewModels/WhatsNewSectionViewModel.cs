using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaunchFast.Core.Models;
using LaunchFast.Core.Stores;

namespace LaunchFast.App.ViewModels;

/// <summary>
/// Content view-model for a project's "What's New" (release-notes) section. Reads
/// REAL on-disk release notes via <see cref="StoreMetadataReader"/>: for the
/// selected <see cref="Platform"/> + locale it surfaces the iOS
/// <c>release_notes.txt</c> / Android latest changelog, with a live char counter
/// against the store's what's-new limit (App Store 4000, Play 500), and the
/// fastlane path the text is written to.
///
/// EDITABLE: the editor textarea is two-way bound; editing flips <see cref="IsDirty"/>.
/// <see cref="SaveChangelogCommand"/> writes the notes to the real file via
/// <see cref="StoreMetadataWriter.WriteReleaseNotes"/> — iOS
/// <c>release_notes.txt</c> / Android <c>changelogs/&lt;versionCode&gt;.txt</c> (the
/// selected version's build) — then re-reads to refresh the baseline and the locale
/// dot. <see cref="DiscardCommand"/> re-reads from disk to revert. Switching
/// platform/version/locale auto-discards unsaved edits (kept simple). The version rail
/// lists the project's current parsed <see cref="Version"/> plus, on Android, any
/// extra versionCodes that have changelog files on disk (real). When no locales exist
/// on disk, <see cref="IsEmpty"/> drives an empty state.
/// </summary>
public partial class WhatsNewSectionViewModel : ObservableObject
{
    readonly Project _project;

    public WhatsNewSectionViewModel(Project project)
    {
        _project = project;

        Versions = new ObservableCollection<ReleaseVersionViewModel>();
        Locales = new ObservableCollection<LocaleTabViewModel>();

        // Default to iOS when it has metadata; otherwise fall back to whichever
        // platform has any on-disk locales (iOS first).
        _platform = HasMetadata(Platform.Ios) || !HasMetadata(Platform.Android)
            ? Platform.Ios
            : Platform.Android;

        ReloadVersions();
        ReloadLocales();
        Reload();
    }

    /// <summary>The project name, shown as the subbar title.</summary>
    public string Name => _project.Name;

    /// <summary>The project's pubspec version string (e.g. "1.4.2+18"), or null.</summary>
    public string? Version => _project.Version;

    // ---- platform ------------------------------------------------------------

    [ObservableProperty]
    private Platform _platform;

    partial void OnPlatformChanged(Platform value)
    {
        ReloadVersions();
        ReloadLocales();
        Reload();
        OnPropertyChanged(nameof(IsIos));
        OnPropertyChanged(nameof(StoreName));
        OnPropertyChanged(nameof(IntroText));
        OnPropertyChanged(nameof(CharLimit));
        OnPropertyChanged(nameof(FastlanePath));
    }

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

    public string StoreName => IsIos ? "App Store" : "Google Play";

    /// <summary>What's-new char limit for the platform (App Store 4000, Play 500).</summary>
    public int CharLimit => IsIos
        ? StoreFieldLimits.AppStoreReleaseNotes
        : StoreFieldLimits.PlayWhatsNew;

    // ---- version rail --------------------------------------------------------

    public ObservableCollection<ReleaseVersionViewModel> Versions { get; }

    [ObservableProperty]
    private ReleaseVersionViewModel? _selectedVersion;

    partial void OnSelectedVersionChanged(ReleaseVersionViewModel? value)
    {
        foreach (var v in Versions) v.IsSelected = v == value;
        Reload();
        OnPropertyChanged(nameof(EditorTitle));
        OnPropertyChanged(nameof(FastlanePath));
    }

    public string EditorTitle =>
        $"What's new in {SelectedVersion?.Name ?? Version ?? "—"}";

    /// <summary>Selects a version from the rail (drives the rail's row buttons).</summary>
    [RelayCommand]
    void SelectVersion(ReleaseVersionViewModel? version)
    {
        if (version is not null) SelectedVersion = version;
    }

    // ---- locale tabs ---------------------------------------------------------

    public ObservableCollection<LocaleTabViewModel> Locales { get; }

    [ObservableProperty]
    private LocaleTabViewModel? _selectedLocale;

    partial void OnSelectedLocaleChanged(LocaleTabViewModel? value)
    {
        foreach (var l in Locales) l.IsSelected = l == value;
        Reload();
        OnPropertyChanged(nameof(LocaleFieldLabel));
        OnPropertyChanged(nameof(FastlanePath));
    }

    public string LocaleFieldLabel => SelectedLocale?.Code ?? "—";

    /// <summary>Selects a locale tab (drives the tab buttons).</summary>
    [RelayCommand]
    void SelectLocale(LocaleTabViewModel? locale)
    {
        if (locale is not null) SelectedLocale = locale;
    }

    public string IntroText =>
        $"Release notes shown to users on the {StoreName} listing for this version. " +
        "Read from — and saved back to — fastlane metadata on disk.";

    // ---- editor content ------------------------------------------------------

    /// <summary>The last-loaded on-disk note text — the clean baseline for dirtiness.</summary>
    string _noteBaseline = string.Empty;

    /// <summary>
    /// The release-notes text for the selected version + locale. Two-way bound and
    /// persisted by <see cref="SaveChangelogCommand"/>.
    /// </summary>
    [ObservableProperty]
    private string _noteText = string.Empty;

    partial void OnNoteTextChanged(string value)
    {
        OnPropertyChanged(nameof(CounterText));
        OnPropertyChanged(nameof(IsOverLimit));
        OnPropertyChanged(nameof(IsNearLimit));
        if (SelectedLocale is not null)
        {
            SelectedLocale.HasText = !string.IsNullOrWhiteSpace(value);
        }
        IsDirty = !string.Equals(value, _noteBaseline, StringComparison.Ordinal);
    }

    /// <summary>True when the editor text differs from the last-loaded on-disk value.</summary>
    [ObservableProperty]
    private bool _isDirty;

    partial void OnIsDirtyChanged(bool value)
    {
        SaveChangelogCommand.NotifyCanExecuteChanged();
        DiscardCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Status / error line shown in the toolbar after a save attempt.</summary>
    [ObservableProperty]
    private string? _saveStatus;

    [ObservableProperty]
    private bool _saveFailed;

    public string CounterText => $"{NoteText.Length} / {CharLimit}";

    public bool IsOverLimit => NoteText.Length > CharLimit;

    public bool IsNearLimit => !IsOverLimit && NoteText.Length >= CharLimit * 0.88;

    // ---- save / discard ------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanSave))]
    void SaveChangelog()
    {
        var locale = SelectedLocale?.Code;
        if (locale is null)
        {
            return;
        }

        // Android writes to changelogs/<versionCode>.txt; iOS ignores the code.
        var versionCode = IsIos ? null : SelectedVersion?.Build;
        if (!IsIos && string.IsNullOrWhiteSpace(versionCode))
        {
            SaveFailed = true;
            SaveStatus = "No versionCode for the selected version — cannot write a changelog.";
            return;
        }

        try
        {
            StoreMetadataWriter.WriteReleaseNotes(_project, Platform, locale, versionCode, NoteText);
            SaveFailed = false;
            SaveStatus = IsOverLimit
                ? $"Saved (exceeds the {StoreName} {CharLimit}-char limit)."
                : "Saved to fastlane metadata.";
            // Re-read so the baseline + locale dots reflect what's on disk now.
            Reload();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            SaveFailed = true;
            SaveStatus = $"Save failed: {ex.Message}";
        }
    }

    bool CanSave() => IsDirty;

    [RelayCommand(CanExecute = nameof(CanDiscard))]
    void Discard() => Reload();

    bool CanDiscard() => IsDirty;

    /// <summary>The fastlane file path the selected version+locale writes to.</summary>
    public string FastlanePath
    {
        get
        {
            var code = SelectedLocale?.Code ?? "<locale>";
            if (IsIos)
            {
                return $"fastlane/metadata/{code}/release_notes.txt";
            }

            var build = SelectedVersion?.Build ?? "<versionCode>";
            return $"fastlane/metadata/android/{code}/changelogs/{build}.txt";
        }
    }

    // ---- empty state ---------------------------------------------------------

    [ObservableProperty]
    private bool _isEmpty;

    public string EmptyStateText =>
        "No store metadata found under fastlane/metadata — run deliver/supply or add release notes.";

    // ---- reload --------------------------------------------------------------

    bool HasMetadata(Platform platform) =>
        StoreMetadataReader.Locales(_project, platform).Count > 0;

    void ReloadVersions()
    {
        Versions.Clear();

        var (name, build) = ParseVersion(_project.Version);

        // Current version from pubspec (real, derived).
        if (name is not null)
        {
            Versions.Add(new ReleaseVersionViewModel(name, build, "Current", isDerived: true));
        }

        // Android: surface any extra on-disk changelog versionCodes (real).
        if (!IsIos)
        {
            foreach (var code in OnDiskAndroidVersionCodes())
            {
                if (code == build) continue; // already shown as current
                Versions.Add(new ReleaseVersionViewModel(
                    name ?? "—", code, "On disk", isDerived: true));
            }
        }

        // Nothing on disk and no parseable version → one placeholder row.
        if (Versions.Count == 0)
        {
            Versions.Add(new ReleaseVersionViewModel("—", null, "Sample", isDerived: false));
        }

        if (SelectedVersion is null || !Versions.Contains(SelectedVersion))
        {
            SelectedVersion = Versions.FirstOrDefault();
        }
    }

    /// <summary>Distinct Android changelog versionCodes present on disk across locales.</summary>
    IReadOnlyList<string> OnDiskAndroidVersionCodes()
    {
        var codes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var locale in StoreMetadataReader.Locales(_project, Platform.Android))
        {
            foreach (var c in StoreMetadataReader.ChangelogVersionCodes(_project, locale))
            {
                codes.Add(c);
            }
        }
        return codes.OrderByDescending(c => c, StringComparer.Ordinal).ToList();
    }

    void ReloadLocales()
    {
        var prior = SelectedLocale?.Code;
        Locales.Clear();

        foreach (var code in StoreMetadataReader.Locales(_project, Platform))
        {
            Locales.Add(new LocaleTabViewModel(code));
        }

        var restore = prior is not null
            ? Locales.FirstOrDefault(l => l.Code == prior)
            : null;
        SelectedLocale = restore ?? Locales.FirstOrDefault();
    }

    /// <summary>Re-reads the release notes for the current platform/version/locale.</summary>
    void Reload()
    {
        IsEmpty = Locales.Count == 0;

        var locale = SelectedLocale?.Code;
        if (locale is null)
        {
            _noteBaseline = string.Empty;
            NoteText = string.Empty;
            IsDirty = false;
            SaveStatus = null;
            SaveFailed = false;
            RaiseDerived();
            return;
        }

        var listing = ReadNotes(locale);
        _noteBaseline = listing;
        NoteText = listing;
        IsDirty = false;
        SaveStatus = null;
        SaveFailed = false;

        // Refresh the full/empty dot for every loaded locale tab (against the
        // currently-selected version, so the Android dot tracks the active build).
        foreach (var tab in Locales)
        {
            tab.HasText = !string.IsNullOrWhiteSpace(ReadNotes(tab.Code));
        }

        RaiseDerived();
    }

    /// <summary>
    /// Reads the release notes for a locale under the currently-selected version: iOS
    /// → <c>release_notes.txt</c>; Android → the changelog for the selected version's
    /// build (falling back to the latest changelog when the version has no build).
    /// </summary>
    string ReadNotes(string locale)
    {
        var listing = StoreMetadataReader.ReadListing(_project, Platform, locale);
        if (IsIos)
        {
            return listing.ReleaseNotes ?? string.Empty;
        }

        var build = SelectedVersion?.Build;
        if (string.IsNullOrWhiteSpace(build))
        {
            // No specific build → reader's latest-changelog view.
            return listing.ReleaseNotes ?? string.Empty;
        }

        return ReadAndroidChangelog(locale, build) ?? string.Empty;
    }

    /// <summary>The on-disk Android changelog text for an explicit versionCode, or null.</summary>
    string? ReadAndroidChangelog(string locale, string versionCode)
    {
        if (_project.AndroidFastlaneDir is not { } android)
        {
            return null;
        }

        var path = Path.Combine(
            android, "metadata", "android", locale, "changelogs", versionCode + ".txt");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var text = File.ReadAllText(path).Trim();
            return text.Length == 0 ? null : text;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    void RaiseDerived()
    {
        OnPropertyChanged(nameof(CounterText));
        OnPropertyChanged(nameof(IsOverLimit));
        OnPropertyChanged(nameof(IsNearLimit));
        OnPropertyChanged(nameof(FastlanePath));
        OnPropertyChanged(nameof(LocaleFieldLabel));
        OnPropertyChanged(nameof(EditorTitle));
    }

    /// <summary>
    /// Splits a pubspec version ("1.4.2+18") into ("1.4.2", "18"). Returns (null,
    /// null) when the input is blank/unparseable for the name portion.
    /// </summary>
    public static (string? Name, string? Build) ParseVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return (null, null);

        var plus = version.IndexOf('+');
        if (plus < 0) return (version.Trim(), null);

        var name = version[..plus].Trim();
        var build = version[(plus + 1)..].Trim();
        return (name.Length == 0 ? null : name, build.Length == 0 ? null : build);
    }
}

/// <summary>
/// One version in the "Release history" rail. Name is the semantic version
/// ("1.4.2"); Build is the build / versionCode. <see cref="IsDerived"/> marks rows
/// sourced from real on-disk/pubspec data vs an illustrative placeholder.
/// </summary>
public sealed partial class ReleaseVersionViewModel : ObservableObject
{
    public ReleaseVersionViewModel(string name, string? build, string tag, bool isDerived)
    {
        Name = name;
        Build = build;
        Tag = tag;
        IsDerived = isDerived;
    }

    public string Name { get; }
    public string? Build { get; }
    public string Tag { get; }
    public bool IsDerived { get; }

    public string BuildMeta => Build is null ? "no build" : $"build {Build} · versionCode {Build}";

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>A locale tab: its store code and whether release notes exist for it.</summary>
public sealed partial class LocaleTabViewModel : ObservableObject
{
    public LocaleTabViewModel(string code)
    {
        Code = code;
    }

    public string Code { get; }

    [ObservableProperty]
    private bool _hasText;

    [ObservableProperty]
    private bool _isSelected;
}

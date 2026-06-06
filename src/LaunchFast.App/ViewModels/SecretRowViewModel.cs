using CommunityToolkit.Mvvm.ComponentModel;

namespace LaunchFast.App.ViewModels;

/// <summary>
/// The resolution source a secret value comes from, in precedence order. Mirrors
/// the order a lane run resolves env: process/CI env → .env files → Keychain.
/// </summary>
public enum SecretSource
{
    None,
    CiEnv,
    EnvFile,
    Keychain,
}

/// <summary>
/// One secret row on the Secrets &amp; credentials screen: a key, its human
/// description, where (if anywhere) it's currently resolved from, and a masked /
/// revealable display of the value. Never logs or persists the raw value.
/// </summary>
public partial class SecretRowViewModel : ObservableObject
{
    const string Mask = "••••••••";

    readonly string? _value;

    public SecretRowViewModel(string name, string description, SecretSource source, string? value)
    {
        Name = name;
        Description = description;
        Source = source;
        _value = value;
    }

    /// <summary>The env var name (mono), e.g. <c>MATCH_PASSWORD</c>.</summary>
    public string Name { get; }

    /// <summary>Short human description for well-known keys, or a generic fallback.</summary>
    public string Description { get; }

    /// <summary>Where the value resolves from (drives the source chip + precedence).</summary>
    public SecretSource Source { get; }

    /// <summary>True when the secret is satisfied from some source.</summary>
    public bool IsSet => Source != SecretSource.None;

    /// <summary>True when the secret is not satisfied anywhere.</summary>
    public bool IsMissing => !IsSet;

    /// <summary>Status pill text ("SET" / "MISSING"); pre-uppercased for the pill style.</summary>
    public string StatusText => IsSet ? "SET" : "MISSING";

    /// <summary>Source chip text: "CI env" / ".env" / "Keychain" / "—".</summary>
    public string SourceText => Source switch
    {
        SecretSource.CiEnv => "CI env",
        SecretSource.EnvFile => ".env",
        SecretSource.Keychain => "Keychain",
        _ => "—",
    };

    /// <summary>Action button label: existing secrets are edited, missing ones added.</summary>
    public string ActionText => IsSet ? "Edit" : "Add";

    /// <summary>True when the action button should use the accent (primary) style.</summary>
    public bool ActionIsAccent => IsMissing;

    // ---- Icon variant (.lic class) -------------------------------------------
    public bool IconBlue => Source == SecretSource.Keychain || Source == SecretSource.CiEnv;
    public bool IconNeutral => Source == SecretSource.EnvFile;
    public bool IconRed => IsMissing;

    // ---- Masked / revealed display -------------------------------------------
    [ObservableProperty]
    private bool _isRevealed;

    /// <summary>
    /// What the value column shows: a generic mask when set, the real value when
    /// revealed, and a dash when missing. The raw value lives only in this VM.
    /// </summary>
    public string Display => !IsSet
        ? "—"
        : IsRevealed && _value is not null ? _value : Mask;

    partial void OnIsRevealedChanged(bool value) => OnPropertyChanged(nameof(Display));

    /// <summary>Sets the reveal state (used by the toolbar "Reveal all" toggle).</summary>
    public void SetRevealed(bool revealed) => IsRevealed = revealed;
}

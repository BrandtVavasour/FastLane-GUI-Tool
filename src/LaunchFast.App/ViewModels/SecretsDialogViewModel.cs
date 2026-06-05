using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LaunchFast.Core.Env;

namespace LaunchFast.App.ViewModels;

/// <summary>One editable secret row: a key label plus the entered value.</summary>
public partial class SecretEntryViewModel(string key) : ObservableObject
{
    public string Key => key;

    [ObservableProperty]
    private string _value = "";
}

/// <summary>
/// Backs the secrets dialog: one row per missing key. <see cref="Save"/> writes
/// every non-empty value into the supplied <see cref="ISecretStore"/>.
/// </summary>
public sealed class SecretsDialogViewModel
{
    readonly ISecretStore _secrets;
    readonly string _projectId;

    public SecretsDialogViewModel(ISecretStore secrets, string projectId, IEnumerable<string> keys)
    {
        _secrets = secrets;
        _projectId = projectId;
        foreach (var key in keys) Entries.Add(new SecretEntryViewModel(key));
    }

    public ObservableCollection<SecretEntryViewModel> Entries { get; } = new();

    /// <summary>Persist every non-empty entry. Returns the count written.</summary>
    public int Save()
    {
        var written = 0;
        foreach (var entry in Entries)
        {
            if (string.IsNullOrEmpty(entry.Value)) continue;
            _secrets.Set(_projectId, entry.Key, entry.Value);
            written++;
        }
        return written;
    }
}

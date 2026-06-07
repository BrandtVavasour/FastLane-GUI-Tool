using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LaunchFast.Core.Running;

namespace LaunchFast.App.ViewModels;

/// <summary>
/// State for a single in-flight (or finished) lane run: the streamed output
/// lines, whether it's running, the latest action line, an elapsed-time string,
/// and the active <see cref="RunHandle"/>.
/// </summary>
public partial class RunViewModel : ObservableObject
{
    public ObservableCollection<string> Lines { get; }

    public RunViewModel()
    {
        Lines = new ObservableCollection<string>();
        Lines.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasLines));
            OnPropertyChanged(nameof(AllText));
        };
    }

    /// <summary>True when there is any output to copy.</summary>
    public bool HasLines => Lines.Count > 0;

    /// <summary>The full run output as a single newline-joined string, for the clipboard.</summary>
    public string AllText => string.Join("\n", Lines);

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string? _currentAction;

    /// <summary>Formatted elapsed timer. Updated by the app via a DispatcherTimer; "" in tests.</summary>
    [ObservableProperty]
    private string _elapsed = "";

    /// <summary>The active run handle, or null when nothing is running.</summary>
    public RunHandle? Handle { get; set; }
}

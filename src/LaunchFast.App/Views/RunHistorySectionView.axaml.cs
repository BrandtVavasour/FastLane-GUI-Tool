using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using LaunchFast.App.ViewModels;

namespace LaunchFast.App.Views;

public partial class RunHistorySectionView : UserControl
{
    public RunHistorySectionView()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
    }

    void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is RunHistorySectionViewModel vm)
            vm.RequestExport = () => _ = ShowSaveDialogAsync(vm);
    }

    async Task ShowSaveDialogAsync(RunHistorySectionViewModel vm)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is not { } sp)
            return;

        // Derive a project-aware suggested file name from the project id stored in the VM.
        var projectSlug = System.IO.Path.GetFileName(vm.ProjectId.TrimEnd('/').TrimEnd('\\'));
        if (string.IsNullOrWhiteSpace(projectSlug))
            projectSlug = "runs";

        var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export run logs",
            SuggestedFileName = $"launchfast-runs-{projectSlug}.txt",
            DefaultExtension = "txt",
            FileTypeChoices =
            [
                new FilePickerFileType("Plain Text") { Patterns = ["*.txt"] },
                new FilePickerFileType("All Files") { Patterns = ["*"] },
            ],
        });

        if (file is null)
            return;

        var localPath = file.TryGetLocalPath();
        if (localPath is null)
            return;

        vm.WriteExport(localPath);
    }
}

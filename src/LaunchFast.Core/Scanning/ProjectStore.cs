using System.Text.Json;

namespace LaunchFast.Core.Scanning;

public sealed class ProjectStore
{
    sealed class Data { public List<string> Recents { get; set; } = []; public List<string> Workspaces { get; set; } = []; }

    readonly string _file;
    Data _data;

    public ProjectStore(string file)
    {
        _file = file;
        _data = File.Exists(file)
            ? JsonSerializer.Deserialize<Data>(File.ReadAllText(file)) ?? new Data()
            : new Data();
    }

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LaunchFast", "projects.json");

    public IReadOnlyList<string> RecentPaths => _data.Recents;
    public IReadOnlyList<string> Workspaces => _data.Workspaces;

    public void AddRecent(string path)
    {
        _data.Recents.Remove(path);
        _data.Recents.Insert(0, path);
        Save();
    }

    public void AddWorkspace(string path)
    {
        if (!_data.Workspaces.Contains(path)) { _data.Workspaces.Add(path); Save(); }
    }

    void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        File.WriteAllText(_file, JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true }));
    }
}

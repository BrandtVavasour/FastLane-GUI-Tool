namespace LaunchFast.Core.Env;

public static class EnvFileReader
{
    public static IReadOnlyDictionary<string, string> Parse(string content)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith("export ")) line = line[7..].Trim();
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim();
            var val = line[(eq + 1)..].Trim().Trim('"', '\'');
            dict[key] = val;
        }
        return dict;
    }
}

using LaunchFast.Core.Models;
using LaunchFast.Core.Stores;

namespace LaunchFast.App.Services;

/// <summary>
/// Builds a <see cref="StoreStatusProvider"/> plus the project's
/// <see cref="StoreIdentifiers"/> by discovering Appfiles on disk and resolving
/// store credentials from the project's resolved env. Lives in the App layer
/// because it touches the filesystem; never throws — missing everything yields a
/// provider with null clients (all "unavailable") and null identifiers.
/// </summary>
public static class StoreStatusFactory
{
    public static (StoreStatusProvider Provider, StoreIdentifiers Ids) Create(
        Project project, IReadOnlyDictionary<string, string> resolvedEnv)
    {
        string? iosBundle = null;
        string? androidPkg = null;
        string? androidKeyFile = null;

        var iosAppfile = ReadAppfile(project.IosFastlaneDir);
        if (iosAppfile is not null)
        {
            iosBundle = AppfileReader.AppIdentifier(iosAppfile);
        }

        var androidAppfile = ReadAppfile(project.AndroidFastlaneDir);
        if (androidAppfile is not null)
        {
            androidPkg = AppfileReader.PackageName(androidAppfile);
            androidKeyFile = AppfileReader.JsonKeyFile(androidAppfile);
        }

        var asc = BuildAsc(resolvedEnv);
        var play = BuildPlay(project, androidKeyFile, resolvedEnv);

        return (new StoreStatusProvider(asc, play), new StoreIdentifiers(iosBundle, androidPkg));
    }

    private static string? ReadAppfile(string? fastlaneDir)
    {
        if (fastlaneDir is null)
        {
            return null;
        }

        try
        {
            var path = Path.Combine(fastlaneDir, "Appfile");
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Resolves a raw <see cref="IAppStoreConnectClient"/> for the project from the
    /// resolved env's <c>APP_STORE_CONNECT_API_KEY_PATH</c>, reusing the same
    /// discovery as store status. Returns null (never throws) when no usable key is
    /// configured — callers should treat null as an honest "unavailable" state.
    /// </summary>
    public static IAppStoreConnectClient? CreateAscClient(IReadOnlyDictionary<string, string> resolvedEnv) =>
        BuildAsc(resolvedEnv);

    private static IAppStoreConnectClient? BuildAsc(IReadOnlyDictionary<string, string> env)
    {
        if (!env.TryGetValue("APP_STORE_CONNECT_API_KEY_PATH", out var path) ||
            string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return File.Exists(path) ? AppStoreConnectClient.FromKeyFile(path) : null;
        }
        catch
        {
            return null;
        }
    }

    private static IPlayStoreClient? BuildPlay(
        Project project, string? androidKeyFile, IReadOnlyDictionary<string, string> env)
    {
        var path = ResolvePlayKeyPath(project, androidKeyFile, env);
        if (path is null)
        {
            return null;
        }

        try
        {
            return File.Exists(path) ? new PlayStoreClient(path) : null;
        }
        catch
        {
            // Any failure constructing the Google client → degrade to unavailable.
            return null;
        }
    }

    private static string? ResolvePlayKeyPath(
        Project project, string? androidKeyFile, IReadOnlyDictionary<string, string> env)
    {
        // Prefer an explicit env override, then the Appfile's json_key_file.
        foreach (var key in (string[])["SUPPLY_JSON_KEY", "GOOGLE_PLAY_JSON_KEY"])
        {
            if (env.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v))
            {
                return v;
            }
        }

        if (string.IsNullOrWhiteSpace(androidKeyFile))
        {
            return null;
        }

        if (Path.IsPathRooted(androidKeyFile))
        {
            return androidKeyFile;
        }

        // Relative paths in the Appfile are resolved against the android dir.
        var androidDir = project.AndroidFastlaneDir is { } fl
            ? Directory.GetParent(fl)?.FullName
            : null;

        return androidDir is null ? androidKeyFile : Path.Combine(androidDir, androidKeyFile);
    }
}

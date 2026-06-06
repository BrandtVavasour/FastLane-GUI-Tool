using LaunchFast.Core.Models;

namespace LaunchFast.Core.Scaffolding;

public sealed record WizardAnswers(
    bool Ios, bool Android,
    string? IosBundleId, string? AppleId, string? TeamId, string? ItcTeamId, string? MatchGitUrl,
    string? AndroidPackage, string? PlayJsonKeyPath,
    IReadOnlyList<string> IosLanes,
    IReadOnlyList<string> AndroidLanes,
    IReadOnlyDictionary<string, string> DartDefines,   // dart-define name -> .env var name
    IReadOnlyList<SecretInput> Secrets);

public sealed record SecretInput(string Key, string Value);

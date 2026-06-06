namespace LaunchFast.Core.Stores;

/// <summary>
/// A point-in-time snapshot of a project's TestFlight state, assembled from the
/// App Store Connect API. All collections are non-null; an "uncredentialed" or
/// failed lookup yields <see cref="Empty"/> rather than a throw.
/// </summary>
public sealed record TestFlightInfo(
    BuildInfo? LatestBuild,
    IReadOnlyList<BetaGroup> Groups,
    IReadOnlyList<BetaTester> Testers)
{
    /// <summary>An honest empty snapshot: no build, no groups, no testers.</summary>
    public static TestFlightInfo Empty { get; } =
        new(null, [], []);
}

/// <summary>
/// The newest TestFlight build's processing + compliance state.
/// <paramref name="Version"/> is the marketing/app version (e.g. <c>1.4.2</c>) and
/// <paramref name="BuildNumber"/> is the ASC build <c>version</c> attribute
/// (e.g. <c>18</c>). <paramref name="WhatsToTest"/> carries beta test notes when present.
/// </summary>
public sealed record BuildInfo(
    string Version,
    string BuildNumber,
    string ProcessingState,
    bool? ExpiredCompliance,
    string? ExpiresText,
    string? WhatsToTest = null);

/// <summary>A TestFlight beta group (internal or external) and its tester count.</summary>
public sealed record BetaGroup(string Name, bool IsInternal, int TesterCount);

/// <summary>A single beta tester and their distribution state.</summary>
public sealed record BetaTester(
    string FirstName,
    string LastName,
    string Email,
    string State,
    string? GroupName);

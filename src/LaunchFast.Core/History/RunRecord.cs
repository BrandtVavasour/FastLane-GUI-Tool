using LaunchFast.Core.Models;

namespace LaunchFast.Core.History;

/// <summary>Terminal outcome of a lane run. Running is transient and never persisted.</summary>
public enum RunStatus { Succeeded, Failed }

/// <summary>
/// One persisted lane run in a project's audit log. Only terminal runs are stored;
/// captures the lane identity, outcome, timing and a tail of the streamed output for
/// the expandable detail. Immutable record so the store can serialize it verbatim.
/// </summary>
public sealed record RunRecord
{
    /// <summary>Stable unique id (guid string) for this run.</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public Platform Platform { get; init; }

    /// <summary>The fastlane lane name (e.g. "beta", "release").</summary>
    public string LaneName { get; init; } = "";

    public RunStatus Status { get; init; }

    public int ExitCode { get; init; }

    /// <summary>UTC instant the run started.</summary>
    public DateTime StartedUtc { get; init; }

    public TimeSpan Duration { get; init; }

    /// <summary>Who/what triggered the run. Default "Local" for a local user run.</summary>
    public string Trigger { get; init; } = "Local";

    /// <summary>Short human summary — last meaningful output line or "exit N".</summary>
    public string ResultSummary { get; init; } = "";

    /// <summary>Last ~50 lines of streamed output, for the expandable mini-terminal.</summary>
    public string OutputTail { get; init; } = "";
}

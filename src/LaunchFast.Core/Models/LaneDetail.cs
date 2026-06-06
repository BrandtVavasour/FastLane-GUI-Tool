namespace LaunchFast.Core.Models;

/// <summary>
/// A single top-level action call inside a lane body: the action <paramref name="Action"/>
/// name (e.g. <c>upload_to_testflight</c>), the fastlane <paramref name="Tool"/> it maps to
/// (e.g. "pilot"; null when unknown), and the best-effort <paramref name="Params"/> text
/// captured from inside the call's parens (possibly joined from multiple lines).
/// </summary>
public sealed record LaneStep(string Action, string? Tool, string Params);

/// <summary>
/// A public lane with its raw Ruby <paramref name="Source"/> block (from the
/// <c>lane :name do</c> line through its matching <c>end</c>) and the parsed
/// top-level <paramref name="Steps"/>.
/// </summary>
public sealed record LaneDetail(Lane Lane, string Source, IReadOnlyList<LaneStep> Steps);

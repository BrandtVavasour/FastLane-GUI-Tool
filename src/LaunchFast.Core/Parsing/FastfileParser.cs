using System.Text.RegularExpressions;
using LaunchFast.Core.Models;

namespace LaunchFast.Core.Parsing;

public static partial class FastfileParser
{
    [GeneratedRegex("""^\s*desc\s+(['"])(?<desc>.*)\1""")]
    private static partial Regex DescRegex();

    [GeneratedRegex("""^\s*lane\s+:(?<name>\w+)""")]
    private static partial Regex LaneRegex();

    [GeneratedRegex("""^\s*private_lane\s+:(?<name>\w+)""")]
    private static partial Regex PrivateLaneRegex();

    public static IReadOnlyList<Lane> Parse(string fastfileText, Platform platform)
    {
        var lanes = new List<Lane>();
        string? pendingDesc = null;

        foreach (var raw in fastfileText.Split('\n'))
        {
            var line = raw.TrimEnd('\r');

            var desc = DescRegex().Match(line);
            if (desc.Success) { pendingDesc = desc.Groups["desc"].Value.Trim(); continue; }

            if (PrivateLaneRegex().IsMatch(line)) { pendingDesc = null; continue; }

            var lane = LaneRegex().Match(line);
            if (lane.Success)
            {
                lanes.Add(new Lane(lane.Groups["name"].Value, pendingDesc ?? "", platform));
                pendingDesc = null;
            }
        }
        return lanes;
    }
}

using System.Text.RegularExpressions;

namespace LaunchFast.Core.Scaffolding;

// BlockDelta / StripComment are duplicated from FastfileParser (private helpers there).
// Extracting a shared internal type would require modifying FastfileParser's internals
// and risked breaking its ParseDetailed snapshot tests; the small duplication is
// acceptable here per the task spec.
public static class FastfileMerger
{
    public static bool HasPlatformBlock(string text, string platform) =>
        FindPlatformEnd(text, platform) is not null;

    public static string InsertLane(string text, string laneRuby, string platform)
    {
        var bounds = FindPlatformEnd(text, platform);
        if (bounds is null) return text;
        var (endLine, lines) = bounds.Value;
        var list = lines.ToList();
        list.Insert(endLine, laneRuby.TrimEnd() + "\n");
        return string.Join('\n', list);
    }

    public static string AddPlatformBlock(string text, string platformBlock) =>
        text.TrimEnd() + "\n\n" + platformBlock.Trim() + "\n";

    static (int EndLine, string[] Lines)? FindPlatformEnd(string text, string platform)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var depth = 0;
        var inBlock = false;
        for (var i = 0; i < lines.Length; i++)
        {
            if (!inBlock)
            {
                if (Regex.IsMatch(lines[i], $@"^\s*platform\s+:{platform}\s+do\b"))
                {
                    inBlock = true;
                    depth = 1;
                }
                continue;
            }
            depth += BlockDelta(lines[i]);
            if (depth == 0) return (i, lines);
        }
        return null;
    }

    static int BlockDelta(string raw)
    {
        var line = StripComment(raw).Trim();
        if (line.Length == 0) return 0;
        var delta = 0;
        if (Regex.IsMatch(line, @"^end\b")) delta--;
        if (Regex.IsMatch(line, @"^(if|unless|case|begin|while|until|for|def)\b")) delta++;
        if (Regex.IsMatch(line, @"(\bdo\b)(\s*\|[^|]*\|)?\s*$")) delta++;
        return delta;
    }

    static string StripComment(string line)
    {
        var inSingle = false;
        var inDouble = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '\'' && !inDouble) inSingle = !inSingle;
            else if (c == '"' && !inSingle) inDouble = !inDouble;
            else if (c == '#' && !inSingle && !inDouble) return line[..i];
        }
        return line;
    }
}

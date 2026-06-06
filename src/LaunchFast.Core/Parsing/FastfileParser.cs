using System.Text;
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

    // ---- detailed parsing (source block + steps) ----------------------------

    /// <summary>Matches a top-level action invocation: a bareword at line start,
    /// followed by "(", end-of-line, or whitespace (and not an assignment).</summary>
    [GeneratedRegex("""^\s*(?<action>[a-z_][a-z0-9_]*)\s*(?<rest>\(|$|\s.*)""")]
    private static partial Regex ActionRegex();

    /// <summary>Ruby keywords / non-action barewords that must never be treated as steps.</summary>
    static readonly HashSet<string> RubyKeywords = new(StringComparer.Ordinal)
    {
        "do", "end", "if", "else", "elsif", "unless", "return", "begin", "case",
        "when", "then", "while", "until", "for", "in", "rescue", "ensure", "yield",
        "next", "break", "redo", "retry", "and", "or", "not", "def", "class",
        "module", "require", "require_relative", "true", "false", "nil", "self",
    };

    /// <summary>Known fastlane action → tool family map (see task spec).</summary>
    static readonly Dictionary<string, string> ToolMap = new(StringComparer.Ordinal)
    {
        ["match"] = "match",
        ["sync_code_signing"] = "match",
        ["sync_certificates"] = "match",
        ["get_certificates"] = "match",
        ["get_provisioning_profile"] = "match",
        ["build_app"] = "gym",
        ["gym"] = "gym",
        ["upload_to_testflight"] = "pilot",
        ["pilot"] = "pilot",
        ["upload_to_app_store"] = "deliver",
        ["deliver"] = "deliver",
        ["upload_to_play_store"] = "supply",
        ["supply"] = "supply",
        ["gradle"] = "gradle",
        ["precheck"] = "precheck",
        ["capture_screenshots"] = "snapshot",
        ["snapshot"] = "snapshot",
        ["capture_ios_screenshots"] = "snapshot",
        ["frameit"] = "frameit",
        ["scan"] = "scan",
        ["run_tests"] = "scan",
        ["ensure_git_status_clean"] = "git",
        ["add_git_tag"] = "git",
        ["push_git_tags"] = "git",
        ["git_pull"] = "git",
        ["commit_version_bump"] = "git",
        ["slack"] = "notify",
    };

    /// <summary>Maps a known fastlane action to its tool family, or null if unknown.</summary>
    public static string? ToolFor(string action) =>
        ToolMap.TryGetValue(action, out var tool) ? tool : null;

    /// <summary>
    /// Parses each PUBLIC lane (skipping <c>private_lane</c>) into its raw Ruby source
    /// block and the list of top-level action steps inside the body. Pragmatic,
    /// line-based block tracking (counts do/def/if/unless/case/begin/while/until/for
    /// openers vs <c>end</c> closers, plus a "do" suffix on the same line). Total and
    /// robust: malformed input yields a best-effort result and never throws.
    /// </summary>
    public static IReadOnlyList<LaneDetail> ParseDetailed(string fastfileText, Platform platform)
    {
        var details = new List<LaneDetail>();
        if (string.IsNullOrEmpty(fastfileText)) return details;

        var lines = fastfileText.Replace("\r\n", "\n").Split('\n');
        string? pendingDesc = null;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            var desc = DescRegex().Match(line);
            if (desc.Success) { pendingDesc = desc.Groups["desc"].Value.Trim(); continue; }

            if (PrivateLaneRegex().IsMatch(line)) { pendingDesc = null; continue; }

            var laneMatch = LaneRegex().Match(line);
            if (!laneMatch.Success) continue;

            var name = laneMatch.Groups["name"].Value;
            var desc2 = pendingDesc ?? "";
            pendingDesc = null;

            // Capture the raw block from this `lane :name do` line through its matching
            // `end`. The lane header opens one block (the trailing `do`), so depth
            // starts at 1 and the lane ends when depth returns to 0.
            var block = new StringBuilder();
            block.Append(line);
            var depth = BlockDelta(line); // = 1 for a well-formed `lane :x do`

            var end = i;
            for (var j = i + 1; j < lines.Length && depth > 0; j++)
            {
                block.Append('\n').Append(lines[j]);
                depth += BlockDelta(lines[j]);
                end = j;
            }

            var source = block.ToString();
            var steps = ParseSteps(lines, i + 1, end);
            details.Add(new LaneDetail(new Lane(name, desc2, platform), source, steps));

            // Continue scanning after the lane's end so following lanes are still found.
            i = end;
        }

        return details;
    }

    /// <summary>
    /// Net block-nesting change a single line contributes: +1 per do/def/if/unless/
    /// case/begin/while/until/for opener (only when used as a statement keyword, not
    /// a postfix modifier), -1 per standalone <c>end</c>. A trailing <c>do</c> (block)
    /// also counts +1. Comment-only lines contribute 0.
    /// </summary>
    static int BlockDelta(string raw)
    {
        var line = StripComment(raw).Trim();
        if (line.Length == 0) return 0;

        var delta = 0;

        // A standalone `end` (or `end` followed by `.something`/modifier) closes a block.
        if (Regex.IsMatch(line, @"^end\b"))
            delta -= 1;

        // Leading block keywords (statement form). Postfix `... if cond` / `... unless
        // cond` modifiers are NOT openers, so only count when the keyword starts the
        // statement. `case`/`begin`/`while`/`until`/`for`/`def` similarly.
        if (Regex.IsMatch(line, @"^(if|unless|case|begin|while|until|for|def)\b"))
            delta += 1;

        // A trailing `do` (with or without a `|block args|`) opens a block on this line,
        // e.g. `lane :x do`, `Dir.chdir("..") do`, `devices.each do |d|`.
        if (Regex.IsMatch(line, @"(\bdo\b)(\s*\|[^|]*\|)?\s*$"))
            delta += 1;

        return delta;
    }

    /// <summary>Removes a trailing Ruby line comment, ignoring '#' inside quotes.</summary>
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

    /// <summary>
    /// Parses top-level action steps in a lane body spanning [bodyStart, bodyEnd)
    /// (bodyEnd is the lane's own `end` line, excluded). A step is an action call at
    /// the body's base indentation that isn't a Ruby keyword, an assignment, or a
    /// nested-block continuation. Params are captured best-effort from the call's
    /// parens (joined across continuation lines).
    /// </summary>
    static IReadOnlyList<LaneStep> ParseSteps(string[] lines, int bodyStart, int bodyEnd)
    {
        var steps = new List<LaneStep>();
        if (bodyStart >= bodyEnd) return steps;

        // Track nesting depth WITHIN the body so we only pick up top-level calls
        // (depth 0). Nested blocks (Dir.chdir(..) do ... end, devices.each do ...)
        // are skipped as step sources; their inner calls aren't top-level steps.
        var depth = 0;

        for (var i = bodyStart; i < bodyEnd; i++)
        {
            var raw = lines[i];
            var line = StripComment(raw);
            var trimmed = line.Trim();

            if (trimmed.Length == 0)
            {
                depth += BlockDelta(raw);
                continue;
            }

            if (depth == 0 && !trimmed.StartsWith("end", StringComparison.Ordinal))
            {
                var m = ActionRegex().Match(line);
                if (m.Success)
                {
                    var action = m.Groups["action"].Value;

                    var isKeyword = RubyKeywords.Contains(action);
                    var isAssignment = IsAssignment(trimmed, action);
                    var isBlockOpenerOnly =
                        Regex.IsMatch(trimmed, @"(\bdo\b)(\s*\|[^|]*\|)?\s*$");

                    if (!isKeyword && !isAssignment && !isBlockOpenerOnly)
                    {
                        var (paramsText, consumed) = CaptureParams(lines, i, bodyEnd);
                        steps.Add(new LaneStep(action, ToolFor(action), paramsText));

                        // Advance past any continuation lines the params spanned so we
                        // don't re-scan them (and don't miscount their nesting).
                        for (var k = i; k <= consumed && k < bodyEnd; k++)
                            depth += BlockDelta(lines[k]);
                        i = consumed;
                        continue;
                    }
                }
            }

            depth += BlockDelta(raw);
        }

        return steps;
    }

    /// <summary>
    /// True when <paramref name="trimmed"/> is an assignment to <paramref name="action"/>
    /// (e.g. <c>x = ...</c>, <c>x ||= ...</c>), i.e. the bareword is a local var, not a call.
    /// </summary>
    static bool IsAssignment(string trimmed, string action) =>
        Regex.IsMatch(trimmed, $@"^{Regex.Escape(action)}\s*(\|\||&&|\+|-|\*|/|%)?=(?!=)");

    /// <summary>
    /// Best-effort capture of the args text inside an action call's parens, joining
    /// continuation lines until the parens balance. Returns the params string (trimmed,
    /// whitespace-collapsed) and the index of the last consumed line.
    /// </summary>
    static (string Params, int LastLine) CaptureParams(string[] lines, int start, int bodyEnd)
    {
        var first = StripComment(lines[start]).Trim();
        var open = first.IndexOf('(');

        // Paren-less call (e.g. `build`, `sync_certificates`) → no params.
        if (open < 0)
        {
            // A bareword call may still carry args without parens: `track: "internal"`.
            // Capture the remainder after the action name as params.
            var sp = first.IndexOf(' ');
            var tail = sp >= 0 ? first[(sp + 1)..].Trim() : string.Empty;
            return (tail, start);
        }

        var buffer = new StringBuilder();
        var depth = 0;
        var done = false;
        var last = start;

        for (var i = start; i < bodyEnd && !done; i++)
        {
            var text = StripComment(lines[i]);
            last = i;
            for (var c = i == start ? open : 0; c < text.Length; c++)
            {
                var ch = text[c];
                if (ch == '(')
                {
                    depth++;
                    if (depth == 1) continue; // skip the outermost opening paren
                }
                else if (ch == ')')
                {
                    depth--;
                    if (depth == 0) { done = true; break; }
                }
                if (depth >= 1) buffer.Append(ch);
            }
            if (!done) buffer.Append(' ');
        }

        var collapsed = Regex.Replace(buffer.ToString().Trim(), @"\s+", " ");
        return (collapsed, last);
    }
}

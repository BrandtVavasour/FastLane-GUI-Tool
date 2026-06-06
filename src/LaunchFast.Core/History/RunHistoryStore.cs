using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LaunchFast.Core.History;

/// <summary>
/// Aggregate stats over a project's run history, computed against an injected
/// <c>nowUtc</c> so they're deterministic and testable.
/// </summary>
public sealed record RunHistoryStats(
    int RunsLast30Days,
    double SuccessRatePercent,
    TimeSpan? MedianDuration,
    DateTime? LastFailureUtc);

/// <summary>
/// Per-project JSON-backed run history (newest first), under
/// <c>ApplicationData/LaunchFast/history</c> by default. Each project gets one file,
/// keyed by a stable sanitized hash of its path. Total and robust: a missing or
/// corrupt file reads as empty and never throws.
/// </summary>
public sealed class RunHistoryStore
{
    /// <summary>Hard cap on stored records per project to bound the file size.</summary>
    const int MaxRecords = 500;

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    readonly string _baseDir;

    public RunHistoryStore(string? baseDir = null)
    {
        _baseDir = baseDir ?? DefaultDir;
    }

    public static string DefaultDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LaunchFast", "history");

    /// <summary>Appends a record (newest-first), capping the stored set to <see cref="MaxRecords"/>.</summary>
    public void Append(string projectId, RunRecord record)
    {
        var records = LoadList(projectId);
        records.Insert(0, record);
        if (records.Count > MaxRecords)
            records.RemoveRange(MaxRecords, records.Count - MaxRecords);
        Save(projectId, records);
    }

    /// <summary>All records for a project, newest first. Empty when none/unreadable.</summary>
    public IReadOnlyList<RunRecord> List(string projectId) => LoadList(projectId);

    /// <summary>
    /// Computes audit stats against <paramref name="nowUtc"/>: count of runs in the
    /// trailing 30 days, overall success rate (% of all stored runs), the median
    /// duration, and the most-recent failure instant (null if none).
    /// </summary>
    public RunHistoryStats Stats(string projectId, DateTime nowUtc)
    {
        var records = LoadList(projectId);
        if (records.Count == 0)
            return new RunHistoryStats(0, 0, null, null);

        var windowStart = nowUtc - TimeSpan.FromDays(30);
        var runsLast30 = records.Count(r => r.StartedUtc >= windowStart);

        var succeeded = records.Count(r => r.Status == RunStatus.Succeeded);
        var successRate = succeeded * 100.0 / records.Count;

        var durations = records.Select(r => r.Duration).OrderBy(d => d).ToList();
        var median = Median(durations);

        var lastFailure = records
            .Where(r => r.Status == RunStatus.Failed)
            .Select(r => (DateTime?)r.StartedUtc)
            .DefaultIfEmpty(null)
            .Max();

        return new RunHistoryStats(runsLast30, successRate, median, lastFailure);
    }

    static TimeSpan? Median(IReadOnlyList<TimeSpan> sorted)
    {
        if (sorted.Count == 0) return null;
        var mid = sorted.Count / 2;
        if (sorted.Count % 2 == 1) return sorted[mid];
        return TimeSpan.FromTicks((sorted[mid - 1].Ticks + sorted[mid].Ticks) / 2);
    }

    List<RunRecord> LoadList(string projectId)
    {
        var file = FileFor(projectId);
        if (!File.Exists(file)) return [];
        try
        {
            var text = File.ReadAllText(file);
            if (string.IsNullOrWhiteSpace(text)) return [];
            return JsonSerializer.Deserialize<List<RunRecord>>(text, JsonOptions) ?? [];
        }
        catch
        {
            // Corrupt / unreadable file → behave as empty; never throw.
            return [];
        }
    }

    void Save(string projectId, List<RunRecord> records)
    {
        try
        {
            Directory.CreateDirectory(_baseDir);
            File.WriteAllText(FileFor(projectId), JsonSerializer.Serialize(records, JsonOptions));
        }
        catch
        {
            // Persistence is best-effort; a write failure must never break a run.
        }
    }

    string FileFor(string projectId) => Path.Combine(_baseDir, KeyFor(projectId) + ".json");

    /// <summary>
    /// Stable per-project filename: a short readable prefix plus a SHA-256 hash of the
    /// full path, so distinct projects never collide and the key is filesystem-safe.
    /// </summary>
    static string KeyFor(string projectId)
    {
        var prefix = new string((projectId ?? "")
            .Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-')
            .ToArray());
        prefix = prefix.Trim('-');
        if (prefix.Length > 32) prefix = prefix[^32..];

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(projectId ?? ""));
        var hex = Convert.ToHexStringLower(hash)[..16];

        return prefix.Length == 0 ? hex : $"{prefix}-{hex}";
    }
}

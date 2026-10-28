using System.Text;
using System.Text.Json;

namespace MarathonCoach;

/// <summary>First half vs second half of a steady run: heart rate, pace, and aerobic decoupling.</summary>
/// <param name="DecouplingPct">How much worse heart rate per unit of speed got in the second half. Under ~5% is a sign of good aerobic fitness.</param>
public sealed record HeartRateDrift(
    double DecouplingPct, int FirstHalfHr, int SecondHalfHr, double FirstHalfSecPerKm, double SecondHalfSecPerKm);

public static class RunAnalysis
{
    private static readonly string[] Excluded = ["WARMUP", "COOLDOWN", "REST", "RECOVERY"];

    /// <summary>
    /// Aerobic decoupling from laps, or null when it isn't meaningful: interval sessions (rest laps), fewer than 4 laps
    /// with heart rate, or under 30 minutes of running. Warm-up and cool-down laps are left out.
    /// </summary>
    public static HeartRateDrift? Drift(IReadOnlyList<Lap> laps)
    {
        if (laps.Any(l => l.IntensityType is "REST" or "RECOVERY")) return null;

        var steady = laps
            .Where(l => l.AvgHr is > 0 && l.Km > 0.2 && !Excluded.Contains(l.IntensityType))
            .ToList();
        var total = steady.Sum(l => l.DurationSec);
        if (steady.Count < 4 || total < 30 * 60) return null;

        // Each lap goes to the half its midpoint falls in.
        var elapsed = 0.0;
        var first = new List<Lap>();
        var second = new List<Lap>();
        foreach (var lap in steady)
        {
            (elapsed + lap.DurationSec / 2 <= total / 2 ? first : second).Add(lap);
            elapsed += lap.DurationSec;
        }
        if (first.Count == 0 || second.Count == 0) return null;

        var (hr1, pace1) = Average(first);
        var (hr2, pace2) = Average(second);
        // Efficiency = speed per heartbeat; decoupling = how much it dropped from the first half to the second.
        double ef1 = 1 / pace1 / hr1, ef2 = 1 / pace2 / hr2;
        return new HeartRateDrift(Math.Round((ef1 - ef2) / ef1 * 100, 1), (int)Math.Round(hr1), (int)Math.Round(hr2), pace1, pace2);

        static (double Hr, double SecPerKm) Average(List<Lap> half)
        {
            var secs = half.Sum(l => l.DurationSec);
            return (half.Sum(l => l.AvgHr!.Value * l.DurationSec) / secs, secs / half.Sum(l => l.Km));
        }
    }

    /// <summary>Lap table, drift and time in zones as plain lines for the review prompt.</summary>
    public static string Describe(RunDetail detail, DistanceUnit u)
    {
        var sb = new StringBuilder();
        var run = detail.Run;
        sb.AppendLine($"Run: {run.When} ({run.Date:yyyy-MM-dd}) \"{run.Name}\", {UnitFormat.Distance(run.Km, u)} in {run.DurationMin} min, " +
                      $"{UnitFormat.ConvertPace(run.Pace, u)}, avg HR {run.AvgHr?.ToString() ?? "n/a"}, " +
                      $"aerobic TE {run.AerobicTe?.ToString("0.0") ?? "n/a"}");

        if (detail.Laps.Count > 0)
        {
            sb.AppendLine("Laps:");
            foreach (var l in detail.Laps)
                sb.AppendLine($"- {l.Index}: {UnitFormat.Distance(l.Km, u)}, " +
                              $"{(l.Km > 0 ? UnitFormat.Pace(l.DurationSec / l.Km, u) : "n/a")}, " +
                              $"HR {l.AvgHr?.ToString() ?? "n/a"}{(l.IntensityType is { } t ? $" ({t})" : "")}");
        }
        else sb.AppendLine("Laps: not available");

        sb.AppendLine(Drift(detail.Laps) is { } d
            ? $"Heart-rate drift (computed): {d.DecouplingPct:0.0}% aerobic decoupling; first half HR {d.FirstHalfHr} at " +
              $"{UnitFormat.Pace(d.FirstHalfSecPerKm, u)}, second half HR {d.SecondHalfHr} at {UnitFormat.Pace(d.SecondHalfSecPerKm, u)} " +
              "(under 5% = good aerobic fitness)"
            : "Heart-rate drift: not computed (interval session, too short, or no lap heart rate)");

        var zoneTotal = detail.HrZones.Sum(z => z.Secs);
        if (zoneTotal > 0)
            sb.AppendLine("Time in HR zones: " + string.Join(", ", detail.HrZones
                .Select(z => $"Z{z.Zone} {z.Secs / zoneTotal:0%} (from {z.LowBpm} bpm)")));
        return sb.ToString();
    }
}

/// <summary>Which runs already got a review (data/reviewed-runs.json), so each run is reviewed once, across restarts.</summary>
public sealed class ReviewedRuns
{
    private const int Keep = 500;
    private readonly string? _path;
    private readonly object _gate = new();
    private List<string>? _keys; // null = first start: nothing recorded yet

    public ReviewedRuns(string? path)
    {
        _path = path;
        if (path is not null && File.Exists(path))
            _keys = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path)) ?? [];
    }

    /// <summary>
    /// Runs not reviewed yet, oldest first. The very first time, every run passed in is recorded as seen and none
    /// are returned, so turning the feature on doesn't send reviews of old runs.
    /// </summary>
    public IReadOnlyList<RunSummary> TakeNew(IReadOnlyList<RunSummary> runs)
    {
        lock (_gate)
        {
            if (_keys is null)
            {
                _keys = runs.Select(r => r.Key).ToList();
                Save();
                return [];
            }
            return runs.Where(r => !_keys.Contains(r.Key)).OrderBy(r => r.Date).ToList();
        }
    }

    public void MarkReviewed(RunSummary run)
    {
        lock (_gate)
        {
            _keys ??= [];
            if (_keys.Contains(run.Key)) return;
            _keys.Add(run.Key);
            if (_keys.Count > Keep) _keys = _keys.TakeLast(Keep).ToList();
            Save();
        }
    }

    private void Save()
    {
        if (_path is null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        File.WriteAllText(_path + ".tmp", JsonSerializer.Serialize(_keys));
        File.Move(_path + ".tmp", _path, overwrite: true);
    }
}

public sealed record PlanEntry(string Kind, DateOnly Date, string Text);

/// <summary>What the coach recommended (/today, the morning DM, /week), by date, so a run can be checked against it.</summary>
public sealed class PlanStore
{
    public const string Day = "day", Week = "week";
    private const int KeepDays = 21;
    private readonly string? _path;
    private readonly object _gate = new();
    private List<PlanEntry> _plans;

    public PlanStore(string? path)
    {
        _path = path;
        _plans = path is not null && File.Exists(path)
            ? JsonSerializer.Deserialize<List<PlanEntry>>(File.ReadAllText(path)) ?? []
            : [];
    }

    public void Save(string kind, DateOnly date, string text)
    {
        lock (_gate)
        {
            _plans.RemoveAll(p => p.Kind == kind && p.Date == date);
            _plans.Add(new PlanEntry(kind, date, text));
            var cutoff = date.AddDays(-KeepDays);
            _plans.RemoveAll(p => p.Date < cutoff);
            if (_path is null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            File.WriteAllText(_path + ".tmp", JsonSerializer.Serialize(_plans, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(_path + ".tmp", _path, overwrite: true);
        }
    }

    /// <summary>That day's /today plan if there is one, else the latest /week plan covering that day, else null.</summary>
    public PlanEntry? For(DateOnly date)
    {
        lock (_gate)
        {
            return _plans.LastOrDefault(p => p.Kind == Day && p.Date == date)
                ?? _plans.Where(p => p.Kind == Week && p.Date <= date && date < p.Date.AddDays(7))
                    .OrderBy(p => p.Date).LastOrDefault();
        }
    }
}

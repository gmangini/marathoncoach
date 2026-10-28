namespace MarathonCoach;

/// <param name="ActivityId">Garmin's id for the run; null for runs without one (sample data, tests).</param>
/// <param name="Start">Local start time, when known.</param>
public record RunSummary(
    DateOnly Date, string Name, double Km, int DurationMin,
    string Pace, int? AvgHr, double? AerobicTe, long? ActivityId = null, DateTime? Start = null)
{
    /// <summary>Stable identity for "have I reviewed this run yet": Garmin's id, or the run's own details.</summary>
    public string Key => ActivityId?.ToString() ?? $"{Date:yyyy-MM-dd}|{Name}|{Km:0.00}|{DurationMin}";

    /// <summary>"Thu Oct 23, 6:42 PM", or "Thu Oct 23" when the start time isn't known.</summary>
    public string When => Start is { } s
        ? s.ToString("ddd MMM d, h:mm tt", System.Globalization.CultureInfo.InvariantCulture)
        : Date.ToString("ddd MMM d", System.Globalization.CultureInfo.InvariantCulture);
}

public record RecoveryDay(
    DateOnly Date, double? SleepHours, int? SleepScore,
    int? HrvLastNight, string? HrvStatus, int? RestingHr);

/// <summary>One lap as the watch recorded it. IntensityType is Garmin's WARMUP / ACTIVE / REST / COOLDOWN, or null.</summary>
public record Lap(int Index, double Km, double DurationSec, int? AvgHr, string? IntensityType);

/// <summary>Seconds spent in one heart-rate zone; LowBpm is where the zone starts.</summary>
public record HrZone(int Zone, int LowBpm, double Secs);

/// <summary>A run plus its laps and heart-rate zones, for the after-run review.</summary>
public record RunDetail(RunSummary Run, IReadOnlyList<Lap> Laps, IReadOnlyList<HrZone> HrZones);

/// <summary>Everything the coach needs from Garmin. Swap implementations freely.</summary>
public interface IGarminSource
{
    Task<IReadOnlyList<RunSummary>> GetRunsAsync(DateOnly start, DateOnly end, CancellationToken ct);
    Task<IReadOnlyList<RecoveryDay>> GetRecoveryAsync(int days, CancellationToken ct);

    /// <summary>Laps and heart-rate zones for one run. Default: none (the review then works from the summary).</summary>
    Task<RunDetail> GetRunDetailAsync(RunSummary run, CancellationToken ct) =>
        Task.FromResult(new RunDetail(run, [], []));
}

public static class Pace
{
    public static string PerKm(double km, double durationSec)
    {
        if (km <= 0) return "n/a";
        var s = durationSec / km;
        return $"{(int)(s / 60)}:{(int)(s % 60):00}/km";
    }
}

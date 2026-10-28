namespace MarathonCoach;

/// <param name="ActivityId">Garmin's id for the run; null for runs without one (sample data, tests).</param>
/// <param name="Start">Local start time, when known.</param>
/// <param name="Load">Garmin's training load for the session, which compares across sports.</param>
public record RunSummary(
    DateOnly Date, string Name, double Km, int DurationMin,
    string Pace, int? AvgHr, double? AerobicTe, long? ActivityId = null, DateTime? Start = null, double? Load = null)
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

public enum Sport { Run, Bike, Swim, Strength, Other }

/// <summary>A non-run session: a ride (TrainerRoad included), swim, strength or anything else Garmin recorded.</summary>
/// <param name="TypeKey">Garmin's own type, e.g. virtual_ride, lap_swimming, strength_training, hiking.</param>
/// <param name="Tss">Training stress score; only rides with power have one.</param>
/// <param name="Load">Garmin's training load, which compares across sports.</param>
public record CrossTraining(
    DateOnly Date, Sport Sport, string TypeKey, string Name, double Km, double DurationSec,
    int? AvgHr = null, double? AerobicTe = null, double? AvgPower = null, double? NormPower = null,
    double? Tss = null, double? Load = null, long? ActivityId = null, DateTime? Start = null);

public static class Sports
{
    /// <summary>Garmin's type key to a sport: running, trail_running -> Run; road_biking, virtual_ride -> Bike; ...</summary>
    public static Sport FromTypeKey(string? key)
    {
        var k = key?.ToLowerInvariant() ?? "";
        if (k.Contains("run")) return Sport.Run;
        if (k.Contains("cycl") || k.Contains("bik") || k.Contains("ride")) return Sport.Bike;
        if (k.Contains("swim")) return Sport.Swim;
        if (k.Contains("strength")) return Sport.Strength;
        return Sport.Other;
    }
}

/// <summary>Everything the coach needs from Garmin. Swap implementations freely.</summary>
public interface IGarminSource
{
    Task<IReadOnlyList<RunSummary>> GetRunsAsync(DateOnly start, DateOnly end, CancellationToken ct);
    Task<IReadOnlyList<RecoveryDay>> GetRecoveryAsync(int days, CancellationToken ct);

    /// <summary>Every non-run session in the range. Default: none (a runs-only source).</summary>
    Task<IReadOnlyList<CrossTraining>> GetCrossTrainingAsync(DateOnly start, DateOnly end, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CrossTraining>>([]);

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

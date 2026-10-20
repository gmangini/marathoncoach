namespace MarathonCoach;

public record RunSummary(
    DateOnly Date, string Name, double Km, int DurationMin,
    string Pace, int? AvgHr, double? AerobicTe);

public record RecoveryDay(
    DateOnly Date, double? SleepHours, int? SleepScore,
    int? HrvLastNight, string? HrvStatus, int? RestingHr);

/// <summary>Everything the coach needs from Garmin. Swap implementations freely.</summary>
public interface IGarminSource
{
    Task<IReadOnlyList<RunSummary>> GetRunsAsync(DateOnly start, DateOnly end, CancellationToken ct);
    Task<IReadOnlyList<RecoveryDay>> GetRecoveryAsync(int days, CancellationToken ct);
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

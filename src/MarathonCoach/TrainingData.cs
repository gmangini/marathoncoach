namespace MarathonCoach;

/// <summary>One snapshot of everything the coach reads: 8 weeks of runs and 7 days of recovery.</summary>
public sealed record TrainingData(
    IReadOnlyList<RunSummary> Runs, IReadOnlyList<RecoveryDay> Recovery, DateOnly Today, DateTimeOffset FetchedAt)
{
    public const int Weeks = 8;
    public const int RecoveryDays = 7;

    public string Summary =>
        $"{Runs.Count} runs over {Weeks} weeks (last: {Runs.LastOrDefault()?.Date.ToString("yyyy-MM-dd") ?? "none"}), " +
        $"{Recovery.Count} days of recovery, fetched {FetchedAt.LocalDateTime:HH:mm}";
}

/// <summary>
/// Keeps the last snapshot so every command doesn't log in to Garmin again (Garmin rate-limits logins).
/// Refreshes when the snapshot is older than <paramref name="maxAge"/>, from another day, or forced by /sync.
/// </summary>
public sealed class TrainingDataCache(IGarminSource source, TimeSpan maxAge, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private TrainingData? _data;

    public async Task<TrainingData> GetAsync(bool forceRefresh, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var now = _clock.GetLocalNow();
            var today = DateOnly.FromDateTime(now.DateTime);
            if (forceRefresh || _data is null || _data.Today != today || now - _data.FetchedAt > maxAge)
            {
                // 8 weeks ending today = today minus 55 days, so the weekly buckets are 0..7.
                var runs = await source.GetRunsAsync(today.AddDays(-(TrainingData.Weeks * 7 - 1)), today, ct);
                var recovery = await source.GetRecoveryAsync(TrainingData.RecoveryDays, ct);
                _data = new TrainingData(runs, recovery, today, now);
            }
            return _data;
        }
        finally { _lock.Release(); }
    }
}

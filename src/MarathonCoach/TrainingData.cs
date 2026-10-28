namespace MarathonCoach;

/// <summary>One snapshot of everything the coach reads: 8 weeks of runs and cross-training, 7 days of recovery.</summary>
public sealed record TrainingData(
    IReadOnlyList<RunSummary> Runs, IReadOnlyList<RecoveryDay> Recovery, DateOnly Today, DateTimeOffset FetchedAt,
    IReadOnlyList<CrossTraining>? Cross = null)
{
    public const int Weeks = 8;
    public const int RecoveryDays = 7;

    public IReadOnlyList<CrossTraining> CrossTraining => Cross ?? [];

    public string Summary =>
        $"{Runs.Count} runs and {CrossTraining.Count} other sessions over {Weeks} weeks " +
        $"(last run: {Runs.LastOrDefault()?.Date.ToString("yyyy-MM-dd") ?? "none"}), " +
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
                var from = today.AddDays(-(TrainingData.Weeks * 7 - 1));
                var runs = await source.GetRunsAsync(from, today, ct);
                var recovery = await source.GetRecoveryAsync(TrainingData.RecoveryDays, ct);
                _data = new TrainingData(runs, recovery, today, now, await CrossTrainingAsync(from, today, ct));
            }
            return _data;
        }
        finally { _lock.Release(); }
    }

    /// <summary>Cross-training is extra context: if Garmin fails on it, coach from the runs alone rather than not at all.</summary>
    private async Task<IReadOnlyList<CrossTraining>> CrossTrainingAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        try { return await source.GetCrossTrainingAsync(from, to, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.Error.WriteLine($"[garmin] skipped cross-training: {ex.GetType().Name}: {ex.Message}");
            return [];
        }
    }

    /// <summary>Just the runs of the last <paramref name="days"/> days: one light Garmin call for the run watcher.</summary>
    public Task<IReadOnlyList<RunSummary>> RecentRunsAsync(int days, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        return source.GetRunsAsync(today.AddDays(-(days - 1)), today, ct);
    }

    public Task<RunDetail> GetRunDetailAsync(RunSummary run, CancellationToken ct) => source.GetRunDetailAsync(run, ct);
}

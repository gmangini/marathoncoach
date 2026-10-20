using MarathonCoach;

namespace MarathonCoach.Tests;

internal sealed class ManualClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

internal sealed class CountingSource : IGarminSource
{
    public int Calls;
    public (DateOnly Start, DateOnly End) LastRange;

    public Task<IReadOnlyList<RunSummary>> GetRunsAsync(DateOnly start, DateOnly end, CancellationToken ct)
    {
        Calls++;
        LastRange = (start, end);
        return Task.FromResult<IReadOnlyList<RunSummary>>([]);
    }

    public Task<IReadOnlyList<RecoveryDay>> GetRecoveryAsync(int days, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<RecoveryDay>>([]);
}

public class TrainingDataCacheTests
{
    private static readonly DateTimeOffset Morning = new(2026, 10, 19, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Second_call_within_max_age_reuses_the_snapshot()
    {
        var source = new CountingSource();
        var clock = new ManualClock(Morning);
        var cache = new TrainingDataCache(source, TimeSpan.FromHours(3), clock);

        await cache.GetAsync(false, default);
        clock.Now = Morning.AddHours(2);
        await cache.GetAsync(false, default);

        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task Snapshot_older_than_max_age_is_refreshed()
    {
        var source = new CountingSource();
        var clock = new ManualClock(Morning);
        var cache = new TrainingDataCache(source, TimeSpan.FromHours(3), clock);

        await cache.GetAsync(false, default);
        clock.Now = Morning.AddHours(4);
        await cache.GetAsync(false, default);

        Assert.Equal(2, source.Calls);
    }

    [Fact]
    public async Task Sync_forces_a_refresh()
    {
        var source = new CountingSource();
        var cache = new TrainingDataCache(source, TimeSpan.FromHours(3), new ManualClock(Morning));

        await cache.GetAsync(false, default);
        await cache.GetAsync(forceRefresh: true, default);

        Assert.Equal(2, source.Calls);
    }

    [Fact]
    public async Task A_new_day_refreshes_even_within_max_age()
    {
        var source = new CountingSource();
        var clock = new ManualClock(new DateTimeOffset(2026, 10, 19, 23, 0, 0, TimeSpan.Zero));
        var cache = new TrainingDataCache(source, TimeSpan.FromHours(3), clock);

        await cache.GetAsync(false, default);
        clock.Now = clock.Now.AddHours(2); // 01:00 the next day
        var data = await cache.GetAsync(false, default);

        Assert.Equal(2, source.Calls);
        Assert.Equal(new DateOnly(2026, 10, 20), data.Today);
    }

    [Fact]
    public async Task Asks_for_exactly_eight_weeks_ending_today()
    {
        var source = new CountingSource();
        await new TrainingDataCache(source, TimeSpan.FromHours(3), new ManualClock(Morning)).GetAsync(false, default);

        Assert.Equal((new DateOnly(2026, 8, 25), new DateOnly(2026, 10, 19)), source.LastRange);
    }
}

public class WeeklyMileageTests
{
    [Fact]
    public void Empty_weeks_show_zero_and_there_are_exactly_eight()
    {
        var today = new DateOnly(2026, 10, 19);
        RunSummary Run(int daysAgo, double km) => new(today.AddDays(-daysAgo), "Run", km, 30, "5:00/km", null, null);
        var config = new Config(null, 0, null, Config.DefaultModel, PromptBuilder.DefaultGoal, "not set yet", "none");

        var prompt = PromptBuilder.Build([Run(1, 10), Run(15, 8), Run(55, 5)], [], today, config);

        Assert.Contains("- last 7 days: 10 km", prompt);
        Assert.Contains("- 1 wk ago: 0 km", prompt);
        Assert.Contains("- 2 wk ago: 8 km", prompt);
        Assert.Contains("- 7 wk ago: 5 km", prompt);
        Assert.DoesNotContain("8 wk ago", prompt);
    }
}

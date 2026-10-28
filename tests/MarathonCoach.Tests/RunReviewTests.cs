using MarathonCoach;

namespace MarathonCoach.Tests;

public class TypedCommandTests
{
    [Theory]
    [InlineData("/review", "review")]
    [InlineData("  /Today ", "today")]
    [InlineData("/week", "week")]
    [InlineData("/goal", "goal")]
    public void A_dm_that_is_exactly_a_command_runs_it(string text, string expected)
    {
        Assert.True(TypedCommand.TryParse(text, out var name));
        Assert.Equal(expected, name);
    }

    [Theory]
    [InlineData("review my run")]           // no slash: chat
    [InlineData("/review was it too fast?")] // more than the command: chat
    [InlineData("/goal 3:25")]               // chat, where the update_goal tool handles it
    [InlineData("/dance")]                   // not a command
    [InlineData("/")]
    public void Anything_else_goes_to_chat(string text) => Assert.False(TypedCommand.TryParse(text, out _));
}

public class HeartRateDriftTests
{
    private static Lap L(int i, int hr, double km = 1, double secs = 600, string? type = "ACTIVE") => new(i, km, secs, hr, type);

    [Fact]
    public void Even_pace_with_hr_140_then_150_is_6_7_percent()
    {
        var d = RunAnalysis.Drift([L(1, 140), L(2, 140), L(3, 150), L(4, 150)])!;

        Assert.Equal(6.7, d.DecouplingPct);
        Assert.Equal(140, d.FirstHalfHr);
        Assert.Equal(150, d.SecondHalfHr);
    }

    [Fact]
    public void Faster_second_half_at_higher_hr_is_not_drift()
    {
        // Second half 10% faster with ~10% higher HR: same efficiency, so about zero decoupling.
        var d = RunAnalysis.Drift([L(1, 140), L(2, 140), L(3, 154, secs: 540), L(4, 154, secs: 540)])!;
        Assert.InRange(d.DecouplingPct, -1, 1);
    }

    [Fact]
    public void Warmup_and_cooldown_laps_are_left_out()
    {
        var laps = new[] { L(0, 120, type: "WARMUP"), L(1, 140), L(2, 140), L(3, 140), L(4, 140), L(5, 170, type: "COOLDOWN") };
        Assert.Equal(0, RunAnalysis.Drift(laps)!.DecouplingPct);
    }

    [Fact]
    public void Interval_sessions_have_no_drift() =>
        Assert.Null(RunAnalysis.Drift([L(1, 160), L(2, 120, type: "REST"), L(3, 165), L(4, 125, type: "REST"), L(5, 168)]));

    [Fact]
    public void Short_runs_or_few_laps_have_no_drift()
    {
        Assert.Null(RunAnalysis.Drift([L(1, 140), L(2, 150), L(3, 155)]));                                  // 3 laps
        Assert.Null(RunAnalysis.Drift([L(1, 140, secs: 300), L(2, 140, secs: 300), L(3, 150, secs: 300), L(4, 150, secs: 300)])); // 20 min
    }

    [Fact]
    public void Laps_without_heart_rate_are_ignored() =>
        Assert.Null(RunAnalysis.Drift([new Lap(1, 1, 600, null, null), new Lap(2, 1, 600, null, null), L(3, 140), L(4, 150)]));

    [Fact]
    public void Description_in_miles_has_laps_drift_and_zones()
    {
        var run = new RunSummary(new DateOnly(2026, 10, 27), "Easy", 4.0, 40, "10:00/km", 145, 2.8, 1,
            new DateTime(2026, 10, 27, 6, 5, 0));
        var detail = new RunDetail(run, [L(1, 140), L(2, 140), L(3, 150), L(4, 150)], [new(2, 125, 1800), new(3, 145, 600)]);

        var text = RunAnalysis.Describe(detail, DistanceUnit.Miles);

        Assert.StartsWith("Run: Tue Oct 27, 6:05 AM (2026-10-27) \"Easy\"", text);
        Assert.Contains("2.5 mi in 40 min, 16:05/mi", text);
        Assert.Contains("- 3: 0.6 mi, 16:05/mi, HR 150 (ACTIVE)", text);
        Assert.Contains("6.7% aerobic decoupling", text);
        Assert.Contains("Z2 75% (from 125 bpm), Z3 25% (from 145 bpm)", text);
        Assert.DoesNotContain("/km", text);
    }
}

public sealed class ReviewStoresTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mc-review-" + Guid.NewGuid().ToString("N"));
    private static RunSummary Run(long id, int day = 27) => new(new DateOnly(2026, 10, day), "Run", 8, 45, "5:37/km", 150, 3, id);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void First_start_records_existing_runs_and_returns_none()
    {
        var store = new ReviewedRuns(null);
        Assert.Empty(store.TakeNew([Run(1), Run(2)]));
        Assert.Equal([3L], store.TakeNew([Run(1), Run(2), Run(3)]).Select(r => r.ActivityId!.Value));
    }

    [Fact]
    public void Reviewed_runs_survive_a_restart()
    {
        var path = Path.Combine(_dir, "reviewed-runs.json");
        var store = new ReviewedRuns(path);
        store.TakeNew([Run(1)]);
        store.MarkReviewed(Run(2));

        Assert.Equal([3L], new ReviewedRuns(path).TakeNew([Run(1), Run(2), Run(3)]).Select(r => r.ActivityId!.Value));
    }

    [Fact]
    public void New_runs_come_oldest_first()
    {
        var store = new ReviewedRuns(null);
        store.TakeNew([]);
        Assert.Equal([26, 27], store.TakeNew([Run(5, 27), Run(4, 26)]).Select(r => r.Date.Day));
    }

    [Fact]
    public void Runs_without_an_id_are_keyed_by_their_details()
    {
        var a = new RunSummary(new DateOnly(2026, 10, 27), "Easy", 8, 45, "5:37/km", 150, 3);
        Assert.Equal(a.Key, (a with { AvgHr = 151 }).Key);
        Assert.NotEqual(a.Key, (a with { Km = 9 }).Key);
    }

    [Fact]
    public void Day_plan_wins_over_week_plan_and_week_plan_covers_seven_days()
    {
        var plans = new PlanStore(null);
        var monday = new DateOnly(2026, 10, 26);
        plans.Save(PlanStore.Week, monday, "week plan");
        plans.Save(PlanStore.Day, monday.AddDays(1), "tuesday plan");

        Assert.Equal("week plan", plans.For(monday)!.Text);
        Assert.Equal("tuesday plan", plans.For(monday.AddDays(1))!.Text);
        Assert.Equal("week plan", plans.For(monday.AddDays(6))!.Text);
        Assert.Null(plans.For(monday.AddDays(7)));
        Assert.Null(plans.For(monday.AddDays(-1)));
    }

    [Fact]
    public void Plans_survive_a_restart_and_old_ones_are_dropped()
    {
        var path = Path.Combine(_dir, "plans.json");
        var plans = new PlanStore(path);
        plans.Save(PlanStore.Day, new DateOnly(2026, 9, 1), "old");
        plans.Save(PlanStore.Day, new DateOnly(2026, 10, 27), "new");

        var reloaded = new PlanStore(path);
        Assert.Equal("new", reloaded.For(new DateOnly(2026, 10, 27))!.Text);
        Assert.Null(reloaded.For(new DateOnly(2026, 9, 1)));
    }
}

/// <summary>Garmin stand-in whose runs can change between checks.</summary>
internal sealed class ChangingSource : IGarminSource
{
    public List<RunSummary> Runs { get; } = [];

    public Task<IReadOnlyList<RunSummary>> GetRunsAsync(DateOnly start, DateOnly end, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<RunSummary>>(Runs.Where(r => r.Date >= start && r.Date <= end).ToList());

    public Task<IReadOnlyList<RecoveryDay>> GetRecoveryAsync(int days, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<RecoveryDay>>([]);

    public Task<RunDetail> GetRunDetailAsync(RunSummary run, CancellationToken ct) =>
        Task.FromResult(new RunDetail(run, [new(1, 2, 600, 140, "ACTIVE"), new(2, 2, 600, 140, "ACTIVE"),
            new(3, 2, 600, 147, "ACTIVE"), new(4, 2, 600, 147, "ACTIVE")], []));
}

internal sealed class FailingOnceLlm : ICoachLlm
{
    public int Calls;
    public Task<string> AskAsync(string system, IReadOnlyList<ChatTurn> turns, CancellationToken ct) =>
        ++Calls == 1 ? throw new HttpRequestException("API down") : Task.FromResult("review text");
}

public class RunWatcherTests
{
    private static readonly Config Env = new(null, 1, null, Config.DefaultModel, "goal", "not set yet", "none", Units: DistanceUnit.Miles);
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);
    private static RunSummary Run(long id, string name = "Easy") => new(Today, name, 8, 45, "5:37/km", 150, 3, id);

    private static (Coach Coach, ChangingSource Source, ScriptedLlm Llm, PlanStore Plans, ChatHistory History) Setup()
    {
        var source = new ChangingSource();
        var llm = new ScriptedLlm();
        var plans = new PlanStore(null);
        var history = new ChatHistory(null);
        var coach = new Coach(new TrainingDataCache(source, TimeSpan.FromHours(3)), llm, Env, history, null, plans, new ReviewedRuns(null));
        return (coach, source, llm, plans, history);
    }

    [Fact]
    public async Task First_check_sends_nothing_then_a_new_run_gets_one_review()
    {
        var (coach, source, llm, _, _) = Setup();
        source.Runs.Add(Run(1));

        Assert.Empty(await coach.CheckNewRunsAsync(default));   // existing run: recorded, not reviewed
        source.Runs.Add(Run(2, "Tempo"));
        var reviews = await coach.CheckNewRunsAsync(default);
        Assert.Empty(await coach.CheckNewRunsAsync(default));   // same run again: no second review

        var review = Assert.Single(reviews);
        var date = Today.ToString("ddd MMM d", System.Globalization.CultureInfo.InvariantCulture);
        Assert.StartsWith($"**🏃 Tempo · {date} · 5 mi · 9:02/mi**", review); // no start time known: date only
        Assert.Single(llm.Calls);
    }

    [Fact]
    public async Task Review_title_shows_the_date_and_start_time()
    {
        var (coach, source, _, _, _) = Setup();
        await coach.CheckNewRunsAsync(default);
        source.Runs.Add(Run(11, "Provo Running") with { Start = Today.ToDateTime(new TimeOnly(18, 42)) });

        var review = Assert.Single(await coach.CheckNewRunsAsync(default));

        var date = Today.ToString("ddd MMM d", System.Globalization.CultureInfo.InvariantCulture);
        Assert.StartsWith($"**🏃 Provo Running · {date}, 6:42 PM · 5 mi · 9:02/mi**", review);
    }

    [Fact]
    public async Task Review_prompt_has_the_plan_laps_and_computed_drift()
    {
        var (coach, source, llm, plans, _) = Setup();
        await coach.CheckNewRunsAsync(default);
        plans.Save(PlanStore.Day, Today, "Easy 5 mi at 9:12-10:00/mi");
        source.Runs.Add(Run(7));

        await coach.CheckNewRunsAsync(default);

        var prompt = llm.Calls.Single().Single().Text;
        Assert.StartsWith("I just finished a run.", prompt);
        Assert.Contains("What you recommended for that day (from /today):\nEasy 5 mi at 9:12-10:00/mi", prompt);
        Assert.Contains("Laps:", prompt);
        Assert.Contains("4.8% aerobic decoupling", prompt); // 1 - 140/147
        Assert.Contains("at most 6 short lines", prompt);
    }

    [Fact]
    public async Task Without_a_plan_the_prompt_says_so()
    {
        var (coach, source, llm, _, _) = Setup();
        await coach.CheckNewRunsAsync(default);
        source.Runs.Add(Run(8));

        await coach.CheckNewRunsAsync(default);

        Assert.Contains("No plan was recorded for that day", llm.Calls.Single().Single().Text);
    }

    [Fact]
    public async Task A_failed_review_is_retried_on_the_next_check()
    {
        var source = new ChangingSource();
        var llm = new FailingOnceLlm();
        var coach = new Coach(new TrainingDataCache(source, TimeSpan.FromHours(3)), llm, Env, new ChatHistory(null),
            null, new PlanStore(null), new ReviewedRuns(null));
        await coach.CheckNewRunsAsync(default);
        source.Runs.Add(Run(3));

        await Assert.ThrowsAsync<HttpRequestException>(() => coach.CheckNewRunsAsync(default));
        var retry = await coach.CheckNewRunsAsync(default);

        Assert.Single(retry);
        Assert.Equal(2, llm.Calls);
    }

    [Fact]
    public async Task Today_saves_the_plan_and_the_review_goes_into_history()
    {
        var (coach, source, llm, plans, history) = Setup();
        await coach.CheckNewRunsAsync(default);

        await coach.TodayAsync(default);                  // ScriptedLlm answers "answer 1"
        Assert.Equal("answer 1", plans.For(Today)!.Text);

        source.Runs.Add(Run(9));
        await coach.CheckNewRunsAsync(default);
        Assert.StartsWith("Review my run:", history.Recent(2)[0].Text);
        Assert.Contains("answer 1", llm.Calls[1].Single().Text); // the review saw the /today plan
    }

    [Fact]
    public async Task Review_command_with_no_runs_says_so()
    {
        var (coach, _, _, _, _) = Setup();
        Assert.Equal("No runs in the last 2 weeks to review.", await coach.ReviewLatestAsync(default));
    }

    [Fact]
    public async Task Review_command_reviews_the_latest_run_and_marks_it()
    {
        var (coach, source, llm, _, _) = Setup();
        await coach.CheckNewRunsAsync(default);
        source.Runs.Add(Run(10, "Long run"));

        var review = await coach.ReviewLatestAsync(default);

        Assert.Contains("Long run", review);
        Assert.Empty(await coach.CheckNewRunsAsync(default)); // already reviewed: the watcher won't repeat it
        Assert.Single(llm.Calls);
    }
}

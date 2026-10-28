using MarathonCoach;

namespace MarathonCoach.Tests;

public class SportMappingTests
{
    [Theory]
    [InlineData("running", Sport.Run)]
    [InlineData("trail_running", Sport.Run)]
    [InlineData("treadmill_running", Sport.Run)]
    [InlineData("indoor_cycling", Sport.Bike)]
    [InlineData("virtual_ride", Sport.Bike)]
    [InlineData("road_biking", Sport.Bike)]
    [InlineData("mountain_biking", Sport.Bike)]
    [InlineData("lap_swimming", Sport.Swim)]
    [InlineData("open_water_swimming", Sport.Swim)]
    [InlineData("strength_training", Sport.Strength)]
    [InlineData("hiking", Sport.Other)]
    [InlineData("indoor_rowing", Sport.Other)]
    [InlineData(null, Sport.Other)]
    public void Garmin_type_keys_map_to_sports(string? key, Sport expected) => Assert.Equal(expected, Sports.FromTypeKey(key));
}

public class MultisportFormatTests
{
    private static readonly DateOnly Today = new(2026, 10, 27);

    [Theory]
    [InlineData(45 * 60, "45 min")]
    [InlineData(3600, "1h00")]
    [InlineData(3900, "1h05")]
    [InlineData(5 * 3600 + 9 * 60 + 20, "5h09")]
    public void Durations_read_naturally(double secs, string expected) => Assert.Equal(expected, MultisportFormat.Duration(secs));

    [Fact]
    public void Swims_use_yards_and_pace_per_100yd_in_miles_mode()
    {
        // 902 yd in 18 min = 1:59/100yd, like your Sep 14 pool swim
        var km = 902 * 0.9144 / 1000;
        Assert.Equal("902 yd", MultisportFormat.SwimDistance(km, DistanceUnit.Miles));
        Assert.Equal("1:59/100yd", MultisportFormat.SwimPace(km, 18 * 60, DistanceUnit.Miles));
        Assert.Equal("1,600 m", MultisportFormat.SwimDistance(1.6, DistanceUnit.Km));
        Assert.Equal("2:11/100m", MultisportFormat.SwimPace(1.6, 2100, DistanceUnit.Km));
    }

    [Fact]
    public void Ride_line_has_power_np_tss_and_load()
    {
        var ride = new CrossTraining(Today, Sport.Bike, "indoor_cycling", "Carson - TrainerRoad", 27.7, 3660, 145, 2.7, 139, 154, 65, 58);

        Assert.Equal("- 2026-10-27 Bike (indoor_cycling) \"Carson - TrainerRoad\": 1h01, 17.2 mi, avg HR 145, " +
                     "139 W avg / 154 W NP, TSS 65, load 58, aerobic TE 2.7", MultisportFormat.SessionLine(ride, DistanceUnit.Miles));
    }

    [Fact]
    public void Strength_line_has_no_distance()
    {
        var lift = new CrossTraining(Today, Sport.Strength, "strength_training", "Strength", 0, 2400, 110, Load: 25);
        Assert.Equal("- 2026-10-27 Strength (strength_training) \"Strength\": 40 min, avg HR 110, load 25",
            MultisportFormat.SessionLine(lift, DistanceUnit.Miles));
    }

    [Fact]
    public void Weekly_lines_add_up_time_and_load_across_sports()
    {
        RunSummary Run(int daysAgo, double km, int min, double load) =>
            new(Today.AddDays(-daysAgo), "Run", km, min, "5:00/km", 150, 3, Load: load);
        var runs = new[] { Run(1, 16.09344, 80, 120), Run(3, 8.04672, 40, 60) };
        var cross = new[]
        {
            new CrossTraining(Today.AddDays(-2), Sport.Bike, "virtual_ride", "TR", 30, 3600, Load: 70),
            new CrossTraining(Today.AddDays(-4), Sport.Swim, "lap_swimming", "Pool", 1.6, 2100, Load: 40),
            new CrossTraining(Today.AddDays(-5), Sport.Strength, "strength_training", "Lift", 0, 1800, Load: 20),
            new CrossTraining(Today.AddDays(-9), Sport.Other, "hiking", "Hike", 10, 7200, Load: 50),
        };

        var lines = MultisportFormat.WeeklyLines(runs, cross, Today, DistanceUnit.Miles).ToList();

        Assert.Equal(TrainingData.Weeks, lines.Count);
        // 80 + 40 + 60 + 35 + 30 min = 4h05; load 120 + 60 + 70 + 40 + 20 = 310
        Assert.Equal("- last 7 days: 4h05 total, load 310 | run 15 mi (2) | bike 1h00, 18.6 mi (1) | swim 1,750 yd (1) | strength 1", lines[0]);
        Assert.Equal("- 1 wk ago: 2h00 total, load 50 | run 0 mi (0) | other 2h00 (1)", lines[1]);
        Assert.Equal("- 2 wk ago: 0 min total | run 0 mi (0)", lines[2]);
    }
}

public class MultisportPromptTests
{
    private static readonly Config Miles = new(null, 1, null, Config.DefaultModel, "goal", "not set yet", "none", Units: DistanceUnit.Miles);

    [Fact]
    public void Prompt_has_weekly_table_sessions_and_the_cross_training_note()
    {
        var today = new DateOnly(2026, 10, 27);
        var cross = new[] { new CrossTraining(today.AddDays(-1), Sport.Bike, "virtual_ride", "TrainerRoad - Galena", 30, 3600, 132, 2.9, 180, 192, 58, 70) };

        var prompt = PromptBuilder.DataBlock([], [], today, Miles, cross);

        Assert.Contains("All training by week, every sport", prompt);
        Assert.Contains("bike 1h00, 18.6 mi (1)", prompt);
        Assert.Contains("\"TrainerRoad - Galena\": 1h00, 18.6 mi, avg HR 132, 180 W avg / 192 W NP, TSS 58", prompt);
        Assert.Contains("Count cross-training in my overall load", prompt);
    }

    [Fact]
    public void Without_cross_training_the_section_says_none()
    {
        var prompt = PromptBuilder.DataBlock([], [], new DateOnly(2026, 10, 27), Miles);
        Assert.Contains("Recent cross-training (last 15 non-run sessions):\n- none", prompt.Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task Today_week_and_chat_prompts_all_carry_cross_training()
    {
        var llm = new ToolCallingLlm(new LlmReply("ok", []));
        var coach = new Coach(new TrainingDataCache(new SampleSource(), TimeSpan.FromHours(3)), llm, Miles, new ChatHistory(null));

        await coach.TodayAsync(default);
        await coach.WeekAsync(default);
        await coach.ChatAsync("how's my week?", default);

        Assert.Equal(3, llm.Calls.Count);
        Assert.All(llm.Calls, turns => Assert.Contains("TrainerRoad - Galena", turns[^1].Text));
    }

    [Fact]
    public async Task A_garmin_failure_on_cross_training_keeps_the_runs()
    {
        var cache = new TrainingDataCache(new CrossTrainingFails(), TimeSpan.FromHours(3));

        var data = await cache.GetAsync(false, default);

        Assert.Single(data.Runs);
        Assert.Empty(data.CrossTraining);
        Assert.StartsWith("1 runs and 0 other sessions", data.Summary);
    }

    private sealed class CrossTrainingFails : IGarminSource
    {
        public Task<IReadOnlyList<RunSummary>> GetRunsAsync(DateOnly start, DateOnly end, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<RunSummary>>([new RunSummary(end, "Easy", 8, 45, "5:37/km", 150, 3)]);
        public Task<IReadOnlyList<RecoveryDay>> GetRecoveryAsync(int days, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<RecoveryDay>>([]);
        public Task<IReadOnlyList<CrossTraining>> GetCrossTrainingAsync(DateOnly start, DateOnly end, CancellationToken ct) =>
            throw new HttpRequestException("Garmin down");
    }
}

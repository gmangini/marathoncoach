using MarathonCoach;

namespace MarathonCoach.Tests;

public class RaceTypeTests
{
    [Theory]
    [InlineData("marathon", RaceType.Marathon)]
    [InlineData("70.3", RaceType.HalfIronman)]
    [InlineData("Half Ironman", RaceType.HalfIronman)]
    [InlineData("ironman", RaceType.Ironman)]
    [InlineData("IM", RaceType.Ironman)]
    [InlineData("140.6", RaceType.Ironman)]
    public void Goal_types_parse(string text, RaceType expected) => Assert.Equal(expected, RaceTypes.Parse(text));

    [Fact]
    public void Unknown_goal_type_is_null() => Assert.Null(RaceTypes.Parse("ultra"));

    [Theory]
    [InlineData("12", RaceType.Ironman, "12:00:00")]
    [InlineData("12h30", RaceType.Ironman, "12:30:00")]
    [InlineData("11:45", RaceType.Ironman, "11:45:00")]
    [InlineData("5:30", RaceType.HalfIronman, "05:30:00")]
    [InlineData("3:25", RaceType.Marathon, "03:25:00")]
    public void Goal_times_parse_within_the_race_range(string text, RaceType race, string expected)
    {
        Assert.True(SettingsStore.TryParseGoal(text, race, out var t));
        Assert.Equal(TimeSpan.Parse(expected), t);
    }

    [Theory]
    [InlineData("3:30", RaceType.Ironman)]   // a marathon time is not an Ironman goal
    [InlineData("12:00", RaceType.Marathon)]
    [InlineData("2:30", RaceType.HalfIronman)]
    public void Goal_times_outside_the_race_range_are_refused(string text, RaceType race) =>
        Assert.False(SettingsStore.TryParseGoal(text, race, out _));
}

public class TriZoneTests
{
    [Fact]
    public void Power_zones_for_ftp_250()
    {
        var zones = PowerZones.For(250).Select(z => z.ToString()).ToList();

        Assert.Equal(6, zones.Count);
        Assert.StartsWith("Z1 Recovery: 0-138 W", zones[0]);
        Assert.StartsWith("Z2 Endurance: 140-188 W", zones[1]);
        Assert.StartsWith("Z4 Threshold: 228-262 W", zones[3]);
        Assert.StartsWith("Z6 Anaerobic: 302+ W", zones[5]);
    }

    [Fact]
    public void Css_1_55_per_100yd_gives_yard_zones_in_miles_mode()
    {
        Assert.True(SwimZones.TryParseCss("1:55", DistanceUnit.Miles, out var css));
        Assert.Equal("1:55/100yd", SwimZones.Pace(css, DistanceUnit.Miles));

        var zones = SwimZones.For(css, DistanceUnit.Miles);
        Assert.StartsWith("Easy: 2:05/100yd or slower", zones[0]);
        Assert.StartsWith("Endurance: 1:59/100yd-2:04/100yd", zones[1]);
        Assert.StartsWith("Threshold (CSS): 1:53/100yd-1:56/100yd", zones[2]);
        Assert.StartsWith("Fast: 1:50/100yd or faster", zones[3]);
    }

    [Theory]
    [InlineData("0:50")]
    [InlineData("4:00")]
    [InlineData("fast")]
    public void Silly_css_values_are_refused(string text) => Assert.False(SwimZones.TryParseCss(text, DistanceUnit.Miles, out _));

    [Fact]
    public void Ironman_split_guide_for_12_hours_in_miles() =>
        Assert.Equal(
            ["Swim 1:12 (1:43/100yd)", "T1 0:11", "Bike 6:14 (17.9 mph)", "T2 0:07", "Run 4:16 (9:44/mi)"],
            TriSplits.Describe(new TimeSpan(12, 0, 0), RaceType.Ironman, DistanceUnit.Miles));

    [Fact]
    public void Half_ironman_split_guide_for_6_hours_in_miles()
    {
        var splits = TriSplits.Describe(new TimeSpan(6, 0, 0), RaceType.HalfIronman, DistanceUnit.Miles);
        Assert.Equal("Swim 0:36 (1:43/100yd)", splits[0]);
        Assert.Equal("Bike 3:00 (18.7 mph)", splits[2]);
        Assert.Equal("Run 2:13 (10:09/mi)", splits[4]);
    }

    [Fact]
    public void Triathlon_phases_talk_about_rides_and_bricks()
    {
        var today = new DateOnly(2026, 10, 27);
        Assert.Contains("longest rides (5-6 h)", TrainingPhase.Describe(today, today.AddDays(35), RaceType.Ironman));
        Assert.Contains("longest rides (3-3.5 h)", TrainingPhase.Describe(today, today.AddDays(35), RaceType.HalfIronman));
        Assert.Contains("weekly brick", TrainingPhase.Describe(today, today.AddDays(70), RaceType.Ironman));
        Assert.Contains("all three sports", TrainingPhase.Describe(today, null, RaceType.Ironman));
        Assert.Contains("long runs", TrainingPhase.Describe(today, today.AddDays(35))); // marathon unchanged
    }
}

public class TriSettingsTests
{
    private static readonly DateOnly Today = new(2026, 10, 27);
    private static readonly Config Env = new(null, 1, null, Config.DefaultModel,
        Config.DescribeGoal(new TimeSpan(3, 30, 0), DistanceUnit.Miles), "not set yet", "none",
        GoalTime: new TimeSpan(3, 30, 0), Units: DistanceUnit.Miles);

    [Fact]
    public void Switching_to_ironman_with_a_time_saves_both()
    {
        var store = new SettingsStore(null);
        var (ok, _) = store.Update(new GoalUpdate(GoalTime: "12", GoalType: "ironman"), RaceType.Marathon, DistanceUnit.Miles, Today);

        var c = store.Apply(Env);
        Assert.True(ok);
        Assert.Equal(RaceType.Ironman, c.Race);
        Assert.Equal(new TimeSpan(12, 0, 0), c.TargetTime);
        Assert.Equal("Ironman in 12:00:00", c.Goal);
    }

    [Fact]
    public void Switching_type_without_a_time_drops_the_old_time_and_says_so()
    {
        var store = new SettingsStore(null);
        store.Update(new GoalUpdate(GoalTime: "3:25"), RaceType.Marathon, DistanceUnit.Miles, Today);

        var (ok, message) = store.Update(new GoalUpdate(GoalType: "ironman"), RaceType.Marathon, DistanceUnit.Miles, Today);

        Assert.True(ok);
        Assert.Contains("Ironman default (12:30)", message);
        Assert.Null(store.Current.GoalTime);
        Assert.Equal(new TimeSpan(12, 30, 0), store.Apply(Env).TargetTime); // not the .env 3:30 marathon time
    }

    [Fact]
    public void A_triathlon_time_without_the_type_is_checked_against_the_current_race()
    {
        var (ok, message) = new SettingsStore(null).Update(new GoalUpdate(GoalTime: "12:00"), RaceType.Marathon, DistanceUnit.Miles, Today);
        Assert.False(ok);
        Assert.Contains("between 2:00 and 7:00 for a Marathon", message);
    }

    [Fact]
    public void Ftp_css_and_marathon_time_are_validated_and_saved()
    {
        var store = new SettingsStore(null);
        Assert.False(store.Update(new GoalUpdate(Ftp: 900), RaceType.Ironman, DistanceUnit.Miles, Today).Ok);
        Assert.False(store.Update(new GoalUpdate(SwimCss: "0:40"), RaceType.Ironman, DistanceUnit.Miles, Today).Ok);
        Assert.True(store.Update(new GoalUpdate(Ftp: 250, SwimCss: "1:55", MarathonTime: "3:20"), RaceType.Ironman, DistanceUnit.Miles, Today).Ok);

        var c = store.Apply(Env with { Race = RaceType.Ironman });
        Assert.Equal(250, c.Ftp);
        Assert.Equal("1:55/100yd", SwimZones.Pace(c.SwimCssSec!.Value, DistanceUnit.Miles));
        Assert.Equal(new TimeSpan(3, 20, 0), c.RunZoneTime);
    }

    [Fact]
    public void Run_zones_follow_marathon_fitness_for_a_triathlon_and_the_goal_for_a_marathon()
    {
        Assert.Equal(new TimeSpan(3, 30, 0), Env.RunZoneTime);
        Assert.Equal(new TimeSpan(3, 30, 0), (Env with { Race = RaceType.Ironman, GoalTime = new TimeSpan(12, 0, 0) }).RunZoneTime);
        Assert.Equal(new TimeSpan(3, 15, 0),
            (Env with { Race = RaceType.Ironman, GoalTime = new TimeSpan(12, 0, 0), MarathonTime = new TimeSpan(3, 15, 0) }).RunZoneTime);
    }
}

public class TriPromptTests
{
    private static readonly Config Ironman = new(null, 1, null, Config.DefaultModel, "Ironman in 12:00:00", "not set yet", "none",
        GoalTime: new TimeSpan(12, 0, 0), Units: DistanceUnit.Miles, Race: RaceType.Ironman);

    [Fact]
    public void Ironman_prompt_has_split_guide_and_asks_for_missing_ftp_and_css()
    {
        var prompt = PromptBuilder.Build([], [], new DateOnly(2026, 10, 27), Ironman);

        Assert.StartsWith("Recommend my ONE session for today (swim, bike, run, brick, strength or rest).", prompt);
        Assert.Contains("My run pace zones (computed from a 3:30:00 standalone marathon fitness", prompt);
        Assert.Contains("Bike power zones: FTP not set", prompt);
        Assert.Contains("Swim pace zones: CSS not set", prompt);
        Assert.Contains("Split guide for a 12:00:00 Ironman", prompt);
        Assert.Contains("Swim 1:12 (1:43/100yd) | T1 0:11 | Bike 6:14 (17.9 mph)", prompt);
        Assert.Contains("Swim, bike and run are all main sports for my Ironman goal", prompt);
    }

    [Fact]
    public void With_ftp_and_css_the_prompt_has_all_zones_and_race_power()
    {
        var prompt = PromptBuilder.Build([], [], new DateOnly(2026, 10, 27), Ironman with { Ftp = 250, SwimCssSec = 115 / 0.9144 });

        Assert.Contains("My bike power zones (FTP 250 W", prompt);
        Assert.Contains("Z2 Endurance: 140-188 W", prompt);
        Assert.Contains("My swim pace zones (CSS 1:55/100yd)", prompt);
        Assert.Contains("Race bike power: 68%-75% of FTP = 170-188 W", prompt);
        Assert.DoesNotContain("FTP not set", prompt);
        Assert.DoesNotContain("CSS not set", prompt);
    }

    [Fact]
    public void Marathon_prompt_shows_bike_zones_only_when_ftp_is_set()
    {
        var marathon = Ironman with { Race = RaceType.Marathon, GoalTime = new TimeSpan(3, 30, 0) };

        var without = PromptBuilder.Build([], [], new DateOnly(2026, 10, 27), marathon);
        Assert.StartsWith("Recommend ONE run for me today.", without);
        Assert.DoesNotContain("power zones", without);
        Assert.DoesNotContain("Split guide", without);

        Assert.Contains("My bike power zones (FTP 250 W", PromptBuilder.Build([], [], new DateOnly(2026, 10, 27), marathon with { Ftp = 250 }));
    }

    [Fact]
    public async Task Week_prompt_for_ironman_asks_for_time_per_sport()
    {
        var llm = new ScriptedLlm();
        var coach = new Coach(new TrainingDataCache(new SampleSource(), TimeSpan.FromHours(3)), llm, Ironman, new ChatHistory(null));

        await coach.WeekAsync(default);

        var prompt = llm.Calls.Single().Single().Text;
        Assert.Contains("weekly time per sport (swim, bike, run)", prompt);
        Assert.Contains("type, duration (h:mm), and the pace, power range", prompt);
        Assert.Contains("Plan in hours: the week's total must land in \"This week\"", prompt);
        Assert.Contains("one brick", prompt);
        Assert.Contains("Weekly volume guide for my Ironman", prompt);
    }

    [Fact]
    public async Task Chat_can_set_ironman_goal_and_ftp_including_a_numeric_ftp()
    {
        var store = new SettingsStore(null);
        var call = new ToolCall("update_goal", new Dictionary<string, string?>
        {
            ["goal_type"] = "ironman", ["goal_time"] = "12:00:00", ["race_date"] = null, ["race_name"] = "Ironman Arizona",
            ["ftp"] = "250", ["swim_css"] = null, ["marathon_time"] = null,
        });
        var marathonEnv = Ironman with { Race = RaceType.Marathon, GoalTime = null, Goal = "Marathon in 3:30:00" };
        var coach = new Coach(new TrainingDataCache(new SampleSource(), TimeSpan.FromHours(3)),
            new ToolCallingLlm(new LlmReply("Arizona, nice!", [call])), marathonEnv, new ChatHistory(null), store);

        var answer = await coach.ChatAsync("I'm doing Ironman Arizona, aiming for 12 hours. My FTP is 250.", default);

        Assert.Equal(new CoachSettings("12:00:00", null, "Ironman Arizona", "ironman", 250), store.Current);
        Assert.Contains("✅ Goal updated.", answer);
        Assert.Contains("**Goal:** Ironman in 12:00:00", answer);
        Assert.Contains("Z4 Threshold: 228-262 W", answer);
    }
}

public class TriVolumeTests
{
    private static readonly DateOnly Today = new(2026, 10, 27);

    private static readonly Config Ironman = new(null, 1, null, Config.DefaultModel, "Ironman in 12:00:00", "2027-03-01", "none",
        GoalTime: new TimeSpan(12, 0, 0), Units: DistanceUnit.Miles, Race: RaceType.Ironman);

    [Theory]
    [InlineData(200, Block.Base)]
    [InlineData(60, Block.Build)]
    [InlineData(30, Block.Peak)]
    [InlineData(10, Block.Taper)]
    [InlineData(0, Block.RaceDay)]
    [InlineData(-3, Block.After)]
    public void Blocks_follow_the_weeks_to_race_day(int daysOut, Block expected) =>
        Assert.Equal(expected, TrainingPhase.BlockOf(Today, Today.AddDays(daysOut)));

    [Fact]
    public void No_race_date_is_its_own_block() => Assert.Equal(Block.NoDate, TrainingPhase.BlockOf(Today, null));

    [Fact]
    public void Below_the_range_grows_about_10_percent()
    {
        var (lo, hi, why) = TriVolume.Target(RaceType.Ironman, Block.Build, 6);
        Assert.Equal(6, lo);
        Assert.Equal(6.6, hi, 3);
        Assert.Contains("below the range", why);
    }

    [Fact]
    public void Growth_never_passes_the_top_of_the_range()
    {
        var (_, hi, _) = TriVolume.Target(RaceType.HalfIronman, Block.Build, 9.5);
        Assert.Equal(10, hi);
    }

    [Fact]
    public void At_the_top_it_holds_and_taper_cuts_back()
    {
        Assert.Equal((11, 14), TriVolume.Target(RaceType.Ironman, Block.Build, 15) is var t ? (t.Lo, t.Hi) : default);
        Assert.Equal((6, 9), TriVolume.Target(RaceType.Ironman, Block.Taper, 15) is var u ? (u.Lo, u.Hi) : default);
    }

    [Fact]
    public void No_recent_training_starts_at_the_low_end()
    {
        var (lo, hi, why) = TriVolume.Target(RaceType.Ironman, Block.Base, 0);
        Assert.Equal((8.0, 8.0), (lo, hi));
        Assert.Contains("start at the low end", why);
    }

    [Fact]
    public void Guide_averages_the_last_4_weeks_by_sport()
    {
        // Each week: 3 h run, 4 h bike, 1 h swim = 8 h; an older week must not count.
        var runs = Enumerable.Range(0, 4).Select(w => new RunSummary(Today.AddDays(-7 * w), "Run", 30, 180, "6:00/km", null, null))
            .Append(new RunSummary(Today.AddDays(-35), "Old", 50, 600, "6:00/km", null, null)).ToList();
        var cross = Enumerable.Range(0, 4).SelectMany(w => new[]
        {
            new CrossTraining(Today.AddDays(-7 * w - 1), Sport.Bike, "virtual_ride", "Ride", 100, 4 * 3600),
            new CrossTraining(Today.AddDays(-7 * w - 2), Sport.Swim, "lap_swimming", "Swim", 3, 3600),
        }).ToList();

        var lines = TriVolume.Lines(Ironman, Today, runs, cross).ToList();

        Assert.Equal("Weekly volume guide for my Ironman (hours, computed; plan the week to fit it):", lines[0]);
        Assert.Equal("- Last 4 weeks averaged 8h00 a week: swim 1h00, bike 4h00, run 3h00", lines[1]);
        Assert.Contains("(base): 8-11 h a week", lines[2]);
        Assert.StartsWith("- This week: 8h00-8h48 in total (within the range", lines[3]);
        Assert.Contains("swim 15-20% (1h16-1h41)", lines[4]);
    }

    [Fact]
    public void Marathon_goal_has_no_volume_guide()
    {
        Assert.Empty(TriVolume.Lines(Ironman with { Race = RaceType.Marathon }, Today, [], []));
        Assert.DoesNotContain("Weekly volume guide", PromptBuilder.Build([], [], Today, Ironman with { Race = RaceType.Marathon }));
    }

    [Fact]
    public void Ironman_today_prompt_carries_the_guide() =>
        Assert.Contains("- This week: 8h00 in total (no training", PromptBuilder.Build([], [], Today, Ironman));
}

using MarathonCoach;

namespace MarathonCoach.Tests;

public class PaceZoneTests
{
    private static readonly TimeSpan Goal = new(3, 30, 0);

    [Fact]
    public void Marathon_pace_for_3_30_is_4_58_per_km() =>
        Assert.Equal("4:58", PaceZones.Format(PaceZones.MarathonPace(Goal)));

    [Fact]
    public void Zones_for_3_30_match_the_expected_ranges()
    {
        var zones = PaceZones.For(Goal).Select(z => z.ToString()).ToList();

        Assert.Equal(5, zones.Count);
        Assert.StartsWith("Easy: 5:43-6:13/km", zones[0]);
        Assert.StartsWith("Long run: 5:22-5:43/km", zones[1]);
        Assert.StartsWith("Marathon pace: 4:55-5:01/km", zones[2]);
        Assert.StartsWith("Threshold: 4:37-4:43/km", zones[3]);
        Assert.StartsWith("Interval: 4:19-4:25/km", zones[4]);
    }

    [Fact]
    public void Every_zone_is_fast_to_slow_and_ordered_slowest_first()
    {
        var zones = PaceZones.For(Goal);
        Assert.All(zones, z => Assert.True(z.FastSecPerKm < z.SlowSecPerKm));
        Assert.Equal(zones.OrderByDescending(z => z.FastSecPerKm), zones);
    }

    [Fact]
    public void A_faster_goal_gives_faster_zones() =>
        Assert.True(PaceZones.For(new TimeSpan(3, 15, 0))[0].FastSecPerKm < PaceZones.For(Goal)[0].FastSecPerKm);
}

public class TrainingPhaseTests
{
    private static readonly DateOnly Today = new(2026, 10, 19);

    [Theory]
    [InlineData(null, "No race date")]
    [InlineData(100, "Base")]
    [InlineData(70, "Build")]
    [InlineData(35, "Peak")]
    [InlineData(10, "Taper")]
    [InlineData(0, "Race day")]
    [InlineData(-3, "recovery")]
    public void Phase_follows_the_days_to_race(int? daysToRace, string expected)
    {
        var race = daysToRace is { } d ? Today.AddDays(d) : (DateOnly?)null;
        Assert.Contains(expected, TrainingPhase.Describe(Today, race));
    }

    [Fact]
    public void Shows_weeks_and_days_to_go() =>
        Assert.StartsWith("14 weeks and 2 days to race day.", TrainingPhase.Describe(Today, Today.AddDays(100)));
}

public class MorningScheduleTests
{
    private static readonly TimeOnly SixThirty = new(6, 30);

    [Fact]
    public void Before_the_time_it_runs_today() =>
        Assert.Equal(new DateTime(2026, 10, 19, 6, 30, 0), MorningSchedule.NextRun(new DateTime(2026, 10, 19, 5, 0, 0), SixThirty));

    [Fact]
    public void After_the_time_it_runs_tomorrow() =>
        Assert.Equal(new DateTime(2026, 10, 20, 6, 30, 0), MorningSchedule.NextRun(new DateTime(2026, 10, 19, 21, 0, 0), SixThirty));

    [Fact]
    public void Exactly_at_the_time_it_moves_to_tomorrow_so_it_never_sends_twice() =>
        Assert.Equal(new DateTime(2026, 10, 20, 6, 30, 0), MorningSchedule.NextRun(new DateTime(2026, 10, 19, 6, 30, 0), SixThirty));
}

public class ConfigGoalTests
{
    [Fact]
    public void Default_goal_text_matches_the_default_zones() =>
        Assert.Equal("Marathon in 3:30:00 (goal pace ~4:58/km)", Config.DescribeGoal(Config.DefaultGoalTime));

    [Fact]
    public void Race_day_parses_only_iso_dates()
    {
        Config With(string race) => new(null, 0, null, Config.DefaultModel, "", race, "none");
        Assert.Equal(new DateOnly(2027, 4, 18), With("2027-04-18").RaceDay);
        Assert.Null(With("April 18").RaceDay);
        Assert.Null(With("not set yet").RaceDay);
    }

    [Fact]
    public void Unset_goal_time_falls_back_to_3_30() =>
        Assert.Equal(new TimeSpan(3, 30, 0), new Config(null, 0, null, "", "", "", "").TargetTime);
}

public class WeekPromptTests
{
    [Fact]
    public async Task Week_sends_zones_phase_and_format_and_is_recorded()
    {
        var llm = new ScriptedLlm();
        var history = new ChatHistory(null);
        var config = new Config(null, 1, null, Config.DefaultModel, Config.DescribeGoal(Config.DefaultGoalTime),
            DateOnly.FromDateTime(DateTime.Today).AddDays(70).ToString("yyyy-MM-dd"), "none");
        var coach = new Coach(new TrainingDataCache(new SampleSource(), TimeSpan.FromHours(3)), llm, config, history);

        await coach.WeekAsync(default);

        var prompt = llm.Calls.Single().Single().Text;
        Assert.StartsWith("Plan my next 7 days", prompt);
        Assert.Contains("Threshold: 4:37-4:43/km", prompt);
        Assert.Contains("Build:", prompt);
        Assert.Contains("one line per day", prompt);
        Assert.StartsWith("/week", history.Recent(2)[0].Text);
    }

    [Fact]
    public void Today_prompt_also_carries_the_zones()
    {
        var config = new Config(null, 0, null, Config.DefaultModel, "goal", "not set yet", "none");
        var prompt = PromptBuilder.Build([], [], new DateOnly(2026, 10, 19), config);
        Assert.Contains("My pace zones (computed from a 3:30:00 goal", prompt);
        Assert.Contains("Phase: No race date set yet", prompt);
    }
}

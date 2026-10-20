using MarathonCoach;

namespace MarathonCoach.Tests;

public class UnitsTests
{
    private static readonly DateOnly Today = new(2026, 10, 19);

    private static Config Miles() => new(null, 1, null, Config.DefaultModel,
        Config.DescribeGoal(Config.DefaultGoalTime, DistanceUnit.Miles), "not set yet", "none",
        Units: DistanceUnit.Miles);

    [Theory]
    [InlineData("mi", DistanceUnit.Miles)]
    [InlineData(" Miles ", DistanceUnit.Miles)]
    [InlineData("mile", DistanceUnit.Miles)]
    [InlineData("km", DistanceUnit.Km)]
    [InlineData(null, DistanceUnit.Km)]
    [InlineData("furlongs", DistanceUnit.Km)]
    public void Units_setting_parses(string? value, DistanceUnit expected) =>
        Assert.Equal(expected, UnitFormat.Parse(value));

    [Fact]
    public void Goal_pace_for_3_30_is_8_00_per_mile() =>
        Assert.Equal("Marathon in 3:30:00 (goal pace ~8:00/mi)", Config.DescribeGoal(Config.DefaultGoalTime, DistanceUnit.Miles));

    [Fact]
    public void Zones_for_3_30_in_miles()
    {
        var zones = PaceZones.For(Config.DefaultGoalTime).Select(z => z.Describe(DistanceUnit.Miles)).ToList();

        Assert.StartsWith("Easy: 9:12-10:00/mi", zones[0]);
        Assert.StartsWith("Long run: 8:39-9:12/mi", zones[1]);
        Assert.StartsWith("Marathon pace: 7:55-8:05/mi", zones[2]);
        Assert.StartsWith("Threshold: 7:26-7:36/mi", zones[3]);
        Assert.StartsWith("Interval: 6:58-7:07/mi", zones[4]);
    }

    [Theory]
    [InlineData("4:45/km", "7:38/mi")]
    [InlineData("6:00/km", "9:39/mi")]
    [InlineData("n/a", "n/a")]
    public void Stored_km_paces_convert_to_miles(string perKm, string perMile) =>
        Assert.Equal(perMile, UnitFormat.ConvertPace(perKm, DistanceUnit.Miles));

    [Fact]
    public void Km_paces_are_left_alone_in_km_mode() =>
        Assert.Equal("4:45/km", UnitFormat.ConvertPace("4:45/km", DistanceUnit.Km));

    [Fact]
    public void Prompt_in_miles_has_no_km_figures()
    {
        RunSummary Run(int daysAgo, double km, string pace) => new(Today.AddDays(-daysAgo), "Run", km, 60, pace, 150, 3.0);

        var prompt = PromptBuilder.Build([Run(1, 10, "4:45/km"), Run(3, 16.09344, "6:00/km")], [], Today, Miles());

        Assert.Contains("Units: miles and min/mile", prompt);
        Assert.Contains("- last 7 days: 16.2 mi", prompt);
        Assert.Contains("6.2 mi in 60 min, 7:38/mi", prompt);
        Assert.Contains("10 mi in 60 min, 9:39/mi", prompt);
        Assert.Contains("Marathon pace: 7:55-8:05/mi", prompt);
        Assert.Contains("goal pace ~8:00/mi", prompt);
        Assert.DoesNotContain("/km", prompt);
        Assert.DoesNotMatch(@"\d km\b", prompt);
    }

    [Fact]
    public async Task Week_prompt_in_miles_asks_for_miles()
    {
        var llm = new ScriptedLlm();
        var coach = new Coach(new TrainingDataCache(new SampleSource(), TimeSpan.FromHours(3)), llm, Miles(), new ChatHistory(null));

        await coach.WeekAsync(default);

        var prompt = llm.Calls.Single().Single().Text;
        Assert.Contains("weekly total in miles", prompt);
        Assert.Contains("@ 7:26-7:36/mi", prompt);
        Assert.DoesNotContain("/km", prompt);
    }

    [Fact]
    public void Goal_set_from_chat_is_described_in_miles()
    {
        var store = new SettingsStore(null);
        store.Update("3:25", null, null, Today);

        Assert.Equal("Marathon in 3:25:00 (goal pace ~7:49/mi)", store.Apply(Miles()).Goal);
    }

    [Fact]
    public void Goal_summary_shows_mile_zones()
    {
        var coach = new Coach(new TrainingDataCache(new SampleSource(), TimeSpan.FromHours(3)), new ScriptedLlm(), Miles(),
            new ChatHistory(null));

        var summary = coach.GoalSummary(Today);

        Assert.Contains("Marathon pace: 7:55-8:05/mi", summary);
        Assert.DoesNotContain("/km", summary);
    }

    [Fact]
    public void Persona_prompt_is_unit_neutral_so_it_stays_cached() =>
        Assert.DoesNotContain("min/km", PromptBuilder.SystemPrompt);
}

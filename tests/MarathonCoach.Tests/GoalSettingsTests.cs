using MarathonCoach;

namespace MarathonCoach.Tests;

public sealed class GoalSettingsTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 19);
    private static readonly Config Env = new(null, 1, null, Config.DefaultModel,
        Config.DescribeGoal(Config.DefaultGoalTime), "not set yet", "none");

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mc-settings-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "coach-settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Theory]
    [InlineData("3:25", "3:25:00")]
    [InlineData("3:25:00", "3:25:00")]
    [InlineData(" 3h25 ", "3:25:00")]
    [InlineData("4:05:30", "4:05:30")]
    public void Goal_time_formats_are_accepted(string input, string saved)
    {
        var store = new SettingsStore(null);
        Assert.True(store.Update(input, null, null, Today).Ok);
        Assert.Equal(saved, store.Current.GoalTime);
    }

    [Theory]
    [InlineData("1:45:00")] // faster than any sane marathon goal
    [InlineData("8:00:00")]
    [InlineData("fast")]
    [InlineData("325")]
    public void Bad_goal_times_are_refused(string input) =>
        Assert.False(new SettingsStore(null).Update(input, null, null, Today).Ok);

    [Fact]
    public void Past_or_badly_formatted_race_dates_are_refused()
    {
        var store = new SettingsStore(null);
        Assert.Contains("in the past", store.Update(null, "2026-10-01", null, Today).Message);
        Assert.Contains("yyyy-MM-dd", store.Update(null, "Oct 3 2027", null, Today).Message);
    }

    [Fact]
    public void One_bad_value_saves_nothing()
    {
        var store = new SettingsStore(null);
        var (ok, _) = store.Update("3:25", "not a date", "St George", Today);

        Assert.False(ok);
        Assert.Equal(new CoachSettings(), store.Current);
    }

    [Fact]
    public void Saved_settings_survive_a_restart_and_override_env()
    {
        new SettingsStore(FilePath).Update("3:25", "2027-10-02", "St George Marathon", Today);

        var config = new SettingsStore(FilePath).Apply(Env);

        Assert.Equal(new TimeSpan(3, 25, 0), config.TargetTime);
        Assert.Equal(new DateOnly(2027, 10, 2), config.RaceDay);
        Assert.Equal("St George Marathon", config.RaceName);
        Assert.Equal("Marathon in 3:25:00 (goal pace ~4:51/km)", config.Goal);
    }

    [Fact]
    public void Changing_one_value_keeps_the_others()
    {
        var store = new SettingsStore(null);
        store.Update("3:25", "2027-10-02", "St George", Today);
        store.Update(null, null, "St George Marathon", Today);

        Assert.Equal(new CoachSettings("3:25:00", "2027-10-02", "St George Marathon"), store.Current);
    }

    [Fact]
    public void Nothing_saved_means_env_values_are_used() =>
        Assert.Equal(Env, new SettingsStore(null).Apply(Env));
}

/// <summary>Plays back one scripted reply, including tool calls.</summary>
internal sealed class ToolCallingLlm(LlmReply reply) : ICoachLlm
{
    public List<IReadOnlyList<ChatTurn>> Calls { get; } = [];

    public Task<string> AskAsync(string system, IReadOnlyList<ChatTurn> turns, CancellationToken ct)
    {
        Calls.Add(turns);
        return Task.FromResult("plain answer");
    }

    public Task<LlmReply> AskWithToolAsync(string system, IReadOnlyList<ChatTurn> turns, CoachTool tool, CancellationToken ct)
    {
        Calls.Add(turns);
        return Task.FromResult(reply);
    }
}

public class GoalFromChatTests
{
    private static readonly Config Env = new(null, 1, null, Config.DefaultModel,
        Config.DescribeGoal(Config.DefaultGoalTime), "not set yet", "none");

    private static (Coach Coach, SettingsStore Store, ToolCallingLlm Llm) Setup(LlmReply reply)
    {
        var store = new SettingsStore(null);
        var llm = new ToolCallingLlm(reply);
        var coach = new Coach(new TrainingDataCache(new SampleSource(), TimeSpan.FromHours(3)), llm, Env,
            new ChatHistory(null), store);
        return (coach, store, llm);
    }

    private static ToolCall Update(string? time, string? date, string? name) =>
        new("update_goal", new Dictionary<string, string?> { ["goal_time"] = time, ["race_date"] = date, ["race_name"] = name });

    [Fact]
    public async Task A_tool_call_saves_the_goal_and_the_reply_shows_new_zones()
    {
        var future = DateOnly.FromDateTime(DateTime.Today).AddDays(120).ToString("yyyy-MM-dd");
        var (coach, store, _) = Setup(new("Nice, St George it is!", [Update("3:25:00", future, "St George Marathon")]));

        var answer = await coach.ChatAsync("I signed up for St George, aiming for 3:25", default);

        Assert.Equal(new CoachSettings("3:25:00", future, "St George Marathon"), store.Current);
        Assert.StartsWith("Nice, St George it is!", answer);
        Assert.Contains("✅ Goal updated.", answer);
        Assert.Contains("Marathon pace: 4:48-4:54/km", answer); // 3:25 zones, not 3:30
    }

    [Fact]
    public async Task An_invalid_tool_call_saves_nothing_and_says_why()
    {
        var (coach, store, _) = Setup(new("", [Update(null, "2001-01-01", null)]));

        var answer = await coach.ChatAsync("my race was in 2001", default);

        Assert.Equal(new CoachSettings(), store.Current);
        Assert.Contains("⚠️ Nothing saved", answer);
        Assert.Contains("in the past", answer);
    }

    [Fact]
    public async Task Without_a_tool_call_nothing_changes()
    {
        var (coach, store, _) = Setup(new("What if 3:20? You'd need more volume.", []));

        var answer = await coach.ChatAsync("what if I ran 3:20?", default);

        Assert.Equal(new CoachSettings(), store.Current);
        Assert.Equal("What if 3:20? You'd need more volume.", answer);
    }

    [Fact]
    public async Task Prompts_after_the_change_use_the_new_goal()
    {
        var (coach, _, llm) = Setup(new("ok", [Update("3:25", null, null)]));

        await coach.ChatAsync("aim for 3:25", default);
        await coach.TodayAsync(default);

        var todayPrompt = llm.Calls[1].Single().Text;
        Assert.Contains("Marathon in 3:25:00", todayPrompt);
        Assert.Contains("computed from a 3:25:00 goal", todayPrompt);
    }

    [Fact]
    public void Goal_command_with_no_options_shows_current_settings()
    {
        var (coach, store, _) = Setup(new("", []));

        var shown = coach.SetGoal(null, null, null, new DateOnly(2026, 10, 19));

        Assert.Contains("**Goal:** Marathon in 3:30:00", shown);
        Assert.Contains("**Race:** not set", shown);
        Assert.Equal(new CoachSettings(), store.Current);
    }

    [Fact]
    public void Update_goal_tool_schema_is_strict_and_all_fields_nullable()
    {
        var schema = System.Text.Json.JsonDocument.Parse(Coach.UpdateGoalTool.JsonSchema).RootElement;
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(3, schema.GetProperty("required").GetArrayLength());
        foreach (var p in schema.GetProperty("properties").EnumerateObject())
            Assert.Contains("null", p.Value.GetProperty("type").EnumerateArray().Select(t => t.GetString()));
    }
}

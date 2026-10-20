using System.Text;

namespace MarathonCoach;

/// <summary>Pulls training data and asks the LLM for coaching. No Discord here, so it is easy to test.</summary>
public sealed class Coach(
    TrainingDataCache data, ICoachLlm llm, Config baseConfig, ChatHistory history, SettingsStore? settings = null)
{
    /// <summary>How many past turns Claude sees (10 exchanges): enough to follow up, cheap to send.</summary>
    public const int ContextTurns = 20;

    private readonly SettingsStore _settings = settings ?? new SettingsStore(null);

    /// <summary>The model may call this when the runner states a new goal or race in a DM.</summary>
    public static readonly CoachTool UpdateGoalTool = new(
        "update_goal",
        "Save the runner's marathon goal or race when they tell you a new one (e.g. 'I signed up for St George on " +
        "Oct 3 2027' or 'let's aim for 3:25'). Pass only what they said; null for the rest. Don't call it for " +
        "hypotheticals ('what if I ran 3:20?'). Convert dates to yyyy-MM-dd using today's date from the data.",
        """
        {
          "type": "object",
          "properties": {
            "goal_time": { "type": ["string", "null"], "description": "Finish time as h:mm:ss, e.g. 3:25:00" },
            "race_date": { "type": ["string", "null"], "description": "Race day as yyyy-MM-dd" },
            "race_name": { "type": ["string", "null"], "description": "Race name, e.g. St George Marathon" }
          },
          "required": ["goal_time", "race_date", "race_name"],
          "additionalProperties": false
        }
        """);

    /// <summary>.env settings with the goal and race saved from Discord on top.</summary>
    public Config Config => _settings.Apply(baseConfig);

    public async Task<string> TodayAsync(CancellationToken ct)
    {
        var d = await data.GetAsync(forceRefresh: false, ct);
        var answer = await llm.AskAsync(
            PromptBuilder.SystemPrompt, [new ChatTurn(true, PromptBuilder.Build(d.Runs, d.Recovery, d.Today, Config))], ct);
        // Recorded so a follow-up DM ("make it shorter") knows what today's workout was.
        history.AddExchange("/today: what should I run today?", answer);
        return answer;
    }

    public async Task<string> WeekAsync(CancellationToken ct)
    {
        var d = await data.GetAsync(forceRefresh: false, ct);
        var answer = await llm.AskAsync(PromptBuilder.SystemPrompt, [new ChatTurn(true, PromptBuilder.Week(d, Config))], ct);
        history.AddExchange("/week: plan my next 7 days", answer);
        return answer;
    }

    /// <summary>The morning DM: fresh Garmin data (last night's sleep and HRV), then today's workout.</summary>
    public async Task<string> MorningAsync(CancellationToken ct)
    {
        await data.GetAsync(forceRefresh: true, ct);
        return "Good morning! " + await TodayAsync(ct);
    }

    /// <summary>
    /// A free-form message: past conversation, then today's data and the message. If Claude calls
    /// <see cref="UpdateGoalTool"/>, the values are checked and saved here, and the reply says what changed.
    /// </summary>
    public async Task<string> ChatAsync(string message, CancellationToken ct)
    {
        var d = await data.GetAsync(forceRefresh: false, ct);
        var turns = history.Recent(ContextTurns).Append(new ChatTurn(true, PromptBuilder.Chat(d, Config, message))).ToList();
        var reply = await llm.AskWithToolAsync(PromptBuilder.SystemPrompt, turns, UpdateGoalTool, ct);

        var answer = new StringBuilder(reply.Text);
        foreach (var call in reply.ToolCalls.Where(c => c.Name == UpdateGoalTool.Name))
        {
            if (answer.Length > 0) answer.AppendLine().AppendLine();
            answer.Append(SetGoal(call.Args.GetValueOrDefault("goal_time"),
                call.Args.GetValueOrDefault("race_date"), call.Args.GetValueOrDefault("race_name"), d.Today));
        }

        var text = answer.Length > 0 ? answer.ToString() : "(no answer)";
        history.AddExchange(message, text);
        return text;
    }

    /// <summary>Checks and saves goal/race (null = unchanged), then shows the result. All null = show current.</summary>
    public string SetGoal(string? goalTime, string? raceDate, string? raceName, DateOnly today)
    {
        if (goalTime is null && raceDate is null && raceName is null) return GoalSummary(today);
        var (ok, message) = _settings.Update(goalTime, raceDate, raceName, today);
        return ok ? "✅ Goal updated.\n" + GoalSummary(today) : "⚠️ Nothing saved: " + message;
    }

    public string GoalSummary(DateOnly today)
    {
        var c = Config;
        var sb = new StringBuilder();
        sb.AppendLine($"**Goal:** {c.Goal}");
        sb.AppendLine($"**Race:** {c.RaceName ?? "not set"}, {(c.RaceDay is { } r ? r.ToString("ddd d MMM yyyy") : "date not set")}");
        sb.AppendLine($"**Phase:** {TrainingPhase.Describe(today, c.RaceDay)}");
        sb.AppendLine("**Pace zones:**");
        foreach (var zone in PaceZones.For(c.TargetTime)) sb.AppendLine($"- {zone.Describe(c.Units)}");
        return sb.ToString().TrimEnd();
    }

    public async Task<string> SyncAsync(CancellationToken ct) =>
        "Synced: " + (await data.GetAsync(forceRefresh: true, ct)).Summary;

    public string Reset()
    {
        history.Clear();
        return "Chat history cleared.";
    }
}

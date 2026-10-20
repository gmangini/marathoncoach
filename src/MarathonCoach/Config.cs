namespace MarathonCoach;

/// <summary>Settings from environment variables, after loading the repo's .env file.</summary>
public sealed record Config(
    string? DiscordToken, ulong OwnerId, ulong? GuildId,
    string AnthropicModel, string Goal, string RaceDate, string Notes,
    TimeSpan? GoalTime = null, TimeOnly? MorningTime = null, string? RaceName = null,
    DistanceUnit Units = DistanceUnit.Km)
{
    public const string DefaultModel = "claude-sonnet-5-5";
    public static readonly TimeSpan DefaultGoalTime = new(3, 30, 0);

    /// <summary>The goal marathon time the pace zones come from (GOAL_TIME, default 3:30:00).</summary>
    public TimeSpan TargetTime => GoalTime ?? DefaultGoalTime;

    /// <summary>RACE_DATE as a date, or null when it is unset or not yyyy-MM-dd.</summary>
    public DateOnly? RaceDay => DateOnly.TryParseExact(RaceDate, "yyyy-MM-dd", out var d) ? d : null;

    public static Config FromEnvironment()
    {
        TimeSpan? goalTime = TimeSpan.TryParse(Env("GOAL_TIME"), out var g) && g > TimeSpan.FromHours(2) ? g : null;
        var units = UnitFormat.Parse(Env("UNITS"));
        return new(
            Env("DISCORD_BOT_TOKEN"),
            ulong.TryParse(Env("DISCORD_OWNER_ID"), out var owner) ? owner : 0,
            ulong.TryParse(Env("DISCORD_GUILD_ID"), out var guild) ? guild : null,
            Env("ANTHROPIC_MODEL") ?? DefaultModel,
            Env("COACH_GOAL") ?? DescribeGoal(goalTime ?? DefaultGoalTime, units),
            Env("RACE_DATE") ?? "not set yet",
            Env("RUN_NOTES") ?? "none",
            goalTime,
            TimeOnly.TryParse(Env("MORNING_TIME"), out var morning) ? morning : null,
            Env("RACE_NAME"),
            units);
    }

    /// <summary>"Marathon in 3:30:00 (goal pace ~4:58/km)", or "~8:00/mi" in miles.</summary>
    public static string DescribeGoal(TimeSpan goal, DistanceUnit units = DistanceUnit.Km) =>
        $"Marathon in {goal:h\\:mm\\:ss} (goal pace ~{UnitFormat.Pace(PaceZones.MarathonPace(goal), units)})";

    private static string? Env(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;
}

/// <summary>Minimal .env loader: KEY=VALUE lines, # comments, optional quotes. Real env vars win.</summary>
public static class DotEnv
{
    /// <summary>Loads the first .env found walking up from <paramref name="startDir"/>.</summary>
    public static string? Load(string startDir)
    {
        for (var dir = new DirectoryInfo(startDir); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, ".env");
            if (!File.Exists(path)) continue;
            foreach (var (key, value) in Parse(File.ReadAllLines(path)))
                if (Environment.GetEnvironmentVariable(key) is null)
                    Environment.SetEnvironmentVariable(key, value);
            return path;
        }
        return null;
    }

    public static IEnumerable<(string Key, string Value)> Parse(IEnumerable<string> lines)
    {
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var value = line[(eq + 1)..].Trim();
            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0])
                value = value[1..^1];
            yield return (line[..eq].Trim(), value);
        }
    }
}

namespace MarathonCoach;

/// <summary>Settings from environment variables, after loading the repo's .env file.</summary>
public sealed record Config(
    string? DiscordToken, ulong OwnerId, ulong? GuildId,
    string AnthropicModel, string Goal, string RaceDate, string Notes)
{
    public const string DefaultModel = "claude-sonnet-5-5";

    public static Config FromEnvironment() => new(
        Env("DISCORD_BOT_TOKEN"),
        ulong.TryParse(Env("DISCORD_OWNER_ID"), out var owner) ? owner : 0,
        ulong.TryParse(Env("DISCORD_GUILD_ID"), out var guild) ? guild : null,
        Env("ANTHROPIC_MODEL") ?? DefaultModel,
        Env("COACH_GOAL") ?? PromptBuilder.DefaultGoal,
        Env("RACE_DATE") ?? "not set yet",
        Env("RUN_NOTES") ?? "none");

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

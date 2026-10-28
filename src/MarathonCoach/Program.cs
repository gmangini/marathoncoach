using MarathonCoach;

// Usage:
//   dotnet run                        Discord bot on your Garmin data
//   dotnet run -- --sample            Discord bot on fake data (no Garmin login)
//   dotnet run -- --console           print today's workout here, no Discord
//   dotnet run -- --prompt            print what would be sent to Claude (no API call, free)
//   dotnet run -- --chat              chat with the coach here, no Discord
//   add --week to --console or --prompt for the 7-day plan, --review for a review of your latest run
// Add --sample to any of them to use fake data instead of Garmin.
//
// Settings come from .env (see .env.example).

DotEnv.Load(Directory.GetCurrentDirectory());
var config = Config.FromEnvironment();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

IGarminSource source = args.Contains("--sample") ? new SampleSource() : new GarminSource();
var data = new TrainingDataCache(source, maxAge: TimeSpan.FromHours(3));
ICoachLlm llm = args.Contains("--prompt") ? new PrintPromptLlm() : new ClaudeClient(config.AnthropicModel);
// --prompt answers are prompts, not coaching, so they never go into the saved history, plans or reviewed runs.
var dryRun = args.Contains("--prompt");
string? Saved(string file) => dryRun ? null : Path.Combine("data", file);
var history = new ChatHistory(dryRun
    ? null
    : Environment.GetEnvironmentVariable("CHAT_HISTORY_PATH") ?? Path.Combine("data", "chat-history.json"));
var settings = new SettingsStore(Path.Combine("data", "coach-settings.json"));
var coach = new Coach(data, llm, config, history, settings,
    new PlanStore(Saved("plans.json")), new ReviewedRuns(Saved("reviewed-runs.json")));

if (coach.Config.RaceDate != "not set yet" && coach.Config.RaceDay is null)
    Console.Error.WriteLine($"RACE_DATE '{coach.Config.RaceDate}' isn't a yyyy-MM-dd date, so it's ignored.");

if (args.Contains("--prompt") || args.Contains("--console"))
{
    // Add --week for the 7-day plan, or --review for a review of your latest run, instead of today's workout.
    Console.WriteLine(
        args.Contains("--week") ? await coach.WeekAsync(cts.Token)
        : args.Contains("--review") ? await coach.ReviewLatestAsync(cts.Token)
        : await coach.TodayAsync(cts.Token));
    return 0;
}

if (args.Contains("--chat"))
{
    Console.WriteLine("Chat with your coach (same history as Discord DMs). Empty line to quit.");
    while (Console.ReadLine() is { Length: > 0 } line)
        Console.WriteLine($"\n{await coach.ChatAsync(line, cts.Token)}\n");
    return 0;
}

if (config.DiscordToken is null || config.OwnerId == 0)
{
    Console.Error.WriteLine("Set DISCORD_BOT_TOKEN and DISCORD_OWNER_ID in .env (see .env.example).");
    return 1;
}

// Two copies of the bot (say, your PC and the server) would both answer every DM. This stops a second one on the
// same machine; across machines, stop one yourself (see README).
using var single = new Mutex(initiallyOwned: true, @"Global\MarathonCoach.Bot", out var first);
if (!first)
{
    Console.Error.WriteLine("Another copy of the bot is already running on this machine. Stop it first.");
    return 2;
}

// On a server the clock is UTC unless TZ is set; the morning DM and "today" follow this zone.
Console.WriteLine($"Time zone: {TimeZoneInfo.Local.Id}, local time {DateTime.Now:yyyy-MM-dd HH:mm}");

// Load the data before going online, so a Garmin 2FA prompt or login error shows here at startup.
Console.WriteLine("Loading training data...");
Console.WriteLine(await coach.SyncAsync(cts.Token));

await new Bot(config, coach).RunAsync(cts.Token);
return 0;

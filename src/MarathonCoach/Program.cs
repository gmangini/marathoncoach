using MarathonCoach;

// Usage:
//   dotnet run                        Discord bot on your Garmin data
//   dotnet run -- --sample            Discord bot on fake data (no Garmin login)
//   dotnet run -- --console           print today's workout here, no Discord
//   dotnet run -- --prompt            print what would be sent to Claude (no API call, free)
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
var coach = new Coach(data, llm, config);

if (args.Contains("--prompt") || args.Contains("--console"))
{
    Console.WriteLine(await coach.TodayAsync(cts.Token));
    return 0;
}

if (config.DiscordToken is null || config.OwnerId == 0)
{
    Console.Error.WriteLine("Set DISCORD_BOT_TOKEN and DISCORD_OWNER_ID in .env (see .env.example).");
    return 1;
}

// Load the data before going online, so a Garmin 2FA prompt or login error shows here at startup.
Console.WriteLine("Loading training data...");
Console.WriteLine(await coach.SyncAsync(cts.Token));

await new Bot(config, coach).RunAsync(cts.Token);
return 0;

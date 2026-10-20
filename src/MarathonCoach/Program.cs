using MarathonCoach;

// Usage:
//   dotnet run -- --sample            Discord bot on fake data (no Garmin login)
//   dotnet run                        Discord bot on your Garmin data
//   dotnet run -- --sample --console  print today's workout here, no Discord
//   dotnet run -- --sample --prompt   print what would be sent to Claude (no API call, free)
//
// Settings come from .env (see .env.example).

DotEnv.Load(Directory.GetCurrentDirectory());
var config = Config.FromEnvironment();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

IGarminSource source = args.Contains("--sample") ? new SampleSource() : new GarminSource();
ICoachLlm llm = args.Contains("--prompt") ? new PrintPromptLlm() : new ClaudeClient(config.AnthropicModel);
var coach = new Coach(source, llm, config);

if (args.Contains("--prompt"))
{
    Console.WriteLine(await coach.TodayAsync(cts.Token));
    return 0;
}

if (args.Contains("--console"))
{
    Console.WriteLine(await coach.TodayAsync(cts.Token));
    return 0;
}

if (config.DiscordToken is null || config.OwnerId == 0)
{
    Console.Error.WriteLine("Set DISCORD_BOT_TOKEN and DISCORD_OWNER_ID in .env (see .env.example).");
    return 1;
}

await new Bot(config, coach).RunAsync(cts.Token);
return 0;

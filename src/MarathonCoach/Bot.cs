using Discord;
using Discord.WebSocket;

namespace MarathonCoach;

/// <summary>Discord front end: slash commands, owner only.</summary>
public sealed class Bot(Config config, Coach coach)
{
    private readonly DiscordSocketClient _client = new(new DiscordSocketConfig { GatewayIntents = GatewayIntents.Guilds });

    public async Task RunAsync(CancellationToken ct)
    {
        _client.Log += m => { Console.WriteLine($"[discord] {m}"); return Task.CompletedTask; };
        _client.Ready += RegisterCommandsAsync;
        _client.SlashCommandExecuted += OnSlashCommandAsync;

        await _client.LoginAsync(TokenType.Bot, config.DiscordToken);
        await _client.StartAsync();
        try { await Task.Delay(Timeout.Infinite, ct); }
        catch (OperationCanceledException) { }
        await _client.StopAsync();
    }

    private async Task RegisterCommandsAsync()
    {
        ApplicationCommandProperties[] commands =
        [
            Command("today", "Today's recommended workout"),
            Command("sync", "Re-read your Garmin data now"),
        ];

        // A server command shows up at once; a global one can take a while the first time.
        if (config.GuildId is { } guildId && _client.GetGuild(guildId) is { } guild)
            await guild.BulkOverwriteApplicationCommandAsync(commands);
        else
            await _client.BulkOverwriteGlobalApplicationCommandsAsync(commands);
        Console.WriteLine("Ready. Send /today or /sync in Discord. Ctrl+C to stop.");

        static ApplicationCommandProperties Command(string name, string description) => new SlashCommandBuilder()
            .WithName(name)
            .WithDescription(description)
            .WithContextTypes(InteractionContextType.Guild, InteractionContextType.BotDm, InteractionContextType.PrivateChannel)
            .Build();
    }

    private async Task OnSlashCommandAsync(SocketSlashCommand command)
    {
        if (!OwnerGuard.IsOwner(command.User.Id, config.OwnerId))
        {
            await command.RespondAsync("This coach only works for its owner.", ephemeral: true);
            return;
        }

        // Claude can take longer than Discord's 3 seconds, so acknowledge first.
        await command.DeferAsync();
        try
        {
            var answer = command.CommandName switch
            {
                "today" => await coach.TodayAsync(CancellationToken.None),
                "sync" => await coach.SyncAsync(CancellationToken.None),
                _ => "Unknown command.",
            };
            foreach (var chunk in DiscordText.Split(answer))
                await command.FollowupAsync(chunk);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[bot] {command.CommandName} failed: {ex}");
            await command.FollowupAsync($"Something went wrong: {ex.GetType().Name}. Details are in the bot's console.");
        }
    }
}

public static class OwnerGuard
{
    /// <summary>Only the configured owner gets data or LLM calls. An unset owner (0) locks everyone out.</summary>
    public static bool IsOwner(ulong userId, ulong ownerId) => ownerId != 0 && userId == ownerId;
}

public static class DiscordText
{
    public const int Limit = 2000;

    /// <summary>Splits text into Discord-sized messages, preferring line breaks.</summary>
    public static IEnumerable<string> Split(string text, int limit = Limit)
    {
        if (string.IsNullOrWhiteSpace(text)) { yield return "(empty answer)"; yield break; }
        var rest = text.Trim();
        while (rest.Length > limit)
        {
            var cut = rest.LastIndexOf('\n', limit - 1);
            if (cut <= 0) cut = limit;
            yield return rest[..cut].TrimEnd();
            rest = rest[cut..].TrimStart();
        }
        yield return rest;
    }
}

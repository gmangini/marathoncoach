using Discord;
using Discord.WebSocket;

namespace MarathonCoach;

/// <summary>Discord front end: slash commands and DM chat, owner only.</summary>
public sealed class Bot(Config config, Coach coach)
{
    // DirectMessages is enough to read DM text: Discord sends DM content without the privileged Message Content intent.
    private readonly DiscordSocketClient _client = new(new DiscordSocketConfig
    {
        GatewayIntents = GatewayIntents.Guilds | GatewayIntents.DirectMessages,
    });

    public async Task RunAsync(CancellationToken ct)
    {
        _client.Log += m => { Console.WriteLine($"[discord] {m}"); return Task.CompletedTask; };
        _client.Ready += RegisterCommandsAsync;
        // Added to the server while running: register there without a restart.
        _client.JoinedGuild += guild => guild.Id == config.GuildId ? RegisterCommandsAsync() : Task.CompletedTask;
        _client.SlashCommandExecuted += OnSlashCommandAsync;
        // Handlers run on the gateway thread; Claude is slow, so hand DMs off to keep the connection healthy.
        _client.MessageReceived += message => { _ = Task.Run(() => OnMessageAsync(message)); return Task.CompletedTask; };

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
            Command("reset", "Forget our chat history"),
        ];

        // A server command shows up at once; a global one can take a while the first time.
        // Only one of the two is kept, so a command never shows up twice.
        if (config.GuildId is { } guildId)
        {
            if (_client.GetGuild(guildId) is not { } guild)
            {
                Console.Error.WriteLine(
                    $"The bot is not in server {guildId} yet, so it can't be found or DM'd. Add it with:\n" +
                    $"https://discord.com/oauth2/authorize?client_id={_client.CurrentUser.Id}" +
                    $"&scope=bot+applications.commands&permissions=2147486720&guild_id={guildId}\n" +
                    "Commands are registered as soon as it joins.");
                return;
            }
            await guild.BulkOverwriteApplicationCommandAsync(commands);
            await _client.BulkOverwriteGlobalApplicationCommandsAsync([]);
        }
        else
            await _client.BulkOverwriteGlobalApplicationCommandsAsync(commands);
        Console.WriteLine("Ready. DM the bot, or send /today, /sync or /reset in Discord. Ctrl+C to stop.");

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
                "reset" => coach.Reset(),
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

    private async Task OnMessageAsync(SocketMessage message)
    {
        // Only DMs from people; server channels and the bot's own messages are ignored.
        if (message.Author.IsBot || message.Channel is not IDMChannel || string.IsNullOrWhiteSpace(message.Content))
            return;

        if (!OwnerGuard.IsOwner(message.Author.Id, config.OwnerId))
        {
            await message.Channel.SendMessageAsync("This coach only works for its owner.");
            return;
        }

        try
        {
            using var typing = message.Channel.EnterTypingState();
            var answer = await coach.ChatAsync(message.Content, CancellationToken.None);
            foreach (var chunk in DiscordText.Split(answer))
                await message.Channel.SendMessageAsync(chunk);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[bot] DM failed: {ex}");
            await message.Channel.SendMessageAsync($"Something went wrong: {ex.GetType().Name}. Details are in the bot's console.");
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

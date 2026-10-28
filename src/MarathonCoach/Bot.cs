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
        // Background jobs run side by side until Ctrl+C; each one catches its own errors and keeps going.
        List<Task> jobs = [Task.Delay(Timeout.Infinite, ct)];
        if (config.MorningTime is { } at) jobs.Add(MorningLoopAsync(at, ct));
        if (config.RunCheckMinutes > 0) jobs.Add(RunWatchLoopAsync(TimeSpan.FromMinutes(config.RunCheckMinutes), ct));
        try { await Task.WhenAll(jobs); }
        catch (OperationCanceledException) { }
        await _client.StopAsync();
    }

    /// <summary>
    /// Every <paramref name="every"/>, checks Garmin for runs not reviewed yet and DMs a review of each. The first
    /// check runs at startup (on a first-ever start it only records the existing runs, so no old reviews are sent).
    /// </summary>
    private async Task RunWatchLoopAsync(TimeSpan every, CancellationToken ct)
    {
        Console.WriteLine($"Checking for new runs every {every.TotalMinutes:0} min.");
        while (true)
        {
            try
            {
                foreach (var review in await coach.CheckNewRunsAsync(ct))
                {
                    await SendToOwnerAsync(review);
                    Console.WriteLine($"[bot] run review sent {DateTime.Now:HH:mm}");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.Error.WriteLine($"[bot] run check failed (will retry): {ex.GetType().Name}: {ex.Message}");
            }
            await Task.Delay(every, ct);
        }
    }

    private async Task SendToOwnerAsync(string text)
    {
        var owner = await _client.GetUserAsync(config.OwnerId);
        foreach (var chunk in DiscordText.Split(text))
            await owner.SendMessageAsync(chunk);
    }

    /// <summary>
    /// DMs today's workout once a day at <paramref name="at"/>. Checks the clock every minute rather than sleeping
    /// until the time, so a PC that slept through it still sends when it wakes (same day only).
    /// </summary>
    private async Task MorningLoopAsync(TimeOnly at, CancellationToken ct)
    {
        var next = MorningSchedule.NextRun(DateTime.Now, at);
        Console.WriteLine($"Morning DM scheduled for {next:ddd HH:mm}.");
        while (true)
        {
            await Task.Delay(TimeSpan.FromMinutes(1), ct);
            var now = DateTime.Now;
            if (now < next) continue;

            if (now.Date == next.Date) await SendMorningAsync();
            next = MorningSchedule.NextRun(now, at);
            Console.WriteLine($"Next morning DM: {next:ddd HH:mm}.");
        }
    }

    private async Task SendMorningAsync()
    {
        try
        {
            await SendToOwnerAsync(await coach.MorningAsync(CancellationToken.None));
            Console.WriteLine($"[bot] morning DM sent {DateTime.Now:HH:mm}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[bot] morning DM failed: {ex}");
        }
    }

    private async Task RegisterCommandsAsync()
    {
        ApplicationCommandProperties[] commands =
        [
            Command("today", "Today's recommended workout"),
            Command("week", "Your plan for the next 7 days"),
            Command("review", "Review your latest run"),
            Command("sync", "Re-read your Garmin data now"),
            Command("reset", "Forget our chat history"),
            new SlashCommandBuilder()
                .WithName("goal")
                .WithDescription("Show or change your goal time and race (leave all empty to show)")
                .AddOption("time", ApplicationCommandOptionType.String, "Goal finish time, e.g. 3:25:00", isRequired: false)
                .AddOption("race_date", ApplicationCommandOptionType.String, "Race day as yyyy-MM-dd", isRequired: false)
                .AddOption("race_name", ApplicationCommandOptionType.String, "Race name", isRequired: false)
                .WithContextTypes(InteractionContextType.Guild, InteractionContextType.BotDm, InteractionContextType.PrivateChannel)
                .Build(),
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
        Console.WriteLine("Ready. DM the bot, or send /today, /week, /review, /goal, /sync or /reset in Discord. Ctrl+C to stop.");

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
            var answer = await RunCommandAsync(command.CommandName,
                name => command.Data.Options.FirstOrDefault(o => o.Name == name)?.Value as string);
            foreach (var chunk in DiscordText.Split(answer))
                await command.FollowupAsync(chunk);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[bot] {command.CommandName} failed: {ex}");
            await command.FollowupAsync($"Something went wrong: {ex.GetType().Name}. Details are in the bot's console.");
        }
    }

    /// <summary>One place for what each command does, whether it came as a slash command or typed in a DM.</summary>
    private async Task<string> RunCommandAsync(string name, Func<string, string?> option) => name switch
    {
        "today" => await coach.TodayAsync(CancellationToken.None),
        "week" => await coach.WeekAsync(CancellationToken.None),
        "review" => await coach.ReviewLatestAsync(CancellationToken.None),
        "sync" => await coach.SyncAsync(CancellationToken.None),
        "reset" => coach.Reset(),
        "goal" => coach.SetGoal(option("time"), option("race_date"), option("race_name"), DateOnly.FromDateTime(DateTime.Today)),
        _ => "Unknown command.",
    };

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
            // Server slash commands don't show up in DMs, so a typed "/review" arrives as text: run it as the command.
            var answer = TypedCommand.TryParse(message.Content, out var name)
                ? await RunCommandAsync(name, _ => null)
                : await coach.ChatAsync(message.Content, CancellationToken.None);
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

public static class TypedCommand
{
    public static readonly string[] Names = ["today", "week", "review", "sync", "reset", "goal"];

    /// <summary>
    /// True when a DM is exactly one of the commands ("/review", "/Today "). Anything more ("/goal 3:25",
    /// "/review was it too fast?") goes to chat, where Claude can still act on it.
    /// </summary>
    public static bool TryParse(string text, out string name)
    {
        var t = text.Trim();
        name = t.Length > 1 && t[0] == '/' ? t[1..].ToLowerInvariant() : "";
        return Names.Contains(name);
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

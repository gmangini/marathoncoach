# marathoncoach

A Discord bot that coaches you toward a 3:30 marathon. It reads your Garmin Connect runs and recovery
(sleep, HRV, resting HR), and Claude recommends what to run. See [SPEC.md](SPEC.md) for what it does
and [PLAN.md](PLAN.md) for progress.

## Setup (once)

1. **.NET 8 SDK**, then `dotnet restore`.
2. **Discord bot:** at https://discord.com/developers/applications click *New Application*. On **Bot**,
   click *Reset Token* and copy it. On **Installation**, tick *Guild Install* with scopes `bot` +
   `applications.commands`, open the install link and add the bot to a private server of yours.
3. **Claude:** create an API key at https://console.anthropic.com.
4. `copy .env.example .env` and fill in `DISCORD_BOT_TOKEN`, `DISCORD_OWNER_ID`, `DISCORD_GUILD_ID`
   and `ANTHROPIC_API_KEY`. `.env` is git-ignored.

## Run

```powershell
dotnet run --project src/MarathonCoach                # Discord bot on your Garmin data
dotnet run --project src/MarathonCoach -- --console   # today's workout printed here, no Discord
dotnet run --project src/MarathonCoach -- --prompt    # what would be sent to Claude (free, no API call)
dotnet run --project src/MarathonCoach -- --chat      # chat with the coach here, no Discord
```

Add `--sample` to any of them to use fake data instead of Garmin.

In Discord, **DM the bot** in plain words to chat ("legs are trashed, swap tomorrow?"). Claude sees your
current data and the last 10 exchanges; the conversation is saved in `data/chat-history.json`
(git-ignored) and survives restarts. Commands: `/today` for the workout, `/sync` to re-read Garmin now
(data otherwise refreshes after 3 hours or on a new day), `/reset` to forget the chat, `/week` for a
7-day plan, `/goal` to show or change your goal time, race date and race name.

You can also change the goal in a DM in plain words ("I signed up for St George on Oct 3 2027, aiming for
3:25"). The values are checked in code and saved in `data/coach-settings.json` (git-ignored), and saved
values win over `.env`. Pace zones (easy, long, marathon, threshold, interval) are computed from the goal
time, never guessed by Claude.

Set `MORNING_TIME=06:30` in `.env` for a daily DM with that day's workout (the bot must be running).
To DM the bot, click its name in your server's member list and send a message. Only the user in `DISCORD_OWNER_ID` gets an answer. If Garmin asks for a 2FA
code, the bot asks for it in its console window at startup.

## Test

```powershell
dotnet test
```

## Notes

- Garmin has no official personal API. `Unofficial.Garmin.Connect` uses Garmin's private web API and
  can break when Garmin changes it. Tokens are cached in `%USERPROFILE%\.marathoncoach-garmin-token.json`.
- TrainerRoad and most apps sync to Garmin Connect, so Garmin alone covers them.
- Each `/today` is one Claude API call (default `claude-sonnet-5-5`, about 2k tokens in).

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
```

Add `--sample` to any of them to use fake data instead of Garmin.

In Discord: `/today` for the workout, `/sync` to re-read Garmin now (data otherwise refreshes after
3 hours or on a new day). Only the user in `DISCORD_OWNER_ID` gets an answer. If Garmin asks for a 2FA
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

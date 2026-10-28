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
(data otherwise refreshes after 3 hours or on a new day), `/reset` to forget the chat, `/help` to list the commands, `/week` for a
7-day plan, `/goal` to show or change your goal time, race date and race name.

You can also change the goal in a DM in plain words ("I signed up for St George on Oct 3 2027, aiming for
3:25"). The values are checked in code and saved in `data/coach-settings.json` (git-ignored), and saved
values win over `.env`. Pace zones (easy, long, marathon, threshold, interval) are computed from the goal
time, never guessed by Claude.

Set `MORNING_TIME=06:30` in `.env` for a daily DM with that day's workout (the bot must be running).

**After every run** the bot checks Garmin (every `RUN_CHECK_MINUTES`, default 30; `0` turns it off) and DMs
a short review: did you hit the plan or zone, heart-rate drift computed from your laps, time in zones, and a
suggestion for tomorrow. It checks against what `/today`, the morning DM or `/week` recommended for that day
(saved in `data/plans.json`). Each run is reviewed once (`data/reviewed-runs.json`); on the first start it only
records your existing runs. `/review` reviews your latest run on demand; on the console use `--prompt --review`
(free) or `--console --review`.
To DM the bot, click its name in your server's member list and send a message. Only the user in `DISCORD_OWNER_ID` gets an answer. If Garmin asks for a 2FA
code, the bot asks for it in its console window at startup.

## Cloud server (always on, for your phone)

The bot runs as a Docker container on a small Linux VPS (Hetzner CX22 or a DigitalOcean $6 droplet).
Secrets come from the server's own `.env`; chat history, goal settings and the Garmin token live in the
`coach-data` Docker volume, so they survive redeploys. The image itself holds no secrets.

**Once:**
1. Create the server: Ubuntu 24.04, the smallest size is plenty, a US region, and add your SSH public key
   (`type $HOME\.ssh\id_ed25519.pub`; make one with `ssh-keygen -t ed25519` if you have none).
2. Install Docker on it: `ssh root@<ip> "curl -fsSL https://get.docker.com | sh"`.
3. Make sure the bot has logged in to Garmin on this PC once (it saves `%USERPROFILE%\.marathoncoach-garmin-token.json`).
   The server can't answer a 2FA prompt, so it uses this token.
4. Deploy with secrets: `pwsh scripts/Deploy-Server.ps1 -Server root@<ip> -CopySecrets -Apply`
   (without `-Apply` it only shows what it would do). It adds `TZ=America/Denver` to the server's `.env`
   if missing, so "today" and `MORNING_TIME` follow your time zone; pass `-TimeZone` to change it.
5. **Stop the bot on your PC**: two running copies would both answer every DM.

**After a code change:** commit, then `pwsh scripts/Deploy-Server.ps1 -Server root@<ip> -Apply`
(only committed code ships).

**On the server:** `cd ~/marathoncoach`, then `docker compose logs -f bot` (logs),
`docker compose restart bot`, `docker compose down` (stop). It restarts by itself after a crash or reboot.

If Garmin ever asks for 2FA again, the log says so: run the bot on your PC once, then redeploy with `-CopySecrets`.

## Test

```powershell
dotnet test
podman build -t marathoncoach:test .   # or docker build
```

## Notes

- Garmin has no official personal API. `Unofficial.Garmin.Connect` uses Garmin's private web API and
  can break when Garmin changes it. Tokens are cached in `%USERPROFILE%\.marathoncoach-garmin-token.json`.
- TrainerRoad and most apps sync to Garmin Connect, so Garmin alone covers them.
- Each `/today` is one Claude API call (default `claude-sonnet-5-5`, about 2k tokens in).

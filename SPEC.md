# Spec: marathoncoach

Status: approved 2026-10-19

## Problem
I want a coach that reads my real training and recovery data and tells me what to run today, so I hit a
3:30 marathon. Garmin Connect has the data (TrainerRoad and the watch sync there) but no coach I can talk to.

## Users and setting
Just me, through Discord (phone or desktop). The bot runs on my Windows PC.

## Acceptance criteria
- [ ] AC-1: Given the bot is running, when I send `/today` in Discord, then it replies with today's workout (type, distance or duration, pace or HR target, and a "swap if" rule) based on my last 8 weeks of runs and 7 days of recovery.
- [ ] AC-2: Given my Garmin login in `.env`, when the bot starts, then it reads my real runs, sleep, HRV and resting HR (2FA asked once on the console, tokens cached), and when I send `/sync` then it refreshes that data.
- [ ] AC-3: Given the bot is running, when I DM it in plain words, then Claude answers using my training data and our recent conversation, and the history survives a restart.
- [ ] AC-4: Given a goal of 3:30 and an optional race date, when I send `/week`, then it replies with a 7-day plan whose paces come from goal-derived zones (marathon pace about 4:58/km) computed in code.
- [ ] AC-5: Given a morning time in `.env`, when that time arrives, then the bot DMs me today's workout.
- [ ] AC-6: Given Strava credentials in `.env`, when the bot loads data, then Strava runs missing from Garmin are added and a session in both is counted once.
- [ ] AC-7: Given a message or command from any Discord user other than the owner ID in `.env`, when the bot receives it, then it replies with no data and makes no LLM call.
- [ ] AC-8: Given the bot is running, when I tell it a new goal time, race date or race name by DM or with `/goal`, then it checks and saves them (surviving a restart), replies with the new pace zones, and later plans use them.
- [ ] AC-9: Given the bot is deployed to my cloud server, when my PC is off, then DMs, slash commands and the morning DM (in my time zone) still work, and chat history, goal settings and the Garmin login survive a redeploy.

## Non-goals (not in this version)
- Pushing workouts to the watch or to Garmin's calendar
- A web UI, multiple users
- Cycling or swimming plans

## Constraints
- Data: personal health data stays on my own machines (this PC and my own cloud server) and goes only to the Anthropic API in prompts. Secrets live in the server's `.env`, never in the image or git.
- Cost: Anthropic API, pay per call (default model `claude-sonnet-5-5`, changeable in `.env`). Discord and Garmin are free. Cloud server about $5/month.
- Garmin: no official personal API; uses the unofficial `Unofficial.Garmin.Connect` package, which can break.
- Strava: the API agreement restricts AI use; AC-6 is optional and last.
- Platform: .NET 8; runs on Windows for development and in a Linux container on a small cloud server (always on, for phone use).

## Stack
`other`: .NET 8 console host + Discord.Net + Anthropic SDK + xUnit, extending the ironmancoach code.

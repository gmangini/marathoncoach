# Plan: marathoncoach

Spec: [SPEC.md](SPEC.md) · Stack: .NET 8, Discord.Net, Anthropic SDK, xUnit

## Slices

| Id | Goal (what the user can do after it) | ACs | Status | Base commit |
|---|---|---|---|---|
| S1 | Pilot: `/today` in Discord answers from sample data + Claude, owner-only | AC-1, AC-7 | done | (first commit) |
| S2 | Real Garmin data behind `/today`, `/sync` refresh | AC-2 | done | 81ed5c9 → 7bb37eb |
| S3 | DM chat with Claude, history persisted | AC-3 | done | 7bb37eb |
| S4 | `/week` plan with code-computed pace zones; morning DM; goal from chat | AC-4, AC-5, AC-8 | done | 3ba0c50 |
| S5 | Strava merge (optional) | AC-6 | todo | |
| S6 | Always on: container on a small cloud server | AC-9 | paused (packaged, not deployed) | 0219eca |
| S7 | Feedback after each run (watcher + review DM, `/review`) | AC-10 | done | a56e6b6 |
| S8 | Multisport data: cross-training in every prompt (first step toward Ironman via `/goal`) | AC-11 | done | 694f8ba |

Status is one of `todo`, `building`, `review`, `done`.

## S1: Pilot
- **Files:** `src/MarathonCoach/` (ironmancoach code + `Config.cs`, `Coach.cs`, `ClaudeClient.cs`, `Bot.cs`),
  `tests/MarathonCoach.Tests/`
- **Tests:** owner guard, `.env` parsing, Discord message splitting, coach prompt carries goal and data (fake LLM)
- **Run it:** `dotnet run --project src/MarathonCoach -- --sample`
- **Done when:** tests pass and `/today` answers in your Discord.

## S2: Real Garmin data
- **Files:** `TrainingData.cs` (snapshot + cache: 3 h max age, new day, or `/sync`), `Coach.cs`, `Bot.cs` (`/sync`),
  `Program.cs` (load at startup), `PromptBuilder.cs` (every week listed, 0 km included)
- **Tests:** `GarminDataTests.cs`: cache reuse/expiry/new day/forced sync, 8-week range, empty weeks show 0 km
- **Run it:** `dotnet run --project src/MarathonCoach -- --console`
- **Done when:** tests pass and `/today` or `--console` coaches from your real runs.

## S3: DM chat
- **Files:** `ChatHistory.cs` (JSON in `data/`, alternating turns, 200 exchanges kept), `Coach.cs` (`ChatAsync`,
  `/today` recorded, `Reset`), `PromptBuilder.cs` (`DataBlock`, `Chat`), `Bot.cs` (DM handler, `/reset`),
  `Program.cs` (`--chat`)
- **Tests:** `ChatTests.cs`: history survives restart, cap and user-first, clear, trim, in-memory; chat sends
  history + data + message, saves only the message, `/today` feeds follow-ups, reset
- **Run it:** `dotnet run --project src/MarathonCoach -- --chat`, then DM the bot
- **Done when:** tests pass and a DM conversation remembers context after a restart.

## S4: Weekly plan, morning DM, goal from chat
- **Files:** `TrainingPlan.cs` (pace zones from goal time, phase from race date, morning schedule),
  `CoachSettings.cs` (goal/race saved from Discord, validated, wins over `.env`), `Coach.cs` (`WeekAsync`,
  `MorningAsync`, `update_goal` tool, `SetGoal`), `ClaudeClient.cs` (one strict tool, call returned not answered),
  `Bot.cs` (`/week`, `/goal`, morning loop), `Config.cs` (`GOAL_TIME`, `RACE_NAME`, `MORNING_TIME`)
- **Tests:** `PlanTests.cs` (zones for 3:30, phases, schedule, week prompt), `GoalSettingsTests.cs` (parsing,
  validation, all-or-nothing, persistence, tool call saves and shows new zones, invalid call saves nothing)
- **Added mid-slice (user request):** set goal time, race date and race name from chat (AC-8 below).
- **Run it:** `/week`, `/goal`, or DM "aim for 3:25 at St George on 2027-10-02"
- **Done when:** tests pass and you tried `/week`, `/goal` and a goal change by DM.

## S6: Always on (cloud server)
- **Files:** `Dockerfile` (Debian .NET 8 runtime, non-root, `/app/data` volume), `compose.yaml` (restart
  unless-stopped, `.env` at run time), `.dockerignore`, `.gitattributes` (LF), `scripts/Deploy-Server.ps1`
  (dry run by default; ships only HEAD; `-CopySecrets` copies `.env` + Garmin token into the volume),
  `GarminSource.cs` (`GARMIN_TOKEN_PATH`, clear error when 2FA has no console), `Program.cs` (one copy per
  machine, logs time zone)
- **Checks:** image has no `.env`/token/history; `TZ=America/Denver` gives Mountain time; volume writable by `app`;
  deploy dry run lists every step
- **Run it:** `pwsh scripts/Deploy-Server.ps1 -Server root@<ip> -CopySecrets -Apply`
- **Done when:** the bot answers from the server with the PC off, and the morning DM arrives on time.
- **Paused 2026-10-19. Resume here:** create the VPS (Ubuntu 24.04, your SSH key), install Docker, then
  `pwsh scripts/Deploy-Server.ps1 -Server root@<ip> -CopySecrets -Apply` and stop the PC copy. No SSH key exists on
  the PC yet (`ssh-keygen -t ed25519`).

## S7: Feedback after each run
- **Files:** `RunReview.cs` (heart-rate drift from laps, reviewed-runs and plan stores), `Models.cs` (activity id,
  laps, HR zones), `GarminSource.cs` (splits + HR-in-zones), `SampleSource.cs` (sample laps), `TrainingData.cs`
  (light recent-runs check), `Coach.cs` (`CheckNewRunsAsync`, `ReviewLatestAsync`, plans saved), `PromptBuilder.cs`
  (`RunReview`), `Bot.cs` (watch loop, `/review`), `Config.cs` (`RUN_CHECK_MINUTES`), `Program.cs` (`--review`)
- **Tests:** `RunReviewTests.cs`: drift values and when it is skipped, stores (first-start, restart, ordering,
  day vs week plan), watcher (one review per run, plan in prompt, retry after a failed review, `/review`)
- **Checked on real data:** `--prompt --review` on the Oct 23 run: laps in miles, -3.1% drift, zones
- **Run it:** `/review`, then go for a run and wait for the DM
- **Done when:** tests pass, `/review` works, and a real run gets a review DM by itself.

## S8: Multisport data
- **Files:** `Models.cs` (`Sport`, `CrossTraining`, load on runs, `GetCrossTrainingAsync`), `GarminSource.cs`
  (all activity types in one call, multisport wrappers skipped, moving time), `Multisport.cs` (weekly table across
  sports, one line per session), `TrainingData.cs` (cross-training in the snapshot; a Garmin failure on it keeps the
  runs), `PromptBuilder.cs` + `Coach.cs` (in `/today`, `/week`, chat and reviews), `SampleSource.cs` (sample ride,
  strength, swim)
- **Tests:** `MultisportTests.cs`: sport mapping, durations, swim yd/m and pace, ride and strength lines, weekly
  totals, prompt sections, every prompt carries it, Garmin failure keeps the runs
- **Checked on real data:** TrainerRoad rides with power/NP/TSS, swims at ~1:57/100yd, hikes on moving time
- **Next (Ironman via `/goal`):** S9 goal type + bike/swim zones (FTP, CSS), S10 triathlon plans in hours,
  S11 reviews for rides and swims

## Gaps
- none

## Decisions
- see `docs/decisions/`

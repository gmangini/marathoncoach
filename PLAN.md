# Plan: marathoncoach

Spec: [SPEC.md](SPEC.md) · Stack: .NET 8, Discord.Net, Anthropic SDK, xUnit

## Slices

| Id | Goal (what the user can do after it) | ACs | Status | Base commit |
|---|---|---|---|---|
| S1 | Pilot: `/today` in Discord answers from sample data + Claude, owner-only | AC-1, AC-7 | done (console-tried; Discord try pending) | (first commit) |
| S2 | Real Garmin data behind `/today`, `/sync` refresh | AC-2 | done | 0aae960 |
| S3 | DM chat with Claude, history persisted | AC-3 | building | S2 commit |
| S4 | `/week` plan with code-computed pace zones; morning DM | AC-4, AC-5 | todo | |
| S5 | Strava merge (optional) | AC-6 | todo | |

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

## Gaps
- none

## Decisions
- see `docs/decisions/`

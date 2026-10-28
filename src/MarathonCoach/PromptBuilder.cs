using System.Text;

namespace MarathonCoach;

public static class PromptBuilder
{
    public const string DefaultGoal = "Marathon in 3:30:00 (goal pace ~4:58/km, ~8:00/mile)";

    /// <summary>Stable coach persona (cached by the API). Per-day data goes in <see cref="Build"/>.</summary>
    public const string SystemPrompt = """
        You are an experienced marathon coach talking to one runner over Discord. You coach from their
        real Garmin training and recovery data, which they send you. Keep answers short enough to read on a
        phone, use the units given in the data (km or miles), be conservative when recovery looks poor, and never invent data you weren't given.
        """;

    /// <summary>The /today request: the data plus the answer format.</summary>
    public static string Build(
        IReadOnlyList<RunSummary> runs, IReadOnlyList<RecoveryDay> recovery, DateOnly today, Config config,
        IReadOnlyList<CrossTraining>? cross = null) =>
        "Recommend ONE run for me today.\n\n" + DataBlock(runs, recovery, today, config, cross) + """

            Respond with:
            1. Today's workout: type, distance/duration, target pace and/or HR range, and the structure if it's a quality session.
            2. Why: 2-3 sentences referencing my recent load and recovery.
            3. A "swap if" rule: when I should downgrade to easy or rest instead.

            """;

    /// <summary>The /week request: a 7-day plan starting today, paces only from the zones.</summary>
    public static string Week(TrainingData data, Config config)
    {
        var (example, unit) = config.Units == DistanceUnit.Miles
            ? ("Tue 21 Oct: Threshold 6 mi: 1 mi easy, 4 x 1 mi @ 7:26-7:36/mi, 1 mi easy", "miles")
            : ("Tue 21 Oct: Threshold 10 km: 2 km easy, 3 x 2 km @ 4:37-4:43/km, 2 km easy", "km");
        return "Plan my next 7 days, starting today.\n\n" + DataBlock(data.Runs, data.Recovery, data.Today, config, data.CrossTraining) + $"""

            Respond with one line per day (e.g. "{example}"):
            type, total {unit}, and the pace range from my zones for every part. Include rest days.
            Fit the week to my phase and to what I actually ran in the last 7 days, and don't put two hard days back to back.
            Then a weekly total in {unit} and one sentence on the week's focus.

            """;
    }

    /// <summary>The after-run review: the run (laps, drift, zones), what was planned, then the usual data.</summary>
    public static string RunReview(RunDetail run, PlanEntry? plan, TrainingData data, Config config)
    {
        var planned = plan is null
            ? "No plan was recorded for that day: judge the run against the zone that fits what it looks like it was meant to be."
            : $"What you recommended for that day (from {(plan.Kind == PlanStore.Day ? "/today" : $"the /week plan of {plan.Date:ddd d MMM}")}):\n{plan.Text}";
        return "I just finished a run. Review it.\n\n" + RunAnalysis.Describe(run, config.Units) + "\n" + planned + "\n\n" +
               DataBlock(data.Runs, data.Recovery, data.Today, config, data.CrossTraining) + """

            Respond in at most 6 short lines, for a phone:
            1. Plan: did I do what was planned and stay in the right pace zone? Name the zone.
            2. Effort: what my heart rate says (use the computed drift and time in zones; don't recompute them).
            3. Tomorrow: one concrete suggestion (type, distance, pace range from my zones), given this run and my recovery.

            """;
    }

    /// <summary>A chat message: today's data first, then what I wrote. Only the message goes into history.</summary>
    public static string Chat(TrainingData data, Config config, string message) =>
        "My current training data (for context):\n\n" + DataBlock(data.Runs, data.Recovery, data.Today, config, data.CrossTraining) +
        "\nMy message: " + message;

    /// <summary>Goal, weekly mileage, recent runs, cross-training and recovery, as plain lines.</summary>
    public static string DataBlock(
        IReadOnlyList<RunSummary> runs, IReadOnlyList<RecoveryDay> recovery, DateOnly today, Config config,
        IReadOnlyList<CrossTraining>? cross = null)
    {
        cross ??= [];
        var u = config.Units;
        var sb = new StringBuilder();
        sb.AppendLine($"Units: {UnitFormat.Name(u)} (answer in these)");
        sb.AppendLine($"Today: {today:yyyy-MM-dd} ({today.DayOfWeek})");
        sb.AppendLine($"Goal: {config.Goal}");
        if (config.RaceName is { } race) sb.AppendLine($"Race: {race}");
        sb.AppendLine($"Race date: {config.RaceDate}");
        sb.AppendLine($"Phase: {TrainingPhase.Describe(today, config.RaceDay)}");
        sb.AppendLine();
        sb.AppendLine($"My pace zones (computed from a {config.TargetTime:h\\:mm\\:ss} goal; use these, don't invent others):");
        foreach (var zone in PaceZones.For(config.TargetTime))
            sb.AppendLine($"- {zone.Describe(u)}");
        sb.AppendLine();
        sb.AppendLine($"Notes from me: {config.Notes}");
        sb.AppendLine();

        sb.AppendLine("Weekly mileage (most recent first):");
        // Every week is listed, including empty ones: a 0 km week is information the coach needs.
        var kmByWeek = runs
            .GroupBy(r => (today.DayNumber - r.Date.DayNumber) / 7)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Km));
        for (var w = 0; w < TrainingData.Weeks; w++)
            sb.AppendLine($"- {(w == 0 ? "last 7 days" : $"{w} wk ago")}: {UnitFormat.Distance(kmByWeek.GetValueOrDefault(w), u)}");

        sb.AppendLine();
        sb.AppendLine("Recent runs (last 20):");
        foreach (var r in runs.OrderBy(r => r.Date).TakeLast(20))
            sb.AppendLine($"- {r.Date:yyyy-MM-dd}: {UnitFormat.Distance(r.Km, u)} in {r.DurationMin} min, {UnitFormat.ConvertPace(r.Pace, u)}, " +
                          $"avg HR {r.AvgHr?.ToString() ?? "n/a"}, aerobic TE {r.AerobicTe?.ToString("0.0") ?? "n/a"} ({r.Name})");

        sb.AppendLine();
        sb.AppendLine("All training by week, every sport (load = Garmin training load, comparable across sports):");
        foreach (var line in MultisportFormat.WeeklyLines(runs, cross, today, u))
            sb.AppendLine(line);
        sb.AppendLine();
        sb.AppendLine("Recent cross-training (last 15 non-run sessions):");
        if (cross.Count == 0) sb.AppendLine("- none");
        foreach (var x in cross.OrderBy(x => x.Date).TakeLast(15))
            sb.AppendLine(MultisportFormat.SessionLine(x, u));
        sb.AppendLine("Count cross-training in my overall load and fatigue when you judge recovery and plan runs; " +
                      "running stays the priority for my marathon goal.");

        sb.AppendLine();
        sb.AppendLine("Recovery, last 7 days (most recent first):");
        foreach (var d in recovery.OrderByDescending(d => d.Date))
            sb.AppendLine($"- {d.Date:yyyy-MM-dd}: sleep {d.SleepHours?.ToString("0.0") ?? "n/a"}h " +
                          $"(score {d.SleepScore?.ToString() ?? "n/a"}), HRV {d.HrvLastNight?.ToString() ?? "n/a"} " +
                          $"({d.HrvStatus ?? "n/a"}), resting HR {d.RestingHr?.ToString() ?? "n/a"}");
        return sb.ToString();
    }
}

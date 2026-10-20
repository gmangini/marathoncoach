using System.Text;

namespace MarathonCoach;

public static class PromptBuilder
{
    public const string DefaultGoal = "Marathon in 3:30:00 (goal pace ~4:58/km, ~8:00/mile)";

    /// <summary>Stable coach persona (cached by the API). Per-day data goes in <see cref="Build"/>.</summary>
    public const string SystemPrompt = """
        You are an experienced marathon coach talking to one runner over Discord. You coach from their
        real Garmin training and recovery data, which they send you. Keep answers short enough to read on a
        phone, use km and min/km, be conservative when recovery looks poor, and never invent data you weren't given.
        """;

    /// <summary>The /today request: the data plus the answer format.</summary>
    public static string Build(
        IReadOnlyList<RunSummary> runs, IReadOnlyList<RecoveryDay> recovery, DateOnly today, Config config) =>
        "Recommend ONE run for me today.\n\n" + DataBlock(runs, recovery, today, config) + """

            Respond with:
            1. Today's workout: type, distance/duration, target pace and/or HR range, and the structure if it's a quality session.
            2. Why: 2-3 sentences referencing my recent load and recovery.
            3. A "swap if" rule: when I should downgrade to easy or rest instead.

            """;

    /// <summary>A chat message: today's data first, then what I wrote. Only the message goes into history.</summary>
    public static string Chat(TrainingData data, Config config, string message) =>
        "My current training data (for context):\n\n" + DataBlock(data.Runs, data.Recovery, data.Today, config) +
        "\nMy message: " + message;

    /// <summary>Goal, weekly mileage, recent runs and recovery, as plain lines.</summary>
    public static string DataBlock(
        IReadOnlyList<RunSummary> runs, IReadOnlyList<RecoveryDay> recovery, DateOnly today, Config config)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Today: {today:yyyy-MM-dd} ({today.DayOfWeek})");
        sb.AppendLine($"Goal: {config.Goal}");
        sb.AppendLine($"Race date: {config.RaceDate}");
        sb.AppendLine($"Notes from me: {config.Notes}");
        sb.AppendLine();

        sb.AppendLine("Weekly mileage (most recent first):");
        // Every week is listed, including empty ones: a 0 km week is information the coach needs.
        var kmByWeek = runs
            .GroupBy(r => (today.DayNumber - r.Date.DayNumber) / 7)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Km));
        for (var w = 0; w < TrainingData.Weeks; w++)
            sb.AppendLine($"- {(w == 0 ? "last 7 days" : $"{w} wk ago")}: {kmByWeek.GetValueOrDefault(w):0.#} km");

        sb.AppendLine();
        sb.AppendLine("Recent runs (last 20):");
        foreach (var r in runs.OrderBy(r => r.Date).TakeLast(20))
            sb.AppendLine($"- {r.Date:yyyy-MM-dd}: {r.Km:0.#} km in {r.DurationMin} min, {r.Pace}, " +
                          $"avg HR {r.AvgHr?.ToString() ?? "n/a"}, aerobic TE {r.AerobicTe?.ToString("0.0") ?? "n/a"} ({r.Name})");

        sb.AppendLine();
        sb.AppendLine("Recovery, last 7 days (most recent first):");
        foreach (var d in recovery.OrderByDescending(d => d.Date))
            sb.AppendLine($"- {d.Date:yyyy-MM-dd}: sleep {d.SleepHours?.ToString("0.0") ?? "n/a"}h " +
                          $"(score {d.SleepScore?.ToString() ?? "n/a"}), HRV {d.HrvLastNight?.ToString() ?? "n/a"} " +
                          $"({d.HrvStatus ?? "n/a"}), resting HR {d.RestingHr?.ToString() ?? "n/a"}");
        return sb.ToString();
    }
}

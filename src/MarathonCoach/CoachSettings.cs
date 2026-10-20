using System.Globalization;
using System.Text.Json;

namespace MarathonCoach;

/// <summary>Goal and race you set from Discord. Saved values win over .env.</summary>
public sealed record CoachSettings(string? GoalTime = null, string? RaceDate = null, string? RaceName = null);

/// <summary>Loads, validates and saves <see cref="CoachSettings"/> (data/coach-settings.json, git-ignored).</summary>
public sealed class SettingsStore
{
    private static readonly TimeSpan Fastest = TimeSpan.FromHours(2), Slowest = TimeSpan.FromHours(7);
    private readonly string? _path;
    private readonly object _gate = new();

    public SettingsStore(string? path)
    {
        _path = path;
        Current = path is not null && File.Exists(path)
            ? JsonSerializer.Deserialize<CoachSettings>(File.ReadAllText(path)) ?? new()
            : new();
    }

    public CoachSettings Current { get; private set; }

    /// <summary>The .env config with the saved goal and race on top.</summary>
    public Config Apply(Config baseConfig)
    {
        var s = Current;
        var goalTime = s.GoalTime is { } t && TryParseGoal(t, out var goal) ? goal : baseConfig.GoalTime;
        return baseConfig with
        {
            GoalTime = goalTime,
            // A goal set here replaces the .env goal sentence, which may quote other paces.
            Goal = s.GoalTime is not null ? Config.DescribeGoal(goalTime ?? Config.DefaultGoalTime) : baseConfig.Goal,
            RaceDate = s.RaceDate ?? baseConfig.RaceDate,
            RaceName = s.RaceName ?? baseConfig.RaceName,
        };
    }

    /// <summary>
    /// Checks and saves any of the three values (null = leave as is). Returns a message for the user:
    /// what changed, or why nothing did. All-or-nothing: one bad value saves none.
    /// </summary>
    public (bool Ok, string Message) Update(string? goalTime, string? raceDate, string? raceName, DateOnly today)
    {
        var errors = new List<string>();
        string? normalizedTime = null;
        if (goalTime is not null)
        {
            if (TryParseGoal(goalTime, out var t)) normalizedTime = t.ToString(@"h\:mm\:ss");
            else errors.Add($"Goal time '{goalTime}' should look like 3:30 or 3:25:00 (between 2:00 and 7:00).");
        }
        if (raceDate is not null)
        {
            if (!DateOnly.TryParseExact(raceDate.Trim(), "yyyy-MM-dd", out var d))
                errors.Add($"Race date '{raceDate}' should be yyyy-MM-dd, e.g. 2027-04-18.");
            else if (d < today)
                errors.Add($"Race date {d:yyyy-MM-dd} is in the past.");
        }
        if (raceName is not null && (raceName.Trim().Length is 0 or > 80))
            errors.Add("Race name should be 1-80 characters.");

        if (errors.Count > 0) return (false, string.Join("\n", errors));
        if (goalTime is null && raceDate is null && raceName is null) return (false, "Nothing to change.");

        lock (_gate)
        {
            Current = Current with
            {
                GoalTime = normalizedTime ?? Current.GoalTime,
                RaceDate = raceDate?.Trim() ?? Current.RaceDate,
                RaceName = raceName?.Trim() ?? Current.RaceName,
            };
            Save();
        }
        return (true, "Saved.");
    }

    /// <summary>"3:30", "3:30:00" or "3h30" style input, within a sane marathon range.</summary>
    public static bool TryParseGoal(string text, out TimeSpan goal)
    {
        var s = text.Trim().ToLowerInvariant().Replace("h", ":").Replace("m", "").TrimEnd(':');
        if (s.Count(c => c == ':') == 1) s += ":00";
        var ok = TimeSpan.TryParseExact(s, [@"h\:mm\:ss", @"h\:m\:ss"], CultureInfo.InvariantCulture, out goal);
        return ok && goal >= Fastest && goal <= Slowest;
    }

    private void Save()
    {
        if (_path is null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, _path, overwrite: true);
    }
}

using System.Globalization;
using System.Text.Json;

namespace MarathonCoach;

/// <summary>Goal, race and fitness numbers you set from Discord. Saved values win over .env.</summary>
/// <param name="SwimCssSecPer100m">CSS pace, always stored per 100 m (shown per 100 yd in miles mode).</param>
public sealed record CoachSettings(
    string? GoalTime = null, string? RaceDate = null, string? RaceName = null,
    string? GoalType = null, int? Ftp = null, double? SwimCssSecPer100m = null, string? MarathonTime = null);

/// <summary>What a /goal command or the update_goal tool asks to change; null = leave as is.</summary>
public sealed record GoalUpdate(
    string? GoalTime = null, string? RaceDate = null, string? RaceName = null,
    string? GoalType = null, int? Ftp = null, string? SwimCss = null, string? MarathonTime = null)
{
    public bool IsEmpty => this == new GoalUpdate();
}

/// <summary>Loads, validates and saves <see cref="CoachSettings"/> (data/coach-settings.json, git-ignored).</summary>
public sealed class SettingsStore
{
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

    public static bool FtpOk(int watts) => watts is >= 80 and <= 500;

    /// <summary>The .env config with the saved goal, race and fitness numbers on top.</summary>
    public Config Apply(Config baseConfig)
    {
        var s = Current;
        var race = RaceTypes.Parse(s.GoalType) ?? baseConfig.Race;
        // A saved time counts only if it fits the race; the .env time only if .env is for the same race.
        var goalTime = s.GoalTime is { } t && TryParseGoal(t, race, out var goal) ? goal
            : race == baseConfig.Race ? baseConfig.GoalTime : null;
        return baseConfig with
        {
            Race = race,
            GoalTime = goalTime,
            // A goal set here replaces the .env goal sentence, which may describe another race or pace.
            Goal = s.GoalTime is not null || s.GoalType is not null
                ? Config.DescribeGoal(goalTime ?? RaceTypes.DefaultGoal(race), baseConfig.Units, race)
                : baseConfig.Goal,
            RaceDate = s.RaceDate ?? baseConfig.RaceDate,
            RaceName = s.RaceName ?? baseConfig.RaceName,
            Ftp = s.Ftp ?? baseConfig.Ftp,
            SwimCssSec = s.SwimCssSecPer100m ?? baseConfig.SwimCssSec,
            MarathonTime = s.MarathonTime is { } m && TryParseGoal(m, RaceType.Marathon, out var mt) ? mt : baseConfig.MarathonTime,
        };
    }

    /// <summary>Goal time, race date and race name only (the original three).</summary>
    public (bool Ok, string Message) Update(string? goalTime, string? raceDate, string? raceName, DateOnly today) =>
        Update(new GoalUpdate(goalTime, raceDate, raceName), RaceType.Marathon, DistanceUnit.Km, today);

    /// <summary>
    /// Checks and saves what <paramref name="u"/> asks for. Returns a message for the user: what changed, or why
    /// nothing did. All-or-nothing: one bad value saves none. A goal time is checked against the race it is for:
    /// the new goal type if one is given, else <paramref name="currentRace"/>.
    /// </summary>
    public (bool Ok, string Message) Update(GoalUpdate u, RaceType currentRace, DistanceUnit units, DateOnly today)
    {
        if (u.IsEmpty) return (false, "Nothing to change.");
        var errors = new List<string>();
        var notes = new List<string>();

        RaceType? newRace = null;
        if (u.GoalType is not null)
        {
            newRace = RaceTypes.Parse(u.GoalType);
            if (newRace is null) errors.Add($"Goal type '{u.GoalType}' should be marathon, 70.3 or ironman.");
        }
        var race = newRace ?? currentRace;

        string? time = null;
        if (u.GoalTime is not null)
        {
            if (TryParseGoal(u.GoalTime, race, out var t)) time = t.ToString(@"h\:mm\:ss");
            else
            {
                var (lo, hi) = RaceTypes.GoalRange(race);
                errors.Add($"Goal time '{u.GoalTime}' should look like {lo:h\\:mm} or {hi:h\\:mm}:00 " +
                           $"(between {lo:h\\:mm} and {hi:h\\:mm} for a {RaceTypes.Name(race)}).");
            }
        }
        if (u.RaceDate is not null)
        {
            if (!DateOnly.TryParseExact(u.RaceDate.Trim(), "yyyy-MM-dd", out var d))
                errors.Add($"Race date '{u.RaceDate}' should be yyyy-MM-dd, e.g. 2027-04-18.");
            else if (d < today)
                errors.Add($"Race date {d:yyyy-MM-dd} is in the past.");
        }
        if (u.RaceName is not null && (u.RaceName.Trim().Length is 0 or > 80))
            errors.Add("Race name should be 1-80 characters.");
        if (u.Ftp is { } ftp && !FtpOk(ftp))
            errors.Add($"FTP {ftp} W should be between 80 and 500 W.");
        double? css = null;
        if (u.SwimCss is not null)
        {
            if (SwimZones.TryParseCss(u.SwimCss, units, out var c)) css = c;
            else errors.Add($"Swim CSS '{u.SwimCss}' should be a pace per 100 {(units == DistanceUnit.Miles ? "yd" : "m")} " +
                            "like 1:55 (between 1:00 and 3:30).");
        }
        string? marathon = null;
        if (u.MarathonTime is not null)
        {
            if (TryParseGoal(u.MarathonTime, RaceType.Marathon, out var m)) marathon = m.ToString(@"h\:mm\:ss");
            else errors.Add($"Marathon time '{u.MarathonTime}' should look like 3:30 (between 2:00 and 7:00).");
        }

        if (errors.Count > 0) return (false, string.Join("\n", errors));

        lock (_gate)
        {
            // A new race type without a new time drops the old time: it was for the other race.
            var dropTime = newRace is { } nr && nr != currentRace && time is null && Current.GoalTime is not null;
            if (newRace is { } r && r != currentRace && time is null)
                notes.Add($"Goal time set to the {RaceTypes.Name(r)} default ({RaceTypes.DefaultGoal(r):h\\:mm}); tell me yours to change it.");
            Current = Current with
            {
                GoalType = newRace is { } n ? RaceTypes.Key(n) : Current.GoalType,
                GoalTime = time ?? (dropTime ? null : Current.GoalTime),
                RaceDate = u.RaceDate?.Trim() ?? Current.RaceDate,
                RaceName = u.RaceName?.Trim() ?? Current.RaceName,
                Ftp = u.Ftp ?? Current.Ftp,
                SwimCssSecPer100m = css ?? Current.SwimCssSecPer100m,
                MarathonTime = marathon ?? Current.MarathonTime,
            };
            Save();
        }
        return (true, notes.Count > 0 ? "Saved. " + string.Join(" ", notes) : "Saved.");
    }

    /// <summary>"3:30", "3:30:00", "3h30" or "12" (hours) style input, within the sane range for the race.</summary>
    public static bool TryParseGoal(string text, RaceType race, out TimeSpan goal)
    {
        goal = default;
        var s = text.Trim().ToLowerInvariant().Replace("hours", "").Replace("hrs", "").Replace("h", ":").Replace("m", "").Trim().TrimEnd(':');
        if (s.Length == 0) return false;
        s += s.Count(c => c == ':') switch { 0 => ":00:00", 1 => ":00", _ => "" };
        var ok = TimeSpan.TryParseExact(s, [@"h\:mm\:ss", @"h\:m\:ss", @"hh\:mm\:ss"], CultureInfo.InvariantCulture, out goal);
        return ok && RaceTypes.InRange(goal, race);
    }

    /// <summary>Marathon-range parse, for the original callers.</summary>
    public static bool TryParseGoal(string text, out TimeSpan goal) => TryParseGoal(text, RaceType.Marathon, out goal);

    private void Save()
    {
        if (_path is null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, _path, overwrite: true);
    }
}

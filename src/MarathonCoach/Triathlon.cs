using System.Globalization;

namespace MarathonCoach;

/// <summary>The race you're training for. The goal time means that race's finish time.</summary>
public enum RaceType { Marathon, HalfIronman, Ironman }

public static class RaceTypes
{
    /// <summary>GOAL_TYPE / /goal type: "marathon", "70.3" (or "half"), "ironman" (or "im", "full"). Null = not recognised.</summary>
    public static RaceType? Parse(string? text) => text?.Trim().ToLowerInvariant().Replace(" ", "") switch
    {
        "marathon" => RaceType.Marathon,
        "70.3" or "half" or "halfironman" or "half-ironman" or "ironman70.3" => RaceType.HalfIronman,
        "ironman" or "im" or "full" or "fullironman" or "140.6" => RaceType.Ironman,
        _ => null,
    };

    public static string Name(RaceType r) => r switch
    {
        RaceType.HalfIronman => "Ironman 70.3",
        RaceType.Ironman => "Ironman",
        _ => "Marathon",
    };

    public static string Key(RaceType r) => r switch
    {
        RaceType.HalfIronman => "70.3",
        RaceType.Ironman => "ironman",
        _ => "marathon",
    };

    public static bool IsTriathlon(RaceType r) => r != RaceType.Marathon;

    /// <summary>Finish times that make sense for each race; anything outside is refused.</summary>
    public static (TimeSpan Fastest, TimeSpan Slowest) GoalRange(RaceType r) => r switch
    {
        RaceType.HalfIronman => (new(3, 30, 0), new(9, 0, 0)),
        RaceType.Ironman => (new(8, 0, 0), new(17, 0, 0)),
        _ => (new(2, 0, 0), new(7, 0, 0)),
    };

    public static TimeSpan DefaultGoal(RaceType r) => r switch
    {
        RaceType.HalfIronman => new(6, 0, 0),
        RaceType.Ironman => new(12, 30, 0),
        _ => new(3, 30, 0),
    };

    public static bool InRange(TimeSpan t, RaceType r) => GoalRange(r) is var (lo, hi) && t >= lo && t <= hi;
}

public sealed record PowerZone(int Zone, string Name, int LowWatts, int? HighWatts, string UsedFor)
{
    public override string ToString() =>
        $"Z{Zone} {Name}: {(HighWatts is { } hi ? $"{LowWatts}-{hi} W" : $"{LowWatts}+ W")} ({UsedFor})";
}

/// <summary>Bike power zones from FTP (the standard Coggan percentages).</summary>
public static class PowerZones
{
    public static IReadOnlyList<PowerZone> For(int ftp)
    {
        int W(double pct) => (int)Math.Round(ftp * pct);
        return
        [
            new(1, "Recovery", 0, W(0.55), "easy spins"),
            new(2, "Endurance", W(0.56), W(0.75), "long rides, most Ironman riding"),
            new(3, "Tempo", W(0.76), W(0.90), "70.3 race effort, steady blocks"),
            new(4, "Threshold", W(0.91), W(1.05), "FTP intervals"),
            new(5, "VO2 max", W(1.06), W(1.20), "3-8 min intervals"),
            new(6, "Anaerobic", W(1.21), null, "short hard efforts"),
        ];
    }

    /// <summary>Race power as a share of FTP (intensity factor): conservative enough to still run well off the bike.</summary>
    public static (double Low, double High) RaceIntensity(RaceType r) => r == RaceType.Ironman ? (0.68, 0.75) : (0.77, 0.85);
}

/// <summary>Swim pace zones from CSS (critical swim speed), stored as seconds per 100 m.</summary>
public static class SwimZones
{
    public const double MetersPerYard = 0.9144;

    /// <summary>Pace per 100 m from what you typed in your units ("1:55" per 100 yd in miles mode).</summary>
    public static bool TryParseCss(string text, DistanceUnit u, out double secPer100m)
    {
        secPer100m = 0;
        if (!TimeSpan.TryParseExact(text.Trim(), [@"m\:ss", @"mm\:ss"], CultureInfo.InvariantCulture, out var t)) return false;
        var perUnit = t.TotalSeconds;
        if (perUnit < 60 || perUnit > 210) return false; // 1:00-3:30 per 100
        secPer100m = u == DistanceUnit.Miles ? perUnit / MetersPerYard : perUnit;
        return true;
    }

    /// <summary>"1:55/100yd" in miles mode, "2:06/100m" otherwise.</summary>
    public static string Pace(double secPer100m, DistanceUnit u) =>
        u == DistanceUnit.Miles
            ? $"{PaceZones.Format(secPer100m * MetersPerYard)}/100yd"
            : $"{PaceZones.Format(secPer100m)}/100m";

    /// <summary>Common CSS-based zones, as seconds added to CSS pace per 100.</summary>
    public static IReadOnlyList<string> For(double css, DistanceUnit u) =>
    [
        $"Easy: {Pace(css + 12, u)} or slower (warm-up, recovery)",
        $"Endurance: {Pace(css + 5, u)}-{Pace(css + 10, u)} (aerobic sets, Ironman race pace)",
        $"Threshold (CSS): {Pace(css - 2, u)}-{Pace(css + 2, u)} (100-400 repeats, 70.3 race pace)",
        $"Fast: {Pace(css - 5, u)} or faster (short sprints)",
    ];
}

/// <summary>A rough split guide from a triathlon goal time, using typical age-group shares of the total.</summary>
public static class TriSplits
{
    private sealed record Leg(string Name, double Share, double Km);

    private static Leg[] Legs(RaceType r) => r == RaceType.Ironman
        ? [new("Swim", 0.10, 3.8), new("T1", 0.015, 0), new("Bike", 0.52, 180.2), new("T2", 0.01, 0), new("Run", 0.355, 42.195)]
        : [new("Swim", 0.10, 1.9), new("T1", 0.02, 0), new("Bike", 0.50, 90.1), new("T2", 0.01, 0), new("Run", 0.37, 21.1)];

    public static IReadOnlyList<string> Describe(TimeSpan goal, RaceType r, DistanceUnit u)
    {
        var lines = new List<string>();
        foreach (var leg in Legs(r))
        {
            var secs = goal.TotalSeconds * leg.Share;
            var time = TimeSpan.FromSeconds(Math.Round(secs / 60) * 60).ToString(@"h\:mm");
            lines.Add(leg.Name switch
            {
                "Swim" => $"Swim {time} ({SwimZones.Pace(secs / (leg.Km * 10), u)})",
                "Bike" => $"Bike {time} ({Speed(leg.Km, secs, u)})",
                "Run" => $"Run {time} ({UnitFormat.Pace(secs / leg.Km, u)})",
                _ => $"{leg.Name} {time}",
            });
        }
        return lines;
    }

    private static string Speed(double km, double secs, DistanceUnit u) =>
        u == DistanceUnit.Miles ? $"{km / UnitFormat.KmPerMile / (secs / 3600):0.0} mph" : $"{km / (secs / 3600):0.0} km/h";
}

/// <summary>
/// How many hours a triathlon week should hold, computed so the coach plans durations instead of guessing them:
/// a typical age-group range for the race and block, capped at about 10% more than the last 4 weeks averaged.
/// </summary>
public static class TriVolume
{
    /// <summary>Weekly hours for the race and block (common age-group ranges, not a law).</summary>
    public static (double Lo, double Hi) Range(RaceType race, Block block) => (race, block) switch
    {
        (RaceType.Ironman, Block.Base) => (8, 11),
        (RaceType.Ironman, Block.Build) => (11, 14),
        (RaceType.Ironman, Block.Peak) => (13, 16),
        (RaceType.Ironman, Block.Taper or Block.RaceDay) => (6, 9),
        (RaceType.Ironman, Block.After) => (3, 6),
        (RaceType.Ironman, _) => (7, 10),
        (_, Block.Base) => (6, 8),
        (_, Block.Build) => (8, 10),
        (_, Block.Peak) => (9, 12),
        (_, Block.Taper or Block.RaceDay) => (4, 6),
        (_, Block.After) => (2, 5),
        _ => (5, 8),
    };

    /// <summary>This week's hours: grow at most ~10% from what you did, never past the top of the range.</summary>
    public static (double Lo, double Hi, string Why) Target(RaceType race, Block block, double recentHours)
    {
        var (lo, hi) = Range(race, block);
        if (block is Block.Taper or Block.RaceDay or Block.After)
            return (lo, hi, block == Block.After ? "recovery after the race" : "taper: less volume, keep some intensity");
        if (recentHours <= 0)
            return (lo, lo, "no training in the last 4 weeks of data: start at the low end and see how it feels");
        if (recentHours >= hi)
            return (lo, hi, "you're already at the top of the range: hold, don't add more");
        var cap = Math.Min(hi, recentHours * 1.1);
        return (recentHours, cap, recentHours < lo
            ? "below the range for this block: build toward it, about 10% a week"
            : "within the range: grow about 10% a week at most");
    }

    /// <summary>The volume guide for the prompts; empty for a marathon goal.</summary>
    public static IEnumerable<string> Lines(
        Config c, DateOnly today, IReadOnlyList<RunSummary> runs, IReadOnlyList<CrossTraining> cross)
    {
        if (!RaceTypes.IsTriathlon(c.Race)) yield break;
        // Last 4 full 7-day buckets, the same weeks as the weekly table.
        bool Recent(DateOnly d) => today.DayNumber - d.DayNumber is >= 0 and < 28;
        double Hours(Sport s) => cross.Where(x => x.Sport == s && Recent(x.Date)).Sum(x => x.DurationSec) / 3600 / 4;
        var run = runs.Where(r => Recent(r.Date)).Sum(r => r.DurationMin) / 60.0 / 4;
        var (bike, swim) = (Hours(Sport.Bike), Hours(Sport.Swim));
        var total = run + bike + swim + Hours(Sport.Strength) + Hours(Sport.Other);

        var block = TrainingPhase.BlockOf(today, c.RaceDay);
        var (lo, hi) = Range(c.Race, block);
        var (a, b, why) = Target(c.Race, block, total);
        var mid = (a + b) / 2;
        string H(double hours) => MultisportFormat.Duration(hours * 3600);
        string Share(double from, double to) => $"{H(mid * from)}-{H(mid * to)}";

        yield return $"Weekly volume guide for my {RaceTypes.Name(c.Race)} (hours, computed; plan the week to fit it):";
        yield return $"- Last 4 weeks averaged {H(total)} a week: swim {H(swim)}, bike {H(bike)}, run {H(run)}";
        yield return $"- Typical range for this block ({(block == Block.NoDate ? "no race date" : block.ToString().ToLowerInvariant())}): {lo:0}-{hi:0} h a week";
        yield return $"- This week: {(a == b ? H(a) : $"{H(a)}-{H(b)}")} in total ({why}); every 3rd or 4th week, a recovery week at about 70%";
        yield return $"- Split it about swim 15-20% ({Share(0.15, 0.20)}), bike 45-55% ({Share(0.45, 0.55)}), " +
                     $"run 25-35% ({Share(0.25, 0.35)}), plus 1-2 short strength sessions; " +
                     "move toward this split over a few weeks rather than cutting a sport sharply";
    }
}

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

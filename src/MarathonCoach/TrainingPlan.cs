namespace MarathonCoach;

public sealed record PaceZone(string Name, double FastSecPerKm, double SlowSecPerKm, string UsedFor)
{
    public override string ToString() =>
        $"{Name}: {PaceZones.Format(FastSecPerKm)}-{PaceZones.Format(SlowSecPerKm)}/km ({UsedFor})";
}

/// <summary>
/// Training paces worked out from the goal time, so the coach never guesses them.
/// Each zone is a fixed fraction of marathon pace (a common rule of thumb; easy runs are well slower than MP,
/// threshold and intervals faster).
/// </summary>
public static class PaceZones
{
    public const double MarathonKm = 42.195;

    public static double MarathonPace(TimeSpan goal) => goal.TotalSeconds / MarathonKm;

    public static IReadOnlyList<PaceZone> For(TimeSpan goal)
    {
        var mp = MarathonPace(goal);
        return
        [
            new("Easy", mp * 1.15, mp * 1.25, "easy and recovery runs"),
            new("Long run", mp * 1.08, mp * 1.15, "most of the long run"),
            new("Marathon pace", mp * 0.99, mp * 1.01, "goal-pace blocks"),
            new("Threshold", mp * 0.93, mp * 0.95, "tempo runs and cruise intervals"),
            new("Interval", mp * 0.87, mp * 0.89, "800 m-1 km repeats"),
        ];
    }

    /// <summary>Seconds per km as m:ss (truncated, matching how Garmin shows pace).</summary>
    public static string Format(double secPerKm) => $"{(int)(secPerKm / 60)}:{(int)(secPerKm % 60):00}";
}

/// <summary>Where you are in the marathon build, from the race date.</summary>
public static class TrainingPhase
{
    public static string Describe(DateOnly today, DateOnly? raceDate)
    {
        if (raceDate is not { } race)
            return "No race date set yet: build aerobic base and consistency, one quality session and one long run a week.";

        var days = race.DayNumber - today.DayNumber;
        if (days < 0) return $"The race was {-days} days ago: recovery, then easy running.";
        if (days == 0) return "Race day.";

        var weeks = days / 7;
        var phase = weeks switch
        {
            < 3 => "Taper: cut volume 30-50%, keep a little marathon-pace work, arrive fresh",
            < 7 => "Peak: highest long runs (30-35 km with marathon-pace segments), volume at its top",
            < 13 => "Build: grow the long run, add marathon-pace and threshold work",
            _ => "Base: build volume gradually (about 10% a week at most), mostly easy, strides",
        };
        return $"{weeks} weeks and {days % 7} days to race day. {phase}.";
    }
}

/// <summary>When the morning DM goes out.</summary>
public static class MorningSchedule
{
    /// <summary>The first time at <paramref name="at"/> that is after <paramref name="now"/> (today or tomorrow).</summary>
    public static DateTime NextRun(DateTime now, TimeOnly at)
    {
        var todayAt = now.Date + at.ToTimeSpan();
        return todayAt > now ? todayAt : todayAt.AddDays(1);
    }
}

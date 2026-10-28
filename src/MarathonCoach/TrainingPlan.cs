namespace MarathonCoach;

public sealed record PaceZone(string Name, double FastSecPerKm, double SlowSecPerKm, string UsedFor)
{
    public override string ToString() => Describe(DistanceUnit.Km);

    public string Describe(DistanceUnit units) =>
        $"{Name}: {UnitFormat.PaceRange(FastSecPerKm, SlowSecPerKm, units)} ({UsedFor})";
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
            new("Interval", mp * 0.87, mp * 0.89, "800-1600 m repeats"),
        ];
    }

    /// <summary>Seconds per km as m:ss (truncated, matching how Garmin shows pace).</summary>
    public static string Format(double secPerKm) => $"{(int)(secPerKm / 60)}:{(int)(secPerKm % 60):00}";
}

/// <summary>The training block a day falls in, from weeks to race day.</summary>
public enum Block { NoDate, Base, Build, Peak, Taper, RaceDay, After }

/// <summary>Where you are in the build, from the race date and the kind of race.</summary>
public static class TrainingPhase
{
    public static Block BlockOf(DateOnly today, DateOnly? raceDate)
    {
        if (raceDate is not { } day) return Block.NoDate;
        var days = day.DayNumber - today.DayNumber;
        return days switch
        {
            < 0 => Block.After,
            0 => Block.RaceDay,
            < 21 => Block.Taper,
            < 49 => Block.Peak,
            < 91 => Block.Build,
            _ => Block.Base,
        };
    }

    public static string Describe(DateOnly today, DateOnly? raceDate, RaceType race = RaceType.Marathon)
    {
        var tri = RaceTypes.IsTriathlon(race);
        var block = BlockOf(today, raceDate);
        if (block == Block.NoDate)
            return tri
                ? "No race date set yet: build aerobic base in all three sports, swim technique and consistency."
                : "No race date set yet: build aerobic base and consistency, one quality session and one long run a week.";

        var days = raceDate!.Value.DayNumber - today.DayNumber;
        if (block == Block.After) return $"The race was {-days} days ago: recovery, then easy {(tri ? "training" : "running")}.";
        if (block == Block.RaceDay) return "Race day.";

        var longRide = race == RaceType.Ironman ? "5-6 h" : "3-3.5 h";
        var phase = (tri, block) switch
        {
            (false, Block.Taper) => "Taper: cut volume 30-50%, keep a little marathon-pace work, arrive fresh",
            (false, Block.Peak) => "Peak: highest long runs (30-35 km / 19-22 mi with marathon-pace segments), volume at its top",
            (false, Block.Build) => "Build: grow the long run, add marathon-pace and threshold work",
            (false, _) => "Base: build volume gradually (about 10% a week at most), mostly easy, strides",
            (true, Block.Taper) => "Taper: cut volume 40-60%, keep short race-pace efforts in each sport, arrive fresh",
            (true, Block.Peak) => $"Peak: longest rides ({longRide}) with race-power blocks, brick runs off the bike, open-water swims",
            (true, Block.Build) => "Build: grow the long ride and long run, a weekly brick, threshold work in each sport",
            (true, _) => "Base: aerobic volume across swim, bike and run (about 10% a week at most), swim technique, strength",
        };
        return $"{days / 7} weeks and {days % 7} days to race day. {phase}.";
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

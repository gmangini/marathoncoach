using System.Globalization;
using System.Text;

namespace MarathonCoach;

/// <summary>Writes runs and cross-training as plain lines for the prompts, in your units.</summary>
public static class MultisportFormat
{
    private const double MetersPerYard = 0.9144;

    /// <summary>"1h05", or "45 min" under an hour.</summary>
    public static string Duration(double secs)
    {
        var t = TimeSpan.FromSeconds(Math.Round(secs / 60) * 60);
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h{t.Minutes:00}" : $"{(int)t.TotalMinutes} min";
    }

    /// <summary>Swim distance in yards (miles mode) or metres.</summary>
    public static string SwimDistance(double km, DistanceUnit u) =>
        u == DistanceUnit.Miles
            ? $"{(km * 1000 / MetersPerYard).ToString("#,0", CultureInfo.InvariantCulture)} yd"
            : $"{(km * 1000).ToString("#,0", CultureInfo.InvariantCulture)} m";

    /// <summary>Swim pace per 100 yd (miles mode) or per 100 m.</summary>
    public static string SwimPace(double km, double secs, DistanceUnit u)
    {
        if (km <= 0) return "n/a";
        var hundreds = u == DistanceUnit.Miles ? km * 1000 / MetersPerYard / 100 : km * 10;
        return $"{PaceZones.Format(secs / hundreds)}/100{(u == DistanceUnit.Miles ? "yd" : "m")}";
    }

    /// <summary>
    /// One line per week (8 weeks, empty weeks included) with time and training load across all sports, then each sport.
    /// Load is Garmin's training load: the one number that adds up across running, riding and swimming.
    /// </summary>
    public static IEnumerable<string> WeeklyLines(
        IReadOnlyList<RunSummary> runs, IReadOnlyList<CrossTraining> cross, DateOnly today, DistanceUnit u)
    {
        int WeekOf(DateOnly d) => (today.DayNumber - d.DayNumber) / 7;
        for (var w = 0; w < TrainingData.Weeks; w++)
        {
            var r = runs.Where(x => WeekOf(x.Date) == w).ToList();
            var c = cross.Where(x => WeekOf(x.Date) == w).ToList();
            var secs = r.Sum(x => x.DurationMin * 60.0) + c.Sum(x => x.DurationSec);
            var load = r.Sum(x => x.Load ?? 0) + c.Sum(x => x.Load ?? 0);

            var parts = new List<string> { $"run {UnitFormat.Distance(r.Sum(x => x.Km), u)} ({r.Count})" };
            if (c.Where(x => x.Sport == Sport.Bike).ToList() is { Count: > 0 } bike)
                parts.Add($"bike {Duration(bike.Sum(x => x.DurationSec))}, {UnitFormat.Distance(bike.Sum(x => x.Km), u)} ({bike.Count})");
            if (c.Where(x => x.Sport == Sport.Swim).ToList() is { Count: > 0 } swim)
                parts.Add($"swim {SwimDistance(swim.Sum(x => x.Km), u)} ({swim.Count})");
            if (c.Count(x => x.Sport == Sport.Strength) is > 0 and var strength)
                parts.Add($"strength {strength}");
            if (c.Where(x => x.Sport == Sport.Other).ToList() is { Count: > 0 } other)
                parts.Add($"other {Duration(other.Sum(x => x.DurationSec))} ({other.Count})");

            yield return $"- {(w == 0 ? "last 7 days" : $"{w} wk ago")}: {Duration(secs)} total" +
                         $"{(load > 0 ? $", load {load:0}" : "")} | {string.Join(" | ", parts)}";
        }
    }

    /// <summary>One session with what matters for its sport: power and TSS for rides, pace per 100 for swims.</summary>
    public static string SessionLine(CrossTraining x, DistanceUnit u)
    {
        var sb = new StringBuilder($"- {x.Date:yyyy-MM-dd} {x.Sport} ({x.TypeKey}) \"{x.Name}\": {Duration(x.DurationSec)}");
        switch (x.Sport)
        {
            case Sport.Bike when x.Km > 0:
                sb.Append($", {UnitFormat.Distance(x.Km, u)}");
                break;
            case Sport.Swim when x.Km > 0:
                sb.Append($", {SwimDistance(x.Km, u)}, {SwimPace(x.Km, x.DurationSec, u)}");
                break;
            case Sport.Other when x.Km > 0.1:
                sb.Append($", {UnitFormat.Distance(x.Km, u)}");
                break;
        }
        if (x.AvgHr is { } hr) sb.Append($", avg HR {hr}");
        if (x.AvgPower is { } p) sb.Append($", {p:0} W avg{(x.NormPower is { } np ? $" / {np:0} W NP" : "")}");
        if (x.Tss is { } tss) sb.Append($", TSS {tss:0}");
        if (x.Load is { } load) sb.Append($", load {load:0}");
        if (x.AerobicTe is { } te) sb.Append($", aerobic TE {te:0.0}");
        return sb.ToString();
    }
}

using System.Text.RegularExpressions;

namespace MarathonCoach;

/// <summary>How distances and paces are shown to you and to Claude. Data is always stored in km.</summary>
public enum DistanceUnit { Km, Miles }

public static partial class UnitFormat
{
    public const double KmPerMile = 1.609344;

    /// <summary>UNITS: "mi", "mile(s)" -> miles; anything else (or unset) -> km.</summary>
    public static DistanceUnit Parse(string? value) =>
        value?.Trim().ToLowerInvariant() is "mi" or "mile" or "miles" ? DistanceUnit.Miles : DistanceUnit.Km;

    public static string Name(DistanceUnit u) => u == DistanceUnit.Miles ? "miles and min/mile" : "km and min/km";

    public static string Short(DistanceUnit u) => u == DistanceUnit.Miles ? "mi" : "km";

    public static string Distance(double km, DistanceUnit u) =>
        u == DistanceUnit.Miles ? $"{km / KmPerMile:0.#} mi" : $"{km:0.#} km";

    public static string Pace(double secPerKm, DistanceUnit u) =>
        u == DistanceUnit.Miles ? $"{PaceZones.Format(secPerKm * KmPerMile)}/mi" : $"{PaceZones.Format(secPerKm)}/km";

    public static string PaceRange(double fastSecPerKm, double slowSecPerKm, DistanceUnit u) =>
        u == DistanceUnit.Miles
            ? $"{PaceZones.Format(fastSecPerKm * KmPerMile)}-{PaceZones.Format(slowSecPerKm * KmPerMile)}/mi"
            : $"{PaceZones.Format(fastSecPerKm)}-{PaceZones.Format(slowSecPerKm)}/km";

    /// <summary>Re-expresses a stored "m:ss/km" pace in <paramref name="u"/>; anything unreadable is returned as is.</summary>
    public static string ConvertPace(string pacePerKm, DistanceUnit u)
    {
        if (u == DistanceUnit.Km) return pacePerKm;
        var m = PerKm().Match(pacePerKm);
        return m.Success ? Pace(int.Parse(m.Groups[1].Value) * 60 + int.Parse(m.Groups[2].Value), u) : pacePerKm;
    }

    [GeneratedRegex(@"^(\d+):(\d{2})/km$")]
    private static partial Regex PerKm();
}

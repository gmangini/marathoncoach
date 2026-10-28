using Garmin.Connect;
using Garmin.Connect.Auth;
using Garmin.Connect.Models;

namespace MarathonCoach;

/// <summary>
/// Reads runs and recovery data from Garmin Connect via the unofficial
/// Unofficial.Garmin.Connect package (personal use only).
/// </summary>
public sealed class GarminSource : IGarminSource
{
    private readonly GarminConnectClient _client;

    public GarminSource()
    {
        var login = Environment.GetEnvironmentVariable("GARMIN_LOGIN")
            ?? throw new InvalidOperationException("Set the GARMIN_LOGIN environment variable");
        var password = Environment.GetEnvironmentVariable("GARMIN_PASSWORD")
            ?? throw new InvalidOperationException("Set the GARMIN_PASSWORD environment variable");

        // Tokens are cached so you don't do a full login (and risk Garmin rate limiting) every run.
        // On the server this points into the data volume; the file is copied there from the PC (no 2FA prompt there).
        var tokenPath = Environment.GetEnvironmentVariable("GARMIN_TOKEN_PATH") is { Length: > 0 } p ? p : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".marathoncoach-garmin-token.json");

        var context = new GarminConnectContext(
            new HttpClient(),
            new BasicAuthParameters(login, password),
            new ConsoleMfaCode(),
            new FileTokenCache(tokenPath));

        _client = new GarminConnectClient(context);
    }

    public async Task<IReadOnlyList<RunSummary>> GetRunsAsync(DateOnly start, DateOnly end, CancellationToken ct)
    {
        var activities = await _client.GetActivitiesByDate(
            start.ToDateTime(TimeOnly.MinValue), end.ToDateTime(TimeOnly.MaxValue), "running", ct);

        return activities
            .Select(a =>
            {
                var km = a.Distance / 1000.0;
                return new RunSummary(
                    DateOnly.FromDateTime(a.StartTimeLocal),
                    a.ActivityName ?? "",
                    Math.Round(km, 1),
                    (int)Math.Round(a.Duration / 60.0),
                    Pace.PerKm(km, a.Duration),
                    a.AverageHr > 0 ? (int)Math.Round(a.AverageHr) : null,
                    a.AerobicTrainingEffect > 0 ? a.AerobicTrainingEffect : null,
                    a.ActivityId,
                    a.StartTimeLocal);
            })
            .OrderBy(r => r.Date)
            .ToList();
    }

    public async Task<RunDetail> GetRunDetailAsync(RunSummary run, CancellationToken ct)
    {
        if (run.ActivityId is not { } id) return new RunDetail(run, [], []);

        // Either call can fail (older watch, manual activity); the review then works with what it has.
        var splits = await TryAsync(() => _client.GetActivitySplits(id, ct));
        var zones = await TryAsync(() => _client.GetActivityHrInTimezones(id, ct));

        var laps = (splits?.LapDtOs ?? [])
            .Where(l => l.Duration > 0)
            .Select(l => new Lap(
                (int)l.LapIndex,
                l.Distance / 1000.0,
                l.Duration,
                l.AverageHr > 0 ? (int)Math.Round(l.AverageHr) : null,
                string.IsNullOrWhiteSpace(l.IntensityType) ? null : l.IntensityType))
            .OrderBy(l => l.Index)
            .ToList();
        var hrZones = (zones ?? [])
            .Select(z => new HrZone((int)z.ZoneNumber, (int)z.ZoneLowBoundary, z.SecsInZone))
            .OrderBy(z => z.Zone)
            .ToList();
        return new RunDetail(run, laps, hrZones);
    }

    public async Task<IReadOnlyList<RecoveryDay>> GetRecoveryAsync(int days, CancellationToken ct)
    {
        var today = DateTime.Today;
        var from = today.AddDays(-(days - 1));

        // Range endpoints: one call each. Any of these can fail depending on watch model.
        var hrv = await TryAsync(() => _client.GetReportHrvStatus(from, today, ct));
        var rhr = await TryAsync(() => _client.GetRestingHeartRate(from, today, ct));

        var hrvByDay = hrv?.HrvSummaries?
            .GroupBy(h => h.CalendarDate)
            .ToDictionary(g => g.Key, g => g.First())
            ?? new Dictionary<DateOnly, GarminHrvSummary>();

        var rhrByDay = rhr?.AllMetrics?.MetricsMap?.Values
            .SelectMany(v => v)
            .GroupBy(e => e.CalendarDate)
            .ToDictionary(g => g.Key, g => g.First().Value)
            ?? new Dictionary<DateOnly, double>();

        var result = new List<RecoveryDay>();
        for (var i = 0; i < days; i++)
        {
            var day = today.AddDays(-i);
            var key = DateOnly.FromDateTime(day);

            // Sleep is per-day only.
            var sleep = (await TryAsync(() => _client.GetWellnessSleepData(day, ct)))?.DailySleepDto;
            hrvByDay.TryGetValue(key, out var h);

            result.Add(new RecoveryDay(
                key,
                sleep is { SleepTimeSeconds: > 0 } ? Math.Round(sleep.SleepTimeSeconds / 3600.0, 1) : null,
                sleep?.SleepScores?.Overall is { Value: > 0 } o ? (int)o.Value : null,
                h is { LastNightAvg: > 0 } ? h.LastNightAvg : null,
                h?.Status,
                rhrByDay.TryGetValue(key, out var r) && r > 0 ? (int)Math.Round(r) : null));
        }
        return result;
    }

    private static async Task<T?> TryAsync<T>(Func<Task<T>> call) where T : class
    {
        try { return await call(); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[garmin] skipped a metric: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }
}

/// <summary>Prompts for the Garmin MFA code on the console if your account uses 2FA.</summary>
public sealed class ConsoleMfaCode : IMfaCodeProvider
{
    public Task<string> GetMfaCodeAsync()
    {
        Console.Write("Garmin MFA code: ");
        // On a server there is no one to type the code, so say what to do instead of failing on an empty code.
        return Console.ReadLine() is { Length: > 0 } code
            ? Task.FromResult(code.Trim())
            : throw new InvalidOperationException(
                "Garmin asked for a 2FA code but there is no console to type it. Log in once on your PC " +
                "(dotnet run -- --prompt), then copy the token file to the server (see README, 'Cloud server').");
    }
}

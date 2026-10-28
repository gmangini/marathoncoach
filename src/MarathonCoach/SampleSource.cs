namespace MarathonCoach;

/// <summary>Fake but plausible data so you can test the prompt + LLM path without Garmin.</summary>
public sealed class SampleSource : IGarminSource
{
    public Task<IReadOnlyList<RunSummary>> GetRunsAsync(DateOnly start, DateOnly end, CancellationToken ct)
    {
        var runs = new List<RunSummary>();
        var rng = new Random(42);
        for (var d = start; d < end; d = d.AddDays(1))
        {
            switch (d.DayOfWeek)
            {
                case DayOfWeek.Tuesday:
                    runs.Add(Make(d, "Tempo", 10 + rng.Next(0, 3), 4.75)); break;
                case DayOfWeek.Thursday:
                    runs.Add(Make(d, "Easy", 8, 6.0)); break;
                case DayOfWeek.Saturday:
                    runs.Add(Make(d, "Easy", 6, 6.1)); break;
                case DayOfWeek.Sunday:
                    runs.Add(Make(d, "Long run", 18 + rng.Next(0, 5), 5.6)); break;
            }
        }
        return Task.FromResult<IReadOnlyList<RunSummary>>(runs);

        static RunSummary Make(DateOnly d, string name, double km, double minPerKm)
        {
            var sec = km * minPerKm * 60;
            return new RunSummary(d, name, km, (int)(sec / 60), Pace.PerKm(km, sec), 150, 3.2);
        }
    }

    /// <summary>A TrainerRoad ride on Mondays, strength on Wednesdays and a swim on Fridays.</summary>
    public Task<IReadOnlyList<CrossTraining>> GetCrossTrainingAsync(DateOnly start, DateOnly end, CancellationToken ct)
    {
        var list = new List<CrossTraining>();
        for (var d = start; d < end; d = d.AddDays(1))
        {
            switch (d.DayOfWeek)
            {
                case DayOfWeek.Monday:
                    list.Add(new(d, Sport.Bike, "virtual_ride", "TrainerRoad - Galena", 30, 3600, 132, 2.9, 180, 192, 58, 70)); break;
                case DayOfWeek.Wednesday:
                    list.Add(new(d, Sport.Strength, "strength_training", "Strength", 0, 2400, 110, 1.5, Load: 25)); break;
                case DayOfWeek.Friday:
                    list.Add(new(d, Sport.Swim, "lap_swimming", "Pool Swim", 1.6, 2100, 128, 2.4, Load: 40)); break;
            }
        }
        return Task.FromResult<IReadOnlyList<CrossTraining>>(list);
    }

    /// <summary>1 km laps at even pace with heart rate creeping up ~1 bpm per km: a little aerobic drift.</summary>
    public Task<RunDetail> GetRunDetailAsync(RunSummary run, CancellationToken ct)
    {
        var laps = Enumerable.Range(1, (int)run.Km)
            .Select(i => new Lap(i, 1.0, run.DurationMin * 60.0 / run.Km, 144 + i, "ACTIVE"))
            .ToList();
        var total = run.DurationMin * 60.0;
        HrZone[] zones = [new(1, 100, total * 0.05), new(2, 125, total * 0.55), new(3, 145, total * 0.35), new(4, 160, total * 0.05), new(5, 175, 0)];
        return Task.FromResult(new RunDetail(run, laps, zones));
    }

    public Task<IReadOnlyList<RecoveryDay>> GetRecoveryAsync(int days, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var list = Enumerable.Range(0, days)
            .Select(i => new RecoveryDay(today.AddDays(-i), 7.2, 78, 62, "BALANCED", 48))
            .ToList();
        return Task.FromResult<IReadOnlyList<RecoveryDay>>(list);
    }
}

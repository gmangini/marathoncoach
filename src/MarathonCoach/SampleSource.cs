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

    public Task<IReadOnlyList<RecoveryDay>> GetRecoveryAsync(int days, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var list = Enumerable.Range(0, days)
            .Select(i => new RecoveryDay(today.AddDays(-i), 7.2, 78, 62, "BALANCED", 48))
            .ToList();
        return Task.FromResult<IReadOnlyList<RecoveryDay>>(list);
    }
}

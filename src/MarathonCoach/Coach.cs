namespace MarathonCoach;

/// <summary>Pulls training data and asks the LLM for coaching. No Discord here, so it is easy to test.</summary>
public sealed class Coach(IGarminSource source, ICoachLlm llm, Config config)
{
    public async Task<string> TodayAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var runs = await source.GetRunsAsync(today.AddDays(-56), today, ct);
        var recovery = await source.GetRecoveryAsync(7, ct);
        var data = PromptBuilder.Build(runs, recovery, today, config);
        return await llm.AskAsync(PromptBuilder.SystemPrompt, [new ChatTurn(true, data)], ct);
    }
}

namespace MarathonCoach;

/// <summary>Pulls training data and asks the LLM for coaching. No Discord here, so it is easy to test.</summary>
public sealed class Coach(TrainingDataCache data, ICoachLlm llm, Config config)
{
    public async Task<string> TodayAsync(CancellationToken ct)
    {
        var d = await data.GetAsync(forceRefresh: false, ct);
        var prompt = PromptBuilder.Build(d.Runs, d.Recovery, d.Today, config);
        return await llm.AskAsync(PromptBuilder.SystemPrompt, [new ChatTurn(true, prompt)], ct);
    }

    public async Task<string> SyncAsync(CancellationToken ct) =>
        "Synced: " + (await data.GetAsync(forceRefresh: true, ct)).Summary;
}

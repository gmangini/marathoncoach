namespace MarathonCoach;

/// <summary>Pulls training data and asks the LLM for coaching. No Discord here, so it is easy to test.</summary>
public sealed class Coach(TrainingDataCache data, ICoachLlm llm, Config config, ChatHistory history)
{
    /// <summary>How many past turns Claude sees (10 exchanges): enough to follow up, cheap to send.</summary>
    public const int ContextTurns = 20;

    public async Task<string> TodayAsync(CancellationToken ct)
    {
        var d = await data.GetAsync(forceRefresh: false, ct);
        var answer = await llm.AskAsync(
            PromptBuilder.SystemPrompt, [new ChatTurn(true, PromptBuilder.Build(d.Runs, d.Recovery, d.Today, config))], ct);
        // Recorded so a follow-up DM ("make it shorter") knows what today's workout was.
        history.AddExchange("/today: what should I run today?", answer);
        return answer;
    }

    /// <summary>A free-form message: past conversation, then today's data and the message.</summary>
    public async Task<string> ChatAsync(string message, CancellationToken ct)
    {
        var d = await data.GetAsync(forceRefresh: false, ct);
        var turns = history.Recent(ContextTurns).Append(new ChatTurn(true, PromptBuilder.Chat(d, config, message))).ToList();
        var answer = await llm.AskAsync(PromptBuilder.SystemPrompt, turns, ct);
        history.AddExchange(message, answer);
        return answer;
    }

    public async Task<string> SyncAsync(CancellationToken ct) =>
        "Synced: " + (await data.GetAsync(forceRefresh: true, ct)).Summary;

    public string Reset()
    {
        history.Clear();
        return "Chat history cleared.";
    }
}

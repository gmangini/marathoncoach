using Anthropic;
using Anthropic.Models.Beta.Messages;

namespace MarathonCoach;

/// <summary>What the coach needs from an LLM. Swap for a fake in tests.</summary>
public interface ICoachLlm
{
    Task<string> AskAsync(string system, IReadOnlyList<ChatTurn> turns, CancellationToken ct);
}

public sealed record ChatTurn(bool FromUser, string Text);

/// <summary>Returns the prompt instead of calling an LLM, so you can check it for free.</summary>
public sealed class PrintPromptLlm : ICoachLlm
{
    public Task<string> AskAsync(string system, IReadOnlyList<ChatTurn> turns, CancellationToken ct) =>
        Task.FromResult($"--- system ---\n{system}\n" +
                        string.Concat(turns.Select(t => $"--- {(t.FromUser ? "user" : "assistant")} ---\n{t.Text}\n")));
}

/// <summary>Claude through the official Anthropic SDK. Reads ANTHROPIC_API_KEY from the environment.</summary>
public sealed class ClaudeClient(string model) : ICoachLlm
{
    private readonly AnthropicClient _client = new();

    public async Task<string> AskAsync(string system, IReadOnlyList<ChatTurn> turns, CancellationToken ct)
    {
        var response = await _client.Beta.Messages.Create(new MessageCreateParams
        {
            Model = model,
            MaxTokens = 4000,
            OutputConfig = new BetaOutputConfig { Effort = Effort.Low },
            // If a safety classifier declines, the API re-serves the request on a fallback model.
            Betas = ["server-side-fallback-2026-07-01"],
            Fallbacks = new Default(),
            // The coach persona is stable, so it is cached; the data and questions come after it.
            System = new List<BetaTextBlockParam>
            {
                new() { Text = system, CacheControl = new BetaCacheControlEphemeral() },
            },
            Messages = turns
                .Select(t => new BetaMessageParam { Role = t.FromUser ? Role.User : Role.Assistant, Content = t.Text })
                .ToList(),
        }, cancellationToken: ct);

        if (response.StopReason == "refusal")
            return "Claude declined that one. Try rephrasing.";

        return string.Join("\n", response.Content
            .Select(b => b.TryPickText(out var t) ? t.Text : null)
            .Where(t => !string.IsNullOrWhiteSpace(t)));
    }
}

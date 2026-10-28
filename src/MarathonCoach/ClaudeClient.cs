using System.Text.Json;
using Anthropic;
using Anthropic.Models.Beta.Messages;

namespace MarathonCoach;

/// <summary>What the coach needs from an LLM. Swap for a fake in tests.</summary>
public interface ICoachLlm
{
    Task<string> AskAsync(string system, IReadOnlyList<ChatTurn> turns, CancellationToken ct);

    /// <summary>
    /// Like <see cref="AskAsync"/>, but the model may also call <paramref name="tool"/>. The call is returned, not
    /// answered: the app applies it and writes the confirmation itself. Default: no tool support.
    /// </summary>
    async Task<LlmReply> AskWithToolAsync(string system, IReadOnlyList<ChatTurn> turns, CoachTool tool, CancellationToken ct) =>
        new(await AskAsync(system, turns, ct), []);
}

public sealed record ChatTurn(bool FromUser, string Text);

/// <summary>A tool the model may call. <see cref="JsonSchema"/> is the input schema as JSON.</summary>
public sealed record CoachTool(string Name, string Description, string JsonSchema);

public sealed record ToolCall(string Name, IReadOnlyDictionary<string, string?> Args);

public sealed record LlmReply(string Text, IReadOnlyList<ToolCall> ToolCalls);

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

    public async Task<string> AskAsync(string system, IReadOnlyList<ChatTurn> turns, CancellationToken ct) =>
        (await SendAsync(system, turns, null, ct)).Text;

    public Task<LlmReply> AskWithToolAsync(string system, IReadOnlyList<ChatTurn> turns, CoachTool tool, CancellationToken ct) =>
        SendAsync(system, turns, tool, ct);

    private async Task<LlmReply> SendAsync(string system, IReadOnlyList<ChatTurn> turns, CoachTool? tool, CancellationToken ct)
    {
        var request = new MessageCreateParams
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
        };
        if (tool is not null)
        {
            request = request with
            {
                Tools =
                [
                    new BetaTool
                    {
                        Name = tool.Name,
                        Description = tool.Description,
                        // strict: the arguments always match the schema.
                        Strict = true,
                        InputSchema = InputSchema.FromRawUnchecked(
                            JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(tool.JsonSchema)!),
                    },
                ],
            };
        }

        var response = await _client.Beta.Messages.Create(request, cancellationToken: ct);

        if (response.StopReason == "refusal")
            return new("Claude declined that one. Try rephrasing.", []);

        var text = string.Join("\n", response.Content
            .Select(b => b.TryPickText(out var t) ? t.Text : null)
            .Where(t => !string.IsNullOrWhiteSpace(t)));
        var calls = response.Content
            .Select(b => b.TryPickToolUse(out var u) ? u : null)
            .OfType<BetaToolUseBlock>()
            .Select(u => new ToolCall(u.Name, u.Input.ToDictionary(
                kv => kv.Key,
                // Numbers (like FTP) come through as their text; null and anything else as null.
                kv => kv.Value.ValueKind switch
                {
                    JsonValueKind.String => kv.Value.GetString(),
                    JsonValueKind.Number => kv.Value.GetRawText(),
                    _ => null,
                })))
            .ToList();
        return new(text, calls);
    }
}

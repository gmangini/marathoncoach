using MarathonCoach;

namespace MarathonCoach.Tests;

internal sealed class ScriptedLlm : ICoachLlm
{
    public List<IReadOnlyList<ChatTurn>> Calls { get; } = [];
    private int _n;

    public Task<string> AskAsync(string system, IReadOnlyList<ChatTurn> turns, CancellationToken ct)
    {
        Calls.Add(turns);
        return Task.FromResult($"answer {++_n}");
    }
}

public sealed class ChatHistoryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mc-tests-" + Guid.NewGuid().ToString("N"));
    private string PathInDir => Path.Combine(_dir, "data", "chat-history.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void History_survives_a_restart()
    {
        new ChatHistory(PathInDir).AddExchange("legs are trashed", "take an easy day");

        var reloaded = new ChatHistory(PathInDir).Recent(20);

        Assert.Equal([new ChatTurn(true, "legs are trashed"), new ChatTurn(false, "take an easy day")], reloaded);
    }

    [Fact]
    public void Recent_is_capped_and_always_starts_with_a_user_turn()
    {
        var h = new ChatHistory(null);
        for (var i = 0; i < 5; i++) h.AddExchange($"q{i}", $"a{i}");

        var recent = h.Recent(3); // last 3 turns would start with an answer, so it drops to 2

        Assert.Equal(["q4", "a4"], recent.Select(t => t.Text));
        Assert.True(recent[0].FromUser);
    }

    [Fact]
    public void Clear_empties_the_saved_file_too()
    {
        var h = new ChatHistory(PathInDir);
        h.AddExchange("q", "a");
        h.Clear();

        Assert.Equal(0, new ChatHistory(PathInDir).Count);
    }

    [Fact]
    public void Old_exchanges_are_trimmed()
    {
        var h = new ChatHistory(null);
        for (var i = 0; i < ChatHistory.KeepExchanges + 5; i++) h.AddExchange($"q{i}", $"a{i}");

        Assert.Equal(ChatHistory.KeepExchanges * 2, h.Count);
    }

    [Fact]
    public void In_memory_history_writes_no_file()
    {
        new ChatHistory(null).AddExchange("q", "a");
        Assert.False(Directory.Exists(_dir));
    }
}

public class CoachChatTests
{
    private static readonly Config Config =
        new(null, 1, null, Config.DefaultModel, PromptBuilder.DefaultGoal, "not set yet", "none");

    private static Coach NewCoach(ScriptedLlm llm, ChatHistory history) =>
        new(new TrainingDataCache(new SampleSource(), TimeSpan.FromHours(3)), llm, Config, history);

    [Fact]
    public async Task Chat_sends_past_turns_then_data_and_message()
    {
        var llm = new ScriptedLlm();
        var history = new ChatHistory(null);
        history.AddExchange("earlier question", "earlier answer");

        var answer = await NewCoach(llm, history).ChatAsync("can I swap tomorrow?", default);

        Assert.Equal("answer 1", answer);
        var turns = llm.Calls.Single();
        Assert.Equal(3, turns.Count);
        Assert.Equal("earlier question", turns[0].Text);
        Assert.Equal("earlier answer", turns[1].Text);
        Assert.True(turns[2].FromUser);
        Assert.Contains("Weekly mileage", turns[2].Text);
        Assert.EndsWith("My message: can I swap tomorrow?", turns[2].Text);
    }

    [Fact]
    public async Task Only_the_message_is_saved_not_the_data()
    {
        var history = new ChatHistory(null);
        await NewCoach(new ScriptedLlm(), history).ChatAsync("hello", default);

        Assert.Equal([new ChatTurn(true, "hello"), new ChatTurn(false, "answer 1")], history.Recent(20));
    }

    [Fact]
    public async Task Today_goes_into_history_so_a_follow_up_can_refer_to_it()
    {
        var llm = new ScriptedLlm();
        var coach = NewCoach(llm, new ChatHistory(null));

        await coach.TodayAsync(default);
        await coach.ChatAsync("make it shorter", default);

        var followUp = llm.Calls[1];
        Assert.StartsWith("/today", followUp[0].Text);
        Assert.Equal("answer 1", followUp[1].Text);
    }

    [Fact]
    public void Reset_clears_history()
    {
        var history = new ChatHistory(null);
        history.AddExchange("q", "a");

        NewCoach(new ScriptedLlm(), history).Reset();

        Assert.Equal(0, history.Count);
    }
}

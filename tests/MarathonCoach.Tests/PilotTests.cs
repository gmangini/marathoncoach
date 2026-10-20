using MarathonCoach;

namespace MarathonCoach.Tests;

public class OwnerGuardTests
{
    [Fact] public void Owner_is_allowed() => Assert.True(OwnerGuard.IsOwner(42, 42));
    [Fact] public void Someone_else_is_refused() => Assert.False(OwnerGuard.IsOwner(7, 42));
    [Fact] public void Unset_owner_refuses_everyone() => Assert.False(OwnerGuard.IsOwner(0, 0));
}

public class DotEnvTests
{
    [Fact]
    public void Parses_values_comments_and_quotes()
    {
        var pairs = DotEnv.Parse([
            "# comment", "", "A=1", " B = two words ", "C=\"quoted=value\"", "D='single'", "not a pair",
        ]).ToDictionary(p => p.Key, p => p.Value);

        Assert.Equal(4, pairs.Count);
        Assert.Equal("1", pairs["A"]);
        Assert.Equal("two words", pairs["B"]);
        Assert.Equal("quoted=value", pairs["C"]);
        Assert.Equal("single", pairs["D"]);
    }
}

public class DiscordTextTests
{
    [Fact]
    public void Short_text_is_one_message() => Assert.Single(DiscordText.Split("hello"));

    [Fact]
    public void Long_text_splits_on_lines_under_the_limit()
    {
        var text = string.Join("\n", Enumerable.Repeat(new string('x', 99), 50)); // ~5000 chars
        var chunks = DiscordText.Split(text).ToList();

        Assert.True(chunks.Count >= 3);
        Assert.All(chunks, c => Assert.InRange(c.Length, 1, DiscordText.Limit));
        Assert.Equal(text.Replace("\n", ""), string.Concat(chunks).Replace("\n", ""));
    }

    [Fact]
    public void A_line_longer_than_the_limit_is_hard_split()
    {
        var chunks = DiscordText.Split(new string('y', 4500)).ToList();
        Assert.Equal([2000, 2000, 500], chunks.Select(c => c.Length));
    }
}

public class CoachTests
{
    private sealed class FakeLlm : ICoachLlm
    {
        public string? System;
        public IReadOnlyList<ChatTurn>? Turns;

        public Task<string> AskAsync(string system, IReadOnlyList<ChatTurn> turns, CancellationToken ct)
        {
            (System, Turns) = (system, turns);
            return Task.FromResult("Easy 8 km");
        }
    }

    [Fact]
    public async Task Today_sends_goal_runs_and_recovery_to_the_llm()
    {
        var llm = new FakeLlm();
        var config = new Config("token", 1, null, Config.DefaultModel, PromptBuilder.DefaultGoal, "2027-04-18", "calf tight");

        var data = new TrainingDataCache(new SampleSource(), TimeSpan.FromHours(3));
        var answer = await new Coach(data, llm, config).TodayAsync(CancellationToken.None);

        Assert.Equal("Easy 8 km", answer);
        Assert.Equal(PromptBuilder.SystemPrompt, llm.System);
        var prompt = Assert.Single(llm.Turns!).Text;
        Assert.Contains("3:30:00", prompt);
        Assert.Contains("Race date: 2027-04-18", prompt);
        Assert.Contains("calf tight", prompt);
        Assert.Contains("Long run", prompt);       // a sample run made it in
        Assert.Contains("HRV 62 (BALANCED)", prompt); // recovery made it in
        Assert.Contains("swap if", prompt);
    }
}

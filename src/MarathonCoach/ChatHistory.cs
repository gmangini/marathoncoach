using System.Text.Json;

namespace MarathonCoach;

/// <summary>
/// The conversation with the coach, saved as JSON after every exchange so it survives a restart.
/// Holds whole exchanges (your message, then the coach's answer), so the turns always alternate.
/// </summary>
public sealed class ChatHistory
{
    public const int KeepExchanges = 200;

    private readonly string? _path;
    private readonly object _gate = new();
    private List<ChatTurn> _turns;

    /// <param name="path">JSON file to load and save; null keeps the history in memory only.</param>
    public ChatHistory(string? path)
    {
        _path = path;
        _turns = path is not null && File.Exists(path)
            ? JsonSerializer.Deserialize<List<ChatTurn>>(File.ReadAllText(path)) ?? []
            : [];
    }

    public int Count { get { lock (_gate) return _turns.Count; } }

    /// <summary>The last <paramref name="maxTurns"/> turns, always starting with one of your messages.</summary>
    public IReadOnlyList<ChatTurn> Recent(int maxTurns)
    {
        lock (_gate)
        {
            var recent = _turns.TakeLast(maxTurns).SkipWhile(t => !t.FromUser).ToList();
            return recent;
        }
    }

    public void AddExchange(string userText, string coachText)
    {
        lock (_gate)
        {
            _turns.Add(new ChatTurn(true, userText));
            _turns.Add(new ChatTurn(false, coachText));
            if (_turns.Count > KeepExchanges * 2) _turns = _turns.TakeLast(KeepExchanges * 2).ToList();
            Save();
        }
    }

    public void Clear()
    {
        lock (_gate) { _turns.Clear(); Save(); }
    }

    private void Save()
    {
        if (_path is null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_turns, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, _path, overwrite: true);
    }
}

namespace UltraSim.Game.Chat;

internal enum GameChatCommandKind : byte
{
    Help = 1,
    Where = 2,
}

internal readonly record struct GameChatCommandDefinition(
    string Name,
    string Summary,
    GameChatCommandKind Kind);

/// <summary>Instance-owned finite command catalog for the first player text surface.</summary>
internal sealed class GameChatCommandRegistry
{
    private readonly GameChatCommandDefinition[] _commands;

    internal GameChatCommandRegistry(int capacity)
    {
        if (capacity < 2)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _commands = new GameChatCommandDefinition[capacity];
        Add(new GameChatCommandDefinition("help", "List available commands.", GameChatCommandKind.Help));
        Add(new GameChatCommandDefinition("where", "Show your authoritative world position.", GameChatCommandKind.Where));
        HelpReply = BuildHelpReply();
    }

    internal const char PlayerPrefix = '/';
    internal int Count { get; private set; }
    internal string HelpReply { get; }

    internal bool TryResolve(ReadOnlySpan<char> name, out GameChatCommandDefinition definition)
    {
        for (int index = 0; index < Count; index++)
        {
            if (name.Equals(_commands[index].Name.AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                definition = _commands[index];
                return true;
            }
        }
        definition = default;
        return false;
    }

    private void Add(in GameChatCommandDefinition definition)
    {
        if (Count == _commands.Length)
            throw new InvalidOperationException("The command registry is full.");
        _commands[Count++] = definition;
    }

    private string BuildHelpReply()
    {
        var lines = new string[Count];
        for (int index = 0; index < Count; index++)
            lines[index] = $"/{_commands[index].Name} — {_commands[index].Summary}";
        return string.Join('\n', lines);
    }
}

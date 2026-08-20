using System;
using System.Collections.Generic;
using System.Text;

namespace UltraSim.Server.Chat
{
    public enum ChatCommandDispatchStatus
    {
        NotCommand = 0,
        Executed = 1,
        UnknownCommand = 2,
        InvalidSyntax = 3,
        PermissionDenied = 4,
        HandlerError = 5,
    }

    public readonly struct ChatCommandDispatchResult
    {
        public ChatCommandDispatchResult(
            ChatCommandDispatchStatus status,
            string? commandName = null,
            string? error = null)
        {
            Status = status;
            CommandName = commandName ?? string.Empty;
            Error = error ?? string.Empty;
        }

        public ChatCommandDispatchStatus Status { get; }
        public string CommandName { get; }
        public string Error { get; }
        public bool IsHandled => Status != ChatCommandDispatchStatus.NotCommand;
    }

    public readonly struct ChatCommandInvocation
    {
        public ChatCommandInvocation(
            char symbol,
            string commandName,
            string rawInput,
            IReadOnlyList<string> args)
        {
            Symbol = symbol;
            CommandName = commandName;
            RawInput = rawInput;
            Args = args;
        }

        public char Symbol { get; }
        public string CommandName { get; }
        public string RawInput { get; }
        public IReadOnlyList<string> Args { get; }
    }

    public readonly struct ChatCommandContext
    {
        public ChatCommandContext(
            long sessionId,
            int connectionId,
            ulong senderEntityId,
            int accessLevel,
            string senderName,
            Action<string>? reply = null)
        {
            SessionId = sessionId;
            ConnectionId = connectionId;
            SenderEntityId = senderEntityId;
            AccessLevel = accessLevel;
            SenderName = senderName ?? string.Empty;
            Reply = reply;
        }

        public long SessionId { get; }
        public int ConnectionId { get; }
        public ulong SenderEntityId { get; }
        public int AccessLevel { get; }
        public string SenderName { get; }
        public Action<string>? Reply { get; }
    }

    public delegate void ChatCommandHandler(
        in ChatCommandContext context,
        in ChatCommandInvocation invocation);

    public sealed class ServerChatCommandRegistry
    {
        private readonly struct CommandKey : IEquatable<CommandKey>
        {
            public CommandKey(char symbol, string name)
            {
                Symbol = symbol;
                Name = name;
            }

            public char Symbol { get; }
            public string Name { get; }

            public bool Equals(CommandKey other)
            {
                return Symbol == other.Symbol &&
                    string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);
            }

            public override bool Equals(object? obj)
            {
                return obj is CommandKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(
                    char.ToUpperInvariant(Symbol),
                    StringComparer.OrdinalIgnoreCase.GetHashCode(Name));
            }
        }

        private sealed class CommandEntry
        {
            public required char Symbol { get; init; }
            public required string Name { get; init; }
            public required ChatCommandHandler Handler { get; init; }
            public required int MinAccessLevel { get; init; }
            public required string HelpText { get; init; }
            public required string[] Aliases { get; init; }
        }

        private readonly Dictionary<CommandKey, CommandEntry> _commands = new();
        private readonly HashSet<char> _symbols = new();

        public ServerChatCommandRegistry()
        {
            AddSymbol('/');
        }

        public IReadOnlyCollection<char> Symbols => _symbols;

        public void AddSymbol(char symbol)
        {
            if (char.IsWhiteSpace(symbol))
                throw new ArgumentException("Command symbol cannot be whitespace.", nameof(symbol));

            _symbols.Add(symbol);
        }

        public bool Register(
            string name,
            ChatCommandHandler handler,
            char symbol = '/',
            int minAccessLevel = 0,
            string? helpText = null,
            IEnumerable<string>? aliases = null)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Command name cannot be empty.", nameof(name));
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));
            if (char.IsWhiteSpace(symbol))
                throw new ArgumentException("Command symbol cannot be whitespace.", nameof(symbol));

            AddSymbol(symbol);
            string canonicalName = name.Trim();
            var aliasList = new List<string>();
            if (aliases != null)
            {
                foreach (string alias in aliases)
                {
                    if (!string.IsNullOrWhiteSpace(alias))
                        aliasList.Add(alias.Trim());
                }
            }

            var entry = new CommandEntry
            {
                Symbol = symbol,
                Name = canonicalName,
                Handler = handler,
                MinAccessLevel = minAccessLevel,
                HelpText = helpText ?? string.Empty,
                Aliases = aliasList.ToArray(),
            };

            bool registered = TryRegisterKey(new CommandKey(symbol, canonicalName), entry);
            for (int i = 0; i < entry.Aliases.Length; i++)
                registered &= TryRegisterKey(new CommandKey(symbol, entry.Aliases[i]), entry);
            return registered;
        }

        public bool TryDispatch(
            string rawInput,
            in ChatCommandContext context,
            out ChatCommandDispatchResult result)
        {
            result = new ChatCommandDispatchResult(ChatCommandDispatchStatus.NotCommand);
            if (string.IsNullOrWhiteSpace(rawInput))
                return false;

            string trimmed = rawInput.Trim();
            if (trimmed.Length < 2)
                return false;

            char symbol = trimmed[0];
            if (!_symbols.Contains(symbol))
                return false;

            if (!TryTokenize(trimmed.AsSpan(1), out List<string> tokens, out string? tokenError) || tokens.Count == 0)
            {
                result = new ChatCommandDispatchResult(
                    ChatCommandDispatchStatus.InvalidSyntax,
                    error: tokenError ?? "Invalid command syntax.");
                return true;
            }

            string commandName = tokens[0];
            if (!_commands.TryGetValue(new CommandKey(symbol, commandName), out CommandEntry? entry))
            {
                result = new ChatCommandDispatchResult(
                    ChatCommandDispatchStatus.UnknownCommand,
                    commandName,
                    $"Unknown command '{symbol}{commandName}'.");
                return true;
            }

            if (context.AccessLevel < entry.MinAccessLevel)
            {
                result = new ChatCommandDispatchResult(
                    ChatCommandDispatchStatus.PermissionDenied,
                    entry.Name,
                    "Permission denied.");
                return true;
            }

            tokens.RemoveAt(0);
            var invocation = new ChatCommandInvocation(symbol, entry.Name, trimmed, tokens);

            try
            {
                entry.Handler(in context, in invocation);
                result = new ChatCommandDispatchResult(ChatCommandDispatchStatus.Executed, entry.Name);
            }
            catch (Exception ex)
            {
                result = new ChatCommandDispatchResult(
                    ChatCommandDispatchStatus.HandlerError,
                    entry.Name,
                    ex.Message);
            }

            return true;
        }

        public string BuildHelp(char symbol = '/')
        {
            var lines = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CommandEntry entry in _commands.Values)
            {
                if (entry.Symbol != symbol || !seen.Add(entry.Name))
                    continue;

                string help = string.IsNullOrWhiteSpace(entry.HelpText) ? string.Empty : $" - {entry.HelpText}";
                lines.Add($"{symbol}{entry.Name}{help}");
            }

            lines.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join('\n', lines);
        }

        private bool TryRegisterKey(CommandKey key, CommandEntry entry)
        {
            if (_commands.ContainsKey(key))
                return false;

            _commands.Add(key, entry);
            return true;
        }

        private static bool TryTokenize(
            ReadOnlySpan<char> input,
            out List<string> tokens,
            out string? error)
        {
            tokens = new List<string>(8);
            error = null;

            var current = new StringBuilder(32);
            bool inQuotes = false;
            bool escaping = false;

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (escaping)
                {
                    current.Append(c);
                    escaping = false;
                    continue;
                }

                if (c == '\\')
                {
                    escaping = true;
                    continue;
                }

                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (!inQuotes && char.IsWhiteSpace(c))
                {
                    FlushToken(current, tokens);
                    continue;
                }

                current.Append(c);
            }

            if (escaping)
            {
                error = "Command ended with escape character.";
                return false;
            }

            if (inQuotes)
            {
                error = "Unclosed quote in command.";
                return false;
            }

            FlushToken(current, tokens);
            return true;
        }

        private static void FlushToken(StringBuilder current, List<string> tokens)
        {
            if (current.Length == 0)
                return;

            tokens.Add(current.ToString());
            current.Clear();
        }
    }
}

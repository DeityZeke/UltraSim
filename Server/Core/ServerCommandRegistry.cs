using System.Text;

namespace UltraSim.Server;

public sealed class ServerCommandRegistry
{
    private sealed class CommandEntry
    {
        public required string Name { get; init; }
        public required string Prefix { get; init; }
        public required int MinAccessLevel { get; init; }
        public required string PermissionKey { get; init; }
        public required string HelpText { get; init; }
        public required ServerTextCommandHandler Handler { get; init; }
    }

    private readonly Dictionary<string, CommandEntry> _commands = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _prefixes = new();
    private readonly ServerAccessRuleRegistry? _accessRules;

    public ServerCommandRegistry(string commandPrefix = "/", ServerAccessRuleRegistry? accessRules = null)
    {
        _accessRules = accessRules;
        AddPrefix(commandPrefix);
    }

    public IReadOnlyList<string> Prefixes => _prefixes;

    public void AddPrefix(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        if (prefix.Length > 0 && string.IsNullOrWhiteSpace(prefix))
            throw new ArgumentException("Command prefix cannot be whitespace.", nameof(prefix));

        string normalized = prefix.Trim();
        for (int i = 0; i < _prefixes.Count; i++)
        {
            if (string.Equals(_prefixes[i], normalized, StringComparison.Ordinal))
                return;
        }

        _prefixes.Add(normalized);
        _prefixes.Sort(static (left, right) => right.Length.CompareTo(left.Length));
    }

    public bool Register(
        string name,
        ServerTextCommandHandler handler,
        string prefix = "/",
        int minAccessLevel = 0,
        string? helpText = null,
        IEnumerable<string>? aliases = null,
        string? permissionKey = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Command name cannot be empty.", nameof(name));
        ArgumentNullException.ThrowIfNull(handler);
        if (minAccessLevel < 0)
            throw new ArgumentOutOfRangeException(nameof(minAccessLevel));

        AddPrefix(prefix);
        var entry = new CommandEntry
        {
            Name = name.Trim(),
            Prefix = prefix.Trim(),
            MinAccessLevel = minAccessLevel,
            PermissionKey = string.IsNullOrWhiteSpace(permissionKey)
                ? ServerCommandPermissionKeys.ForCommandName(name)
                : ServerAccessKey.NormalizePermissionKey(permissionKey),
            HelpText = helpText ?? string.Empty,
            Handler = handler,
        };

        bool registered = RegisterEntry(entry.Name, entry);
        if (aliases is not null)
        {
            foreach (string alias in aliases)
            {
                if (!string.IsNullOrWhiteSpace(alias))
                    registered &= RegisterEntry(alias.Trim(), entry);
            }
        }

        return registered;
    }

    public bool TryDispatch(in ServerCommandRequest request, out ServerCommandDispatchResult result)
    {
        result = ServerCommandDispatchResult.NotCommand();
        string raw = request.RawText?.Trim() ?? string.Empty;
        if (raw.Length == 0)
            return false;

        if (!TryMatchPrefix(raw, request.Source, out string prefix))
            return false;

        ReadOnlySpan<char> commandSpan = raw.AsSpan(prefix.Length).TrimStart();
        if (commandSpan.Length == 0)
        {
            result = ServerCommandDispatchResult.InvalidSyntax("Missing command name.");
            return true;
        }

        if (!TryTokenize(commandSpan, out List<string> tokens, out string rawArguments, out string? error))
        {
            result = ServerCommandDispatchResult.InvalidSyntax(error ?? "Invalid command syntax.");
            return true;
        }

        if (tokens.Count == 0)
        {
            result = ServerCommandDispatchResult.InvalidSyntax("Missing command name.");
            return true;
        }

        string commandName = tokens[0];
        if (!_commands.TryGetValue(MakeKey(prefix, commandName), out CommandEntry? entry))
        {
            result = ServerCommandDispatchResult.UnknownCommand(
                commandName,
                $"Unknown command '{prefix}{commandName}'.");
            return true;
        }

        if (!CanExecute(in request, entry))
        {
            result = ServerCommandDispatchResult.PermissionDenied(entry.Name);
            return true;
        }

        tokens.RemoveAt(0);
        var named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < tokens.Count; i++)
        {
            string token = tokens[i].TrimEnd(',');
            int equals = token.IndexOf('=');
            if (equals <= 0)
                continue;

            string key = token[..equals].Trim();
            string value = token[(equals + 1)..].Trim();
            if (key.Length > 0)
                named[key] = value.TrimEnd(',');
        }

        var invocation = new ServerTextCommandInvocation(
            prefix,
            entry.Name,
            raw,
            rawArguments,
            tokens,
            named);

        try
        {
            entry.Handler(in request, invocation);
            result = ServerCommandDispatchResult.Executed(entry.Name);
        }
        catch (Exception ex)
        {
            result = ServerCommandDispatchResult.HandlerError(entry.Name, ex.Message);
        }

        return true;
    }

    public string BuildHelp(string prefix = "/")
    {
        var lines = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (CommandEntry entry in _commands.Values)
        {
            if (!string.Equals(entry.Prefix, prefix, StringComparison.Ordinal) || !seen.Add(entry.Name))
                continue;

            string help = string.IsNullOrWhiteSpace(entry.HelpText) ? string.Empty : $" - {entry.HelpText}";
            lines.Add($"{entry.Prefix}{entry.Name}{help}");
        }

        lines.Sort(StringComparer.OrdinalIgnoreCase);
        return string.Join('\n', lines);
    }

    private bool RegisterEntry(string name, CommandEntry entry)
    {
        string key = MakeKey(entry.Prefix, name);
        if (_commands.ContainsKey(key))
            return false;

        _commands.Add(key, entry);
        return true;
    }

    private bool CanExecute(in ServerCommandRequest request, CommandEntry entry)
    {
        if (_accessRules is null)
            return request.AccessLevel >= entry.MinAccessLevel;

        var accessRequest = new ServerAccessEvaluationRequest(
            entry.PermissionKey,
            request.AccessRuleKeys,
            request.DirectPermissionGrants,
            request.DirectPermissionDenies,
            request.AccessLevel,
            entry.MinAccessLevel);
        return _accessRules.Evaluate(accessRequest).IsAllowed;
    }

    private bool TryMatchPrefix(string raw, ServerCommandSource source, out string prefix)
    {
        for (int i = 0; i < _prefixes.Count; i++)
        {
            string candidate = _prefixes[i];
            if (candidate.Length == 0)
            {
                if (source == ServerCommandSource.ChatInput)
                    continue;

                prefix = candidate;
                return true;
            }

            if (raw.StartsWith(candidate, StringComparison.Ordinal))
            {
                prefix = candidate;
                return true;
            }
        }

        prefix = string.Empty;
        return false;
    }

    private static string MakeKey(string prefix, string commandName) =>
        $"{prefix}\u001F{commandName}";

    private static bool TryTokenize(
        ReadOnlySpan<char> input,
        out List<string> tokens,
        out string rawArguments,
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

            if (!inQuotes && (char.IsWhiteSpace(c) || c == ','))
            {
                FlushToken(current, tokens);
                continue;
            }

            current.Append(c);
        }

        if (escaping)
        {
            rawArguments = string.Empty;
            error = "Command ended with escape character.";
            return false;
        }

        if (inQuotes)
        {
            rawArguments = string.Empty;
            error = "Unclosed quote in command.";
            return false;
        }

        FlushToken(current, tokens);
        rawArguments = tokens.Count <= 1 ? string.Empty : string.Join(' ', tokens.Skip(1));
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

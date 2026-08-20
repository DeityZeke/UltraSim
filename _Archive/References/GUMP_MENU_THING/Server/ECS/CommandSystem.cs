
#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;

using Godot;

public class CommandSystem : BaseSystem
{
    // Command registration: command name (lowercase) -> CommandInfo
    private readonly Dictionary<string, CommandInfo> _commands = new();

    // Archetype (AccessLevel) -> set of command names
    private readonly Dictionary<AccessLevel, HashSet<string>> _archetypeCommands = new();

    // Per-entity explicit grants: EntityID -> set of command names
    private readonly Dictionary<EntityID, HashSet<string>> _extraGrants = new();

    // Per-entity explicit revokes: EntityID -> set of command names
    private readonly Dictionary<EntityID, HashSet<string>> _extraRevokes = new();

    // For logging purposes (optional, can be expanded)
    private readonly List<string> _commandLog = new();

    public override string Name => "Command System";

    // -- Command Registration --

    public void Register(string command, AccessLevel defaultAccessLevel, CommandEventHandler handler)
    {
        var method = handler.Method;
        var usage = method.GetCustomAttribute<UsageAttribute>()?.Usage ?? command;
        var desc = method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "";

        var info = new CommandInfo(command, defaultAccessLevel, handler, usage, desc);

        _commands[command.ToLowerInvariant()] = info;

        // By default, add to the archetype for the default access level
        if (!_archetypeCommands.TryGetValue(defaultAccessLevel, out var set))
        {
            set = new HashSet<string>();
            _archetypeCommands[defaultAccessLevel] = set;
        }
        set.Add(command.ToLowerInvariant());
    }

    public bool HasCommand(string command) => _commands.ContainsKey(command.ToLowerInvariant());

    public CommandInfo? GetCommand(string command)
    {
        _commands.TryGetValue(command.ToLowerInvariant(), out var info);
        return info;
    }

    public IEnumerable<CommandInfo> GetAllCommands() => _commands.Values;

    // -- Archetype (Role) Management --

    public void AddCommandToArchetype(AccessLevel level, string command)
    {
        var cmd = command.ToLowerInvariant();
        if (!_commands.ContainsKey(cmd)) return;
        if (!_archetypeCommands.TryGetValue(level, out var set))
        {
            set = new HashSet<string>();
            _archetypeCommands[level] = set;
        }
        set.Add(cmd);
    }

    public void RemoveCommandFromArchetype(AccessLevel level, string command)
    {
        var cmd = command.ToLowerInvariant();
        if (_archetypeCommands.TryGetValue(level, out var set))
        {
            set.Remove(cmd);
        }
    }

    public IEnumerable<string> GetCommandsForArchetype(AccessLevel level)
    {
        if (_archetypeCommands.TryGetValue(level, out var set))
            return set;
        return Array.Empty<string>();
    }

    // -- Per-Entity Grant/Revoke --

    public void GrantCommand(EntityID entity, string command)
    {
        var cmd = command.ToLowerInvariant();
        if (!_commands.ContainsKey(cmd)) return;
        if (!_extraGrants.TryGetValue(entity, out var set))
        {
            set = new HashSet<string>();
            _extraGrants[entity] = set;
        }
        set.Add(cmd);

        // If a grant exists, remove any explicit revoke
        if (_extraRevokes.TryGetValue(entity, out var revokes))
            revokes.Remove(cmd);
    }

    public void RevokeCommand(EntityID entity, string command)
    {
        var cmd = command.ToLowerInvariant();
        if (!_commands.ContainsKey(cmd)) return;
        if (!_extraRevokes.TryGetValue(entity, out var set))
        {
            set = new HashSet<string>();
            _extraRevokes[entity] = set;
        }
        set.Add(cmd);

        // If a revoke exists, remove any explicit grant
        if (_extraGrants.TryGetValue(entity, out var grants))
            grants.Remove(cmd);
    }

    public void ClearCommandGrant(EntityID entity, string command)
    {
        var cmd = command.ToLowerInvariant();
        _extraGrants.TryGetValue(entity, out var grants);
        grants?.Remove(cmd);
        _extraRevokes.TryGetValue(entity, out var revokes);
        revokes?.Remove(cmd);
    }

    // -- Permission Checking --

    public bool CanUseCommand(EntityID entity, AccessLevel archetype, string command)
    {
        var cmd = command.ToLowerInvariant();

        // Explicit revoke always wins
        if (_extraRevokes.TryGetValue(entity, out var revoked) && revoked.Contains(cmd))
            return false;

        // Explicit grant
        if (_extraGrants.TryGetValue(entity, out var granted) && granted.Contains(cmd))
            return true;

        // Archetype default
        return _archetypeCommands.TryGetValue(archetype, out var cmds) && cmds.Contains(cmd);
    }

    // -- Command Execution --

    public bool TryExecute(string input, EntityID sender, AccessLevel archetype, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(input))
        {
            error = "No command entered.";
            return false;
        }

        var split = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cmd = split[0].ToLowerInvariant();
        var args = split.Length > 1 ? split[1..] : Array.Empty<string>();

        if (!_commands.TryGetValue(cmd, out var info))
        {
            error = $"Unknown command '{cmd}'.";
            return false;
        }

        if (!CanUseCommand(sender, archetype, cmd))
        {
            error = "You do not have permission to use this command.";
            return false;
        }

        var eventArgs = new CommandEventArgs(cmd, args, sender);
        try
        {
            info.Handler(eventArgs);

            // Optionally log the command execution
            _commandLog.Add($"{DateTime.UtcNow:s} | {sender} | {cmd} {string.Join(" ", args)}");
        }
        catch (Exception ex)
        {
            error = $"Command error: {ex.Message}";
            return false;
        }
        return true;
    }

    // -- Logging Save/Load for future expansion --

    public override void Save()
    {
        // TODO: Save _commandLog or audit trail to disk or external storage
    }

    public override void Load()
    {
        // TODO: Load _commandLog or audit trail from disk or external storage
    }

    public override void Enable()
    {
        Console.WriteLine($"{Name} enabled!");
    }

    public override void Disable()
    {
        Console.WriteLine($"{Name} disabled!");
    }

    public override void Update(double delta)
    {
        // Not used for command system, but could poll for queued commands, etc.
    }
}

// Supporting types for completeness

public enum AccessLevel
{
    Player,
    Veteran,
    Moderator,
    GameMaster,
    Admin
}

public delegate void CommandEventHandler(CommandEventArgs e);

public class CommandEventArgs
{
    public string Command { get; }
    public string[] Arguments { get; }
    public EntityID Sender { get; }

    public CommandEventArgs(string command, string[] arguments, EntityID sender)
    {
        Command = command;
        Arguments = arguments;
        Sender = sender;
    }
}

public class CommandInfo
{
    public string Name { get; }
    public AccessLevel DefaultAccess { get; }
    public CommandEventHandler Handler { get; }
    public string Usage { get; }
    public string Description { get; }

    public CommandInfo(string name, AccessLevel defaultAccess, CommandEventHandler handler, string usage, string description)
    {
        Name = name;
        DefaultAccess = defaultAccess;
        Handler = handler;
        Usage = usage;
        Description = description;
    }
}

// Example attribute definitions
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class UsageAttribute : Attribute
{
    public string Usage { get; }
    public UsageAttribute(string usage) => Usage = usage;
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class DescriptionAttribute : Attribute
{
    public string Description { get; }
    public DescriptionAttribute(string description) => Description = description;
}

// You should have your EntityID struct/class defined elsewhere in your codebase.
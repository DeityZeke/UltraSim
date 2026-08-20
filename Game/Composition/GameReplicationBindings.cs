using UltraSim.ECS;
using UltraSim.Game.Admission;

namespace UltraSim.Game.Composition;

/// <summary>Selects whether a game generation reserves replication observation and ingress machinery.</summary>
public enum GameRuntimeProfile : byte
{
    /// <summary>Ordinary local generation with Spatial lifecycle retention but no replication machinery.</summary>
    Local = 0,

    /// <summary>Network-enabled generation with exact observation, lifecycle, and bounded command ingress.</summary>
    Networked = 1,
}

/// <summary>One prevalidated simulation command admitted for the next scheduler Input stage.</summary>
public readonly record struct GameSimulationCommand(
    ParticipantId Participant,
    Entity Entity,
    ulong ClientSequence,
    GameSimulationCommandKind Kind,
    int TickMarkerDelta);

/// <summary>Closed game command set consumed by the bounded replication ingress system.</summary>
public enum GameSimulationCommandKind : byte
{
    ApplyTickMarkerDelta = 0,
    AddSimulationState = 1,
    RemoveSimulationState = 2,
    RetireControlledEntity = 3,
    /// <summary>Sets the controlled diagnostic velocity; zero is the explicit stop command.</summary>
    SetInputVelocity = 4,
}

/// <summary>
/// Generation-bound prepared ECS capabilities consumed by the game replication layer.
/// </summary>
/// <remarks>
/// This object owns no second world authority. Its methods require the matching
/// callback-scoped world and expose only the prepared baseline/change/ingress work
/// selected by the concrete game composition.
/// </remarks>
public sealed class GameReplicationBindings
{
    private readonly WorldId _worldId;
    private readonly ReadJobPlan<GameSimulationState> _baseline;
    private readonly ChangedRowsJobPlan<GameSimulationState, GameSimulationState> _changes;
    private readonly GameSimulationCommandRuntime _commands;

    internal GameReplicationBindings(
        WorldId worldId,
        in ReadJobPlan<GameSimulationState> baseline,
        in ChangedRowsJobPlan<GameSimulationState, GameSimulationState> changes,
        GameSimulationCommandRuntime commands,
        ComponentId simulationComponentId)
    {
        _worldId = worldId;
        _baseline = baseline;
        _changes = changes;
        _commands = commands;
        SimulationComponentId = simulationComponentId;
    }

    public WorldId WorldId => _worldId;

    public ComponentId SimulationComponentId { get; }

    public int CommandCapacity => _commands.Capacity;

    public int PendingCommandCount => _commands.Count;

    public long AppliedCommandCount => _commands.AppliedCount;

    public long RejectedCommandCount => _commands.RejectedCount;

    public int ExecuteBaseline<TJob>(EcsWorld world, ref TJob job)
        where TJob : struct, IReadChunkJob<GameSimulationState>
    {
        ValidateWorld(world);
        return world.ExecuteJob(_baseline, ref job);
    }

    public int ExecuteChanges<TJob>(EcsWorld world, ref ChangeCursor cursor, ref TJob job)
        where TJob : struct, IReadOnlyChunkJob<GameSimulationState>
    {
        ValidateWorld(world);
        return world.ExecuteChangedRowsJob(_changes, ref cursor, ref job);
    }

    /// <summary>Transactionally appends a prevalidated command range for the next tick.</summary>
    public bool TryEnqueueCommands(ReadOnlySpan<GameSimulationCommand> commands) =>
        _commands.TryEnqueue(commands);

    private void ValidateWorld(EcsWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (world.Id != _worldId)
            throw new ArgumentException("Replication bindings belong to a different ECS world.", nameof(world));
    }
}

internal sealed class GameSimulationCommandRuntime : IRangeSystemRuntime<GameSimulationCommandJob>
{
    private readonly GameSimulationCommand[] _commands;
    private readonly ComponentLookup<GameSimulationState> _simulation;
    private int _count;

    internal GameSimulationCommandRuntime(
        int capacity,
        in ComponentLookup<GameSimulationState> simulation)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _commands = new GameSimulationCommand[capacity];
        _simulation = simulation;
    }

    internal int Capacity => _commands.Length;
    internal int Count => _count;
    internal long AppliedCount { get; private set; }
    internal long RejectedCount { get; private set; }

    internal bool TryEnqueue(ReadOnlySpan<GameSimulationCommand> commands)
    {
        if (commands.Length > _commands.Length - _count)
            return false;
        for (int index = 0; index < commands.Length; index++)
        {
            ref readonly GameSimulationCommand command = ref commands[index];
            if (!command.Participant.IsValid || !command.Entity.IsValid || command.ClientSequence == 0 ||
                (uint)command.Kind > (uint)GameSimulationCommandKind.SetInputVelocity)
                return false;
        }
        commands.CopyTo(_commands.AsSpan(_count));
        _count += commands.Length;
        return true;
    }

    public int GetRangeCount(in SystemPrepareContext context) => _count;

    public GameSimulationCommandJob Prepare(in SystemPrepareContext context) =>
        new(_commands, _simulation);

    public void Complete(
        in SystemCompleteContext context,
        ReadOnlySpan<GameSimulationCommandJob> participantResults)
    {
        long applied = 0;
        long rejected = 0;
        for (int index = 0; index < participantResults.Length; index++)
        {
            applied += participantResults[index].Applied;
            rejected += participantResults[index].Rejected;
        }
        AppliedCount = checked(AppliedCount + applied);
        RejectedCount = checked(RejectedCount + rejected);
        _commands.AsSpan(0, _count).Clear();
        _count = 0;
    }
}

internal struct GameSimulationCommandJob : IRangeJob
{
    private readonly GameSimulationCommand[] _commands;
    private readonly ComponentLookup<GameSimulationState> _simulation;

    internal GameSimulationCommandJob(
        GameSimulationCommand[] commands,
        in ComponentLookup<GameSimulationState> simulation)
    {
        _commands = commands;
        _simulation = simulation;
        Applied = 0;
        Rejected = 0;
    }

    internal int Applied;
    internal int Rejected;

    public void Execute(in SystemJobContext context, int start, int count)
    {
        int end = checked(start + count);
        for (int index = start; index < end; index++)
        {
            ref readonly GameSimulationCommand command = ref _commands[index];
            switch (command.Kind)
            {
                case GameSimulationCommandKind.ApplyTickMarkerDelta:
                    if (!context.TryRead(command.Entity, _simulation, out GameSimulationState state))
                    {
                        Rejected++;
                        break;
                    }
                    state.TickMarker = unchecked(state.TickMarker + command.TickMarkerDelta);
                    if (context.TryWrite(command.Entity, _simulation, state)) Applied++;
                    else Rejected++;
                    break;
                case GameSimulationCommandKind.AddSimulationState:
                    if (context.Commands.TryAdd(
                        command.Entity,
                        new GameSimulationState { TickMarker = command.TickMarkerDelta })) Applied++;
                    else Rejected++;
                    break;
                case GameSimulationCommandKind.RemoveSimulationState:
                    if (context.Commands.TryRemove<GameSimulationState>(command.Entity)) Applied++;
                    else Rejected++;
                    break;
                case GameSimulationCommandKind.RetireControlledEntity:
                    if (context.Commands.TryDestroy(command.Entity)) Applied++;
                    else Rejected++;
                    break;
                case GameSimulationCommandKind.SetInputVelocity:
                    if (!context.TryRead(command.Entity, _simulation, out GameSimulationState velocityState))
                    {
                        Rejected++;
                        break;
                    }
                    velocityState.InputVelocity = command.TickMarkerDelta;
                    if (context.TryWrite(command.Entity, _simulation, velocityState)) Applied++;
                    else Rejected++;
                    break;
                default:
                    Rejected++;
                    break;
            }
        }
    }
}

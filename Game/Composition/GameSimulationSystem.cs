using UltraSim.ECS;

namespace UltraSim.Game.Composition;

/// <summary>
/// Minimal deterministic S0 simulation. It deliberately performs real
/// topology-selected scheduled ECS work without inventing gameplay policy.
/// <see cref="GameSimulationState.TickMarker"/> is deliberately step-relative
/// diagnostic state; elapsed-time gameplay must use the job context delta.
/// </summary>
internal sealed class GameSimulationSystemRuntime : ISystemRuntime<GameSimulationJob>
{
    public GameSimulationJob Prepare(in SystemPrepareContext context) => default;

    public void Complete(
        in SystemCompleteContext context,
        ReadOnlySpan<GameSimulationJob> jobs)
    {
    }
}

internal struct GameSimulationJob : IScheduledWriteChunkJob<GameSimulationState>
{
    public void Execute(
        in SystemJobContext context,
        in WriteComponentChunk<GameSimulationState> chunk)
    {
        Span<GameSimulationState> states = chunk.WriteComponents;
        for (int row = 0; row < chunk.Count; row++)
            states[row].TickMarker = unchecked(states[row].TickMarker + 1);
    }
}

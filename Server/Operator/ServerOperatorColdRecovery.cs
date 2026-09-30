using UltraSim.ECS;
using UltraSim.Framework.Persistence;
using UltraSim.Game.Composition;
using UltraSim.Game.Persistence;
using UltraSim.Server.Runtime;

namespace UltraSim.Server.Operator;

/// <summary>
/// Server-composed cold restore. It is invoked before session admission, publication sources or
/// frontends bind to a generation. It never replaces a generation in a live shared authority.
/// </summary>
public static class ServerOperatorColdRecovery
{
    public static ulong Restore(ServerRuntime runtime, UltraSimGameComposition composition,
        string saveRoot, SaveSetRecoveryMode mode)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(composition);
        var persistence = new GameRuntimePersistence(runtime.Game, composition,
            new PersistenceRestoreOptions(PersistenceLimits.ServerDefault, ChunkLayoutOptions.KiB16),
            new SaveSetCoordinator(saveRoot));
        GameLoadResult result = persistence.TryLoad(mode);
        if (!result.Succeeded || result.SelectedSequence == 0)
            throw new InvalidDataException("The cold server could not restore a complete save generation.",
                result.Failure);
        return result.SelectedSequence;
    }
}

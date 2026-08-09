using System.Runtime.InteropServices;

namespace UltraSim.ECS;

/// <summary>
/// Barrier-local token for an entity requested by a deferred create command.
/// </summary>
/// <remarks>
/// A token may target later commands in the same recording epoch. After successful
/// playback it can be resolved until the buffer begins another epoch or is cleared.
/// It is transient and must never be persisted.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 16)]
public readonly struct DeferredEntity : IEquatable<DeferredEntity>
{
    private readonly uint _worldId;
    private readonly uint _bufferId;
    private readonly uint _epoch;
    private readonly uint _commandIndex;

    internal DeferredEntity(WorldId worldId, uint bufferId, uint epoch, uint commandIndex)
    {
        _worldId = worldId.Value;
        _bufferId = bufferId;
        _epoch = epoch;
        _commandIndex = commandIndex;
    }

    public bool IsValid => _worldId != 0 && _bufferId != 0 && _epoch != 0;

    public WorldId World => new(_worldId);

    public uint Epoch => _epoch;

    internal uint BufferId => _bufferId;

    internal uint CommandIndex => _commandIndex;

    public bool Equals(DeferredEntity other) =>
        _worldId == other._worldId &&
        _bufferId == other._bufferId &&
        _epoch == other._epoch &&
        _commandIndex == other._commandIndex;

    public override bool Equals(object? obj) => obj is DeferredEntity other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(
        _worldId,
        _bufferId,
        _epoch,
        _commandIndex);

    public static bool operator ==(DeferredEntity left, DeferredEntity right) => left.Equals(right);

    public static bool operator !=(DeferredEntity left, DeferredEntity right) => !left.Equals(right);

    public override string ToString() => IsValid
        ? $"DeferredEntity({_worldId}:{_bufferId}:{_epoch}:{_commandIndex})"
        : "DeferredEntity(Invalid)";
}

using System.Runtime.InteropServices;

namespace UltraSim.ECS;

/// <summary>
/// World-bound acknowledgement position for one independent change consumer.
/// Copying the value creates an independent cursor at the same observation point.
/// It is transient runtime state, not a durable consumer or persistence identity.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 8, Size = 16)]
public readonly struct ChangeCursor : IEquatable<ChangeCursor>
{
    private readonly ChangeVersion _version;
    private readonly WorldId _worldId;

    internal ChangeCursor(WorldId worldId, ChangeVersion version)
    {
        if (!worldId.IsValid)
            throw new ArgumentException("A valid world ID is required.", nameof(worldId));
        _version = version;
        _worldId = worldId;
    }

    public bool IsValid => _worldId.IsValid;

    public WorldId WorldId => _worldId;

    public ChangeVersion Version => _version;

    internal ChangeCursor AdvanceTo(ChangeVersion version) => new(_worldId, version);

    public bool Equals(ChangeCursor other) =>
        _version == other._version && _worldId == other._worldId;

    public override bool Equals(object? obj) => obj is ChangeCursor other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_version, _worldId);

    public override string ToString() => IsValid
        ? $"ChangeCursor({_worldId}, {_version})"
        : "ChangeCursor(Invalid)";

    public static bool operator ==(ChangeCursor left, ChangeCursor right) => left.Equals(right);

    public static bool operator !=(ChangeCursor left, ChangeCursor right) => !left.Equals(right);
}

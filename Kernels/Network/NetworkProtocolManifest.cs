using System.Runtime.InteropServices;

namespace UltraSim.Network;

[StructLayout(LayoutKind.Sequential, Size = 2)]
public readonly struct NetworkMessageId : IEquatable<NetworkMessageId>, IComparable<NetworkMessageId>
{
    public ushort Value { get; }
    public bool IsValid => Value != 0;
    public NetworkMessageId(ushort value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(value);
        Value = value;
    }
    public bool Equals(NetworkMessageId other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is NetworkMessageId other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public int CompareTo(NetworkMessageId other) => Value.CompareTo(other.Value);
    public static bool operator ==(NetworkMessageId left, NetworkMessageId right) => left.Equals(right);
    public static bool operator !=(NetworkMessageId left, NetworkMessageId right) => !left.Equals(right);
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class NetworkProtocolAttribute : Attribute
{
    public ushort Version { get; }
    public NetworkProtocolAttribute(ushort version)
    {
        ArgumentOutOfRangeException.ThrowIfZero(version);
        Version = version;
    }
}

[AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class NetworkMessageAttribute : Attribute
{
    public ushort Id { get; }
    public string CanonicalKey { get; }
    public ushort Version { get; }
    public NetworkLane Lane { get; }

    public NetworkMessageAttribute(ushort id, string canonicalKey, ushort version, NetworkLane lane)
    {
        ArgumentOutOfRangeException.ThrowIfZero(id);
        if (string.IsNullOrWhiteSpace(canonicalKey)) throw new ArgumentException("A canonical key is required.", nameof(canonicalKey));
        ArgumentOutOfRangeException.ThrowIfZero(version);
        if ((uint)lane >= NetworkLaneContracts.Count) throw new ArgumentOutOfRangeException(nameof(lane));
        Id = id;
        CanonicalKey = canonicalKey;
        Version = version;
        Lane = lane;
    }
}

public readonly record struct NetworkMessageDescriptor(
    NetworkMessageId Id,
    string CanonicalKey,
    ushort Version,
    NetworkLane Lane,
    int MaximumEncodedBytes,
    ulong CodecFingerprint);

/// <summary>Immutable collision-checked message catalog built cold without runtime reflection.</summary>
public sealed class NetworkProtocolManifest
{
    private readonly NetworkMessageDescriptor[] _byId;

    private NetworkProtocolManifest(
        ushort version,
        NetworkMessageDescriptor[] byId,
        ulong fingerprint,
        int maximumUnreliableDataFramesPerDatagram)
    {
        Version = version;
        _byId = byId;
        Fingerprint = fingerprint;
        MaximumUnreliableDataFramesPerDatagram = maximumUnreliableDataFramesPerDatagram;
    }

    public ushort Version { get; }
    public int Count => _byId.Length;
    public ulong Fingerprint { get; }
    /// <summary>
    /// Maximum number of unreliable Data frames this manifest permits a peer to
    /// decode in one authenticated datagram. This is a negotiated wire capability;
    /// the local send policy remains <see cref="NetworkProtocolLimits.MaximumUnreliableFramesPerDatagram"/>.
    /// </summary>
    public int MaximumUnreliableDataFramesPerDatagram { get; }
    public bool SupportsUnreliableDataAggregation => MaximumUnreliableDataFramesPerDatagram > 1;
    public ReadOnlySpan<NetworkMessageDescriptor> Messages => _byId;

    public static NetworkProtocolManifest Create(
        ushort version,
        ReadOnlySpan<NetworkMessageDescriptor> descriptors,
        int maximumMessages = ushort.MaxValue,
        int maximumUnreliableDataFramesPerDatagram = 1)
    {
        ArgumentOutOfRangeException.ThrowIfZero(version);
        if (maximumMessages <= 0) throw new ArgumentOutOfRangeException(nameof(maximumMessages));
        if (maximumUnreliableDataFramesPerDatagram is <= 0 or > 64)
            throw new ArgumentOutOfRangeException(nameof(maximumUnreliableDataFramesPerDatagram));
        if (descriptors.Length == 0 || descriptors.Length > maximumMessages)
            throw new ArgumentOutOfRangeException(nameof(descriptors));
        var copy = descriptors.ToArray();
        Array.Sort(copy, static (left, right) => left.Id.CompareTo(right.Id));
        ulong fingerprint = 14695981039346656037UL;
        Mix(ref fingerprint, version);
        Mix(ref fingerprint, unchecked((ulong)maximumUnreliableDataFramesPerDatagram));
        NetworkMessageDescriptor previous = default;
        for (int index = 0; index < copy.Length; index++)
        {
            NetworkMessageDescriptor current = copy[index];
            if (!current.Id.IsValid || string.IsNullOrWhiteSpace(current.CanonicalKey) || current.Version == 0 ||
                (uint)current.Lane >= NetworkLaneContracts.Count || current.MaximumEncodedBytes < 0)
                throw new ArgumentException("A protocol descriptor is invalid.", nameof(descriptors));
            if (index != 0 && previous.Id == current.Id)
                throw new ArgumentException($"Duplicate network message ID {current.Id.Value}.", nameof(descriptors));
            for (int keyIndex = 0; keyIndex < index; keyIndex++)
                if (string.Equals(copy[keyIndex].CanonicalKey, current.CanonicalKey, StringComparison.Ordinal))
                    throw new ArgumentException($"Duplicate network message key '{current.CanonicalKey}'.", nameof(descriptors));
            Mix(ref fingerprint, current.Id.Value);
            Mix(ref fingerprint, current.Version);
            Mix(ref fingerprint, (byte)current.Lane);
            Mix(ref fingerprint, unchecked((ulong)current.MaximumEncodedBytes));
            Mix(ref fingerprint, current.CodecFingerprint);
            foreach (char character in current.CanonicalKey) Mix(ref fingerprint, character);
            previous = current;
        }
        return new NetworkProtocolManifest(
            version, copy, fingerprint, maximumUnreliableDataFramesPerDatagram);
    }

    public bool TryGet(NetworkMessageId id, out NetworkMessageDescriptor descriptor)
    {
        return TryGet(id, out descriptor, out _);
    }

    internal bool TryGet(
        NetworkMessageId id,
        out NetworkMessageDescriptor descriptor,
        out int messageSlot)
    {
        int low = 0;
        int high = _byId.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            NetworkMessageDescriptor candidate = _byId[middle];
            int comparison = candidate.Id.CompareTo(id);
            if (comparison == 0)
            {
                descriptor = candidate;
                messageSlot = middle;
                return true;
            }
            if (comparison < 0) low = middle + 1;
            else high = middle - 1;
        }
        descriptor = default;
        messageSlot = -1;
        return false;
    }

    private static void Mix(ref ulong hash, ulong value) => hash = unchecked((hash ^ value) * 1099511628211UL);
}

[NetworkProtocol(1)]
public static partial class CoreNetworkProtocol
{
}

public enum SignedProtocolProbe : short
{
    Negative = -1,
    Positive = 1,
}

/// <summary>Small generated-codec probe that freezes the primitive wire contract before N0.3.</summary>
[NetworkMessage(1, "ultrasim.network.protocol-probe", 1, NetworkLane.Control)]
public partial struct ProtocolProbeMessage : IEquatable<ProtocolProbeMessage>
{
    public ulong Nonce;
    public uint CapabilityHash;
    public ushort RequestedVersion;
    public bool RequestManifest;
    public NetworkLane PreferredLane;
    public sbyte SignedDelta;
    public SignedProtocolProbe SignedMode;

    public readonly bool Equals(ProtocolProbeMessage other) =>
        Nonce == other.Nonce && CapabilityHash == other.CapabilityHash &&
        RequestedVersion == other.RequestedVersion && RequestManifest == other.RequestManifest &&
        PreferredLane == other.PreferredLane && SignedDelta == other.SignedDelta &&
        SignedMode == other.SignedMode;
    public readonly override bool Equals(object? obj) => obj is ProtocolProbeMessage other && Equals(other);
    public readonly override int GetHashCode() => HashCode.Combine(
        Nonce, CapabilityHash, RequestedVersion, RequestManifest, PreferredLane, SignedDelta, SignedMode);
}

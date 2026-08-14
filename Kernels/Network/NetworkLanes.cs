namespace UltraSim.Network;

/// <summary>Identifies a semantic delivery lane rather than a socket or packet queue.</summary>
public enum NetworkLane : byte
{
    Control = 0,
    Input = 1,
    OwnerState = 2,
    WorldState = 3,
    Event = 4,
    Bulk = 5,
    Disposable = 6,
}

/// <summary>Defines whether loss may be recovered by protocol retransmission.</summary>
public enum NetworkDeliveryGuarantee : byte
{
    Unreliable = 0,
    Reliable = 1,
}

/// <summary>Defines the ordering contract within one lane and logical stream.</summary>
public enum NetworkDeliveryOrder : byte
{
    None = 0,
    Sequenced = 1,
    Ordered = 2,
}

/// <summary>Defines what bounded pending state represents when newer work arrives.</summary>
public enum NetworkRetentionPolicy : byte
{
    ProtocolState = 0,
    ExpiringInput = 1,
    LatestState = 2,
    ApplicationEvent = 3,
    SegmentedBulk = 4,
    Disposable = 5,
}

/// <summary>Describes the fixed semantic contract of one <see cref="NetworkLane"/>.</summary>
public readonly struct NetworkLaneContract : IEquatable<NetworkLaneContract>
{
    public NetworkLane Lane { get; }
    public NetworkDeliveryGuarantee Delivery { get; }
    public NetworkDeliveryOrder Order { get; }
    public NetworkRetentionPolicy Retention { get; }
    public byte Priority { get; }
    public bool AllowsFragmentation { get; }

    internal NetworkLaneContract(
        NetworkLane lane,
        NetworkDeliveryGuarantee delivery,
        NetworkDeliveryOrder order,
        NetworkRetentionPolicy retention,
        byte priority,
        bool allowsFragmentation)
    {
        Lane = lane;
        Delivery = delivery;
        Order = order;
        Retention = retention;
        Priority = priority;
        AllowsFragmentation = allowsFragmentation;
    }

    /// <inheritdoc />
    public bool Equals(NetworkLaneContract other) =>
        Lane == other.Lane && Delivery == other.Delivery && Order == other.Order &&
        Retention == other.Retention && Priority == other.Priority &&
        AllowsFragmentation == other.AllowsFragmentation;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is NetworkLaneContract other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(
        Lane, Delivery, Order, Retention, Priority, AllowsFragmentation);

    public static bool operator ==(NetworkLaneContract left, NetworkLaneContract right) => left.Equals(right);
    public static bool operator !=(NetworkLaneContract left, NetworkLaneContract right) => !left.Equals(right);
}

/// <summary>Provides the exhaustive immutable lane table for the Network V3 contract.</summary>
public static class NetworkLaneContracts
{
    /// <summary>Gets the number of admitted semantic lanes.</summary>
    public const int Count = 7;

    /// <summary>Gets the fixed contract for <paramref name="lane"/>.</summary>
    /// <param name="lane">The admitted semantic lane.</param>
    /// <returns>The immutable lane contract.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="lane"/> is not admitted.</exception>
    public static NetworkLaneContract Get(NetworkLane lane) => lane switch
    {
        NetworkLane.Control => new(lane, NetworkDeliveryGuarantee.Reliable,
            NetworkDeliveryOrder.Ordered, NetworkRetentionPolicy.ProtocolState, 0, true),
        NetworkLane.Input => new(lane, NetworkDeliveryGuarantee.Unreliable,
            NetworkDeliveryOrder.Sequenced, NetworkRetentionPolicy.ExpiringInput, 1, false),
        NetworkLane.OwnerState => new(lane, NetworkDeliveryGuarantee.Unreliable,
            NetworkDeliveryOrder.Sequenced, NetworkRetentionPolicy.LatestState, 2, false),
        NetworkLane.WorldState => new(lane, NetworkDeliveryGuarantee.Unreliable,
            NetworkDeliveryOrder.Sequenced, NetworkRetentionPolicy.LatestState, 3, false),
        NetworkLane.Event => new(lane, NetworkDeliveryGuarantee.Reliable,
            NetworkDeliveryOrder.Ordered, NetworkRetentionPolicy.ApplicationEvent, 2, true),
        NetworkLane.Bulk => new(lane, NetworkDeliveryGuarantee.Reliable,
            NetworkDeliveryOrder.Ordered, NetworkRetentionPolicy.SegmentedBulk, 4, true),
        NetworkLane.Disposable => new(lane, NetworkDeliveryGuarantee.Unreliable,
            NetworkDeliveryOrder.None, NetworkRetentionPolicy.Disposable, 5, false),
        _ => throw new ArgumentOutOfRangeException(nameof(lane), lane, "Unknown network lane."),
    };
}

using System.Buffers.Binary;
using System.Security.Cryptography;
using UltraSim.Network;

namespace UltraSim.Game.Protocol;

public enum RemoteServerPerformanceApplyStatus : byte
{
    Applied = 1,
    Rejected = 2,
}

public readonly record struct RemoteServerPerformanceProfileHash(
    ulong Part0, ulong Part1, ulong Part2, ulong Part3)
{
    public bool IsValid => Part0 != 0 || Part1 != 0 || Part2 != 0 || Part3 != 0;

    internal static RemoteServerPerformanceProfileHash FromBytes(ReadOnlySpan<byte> bytes) => new(
        BinaryPrimitives.ReadUInt64LittleEndian(bytes),
        BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]),
        BinaryPrimitives.ReadUInt64LittleEndian(bytes[16..]),
        BinaryPrimitives.ReadUInt64LittleEndian(bytes[24..]));
}

/// <summary>
/// Bounded authenticated execution/delivery policy for one application session. The server owns
/// the hard input limit; the remaining cadences are finite informational expectations or stock-
/// client hints. This is data, never executable behavior or a render-frame policy.
/// </summary>
public sealed class RemoteServerPerformanceProfile
{
    private RemoteServerPerformanceProfile(
        ulong revision,
        ushort simulationFrequencyHz,
        uint maximumNetworkServiceIntervalMicroseconds,
        ushort maximumInputHz,
        ushort recommendedInputHz,
        ushort inputBurstCapacity,
        ushort inputBurstWindowMilliseconds,
        ushort expectedOwnerStateHz,
        ushort expectedImmediatePublicHz,
        ushort expectedNearPublicHz,
        ushort expectedFarPublicHz,
        ushort interpolationBufferMilliseconds)
    {
        Revision = revision;
        SimulationFrequencyHz = simulationFrequencyHz;
        MaximumNetworkServiceIntervalMicroseconds = maximumNetworkServiceIntervalMicroseconds;
        MaximumInputHz = maximumInputHz;
        RecommendedInputHz = recommendedInputHz;
        InputBurstCapacity = inputBurstCapacity;
        InputBurstWindowMilliseconds = inputBurstWindowMilliseconds;
        ExpectedOwnerStateHz = expectedOwnerStateHz;
        ExpectedImmediatePublicHz = expectedImmediatePublicHz;
        ExpectedNearPublicHz = expectedNearPublicHz;
        ExpectedFarPublicHz = expectedFarPublicHz;
        InterpolationBufferMilliseconds = interpolationBufferMilliseconds;
        Hash = ComputeHash(this);
    }

    public ulong Revision { get; }
    public RemoteServerPerformanceProfileHash Hash { get; }
    public ushort SimulationFrequencyHz { get; }
    public uint MaximumNetworkServiceIntervalMicroseconds { get; }
    public ushort MaximumInputHz { get; }
    public ushort RecommendedInputHz { get; }
    public ushort InputBurstCapacity { get; }
    public ushort InputBurstWindowMilliseconds { get; }
    public ushort ExpectedOwnerStateHz { get; }
    public ushort ExpectedImmediatePublicHz { get; }
    public ushort ExpectedNearPublicHz { get; }
    public ushort ExpectedFarPublicHz { get; }
    public ushort InterpolationBufferMilliseconds { get; }

    public static bool TryCreate(
        ulong revision,
        int simulationFrequencyHz,
        int maximumNetworkServiceIntervalMicroseconds,
        int maximumInputHz,
        int recommendedInputHz,
        int inputBurstCapacity,
        int inputBurstWindowMilliseconds,
        int expectedOwnerStateHz,
        int expectedImmediatePublicHz,
        int expectedNearPublicHz,
        int expectedFarPublicHz,
        int interpolationBufferMilliseconds,
        out RemoteServerPerformanceProfile? profile)
    {
        profile = null;
        if (revision == 0 ||
            simulationFrequencyHz is <= 0 or > 1_000 ||
            maximumNetworkServiceIntervalMicroseconds is < 100 or > 1_000_000 ||
            maximumInputHz is <= 0 or > 240 ||
            recommendedInputHz <= 0 || recommendedInputHz > maximumInputHz ||
            inputBurstCapacity is <= 0 or > 32 ||
            inputBurstWindowMilliseconds is < 10 or > 5_000 ||
            expectedOwnerStateHz is <= 0 or > 240 ||
            expectedImmediatePublicHz is <= 0 or > 240 ||
            expectedNearPublicHz <= 0 || expectedNearPublicHz > expectedImmediatePublicHz ||
            expectedFarPublicHz <= 0 || expectedFarPublicHz > expectedNearPublicHz ||
            interpolationBufferMilliseconds is < 0 or > 2_000)
            return false;

        // The token bucket's finite capacity must hold at least the hard-rate traffic produced
        // across the declared burst horizon. This keeps MaximumInputHz attainable without a
        // second phase-sensitive fixed window at the authority receive boundary.
        long maximumPerWindow = checked(
            ((long)maximumInputHz * inputBurstWindowMilliseconds + 999) / 1_000);
        if (inputBurstCapacity < Math.Max(1, maximumPerWindow)) return false;

        profile = new RemoteServerPerformanceProfile(
            revision,
            checked((ushort)simulationFrequencyHz),
            checked((uint)maximumNetworkServiceIntervalMicroseconds),
            checked((ushort)maximumInputHz),
            checked((ushort)recommendedInputHz),
            checked((ushort)inputBurstCapacity),
            checked((ushort)inputBurstWindowMilliseconds),
            checked((ushort)expectedOwnerStateHz),
            checked((ushort)expectedImmediatePublicHz),
            checked((ushort)expectedNearPublicHz),
            checked((ushort)expectedFarPublicHz),
            checked((ushort)interpolationBufferMilliseconds));
        return true;
    }

    private static RemoteServerPerformanceProfileHash ComputeHash(
        RemoteServerPerformanceProfile value)
    {
        Span<byte> canonical = stackalloc byte[8 + 2 + 4 + 2 * 9];
        BinaryPrimitives.WriteUInt64LittleEndian(canonical, value.Revision);
        BinaryPrimitives.WriteUInt16LittleEndian(canonical[8..], value.SimulationFrequencyHz);
        BinaryPrimitives.WriteUInt32LittleEndian(
            canonical[10..], value.MaximumNetworkServiceIntervalMicroseconds);
        BinaryPrimitives.WriteUInt16LittleEndian(canonical[14..], value.MaximumInputHz);
        BinaryPrimitives.WriteUInt16LittleEndian(canonical[16..], value.RecommendedInputHz);
        BinaryPrimitives.WriteUInt16LittleEndian(canonical[18..], value.InputBurstCapacity);
        BinaryPrimitives.WriteUInt16LittleEndian(canonical[20..], value.InputBurstWindowMilliseconds);
        BinaryPrimitives.WriteUInt16LittleEndian(canonical[22..], value.ExpectedOwnerStateHz);
        BinaryPrimitives.WriteUInt16LittleEndian(canonical[24..], value.ExpectedImmediatePublicHz);
        BinaryPrimitives.WriteUInt16LittleEndian(canonical[26..], value.ExpectedNearPublicHz);
        BinaryPrimitives.WriteUInt16LittleEndian(canonical[28..], value.ExpectedFarPublicHz);
        BinaryPrimitives.WriteUInt16LittleEndian(canonical[30..], value.InterpolationBufferMilliseconds);
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(canonical, digest);
        return RemoteServerPerformanceProfileHash.FromBytes(digest);
    }
}

[NetworkMessage(146, "ultrasim.game.server-performance-profile", 1, NetworkLane.Control)]
public partial struct RemoteServerPerformanceProfileMessage
{
    public ulong ApplicationSessionEpoch;
    public ulong ProfileRevision;
    public ulong ProfileHash0;
    public ulong ProfileHash1;
    public ulong ProfileHash2;
    public ulong ProfileHash3;
    public ushort SimulationFrequencyHz;
    public uint MaximumNetworkServiceIntervalMicroseconds;
    public ushort MaximumInputHz;
    public ushort RecommendedInputHz;
    public ushort InputBurstCapacity;
    public ushort InputBurstWindowMilliseconds;
    public ushort ExpectedOwnerStateHz;
    public ushort ExpectedImmediatePublicHz;
    public ushort ExpectedNearPublicHz;
    public ushort ExpectedFarPublicHz;
    public ushort InterpolationBufferMilliseconds;
}

[NetworkMessage(147, "ultrasim.game.server-performance-profile-applied", 1, NetworkLane.Control)]
public partial struct RemoteServerPerformanceProfileAppliedMessage
{
    public ulong ApplicationSessionEpoch;
    public ulong ProfileRevision;
    public ulong ProfileHash0;
    public ulong ProfileHash1;
    public ulong ProfileHash2;
    public ulong ProfileHash3;
    public RemoteServerPerformanceApplyStatus Status;
}

public static partial class UltraSimGameProtocol
{
    public static RemoteServerPerformanceProfileMessage CreateServerPerformanceProfile(
        ulong applicationSessionEpoch,
        RemoteServerPerformanceProfile profile) => new()
    {
        ApplicationSessionEpoch = applicationSessionEpoch,
        ProfileRevision = profile.Revision,
        ProfileHash0 = profile.Hash.Part0,
        ProfileHash1 = profile.Hash.Part1,
        ProfileHash2 = profile.Hash.Part2,
        ProfileHash3 = profile.Hash.Part3,
        SimulationFrequencyHz = profile.SimulationFrequencyHz,
        MaximumNetworkServiceIntervalMicroseconds =
            profile.MaximumNetworkServiceIntervalMicroseconds,
        MaximumInputHz = profile.MaximumInputHz,
        RecommendedInputHz = profile.RecommendedInputHz,
        InputBurstCapacity = profile.InputBurstCapacity,
        InputBurstWindowMilliseconds = profile.InputBurstWindowMilliseconds,
        ExpectedOwnerStateHz = profile.ExpectedOwnerStateHz,
        ExpectedImmediatePublicHz = profile.ExpectedImmediatePublicHz,
        ExpectedNearPublicHz = profile.ExpectedNearPublicHz,
        ExpectedFarPublicHz = profile.ExpectedFarPublicHz,
        InterpolationBufferMilliseconds = profile.InterpolationBufferMilliseconds,
    };

    public static bool TryReadServerPerformanceProfile(
        in RemoteServerPerformanceProfileMessage message,
        out RemoteServerPerformanceProfile? profile)
    {
        profile = null;
        return message.ApplicationSessionEpoch != 0 &&
            message.MaximumNetworkServiceIntervalMicroseconds <= int.MaxValue &&
            RemoteServerPerformanceProfile.TryCreate(
                message.ProfileRevision,
                message.SimulationFrequencyHz,
                (int)message.MaximumNetworkServiceIntervalMicroseconds,
                message.MaximumInputHz,
                message.RecommendedInputHz,
                message.InputBurstCapacity,
                message.InputBurstWindowMilliseconds,
                message.ExpectedOwnerStateHz,
                message.ExpectedImmediatePublicHz,
                message.ExpectedNearPublicHz,
                message.ExpectedFarPublicHz,
                message.InterpolationBufferMilliseconds,
                out profile) &&
            profile!.Hash == new RemoteServerPerformanceProfileHash(
                message.ProfileHash0, message.ProfileHash1,
                message.ProfileHash2, message.ProfileHash3);
    }

    public static RemoteServerPerformanceProfileAppliedMessage
        CreateServerPerformanceProfileApplied(
            ulong applicationSessionEpoch,
            RemoteServerPerformanceProfile profile,
            RemoteServerPerformanceApplyStatus status) => new()
        {
            ApplicationSessionEpoch = applicationSessionEpoch,
            ProfileRevision = profile.Revision,
            ProfileHash0 = profile.Hash.Part0,
            ProfileHash1 = profile.Hash.Part1,
            ProfileHash2 = profile.Hash.Part2,
            ProfileHash3 = profile.Hash.Part3,
            Status = status,
        };

    public static bool IsValidServerPerformanceProfileApplied(
        in RemoteServerPerformanceProfileAppliedMessage message) =>
        message.ApplicationSessionEpoch != 0 && message.ProfileRevision != 0 &&
        (message.ProfileHash0 != 0 || message.ProfileHash1 != 0 ||
         message.ProfileHash2 != 0 || message.ProfileHash3 != 0) &&
        message.Status is RemoteServerPerformanceApplyStatus.Applied or
            RemoteServerPerformanceApplyStatus.Rejected;
}

using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace UltraSim.Network;

/// <summary>Monotonic ownership generation of one authenticated UDP path.</summary>
[StructLayout(LayoutKind.Sequential, Size = 4)]
public readonly struct HostedPathGeneration : IEquatable<HostedPathGeneration>,
    IComparable<HostedPathGeneration>
{
    public uint Value { get; }
    public bool IsValid => Value != 0;

    public HostedPathGeneration(uint value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(value);
        Value = value;
    }

    public int CompareTo(HostedPathGeneration other) => Value.CompareTo(other.Value);
    public bool Equals(HostedPathGeneration other) => Value == other.Value;
    public override bool Equals(object? obj) =>
        obj is HostedPathGeneration other && Equals(other);
    public override int GetHashCode() => unchecked((int)Value);
    public override string ToString() => IsValid ? Value.ToString() : "invalid";
    public static bool operator ==(HostedPathGeneration left, HostedPathGeneration right) =>
        left.Equals(right);
    public static bool operator !=(HostedPathGeneration left, HostedPathGeneration right) =>
        !left.Equals(right);
}

/// <summary>Current client-side phase of one optional path ownership transaction.</summary>
public enum HostedPathRebindState : byte
{
    None = 0,
    Requesting = 1,
    Proving = 2,
}

internal enum HostedPathDatagramKind : byte
{
    Request = 7,
    Challenge = 8,
    Proof = 9,
    Commit = 10,
}

/// <summary>
/// Canonical authenticated path-control flights. These are transport-lifecycle
/// messages, not secure application messages and not Game protocol messages.
/// </summary>
internal static class HostedUdpPathRebindCodec
{
    internal const int NonceBytes = 32;
    internal const int ChallengeBytes = 32;
    internal const int PathBindingBytes = 7;
    internal const int AuthenticationBytes = 32;
    internal const int KeyBytes = 32;
    internal const int RequestBytes =
        1 + 2 + 8 + 4 + 4 + 8 + NonceBytes + AuthenticationBytes;
    internal const int PathFlightBytes =
        1 + 2 + 8 + 4 + 4 + 8 + 4 + NonceBytes + ChallengeBytes +
        PathBindingBytes + AuthenticationBytes;

    internal static void DeriveKey(
        ReadOnlySpan<byte> master,
        NetworkProtocolManifest manifest,
        ConnectionId connection,
        SecurityEpoch epoch,
        Span<byte> destination)
    {
        if (destination.Length < KeyBytes)
            throw new ArgumentException("The hosted path key destination is too small.",
                nameof(destination));
        Span<byte> context = stackalloc byte[2 + 8 + 4 + 4 + 4 + 24];
        var writer = new NetworkPacketWriter(context);
        if (!writer.TryWriteUInt16(manifest.Version) ||
            !writer.TryWriteUInt64(manifest.Fingerprint) ||
            !writer.TryWriteUInt32(connection.Slot) ||
            !writer.TryWriteUInt32(connection.Generation) ||
            !writer.TryWriteUInt32(epoch.Value) ||
            !writer.TryWriteBytes("UltraSim.HostedPath.v1"u8))
            throw new InvalidOperationException("The hosted path key context is invalid.");
        HMACSHA256.HashData(master, writer.WrittenSpan, destination[..KeyBytes]);
        CryptographicOperations.ZeroMemory(context);
    }

    internal static int WriteRequest(
        Span<byte> destination,
        NetworkProtocolManifest manifest,
        ConnectionId connection,
        ulong attempt,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> key)
    {
        if (!connection.IsValid || attempt == 0 || nonce.Length != NonceBytes ||
            key.Length != KeyBytes || destination.Length < RequestBytes)
            throw new ArgumentException("The hosted path request is invalid.");
        var writer = new NetworkPacketWriter(destination[..RequestBytes]);
        if (!WritePrefix(ref writer, HostedPathDatagramKind.Request, manifest, connection) ||
            !writer.TryWriteUInt64(attempt) || !writer.TryWriteBytes(nonce))
            throw new InvalidOperationException("The hosted path request buffer is invalid.");
        WriteAuthentication(destination[..RequestBytes], writer.WrittenCount, key);
        return RequestBytes;
    }

    internal static bool TryReadRequest(
        ReadOnlySpan<byte> source,
        NetworkProtocolManifest manifest,
        ConnectionId expectedConnection,
        ReadOnlySpan<byte> key,
        out ulong attempt,
        out ReadOnlySpan<byte> nonce)
    {
        attempt = 0;
        nonce = default;
        if (source.Length != RequestBytes || key.Length != KeyBytes ||
            !AuthenticationMatches(source, key)) return false;
        var reader = new NetworkPacketReader(source[..^AuthenticationBytes]);
        if (!ReadPrefix(ref reader, HostedPathDatagramKind.Request, manifest,
                expectedConnection) ||
            !reader.TryReadUInt64(out attempt) || attempt == 0 ||
            !reader.TryReadBytes(NonceBytes, out nonce) || !reader.IsComplete)
        {
            attempt = 0;
            nonce = default;
            return false;
        }
        return true;
    }

    internal static int WritePathFlight(
        Span<byte> destination,
        HostedPathDatagramKind kind,
        NetworkProtocolManifest manifest,
        ConnectionId connection,
        ulong attempt,
        HostedPathGeneration generation,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> challenge,
        ReadOnlySpan<byte> pathBinding,
        ReadOnlySpan<byte> key)
    {
        if (kind is not (HostedPathDatagramKind.Challenge or
                HostedPathDatagramKind.Proof or HostedPathDatagramKind.Commit) ||
            !connection.IsValid || attempt == 0 || !generation.IsValid ||
            nonce.Length != NonceBytes || challenge.Length != ChallengeBytes ||
            pathBinding.Length != PathBindingBytes || key.Length != KeyBytes ||
            destination.Length < PathFlightBytes)
            throw new ArgumentException("The hosted path flight is invalid.");
        var writer = new NetworkPacketWriter(destination[..PathFlightBytes]);
        if (!WritePrefix(ref writer, kind, manifest, connection) ||
            !writer.TryWriteUInt64(attempt) ||
            !writer.TryWriteUInt32(generation.Value) ||
            !writer.TryWriteBytes(nonce) ||
            !writer.TryWriteBytes(challenge) ||
            !writer.TryWriteBytes(pathBinding))
            throw new InvalidOperationException("The hosted path flight buffer is invalid.");
        WriteAuthentication(destination[..PathFlightBytes], writer.WrittenCount, key);
        return PathFlightBytes;
    }

    internal static bool TryReadPathFlight(
        ReadOnlySpan<byte> source,
        HostedPathDatagramKind kind,
        NetworkProtocolManifest manifest,
        ConnectionId expectedConnection,
        ReadOnlySpan<byte> key,
        out ulong attempt,
        out HostedPathGeneration generation,
        out ReadOnlySpan<byte> nonce,
        out ReadOnlySpan<byte> challenge,
        out ReadOnlySpan<byte> pathBinding)
    {
        attempt = 0;
        generation = default;
        nonce = challenge = pathBinding = default;
        if (source.Length != PathFlightBytes || key.Length != KeyBytes ||
            !AuthenticationMatches(source, key)) return false;
        var reader = new NetworkPacketReader(source[..^AuthenticationBytes]);
        if (!ReadPrefix(ref reader, kind, manifest, expectedConnection) ||
            !reader.TryReadUInt64(out attempt) || attempt == 0 ||
            !reader.TryReadUInt32(out uint generationValue) || generationValue == 0 ||
            !reader.TryReadBytes(NonceBytes, out nonce) ||
            !reader.TryReadBytes(ChallengeBytes, out challenge) ||
            !reader.TryReadBytes(PathBindingBytes, out pathBinding) ||
            !reader.IsComplete)
        {
            attempt = 0;
            generation = default;
            nonce = challenge = pathBinding = default;
            return false;
        }
        generation = new(generationValue);
        return true;
    }

    internal static bool TryPeekConnection(
        ReadOnlySpan<byte> source,
        HostedPathDatagramKind kind,
        NetworkProtocolManifest manifest,
        out ConnectionId connection)
    {
        connection = default;
        int expected = kind == HostedPathDatagramKind.Request
            ? RequestBytes
            : PathFlightBytes;
        if (source.Length != expected) return false;
        var reader = new NetworkPacketReader(source);
        return ReadPrefix(ref reader, kind, manifest, default, out connection);
    }

    private static bool WritePrefix(
        ref NetworkPacketWriter writer,
        HostedPathDatagramKind kind,
        NetworkProtocolManifest manifest,
        ConnectionId connection) =>
        writer.TryWriteByte((byte)kind) &&
        writer.TryWriteUInt16(manifest.Version) &&
        writer.TryWriteUInt64(manifest.Fingerprint) &&
        writer.TryWriteUInt32(connection.Slot) &&
        writer.TryWriteUInt32(connection.Generation);

    private static bool ReadPrefix(
        ref NetworkPacketReader reader,
        HostedPathDatagramKind kind,
        NetworkProtocolManifest manifest,
        ConnectionId expectedConnection) =>
        ReadPrefix(ref reader, kind, manifest, expectedConnection, out _);

    private static bool ReadPrefix(
        ref NetworkPacketReader reader,
        HostedPathDatagramKind kind,
        NetworkProtocolManifest manifest,
        ConnectionId expectedConnection,
        out ConnectionId connection)
    {
        connection = default;
        if (!reader.TryReadByte(out byte actualKind) || actualKind != (byte)kind ||
            !reader.TryReadUInt16(out ushort version) || version != manifest.Version ||
            !reader.TryReadUInt64(out ulong fingerprint) ||
                fingerprint != manifest.Fingerprint ||
            !reader.TryReadUInt32(out uint slot) ||
            !reader.TryReadUInt32(out uint generation) || generation == 0)
            return false;
        connection = new(slot, generation);
        return !expectedConnection.IsValid || connection == expectedConnection;
    }

    private static void WriteAuthentication(
        Span<byte> destination,
        int prefixLength,
        ReadOnlySpan<byte> key)
    {
        if (prefixLength != destination.Length - AuthenticationBytes)
            throw new InvalidOperationException("The hosted path flight length is invalid.");
        HMACSHA256.HashData(
            key, destination[..prefixLength], destination[prefixLength..]);
    }

    private static bool AuthenticationMatches(
        ReadOnlySpan<byte> source,
        ReadOnlySpan<byte> key)
    {
        Span<byte> expected = stackalloc byte[AuthenticationBytes];
        HMACSHA256.HashData(key, source[..^AuthenticationBytes], expected);
        bool matches = CryptographicOperations.FixedTimeEquals(
            expected, source[^AuthenticationBytes..]);
        CryptographicOperations.ZeroMemory(expected);
        return matches;
    }
}

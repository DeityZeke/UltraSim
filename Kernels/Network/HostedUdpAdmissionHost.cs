using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Security.Cryptography;

namespace UltraSim.Network;

/// <summary>Bounded pre-session admission policy for one externally reachable UDP listener.</summary>
public readonly record struct HostedUdpAdmissionLimits(
    int MaximumValidatedCandidates = 64,
    int MaximumTrackedSourceAddresses = 1_024,
    int MaximumChallengesPerAddressPerBucket = 8,
    int MaximumValidationsPerAddressPerBucket = 4,
    int ValidatedCandidateTimeoutMilliseconds = 5_000)
{
    public static HostedUdpAdmissionLimits Default => new();

    internal void Validate(UdpProtocolLimits udp)
    {
        udp.Validate();
        if (MaximumValidatedCandidates is <= 0 or > 65_536)
            throw new ArgumentOutOfRangeException(nameof(MaximumValidatedCandidates));
        if (MaximumTrackedSourceAddresses is <= 0 or > 1_048_576)
            throw new ArgumentOutOfRangeException(nameof(MaximumTrackedSourceAddresses));
        if (MaximumChallengesPerAddressPerBucket is <= 0 or > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(MaximumChallengesPerAddressPerBucket));
        if (MaximumValidationsPerAddressPerBucket is <= 0 or > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(MaximumValidationsPerAddressPerBucket));
        if (ValidatedCandidateTimeoutMilliseconds <= 0 ||
            ValidatedCandidateTimeoutMilliseconds > udp.IdleTimeoutMilliseconds)
            throw new ArgumentOutOfRangeException(nameof(ValidatedCandidateTimeoutMilliseconds));
        _ = RateTableCapacity;
        _ = EstimateReservedBytes();
    }

    internal int RateTableCapacity => checked((int)BitOperations.RoundUpToPowerOf2(
        checked((uint)MaximumTrackedSourceAddresses * 2U)));

    public long EstimateReservedBytes()
    {
        int rateCapacity = RateTableCapacity;
        return checked(
            32L +
            (long)rateCapacity * (sizeof(ulong) + sizeof(ulong) + sizeof(ushort) + sizeof(ushort)) +
            (long)MaximumValidatedCandidates *
                (sizeof(ulong) + sizeof(ulong) + sizeof(ushort) + sizeof(int) + sizeof(uint)) +
            (long)MaximumValidatedCandidates * ProtocolLoopbackHandshake.NonceBytes +
            (long)MaximumValidatedCandidates * ProtocolLoopbackHandshake.MaximumClientPublicKeyBytes);
    }
}

/// <summary>Cold evidence for bounded externally sourced UDP admission.</summary>
public readonly record struct HostedUdpAdmissionSnapshot(
    int ValidatedCandidates,
    int ValidatedCandidateCapacity,
    ulong UnknownDatagrams,
    ulong UnknownBytes,
    ulong ClientHellos,
    ulong CookieChallengesSent,
    ulong CookieChallengeBytes,
    ulong CookieValidations,
    ulong InvalidDatagrams,
    ulong RateLimitedDatagrams,
    ulong RateTableCapacityDrops,
    ulong AmplificationDrops,
    ulong CandidateCapacityDrops,
    ulong CandidateRefreshes,
    ulong CandidateTimeouts,
    ulong SendCapacityDrops,
    long ReservedBytes,
    SharedUdpListenerSnapshot Listener);

/// <summary>
/// Owner-thread server endpoint for H0.0/H0.1 external UDP admission.
/// </summary>
/// <remarks>
/// Unknown IPv4 paths receive only a stateless, listener-lifetime cookie challenge.
/// No endpoint, connection, ECDH state, protocol peer, or game session is allocated
/// before a valid current/previous-bucket cookie is returned. Validated candidates
/// occupy one bounded private row and expire independently. H0.2 will promote those
/// candidates into the existing authenticated protocol; this checkpoint deliberately
/// exposes no self-trusted client construction and no Game meaning.
/// </remarks>
public sealed class HostedUdpAdmissionHost : IDisposable, ISharedUdpUnknownDatagramHandler
{
    private const ulong EmptyRateKey = 0;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly UdpProtocolLimits _udpLimits;
    private readonly HostedUdpAdmissionLimits _admissionLimits;
    private readonly NetworkProtocolManifest _manifest;
    private readonly INetworkMonotonicClock _clock;
    private readonly SharedUdpServerListener _listener;
    private readonly byte[] _serverPublicKey;
    private readonly byte[] _cookieKey;
    private readonly ulong[] _rateKeys;
    private readonly ulong[] _rateBuckets;
    private readonly ushort[] _rateChallenges;
    private readonly ushort[] _rateValidations;
    private readonly ulong[] _candidatePaths;
    private readonly ulong[] _candidateExpirations;
    private readonly ushort[] _candidatePublicKeyLengths;
    private readonly uint[] _candidateGenerations;
    private readonly int[] _candidateFree;
    private readonly byte[] _candidateNonces;
    private readonly byte[] _candidatePublicKeys;
    private int _candidateCount;
    private int _candidateFreeCount;
    private ulong _unknownDatagrams;
    private ulong _unknownBytes;
    private ulong _clientHellos;
    private ulong _cookieChallengesSent;
    private ulong _cookieChallengeBytes;
    private ulong _cookieValidations;
    private ulong _invalidDatagrams;
    private ulong _rateLimitedDatagrams;
    private ulong _rateTableCapacityDrops;
    private ulong _amplificationDrops;
    private ulong _candidateCapacityDrops;
    private ulong _candidateRefreshes;
    private ulong _candidateTimeouts;
    private ulong _sendCapacityDrops;
    private ulong _lastMilliseconds;
    private bool _disposed;

    public HostedUdpAdmissionHost(
        IPEndPoint bindEndPoint,
        NetworkServerIdentity serverIdentity,
        UdpProtocolLimits udpLimits,
        SharedUdpProtocolLimits sharedLimits,
        HostedUdpAdmissionLimits admissionLimits,
        NetworkProtocolManifest? manifest = null)
        : this(bindEndPoint, serverIdentity, udpLimits, sharedLimits, admissionLimits,
            StopwatchNetworkMonotonicClock.Instance, manifest)
    {
    }

    internal HostedUdpAdmissionHost(
        IPEndPoint bindEndPoint,
        NetworkServerIdentity serverIdentity,
        UdpProtocolLimits udpLimits,
        SharedUdpProtocolLimits sharedLimits,
        HostedUdpAdmissionLimits admissionLimits,
        INetworkMonotonicClock clock,
        NetworkProtocolManifest? manifest = null)
    {
        ArgumentNullException.ThrowIfNull(bindEndPoint);
        ArgumentNullException.ThrowIfNull(serverIdentity);
        ArgumentNullException.ThrowIfNull(clock);
        if (bindEndPoint.AddressFamily != AddressFamily.InterNetwork)
            throw new ArgumentException("Hosted H0 currently admits IPv4 endpoints only.",
                nameof(bindEndPoint));
        sharedLimits.Validate(udpLimits);
        admissionLimits.Validate(udpLimits);
        _udpLimits = udpLimits;
        _admissionLimits = admissionLimits;
        _manifest = manifest ?? CoreNetworkProtocol.Manifest;
        _clock = clock;
        _lastMilliseconds = clock.Milliseconds;
        _serverPublicKey = serverIdentity.PublicKey.ToArray();
        _cookieKey = RandomNumberGenerator.GetBytes(32);
        int rateCapacity = admissionLimits.RateTableCapacity;
        _rateKeys = new ulong[rateCapacity];
        _rateBuckets = new ulong[rateCapacity];
        _rateChallenges = new ushort[rateCapacity];
        _rateValidations = new ushort[rateCapacity];
        int candidates = admissionLimits.MaximumValidatedCandidates;
        _candidatePaths = new ulong[candidates];
        _candidateExpirations = new ulong[candidates];
        _candidatePublicKeyLengths = new ushort[candidates];
        _candidateGenerations = new uint[candidates];
        _candidateFree = new int[candidates];
        _candidateNonces = new byte[checked(candidates * ProtocolLoopbackHandshake.NonceBytes)];
        _candidatePublicKeys = new byte[checked(
            candidates * ProtocolLoopbackHandshake.MaximumClientPublicKeyBytes)];
        for (int index = 0; index < candidates; index++)
            _candidateFree[index] = candidates - 1 - index;
        _candidateFreeCount = candidates;
        _listener = new SharedUdpServerListener(
            udpLimits, sharedLimits, bindEndPoint, this);
    }

    /// <summary>The kernel-selected IPv4 listener endpoint.</summary>
    public IPEndPoint LocalEndPoint
    {
        get
        {
            EnsureOperational();
            return new IPEndPoint(
                _listener.LocalEndPoint.Address,
                _listener.LocalEndPoint.Port);
        }
    }

    /// <summary>Copies the deployment-owned public key clients must pin out of band.</summary>
    public bool TryCopyServerPublicKey(Span<byte> destination, out int written)
    {
        EnsureOperational();
        written = 0;
        if (destination.Length < _serverPublicKey.Length) return false;
        _serverPublicKey.CopyTo(destination);
        written = _serverPublicKey.Length;
        return true;
    }

    /// <summary>Runs one bounded admission receive and candidate-expiry pass.</summary>
    public void Pump()
    {
        EnsureOperational();
        ulong now = GetMonotonicMilliseconds();
        SweepExpiredCandidates(now);
        _listener.PumpInbound();
    }

    public HostedUdpAdmissionSnapshot GetSnapshot()
    {
        EnsureOperational();
        return new(
            _candidateCount,
            _candidatePaths.Length,
            _unknownDatagrams,
            _unknownBytes,
            _clientHellos,
            _cookieChallengesSent,
            _cookieChallengeBytes,
            _cookieValidations,
            _invalidDatagrams,
            _rateLimitedDatagrams,
            _rateTableCapacityDrops,
            _amplificationDrops,
            _candidateCapacityDrops,
            _candidateRefreshes,
            _candidateTimeouts,
            _sendCapacityDrops,
            checked(_listener.ReservedBytes + _serverPublicKey.Length +
                _admissionLimits.EstimateReservedBytes()),
            _listener.GetSnapshot());
    }

    void ISharedUdpUnknownDatagramHandler.HandleUnknownDatagram(
        SharedUdpServerListener listener,
        ReadOnlySpan<byte> datagram,
        ReadOnlySpan<byte> remoteAddress)
    {
        if (!ReferenceEquals(listener, _listener) || remoteAddress.Length != 16)
            throw new InvalidOperationException("The hosted admission listener route is invalid.");
        _unknownDatagrams = SaturatingIncrement(_unknownDatagrams);
        _unknownBytes = SaturatingAdd(_unknownBytes, checked((ulong)datagram.Length));
        ulong now = GetMonotonicMilliseconds();
        ulong bucket = now / checked((ulong)_udpLimits.CookieBucketMilliseconds);
        ulong addressKey = PackAddressKey(remoteAddress);

        if (ProtocolLoopbackHandshake.TryReadClientHello(
                datagram, _manifest, out ReadOnlySpan<byte> nonce,
                out ReadOnlySpan<byte> publicKey))
        {
            _clientHellos = SaturatingIncrement(_clientHellos);
            if (!TryConsumeRate(addressKey, bucket, challenge: true)) return;
            Span<byte> cookie = stackalloc byte[ProtocolLoopbackHandshake.CookieBytes];
            Span<byte> pathBinding = stackalloc byte[7];
            WritePathBinding(remoteAddress, pathBinding);
            ProtocolLoopbackHandshake.ComputeCookie(
                _cookieKey, nonce, publicKey, pathBinding, bucket, cookie);
            Span<byte> challenge = stackalloc byte[
                1 + ProtocolLoopbackHandshake.NonceBytes + ProtocolLoopbackHandshake.CookieBytes];
            int length = ProtocolLoopbackHandshake.WriteCookieChallenge(
                challenge, nonce, cookie);
            if (length > datagram.Length)
            {
                _amplificationDrops = SaturatingIncrement(_amplificationDrops);
                return;
            }
            if (listener.TrySendUnknown(challenge[..length], remoteAddress) !=
                DatagramSendStatus.Accepted)
            {
                _sendCapacityDrops = SaturatingIncrement(_sendCapacityDrops);
                return;
            }
            _cookieChallengesSent = SaturatingIncrement(_cookieChallengesSent);
            _cookieChallengeBytes = SaturatingAdd(
                _cookieChallengeBytes, checked((ulong)length));
            return;
        }

        if (ProtocolLoopbackHandshake.TryReadClientKeyExchange(
                datagram, _manifest, out nonce, out publicKey,
                out ReadOnlySpan<byte> suppliedCookie))
        {
            if (!TryConsumeRate(addressKey, bucket, challenge: false)) return;
            Span<byte> expected = stackalloc byte[ProtocolLoopbackHandshake.CookieBytes];
            Span<byte> pathBinding = stackalloc byte[7];
            WritePathBinding(remoteAddress, pathBinding);
            ProtocolLoopbackHandshake.ComputeCookie(
                _cookieKey, nonce, publicKey, pathBinding, bucket, expected);
            bool valid = CryptographicOperations.FixedTimeEquals(expected, suppliedCookie);
            if (!valid && bucket != 0)
            {
                ProtocolLoopbackHandshake.ComputeCookie(
                    _cookieKey, nonce, publicKey, pathBinding, bucket - 1, expected);
                valid = CryptographicOperations.FixedTimeEquals(expected, suppliedCookie);
            }
            if (!valid)
            {
                _invalidDatagrams = SaturatingIncrement(_invalidDatagrams);
                return;
            }
            _cookieValidations = SaturatingIncrement(_cookieValidations);
            AdmitCandidate(PackPath(remoteAddress), nonce, publicKey, now);
            return;
        }

        _invalidDatagrams = SaturatingIncrement(_invalidDatagrams);
    }

    public void Dispose()
    {
        EnsureOwnerThread();
        if (_disposed) return;
        _listener.Dispose();
        CryptographicOperations.ZeroMemory(_cookieKey);
        CryptographicOperations.ZeroMemory(_serverPublicKey);
        CryptographicOperations.ZeroMemory(_candidateNonces);
        CryptographicOperations.ZeroMemory(_candidatePublicKeys);
        Array.Clear(_candidatePaths);
        Array.Clear(_candidateExpirations);
        Array.Clear(_candidatePublicKeyLengths);
        Array.Clear(_candidateGenerations);
        Array.Clear(_rateKeys);
        Array.Clear(_rateBuckets);
        Array.Clear(_rateChallenges);
        Array.Clear(_rateValidations);
        _disposed = true;
    }

    private bool TryConsumeRate(ulong addressKey, ulong bucket, bool challenge)
    {
        int mask = _rateKeys.Length - 1;
        int index = Hash(addressKey) & mask;
        int stale = -1;
        for (int probe = 0; probe < _rateKeys.Length; probe++)
        {
            ulong current = _rateKeys[index];
            if (current == addressKey)
            {
                if (_rateBuckets[index] != bucket)
                {
                    _rateBuckets[index] = bucket;
                    _rateChallenges[index] = 0;
                    _rateValidations[index] = 0;
                }
                return IncrementRate(index, challenge);
            }
            if (current == EmptyRateKey)
            {
                int target = stale >= 0 ? stale : index;
                _rateKeys[target] = addressKey;
                _rateBuckets[target] = bucket;
                _rateChallenges[target] = 0;
                _rateValidations[target] = 0;
                return IncrementRate(target, challenge);
            }
            if (stale < 0 && IsRateRowStale(_rateBuckets[index], bucket)) stale = index;
            index = (index + 1) & mask;
        }
        if (stale >= 0)
        {
            _rateKeys[stale] = addressKey;
            _rateBuckets[stale] = bucket;
            _rateChallenges[stale] = 0;
            _rateValidations[stale] = 0;
            return IncrementRate(stale, challenge);
        }
        _rateTableCapacityDrops = SaturatingIncrement(_rateTableCapacityDrops);
        return false;
    }

    private bool IncrementRate(int index, bool challenge)
    {
        if (challenge)
        {
            if (_rateChallenges[index] >= _admissionLimits.MaximumChallengesPerAddressPerBucket)
            {
                _rateLimitedDatagrams = SaturatingIncrement(_rateLimitedDatagrams);
                return false;
            }
            _rateChallenges[index]++;
            return true;
        }
        if (_rateValidations[index] >= _admissionLimits.MaximumValidationsPerAddressPerBucket)
        {
            _rateLimitedDatagrams = SaturatingIncrement(_rateLimitedDatagrams);
            return false;
        }
        _rateValidations[index]++;
        return true;
    }

    private void AdmitCandidate(
        ulong path,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> publicKey,
        ulong now)
    {
        int existing = FindCandidate(path);
        if (existing >= 0)
        {
            StoreCandidate(existing, path, nonce, publicKey, now);
            _candidateRefreshes = SaturatingIncrement(_candidateRefreshes);
            return;
        }
        if (_candidateFreeCount == 0)
        {
            _candidateCapacityDrops = SaturatingIncrement(_candidateCapacityDrops);
            return;
        }
        int slot = _candidateFree[--_candidateFreeCount];
        if (_candidateGenerations[slot] == uint.MaxValue)
        {
            _candidateCapacityDrops = SaturatingIncrement(_candidateCapacityDrops);
            return;
        }
        _candidateGenerations[slot]++;
        StoreCandidate(slot, path, nonce, publicKey, now);
        _candidateCount++;
    }

    private void StoreCandidate(
        int slot,
        ulong path,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> publicKey,
        ulong now)
    {
        if (nonce.Length != ProtocolLoopbackHandshake.NonceBytes ||
            publicKey.Length > ProtocolLoopbackHandshake.MaximumClientPublicKeyBytes)
            throw new InvalidOperationException("A validated hosted candidate is malformed.");
        int nonceOffset = checked(slot * ProtocolLoopbackHandshake.NonceBytes);
        int keyOffset = checked(slot * ProtocolLoopbackHandshake.MaximumClientPublicKeyBytes);
        _candidateNonces.AsSpan(nonceOffset, ProtocolLoopbackHandshake.NonceBytes).Clear();
        _candidatePublicKeys.AsSpan(
            keyOffset, ProtocolLoopbackHandshake.MaximumClientPublicKeyBytes).Clear();
        nonce.CopyTo(_candidateNonces.AsSpan(nonceOffset));
        publicKey.CopyTo(_candidatePublicKeys.AsSpan(keyOffset));
        _candidatePaths[slot] = path;
        _candidatePublicKeyLengths[slot] = checked((ushort)publicKey.Length);
        _candidateExpirations[slot] = checked(
            now + (ulong)_admissionLimits.ValidatedCandidateTimeoutMilliseconds);
    }

    private void SweepExpiredCandidates(ulong now)
    {
        for (int slot = 0; slot < _candidatePaths.Length; slot++)
        {
            if (_candidatePaths[slot] == 0 || now < _candidateExpirations[slot]) continue;
            ReleaseCandidate(slot);
            _candidateTimeouts = SaturatingIncrement(_candidateTimeouts);
        }
    }

    private void ReleaseCandidate(int slot)
    {
        int nonceOffset = checked(slot * ProtocolLoopbackHandshake.NonceBytes);
        int keyOffset = checked(slot * ProtocolLoopbackHandshake.MaximumClientPublicKeyBytes);
        CryptographicOperations.ZeroMemory(
            _candidateNonces.AsSpan(nonceOffset, ProtocolLoopbackHandshake.NonceBytes));
        CryptographicOperations.ZeroMemory(_candidatePublicKeys.AsSpan(
            keyOffset, ProtocolLoopbackHandshake.MaximumClientPublicKeyBytes));
        _candidatePaths[slot] = 0;
        _candidateExpirations[slot] = 0;
        _candidatePublicKeyLengths[slot] = 0;
        _candidateFree[_candidateFreeCount++] = slot;
        _candidateCount--;
    }

    private int FindCandidate(ulong path)
    {
        for (int slot = 0; slot < _candidatePaths.Length; slot++)
            if (_candidatePaths[slot] == path) return slot;
        return -1;
    }

    private ulong GetMonotonicMilliseconds()
    {
        ulong now = _clock.Milliseconds;
        if (now < _lastMilliseconds)
            throw new InvalidOperationException("The hosted admission monotonic clock moved backward.");
        _lastMilliseconds = now;
        return now;
    }

    private static bool IsRateRowStale(ulong rowBucket, ulong currentBucket) =>
        currentBucket > rowBucket && currentBucket - rowBucket > 1;

    private static int Hash(ulong key)
    {
        unchecked
        {
            key ^= key >> 33;
            key *= 0xff51afd7ed558ccdUL;
            key ^= key >> 33;
            return (int)key;
        }
    }

    private static ulong PackAddressKey(ReadOnlySpan<byte> address)
    {
        uint ipv4 = ((uint)address[4] << 24) | ((uint)address[5] << 16) |
            ((uint)address[6] << 8) | address[7];
        return (ulong)ipv4 + 1UL;
    }

    private static ulong PackPath(ReadOnlySpan<byte> address)
    {
        uint ipv4 = ((uint)address[4] << 24) | ((uint)address[5] << 16) |
            ((uint)address[6] << 8) | address[7];
        ushort port = (ushort)((address[2] << 8) | address[3]);
        return (((ulong)ipv4 << 16) | port) + 1UL;
    }

    private static void WritePathBinding(
        ReadOnlySpan<byte> address,
        Span<byte> destination)
    {
        if (destination.Length < 7) throw new ArgumentException(
            "The hosted path binding destination is too small.", nameof(destination));
        destination[0] = 4;
        address.Slice(4, 4).CopyTo(destination[1..]);
        destination[5] = address[2];
        destination[6] = address[3];
    }

    private static ulong SaturatingIncrement(ulong value) =>
        value == ulong.MaxValue ? value : value + 1;

    private static ulong SaturatingAdd(ulong left, ulong right) =>
        ulong.MaxValue - left < right ? ulong.MaxValue : left + right;

    private void EnsureOperational()
    {
        EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The hosted UDP admission host is owner-thread-bound.");
    }
}

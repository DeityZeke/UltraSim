using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace UltraSim.Network;

/// <summary>Generation-fenced identity for one bounded hosted admission row.</summary>
[StructLayout(LayoutKind.Sequential, Size = 8)]
public readonly struct HostedCandidateId : IEquatable<HostedCandidateId>
{
    public uint Slot { get; }
    public uint Generation { get; }
    public bool IsValid => Generation != 0;

    public HostedCandidateId(uint slot, uint generation)
    {
        ArgumentOutOfRangeException.ThrowIfZero(generation);
        Slot = slot;
        Generation = generation;
    }

    public bool Equals(HostedCandidateId other) =>
        Slot == other.Slot && Generation == other.Generation;
    public override bool Equals(object? obj) =>
        obj is HostedCandidateId other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Slot, Generation);
    public override string ToString() => IsValid ? $"{Slot}:{Generation}" : "invalid";
    public static bool operator ==(HostedCandidateId left, HostedCandidateId right) =>
        left.Equals(right);
    public static bool operator !=(HostedCandidateId left, HostedCandidateId right) =>
        !left.Equals(right);
}

/// <summary>Current immutable-credential phase of a hosted candidate.</summary>
public enum HostedCandidateState : byte
{
    Validated = 1,
    Promoting = 2,
}

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
                (sizeof(ulong) + sizeof(ulong) + sizeof(ushort) + sizeof(int) + sizeof(uint) +
                    sizeof(byte)) +
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
    int PromotingCandidates,
    int EstablishedSessions,
    ulong CandidateCredentialConflicts,
    ulong PromotionCapacityDrops,
    ulong PromotionFailures,
    ulong ServerKeyExchangeFlights,
    ulong EstablishedPromotions,
    ulong DuplicateClientFinishes,
    ulong ServerFinishFlights,
    long ReservedBytes,
    SharedUdpListenerSnapshot Listener,
    NetworkConnectionSnapshot Connections);

/// <summary>
/// Owner-thread server endpoint for H0.0-H0.2 external UDP admission and promotion.
/// </summary>
/// <remarks>
/// Unknown IPv4 paths receive only a stateless, listener-lifetime cookie challenge.
/// No endpoint, connection, ECDH state, protocol peer, or game session is allocated
/// before a valid current/previous-bucket cookie is returned. Validated candidates
/// occupy one bounded private row and expire independently. When constructed with
/// protocol limits, H0.2 promotes proof-complete candidates into the existing
/// authenticated protocol transactionally. It exposes no self-trusted client
/// construction and no Game meaning.
/// </remarks>
public sealed class HostedUdpAdmissionHost : IDisposable,
    ISharedUdpUnknownDatagramHandler,
    ISharedUdpKnownDatagramHandler
{
    private const ulong EmptyRateKey = 0;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly UdpProtocolLimits _udpLimits;
    private readonly HostedUdpAdmissionLimits _admissionLimits;
    private readonly NetworkProtocolLimits _protocolLimits;
    private readonly bool _promotionEnabled;
    private readonly NetworkProtocolManifest _manifest;
    private readonly INetworkMonotonicClock _clock;
    private readonly SharedUdpServerListener _listener;
    private readonly NetworkServerIdentity _serverIdentity;
    private readonly NetworkConnectionTable? _connections;
    private readonly HostedUdpProtocolSession?[] _sessions;
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
    private readonly byte[] _candidateStates;
    private readonly byte[] _candidateCookies;
    private readonly ConnectionId[] _candidateConnections;
    private readonly SecurityEpoch[] _candidateSecurityEpochs;
    private readonly ECDiffieHellman?[] _candidateServerKeys;
    private readonly ushort[] _candidateServerPublicKeyLengths;
    private readonly ushort[] _candidateTranscriptLengths;
    private readonly ushort[] _candidateServerFlightLengths;
    private readonly byte[] _candidateServerPublicKeys;
    private readonly byte[] _candidateTranscripts;
    private readonly byte[] _candidateMasterSecrets;
    private readonly byte[] _candidateServerFlights;
    private int _candidateCount;
    private int _promotingCandidateCount;
    private int _sessionCount;
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
    private ulong _candidateCredentialConflicts;
    private ulong _promotionCapacityDrops;
    private ulong _promotionFailures;
    private ulong _serverKeyExchangeFlights;
    private ulong _establishedPromotions;
    private ulong _duplicateClientFinishes;
    private ulong _serverFinishFlights;
    private ulong _lastMilliseconds;
    private bool _disposed;

    /// <summary>
    /// Creates the admission-only H0.1 evidence boundary. This overload never
    /// promotes a validated candidate; use the protocol-limits overload for H0.2.
    /// </summary>
    public HostedUdpAdmissionHost(
        IPEndPoint bindEndPoint,
        NetworkServerIdentity serverIdentity,
        UdpProtocolLimits udpLimits,
        SharedUdpProtocolLimits sharedLimits,
        HostedUdpAdmissionLimits admissionLimits,
        NetworkProtocolManifest? manifest = null)
        : this(bindEndPoint, serverIdentity, protocolLimits: null, udpLimits,
            sharedLimits, admissionLimits, StopwatchNetworkMonotonicClock.Instance, manifest)
    {
    }

    /// <summary>
    /// Creates an externally reachable host that promotes cookie-validated
    /// candidates into the existing secure protocol.
    /// </summary>
    public HostedUdpAdmissionHost(
        IPEndPoint bindEndPoint,
        NetworkServerIdentity serverIdentity,
        NetworkProtocolLimits protocolLimits,
        UdpProtocolLimits udpLimits,
        SharedUdpProtocolLimits sharedLimits,
        HostedUdpAdmissionLimits admissionLimits,
        NetworkProtocolManifest? manifest = null)
        : this(bindEndPoint, serverIdentity, protocolLimits, udpLimits,
            sharedLimits, admissionLimits, StopwatchNetworkMonotonicClock.Instance, manifest)
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
        : this(bindEndPoint, serverIdentity, protocolLimits: null, udpLimits,
            sharedLimits, admissionLimits, clock, manifest)
    {
    }

    internal HostedUdpAdmissionHost(
        IPEndPoint bindEndPoint,
        NetworkServerIdentity serverIdentity,
        NetworkProtocolLimits protocolLimits,
        UdpProtocolLimits udpLimits,
        SharedUdpProtocolLimits sharedLimits,
        HostedUdpAdmissionLimits admissionLimits,
        INetworkMonotonicClock clock,
        NetworkProtocolManifest? manifest = null)
        : this(bindEndPoint, serverIdentity, (NetworkProtocolLimits?)protocolLimits,
            udpLimits, sharedLimits, admissionLimits, clock, manifest)
    {
    }

    private HostedUdpAdmissionHost(
        IPEndPoint bindEndPoint,
        NetworkServerIdentity serverIdentity,
        NetworkProtocolLimits? protocolLimits,
        UdpProtocolLimits udpLimits,
        SharedUdpProtocolLimits sharedLimits,
        HostedUdpAdmissionLimits admissionLimits,
        INetworkMonotonicClock clock,
        NetworkProtocolManifest? manifest)
    {
        ArgumentNullException.ThrowIfNull(bindEndPoint);
        ArgumentNullException.ThrowIfNull(serverIdentity);
        ArgumentNullException.ThrowIfNull(clock);
        if (bindEndPoint.AddressFamily != AddressFamily.InterNetwork)
            throw new ArgumentException("Hosted H0 currently admits IPv4 endpoints only.",
                nameof(bindEndPoint));
        sharedLimits.Validate(udpLimits);
        admissionLimits.Validate(udpLimits);
        if (protocolLimits.HasValue)
            protocolLimits.Value.Validate(new DeterministicDatagramLimits(
                1, udpLimits.MaximumDatagramBytes));
        _udpLimits = udpLimits;
        _admissionLimits = admissionLimits;
        _protocolLimits = protocolLimits.GetValueOrDefault();
        _promotionEnabled = protocolLimits.HasValue;
        _manifest = manifest ?? CoreNetworkProtocol.Manifest;
        _clock = clock;
        _lastMilliseconds = clock.Milliseconds;
        _serverIdentity = serverIdentity;
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
        _candidateStates = new byte[candidates];
        if (_promotionEnabled)
        {
            _connections = new NetworkConnectionTable(sharedLimits.MaximumSessions);
            _sessions = new HostedUdpProtocolSession[sharedLimits.MaximumSessions];
            _candidateCookies = new byte[checked(
                candidates * ProtocolLoopbackHandshake.CookieBytes)];
            _candidateConnections = new ConnectionId[candidates];
            _candidateSecurityEpochs = new SecurityEpoch[candidates];
            _candidateServerKeys = new ECDiffieHellman[candidates];
            _candidateServerPublicKeyLengths = new ushort[candidates];
            _candidateTranscriptLengths = new ushort[candidates];
            _candidateServerFlightLengths = new ushort[candidates];
            _candidateServerPublicKeys = new byte[checked(
                candidates * ProtocolLoopbackHandshake.MaximumClientPublicKeyBytes)];
            _candidateTranscripts = new byte[checked(
                candidates * ProtocolLoopbackHandshake.MaximumTranscriptBytes)];
            _candidateMasterSecrets = new byte[checked(
                candidates * ProtocolLoopbackHandshake.MasterSecretBytes)];
            _candidateServerFlights = new byte[checked(
                candidates * udpLimits.MaximumDatagramBytes)];
        }
        else
        {
            _connections = null;
            _sessions = Array.Empty<HostedUdpProtocolSession>();
            _candidateCookies = Array.Empty<byte>();
            _candidateConnections = Array.Empty<ConnectionId>();
            _candidateSecurityEpochs = Array.Empty<SecurityEpoch>();
            _candidateServerKeys = Array.Empty<ECDiffieHellman>();
            _candidateServerPublicKeyLengths = Array.Empty<ushort>();
            _candidateTranscriptLengths = Array.Empty<ushort>();
            _candidateServerFlightLengths = Array.Empty<ushort>();
            _candidateServerPublicKeys = Array.Empty<byte>();
            _candidateTranscripts = Array.Empty<byte>();
            _candidateMasterSecrets = Array.Empty<byte>();
            _candidateServerFlights = Array.Empty<byte>();
        }
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

    public int SessionCount
    {
        get { EnsureOperational(); return _sessionCount; }
    }

    /// <summary>Returns a cold generation-fenced view of one active candidate row.</summary>
    public bool TryGetCandidate(
        int ordinal,
        out HostedCandidateId candidate,
        out HostedCandidateState state)
    {
        EnsureOperational();
        if (ordinal < 0)
        {
            candidate = default;
            state = default;
            return false;
        }
        for (int slot = 0; slot < _candidateStates.Length; slot++)
        {
            if (_candidateStates[slot] == 0) continue;
            if (ordinal-- != 0) continue;
            candidate = new HostedCandidateId(
                checked((uint)slot), _candidateGenerations[slot]);
            state = (HostedCandidateState)_candidateStates[slot];
            return true;
        }
        candidate = default;
        state = default;
        return false;
    }

    public bool TryGetSession(
        ConnectionId connection,
        out HostedUdpProtocolSession? session)
    {
        EnsureOperational();
        if (!connection.IsValid || connection.Slot >= (uint)_sessions.Length ||
            _sessions[connection.Slot] is not HostedUdpProtocolSession current ||
            current.Connection != connection)
        {
            session = null;
            return false;
        }
        session = current;
        return true;
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

    /// <summary>Runs one bounded admission, promotion, protocol, and lifetime pass.</summary>
    public void Pump()
    {
        EnsureOperational();
        ulong now = GetMonotonicMilliseconds();
        SweepExpiredCandidates(now);
        _listener.PumpInbound();
        // Admission/promotion callbacks may sample a later monotonic value while
        // routing inbound datagrams. Refresh before session lifetime arithmetic.
        now = GetMonotonicMilliseconds();

        for (int index = 0; index < _sessions.Length; index++)
        {
            if (_sessions[index] is not HostedUdpProtocolSession current ||
                current.RawState == UdpProtocolSessionState.Closed)
                continue;
            ulong received = current.Server.ReceivedPacketCount;
            _ = current.Server.ProcessInbound(now);
            if (current.Server.ReceivedPacketCount != received)
                current.LastActivityMilliseconds = now;
            if (current.RawState == UdpProtocolSessionState.Established &&
                now - current.LastKeepAliveMilliseconds >=
                    (ulong)_udpLimits.KeepAliveIntervalMilliseconds &&
                current.Server.TryEmitKeepAlive())
                current.LastKeepAliveMilliseconds = now;
            current.Server.Emit(now);
        }

        _listener.PumpOutbound();
        for (int index = 0; index < _sessions.Length; index++)
        {
            if (_sessions[index] is not HostedUdpProtocolSession current) continue;
            if (current.RawState == UdpProtocolSessionState.Established &&
                now - current.LastActivityMilliseconds >=
                    (ulong)_udpLimits.IdleTimeoutMilliseconds)
                BeginDrainCore(current, now);
            if (current.RawState == UdpProtocolSessionState.Draining &&
                (current.Server.ReliableQueued == 0 ||
                 now >= current.DrainDeadlineMilliseconds))
                CloseTransport(current);
        }
    }

    public HostedUdpAdmissionSnapshot GetSnapshot()
    {
        EnsureOperational();
        long promotionReserved = 0;
        if (_promotionEnabled)
        {
            promotionReserved = checked(
                _connections!.GetSnapshot().ReservedBytes +
                (long)_sessions.Length * IntPtr.Size +
                _candidateCookies.Length +
                (long)_candidateConnections.Length * 8 +
                (long)_candidateSecurityEpochs.Length * 4 +
                (long)_candidateServerKeys.Length * IntPtr.Size +
                (long)_candidateServerPublicKeyLengths.Length * sizeof(ushort) +
                (long)_candidateTranscriptLengths.Length * sizeof(ushort) +
                (long)_candidateServerFlightLengths.Length * sizeof(ushort) +
                _candidateServerPublicKeys.Length +
                _candidateTranscripts.Length +
                _candidateMasterSecrets.Length +
                _candidateServerFlights.Length);
            for (int index = 0; index < _sessions.Length; index++)
                if (_sessions[index] is HostedUdpProtocolSession current)
                    promotionReserved = checked(
                        promotionReserved + current.Server.GetSnapshot().ReservedBytes);
        }
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
            _promotingCandidateCount,
            _sessionCount,
            _candidateCredentialConflicts,
            _promotionCapacityDrops,
            _promotionFailures,
            _serverKeyExchangeFlights,
            _establishedPromotions,
            _duplicateClientFinishes,
            _serverFinishFlights,
            checked(_listener.ReservedBytes + _serverPublicKey.Length +
                _admissionLimits.EstimateReservedBytes() + promotionReserved),
            _listener.GetSnapshot(),
            _connections?.GetSnapshot() ?? default);
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
                _cookieKey, _manifest, nonce, publicKey, pathBinding, bucket, cookie);
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
                _cookieKey, _manifest, nonce, publicKey, pathBinding, bucket, expected);
            bool valid = CryptographicOperations.FixedTimeEquals(expected, suppliedCookie);
            if (!valid && bucket != 0)
            {
                ProtocolLoopbackHandshake.ComputeCookie(
                    _cookieKey, _manifest, nonce, publicKey, pathBinding, bucket - 1, expected);
                valid = CryptographicOperations.FixedTimeEquals(expected, suppliedCookie);
            }
            if (!valid)
            {
                _invalidDatagrams = SaturatingIncrement(_invalidDatagrams);
                return;
            }
            _cookieValidations = SaturatingIncrement(_cookieValidations);
            AdmitCandidate(
                PackPath(remoteAddress), nonce, publicKey, suppliedCookie,
                now, listener, remoteAddress);
            return;
        }

        if (_promotionEnabled)
        {
            int candidate = FindCandidate(PackPath(remoteAddress));
            if (candidate >= 0 &&
                _candidateStates[candidate] == (byte)HostedCandidateState.Promoting &&
                TryCompletePromotion(candidate, datagram, now))
                return;
        }

        _invalidDatagrams = SaturatingIncrement(_invalidDatagrams);
    }

    bool ISharedUdpKnownDatagramHandler.TryHandleKnownDatagram(
        SharedUdpServerEndpoint endpoint,
        ReadOnlySpan<byte> datagram)
    {
        if (!_promotionEnabled || !endpoint.Connection.IsValid ||
            endpoint.Connection.Slot >= (uint)_sessions.Length ||
            _sessions[endpoint.Connection.Slot] is not HostedUdpProtocolSession session ||
            session.Connection != endpoint.Connection ||
            datagram.Length != session.ClientFinish.Length ||
            !CryptographicOperations.FixedTimeEquals(datagram, session.ClientFinish))
            return false;
        _duplicateClientFinishes = SaturatingIncrement(_duplicateClientFinishes);
        if (endpoint.TrySendHandshake(session.ServerFinish) == DatagramSendStatus.Accepted)
            _serverFinishFlights = SaturatingIncrement(_serverFinishFlights);
        else
            _sendCapacityDrops = SaturatingIncrement(_sendCapacityDrops);
        session.LastActivityMilliseconds = GetMonotonicMilliseconds();
        return true;
    }

    public void Dispose()
    {
        EnsureOwnerThread();
        if (_disposed) return;
        for (int index = 0; index < _sessions.Length; index++)
            if (_sessions[index] is HostedUdpProtocolSession session)
                RetireCore(session);
        for (int slot = 0; slot < _candidateStates.Length; slot++)
            if (_candidateStates[slot] != 0)
                ReleaseCandidate(slot, releaseConnection: true);
        _listener.Dispose();
        CryptographicOperations.ZeroMemory(_cookieKey);
        CryptographicOperations.ZeroMemory(_serverPublicKey);
        CryptographicOperations.ZeroMemory(_candidateNonces);
        CryptographicOperations.ZeroMemory(_candidatePublicKeys);
        CryptographicOperations.ZeroMemory(_candidateCookies);
        CryptographicOperations.ZeroMemory(_candidateServerPublicKeys);
        CryptographicOperations.ZeroMemory(_candidateTranscripts);
        CryptographicOperations.ZeroMemory(_candidateMasterSecrets);
        CryptographicOperations.ZeroMemory(_candidateServerFlights);
        Array.Clear(_candidatePaths);
        Array.Clear(_candidateExpirations);
        Array.Clear(_candidatePublicKeyLengths);
        Array.Clear(_candidateGenerations);
        Array.Clear(_candidateStates);
        Array.Clear(_candidateConnections);
        Array.Clear(_candidateSecurityEpochs);
        Array.Clear(_candidateServerPublicKeyLengths);
        Array.Clear(_candidateTranscriptLengths);
        Array.Clear(_candidateServerFlightLengths);
        Array.Clear(_rateKeys);
        Array.Clear(_rateBuckets);
        Array.Clear(_rateChallenges);
        Array.Clear(_rateValidations);
        _disposed = true;
    }

    internal UdpProtocolSessionState GetState(HostedUdpProtocolSession session)
    {
        EnsureSession(session);
        return session.RawState;
    }

    internal void BeginDrain(HostedUdpProtocolSession session)
    {
        EnsureSession(session);
        if (session.RawState == UdpProtocolSessionState.Established)
            BeginDrainCore(session, GetMonotonicMilliseconds());
    }

    internal HostedUdpProtocolSessionSnapshot GetSessionSnapshot(
        HostedUdpProtocolSession session)
    {
        EnsureSession(session);
        return new(
            session.RawState,
            session.Connection,
            session.Candidate,
            session.Server.GetSnapshot(),
            session.Endpoint.GetSnapshot());
    }

    internal void Retire(HostedUdpProtocolSession session)
    {
        EnsureSession(session);
        RetireCore(session);
    }

    private void BeginDrainCore(HostedUdpProtocolSession session, ulong now)
    {
        session.Server.StopAcceptingNewSends();
        session.RawState = UdpProtocolSessionState.Draining;
        session.DrainDeadlineMilliseconds = checked(
            now + (ulong)_udpLimits.DrainTimeoutMilliseconds);
        if (_connections!.TryGetState(
                session.Connection, out NetworkConnectionState state) &&
            state == NetworkConnectionState.Established)
            _ = _connections.TryAdvance(
                session.Connection, NetworkConnectionState.Draining);
    }

    private void CloseTransport(HostedUdpProtocolSession session)
    {
        if (session.RawState == UdpProtocolSessionState.Closed) return;
        session.Endpoint.Dispose();
        if (_connections!.TryGetState(
                session.Connection, out NetworkConnectionState state) &&
            state != NetworkConnectionState.Closed)
            _ = _connections.TryAdvance(
                session.Connection, NetworkConnectionState.Closed);
        session.RawState = UdpProtocolSessionState.Closed;
    }

    private void RetireCore(HostedUdpProtocolSession session)
    {
        if (session.RawState == UdpProtocolSessionState.Established)
            BeginDrainCore(session, GetMonotonicMilliseconds());
        CloseTransport(session);
        session.Server.Dispose();
        CloseAndReleaseConnection(session.Connection);
        _sessions[session.Connection.Slot] = null;
        _sessionCount--;
        session.MarkDisposed();
    }

    private void EnsureSession(HostedUdpProtocolSession session)
    {
        EnsureOperational();
        ArgumentNullException.ThrowIfNull(session);
        if (!ReferenceEquals(session.Owner, this) ||
            session.Connection.Slot >= (uint)_sessions.Length ||
            !ReferenceEquals(_sessions[session.Connection.Slot], session))
            throw new ArgumentException(
                "The hosted UDP session does not belong to this host.",
                nameof(session));
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
        ReadOnlySpan<byte> cookie,
        ulong now,
        SharedUdpServerListener listener,
        ReadOnlySpan<byte> remoteAddress)
    {
        int existing = FindCandidate(path);
        if (existing >= 0)
        {
            if (CandidateCredentialsEqual(existing, nonce, publicKey))
            {
                _candidateExpirations[existing] = checked(
                    now + (ulong)_admissionLimits.ValidatedCandidateTimeoutMilliseconds);
                _candidateRefreshes = SaturatingIncrement(_candidateRefreshes);
                if (_promotionEnabled &&
                    _candidateStates[existing] == (byte)HostedCandidateState.Promoting)
                    SendCachedServerKeyExchange(existing, listener, remoteAddress);
                else if (_promotionEnabled)
                    TryPromoteCandidate(existing, cookie, listener, remoteAddress);
                return;
            }
            if (_candidateStates[existing] == (byte)HostedCandidateState.Promoting)
            {
                _candidateCredentialConflicts = SaturatingIncrement(
                    _candidateCredentialConflicts);
                return;
            }
            ReleaseCandidate(existing, releaseConnection: true);
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
        if (_promotionEnabled)
            TryPromoteCandidate(slot, cookie, listener, remoteAddress);
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
        _candidateStates[slot] = (byte)HostedCandidateState.Validated;
        _candidateExpirations[slot] = checked(
            now + (ulong)_admissionLimits.ValidatedCandidateTimeoutMilliseconds);
    }

    private bool CandidateCredentialsEqual(
        int slot,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> publicKey)
    {
        if (_candidatePublicKeyLengths[slot] != publicKey.Length) return false;
        int nonceOffset = checked(slot * ProtocolLoopbackHandshake.NonceBytes);
        int keyOffset = checked(slot * ProtocolLoopbackHandshake.MaximumClientPublicKeyBytes);
        return CryptographicOperations.FixedTimeEquals(
                nonce,
                _candidateNonces.AsSpan(
                    nonceOffset, ProtocolLoopbackHandshake.NonceBytes)) &&
            CryptographicOperations.FixedTimeEquals(
                publicKey,
                _candidatePublicKeys.AsSpan(keyOffset, publicKey.Length));
    }

    private void TryPromoteCandidate(
        int slot,
        ReadOnlySpan<byte> cookie,
        SharedUdpServerListener listener,
        ReadOnlySpan<byte> remoteAddress)
    {
        if (!_promotionEnabled ||
            _candidateStates[slot] != (byte)HostedCandidateState.Validated)
            return;
        if (!_connections!.TryOpenValidated(out ConnectionId connection))
        {
            _promotionCapacityDrops = SaturatingIncrement(_promotionCapacityDrops);
            return;
        }

        ECDiffieHellman? serverKey = null;
        byte[]? serverPublic = null;
        byte[]? transcript = null;
        byte[]? master = null;
        byte[]? secret = null;
        bool committed = false;
        try
        {
            int keyOffset = checked(
                slot * ProtocolLoopbackHandshake.MaximumClientPublicKeyBytes);
            ReadOnlySpan<byte> clientPublic = _candidatePublicKeys.AsSpan(
                keyOffset, _candidatePublicKeyLengths[slot]);
            using ECDiffieHellman imported = ECDiffieHellman.Create();
            imported.ImportSubjectPublicKeyInfo(clientPublic, out int read);
            if (read != clientPublic.Length)
                throw new CryptographicException(
                    "The hosted candidate public key contains trailing data.");
            serverKey = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            serverPublic = serverKey.ExportSubjectPublicKeyInfo();
            if (serverPublic.Length > ProtocolLoopbackHandshake.MaximumClientPublicKeyBytes)
                throw new CryptographicException(
                    "The hosted server ECDH public key exceeds its bound.");
            secret = serverKey.DeriveRawSecretAgreement(imported.PublicKey);
            if (!_connections.TryAdvance(connection, NetworkConnectionState.KeyExchange) ||
                !_connections.TryGetSecurityEpoch(connection, out SecurityEpoch epoch))
                throw new InvalidOperationException(
                    "The hosted promotion connection could not enter key exchange.");
            int nonceOffset = checked(slot * ProtocolLoopbackHandshake.NonceBytes);
            ReadOnlySpan<byte> nonce = _candidateNonces.AsSpan(
                nonceOffset, ProtocolLoopbackHandshake.NonceBytes);
            transcript = ProtocolLoopbackHandshake.BuildTranscript(
                _manifest, nonce, clientPublic, serverPublic, connection, epoch);
            master = ProtocolLoopbackHandshake.DeriveMaster(
                secret, nonce, cookie, transcript);
            Span<byte> signature = stackalloc byte[ProtocolLoopbackHandshake.SignatureBytes];
            if (!_serverIdentity.TrySign(transcript, signature, out int signatureLength) ||
                signatureLength != ProtocolLoopbackHandshake.SignatureBytes)
                throw new CryptographicException(
                    "The deployment identity could not sign the hosted transcript.");

            serverPublic.CopyTo(CandidateServerPublic(slot));
            transcript.CopyTo(CandidateTranscript(slot));
            master.CopyTo(CandidateMaster(slot));
            cookie.CopyTo(CandidateCookie(slot));
            int flightLength = ProtocolLoopbackHandshake.WriteServerKeyExchange(
                CandidateServerFlight(slot), connection, epoch, serverPublic, signature);
            _candidateServerPublicKeyLengths[slot] = checked((ushort)serverPublic.Length);
            _candidateTranscriptLengths[slot] = checked((ushort)transcript.Length);
            _candidateServerFlightLengths[slot] = checked((ushort)flightLength);
            _candidateConnections[slot] = connection;
            _candidateSecurityEpochs[slot] = epoch;
            _candidateServerKeys[slot] = serverKey;
            serverKey = null;
            _candidateStates[slot] = (byte)HostedCandidateState.Promoting;
            _promotingCandidateCount++;
            committed = true;
            SendCachedServerKeyExchange(slot, listener, remoteAddress);
        }
        catch (Exception exception) when (exception is CryptographicException or
            ObjectDisposedException or InvalidOperationException)
        {
            _promotionFailures = SaturatingIncrement(_promotionFailures);
            _invalidDatagrams = SaturatingIncrement(_invalidDatagrams);
        }
        finally
        {
            serverKey?.Dispose();
            if (secret is not null) CryptographicOperations.ZeroMemory(secret);
            if (serverPublic is not null) CryptographicOperations.ZeroMemory(serverPublic);
            if (transcript is not null) CryptographicOperations.ZeroMemory(transcript);
            if (master is not null) CryptographicOperations.ZeroMemory(master);
            if (!committed)
            {
                CloseAndReleaseConnection(connection);
                ReleaseCandidate(slot, releaseConnection: false);
            }
        }
    }

    private void SendCachedServerKeyExchange(
        int slot,
        SharedUdpServerListener listener,
        ReadOnlySpan<byte> remoteAddress)
    {
        int length = _candidateServerFlightLengths[slot];
        if (length == 0) return;
        if (listener.TrySendUnknown(
                CandidateServerFlight(slot)[..length], remoteAddress) ==
            DatagramSendStatus.Accepted)
            _serverKeyExchangeFlights = SaturatingIncrement(_serverKeyExchangeFlights);
        else
            _sendCapacityDrops = SaturatingIncrement(_sendCapacityDrops);
    }

    private bool TryCompletePromotion(
        int slot,
        ReadOnlySpan<byte> datagram,
        ulong now)
    {
        ConnectionId connection = _candidateConnections[slot];
        SecurityEpoch epoch = _candidateSecurityEpochs[slot];
        if (!ProtocolLoopbackHandshake.TryReadClientFinish(
                datagram, connection, epoch, out ReadOnlySpan<byte> proof))
            return false;
        Span<byte> expected = stackalloc byte[ProtocolLoopbackHandshake.ProofBytes];
        int transcriptLength = _candidateTranscriptLengths[slot];
        ReadOnlySpan<byte> transcript = CandidateTranscript(slot)[..transcriptLength];
        ReadOnlySpan<byte> master = CandidateMaster(slot);
        ProtocolLoopbackHandshake.ComputeClientProof(master, transcript, expected);
        if (!CryptographicOperations.FixedTimeEquals(expected, proof))
        {
            _invalidDatagrams = SaturatingIncrement(_invalidDatagrams);
            return true;
        }
        if (connection.Slot >= (uint)_sessions.Length ||
            _sessions[connection.Slot] is not null)
        {
            _promotionCapacityDrops = SaturatingIncrement(_promotionCapacityDrops);
            return true;
        }

        IPEndPoint remote = UnpackPath(_candidatePaths[slot]);
        if (!_listener.TryRegister(remote, out SharedUdpServerEndpoint? endpoint) ||
            endpoint is null)
        {
            _promotionCapacityDrops = SaturatingIncrement(_promotionCapacityDrops);
            return true;
        }

        NetworkProtocolPeer? server = null;
        bool published = false;
        try
        {
            server = new NetworkProtocolPeer(
                endpoint,
                _manifest,
                _protocolLimits,
                new DeterministicDatagramLimits(1, _udpLimits.MaximumDatagramBytes),
                connection,
                epoch,
                master,
                sendsClientToServer: false,
                usesMonotonicPacing: true);
            byte[] clientFinish = datagram.ToArray();
            byte[] serverFinish = new byte[1 + 4 + 4 + 4 + ProtocolLoopbackHandshake.ProofBytes];
            int finishLength = ProtocolLoopbackHandshake.WriteServerFinish(
                serverFinish, connection, epoch, master, transcript);
            if (finishLength != serverFinish.Length ||
                endpoint.TrySendHandshake(serverFinish) != DatagramSendStatus.Accepted)
            {
                CryptographicOperations.ZeroMemory(clientFinish);
                CryptographicOperations.ZeroMemory(serverFinish);
                _sendCapacityDrops = SaturatingIncrement(_sendCapacityDrops);
                return true;
            }
            if (!_connections!.TryAdvance(
                    connection, NetworkConnectionState.ProtocolValidated) ||
                !_connections.TryAdvance(connection, NetworkConnectionState.Established))
                throw new InvalidOperationException(
                    "The hosted promotion could not publish an established connection.");
            endpoint.BindKnownDatagramHandler(this);
            endpoint.BindConnection(connection);
            HostedCandidateId candidateId = new(
                checked((uint)slot), _candidateGenerations[slot]);
            var session = new HostedUdpProtocolSession(
                this, endpoint, server, connection, candidateId,
                clientFinish, serverFinish, now);
            _sessions[connection.Slot] = session;
            _sessionCount++;
            _establishedPromotions = SaturatingIncrement(_establishedPromotions);
            _serverFinishFlights = SaturatingIncrement(_serverFinishFlights);
            server = null;
            published = true;
            ReleaseCandidate(slot, releaseConnection: false);
            return true;
        }
        catch (InvalidOperationException)
        {
            _promotionFailures = SaturatingIncrement(_promotionFailures);
            return true;
        }
        finally
        {
            if (!published)
            {
                server?.Dispose();
                endpoint.Dispose();
            }
        }
    }

    private void SweepExpiredCandidates(ulong now)
    {
        for (int slot = 0; slot < _candidatePaths.Length; slot++)
        {
            if (_candidatePaths[slot] == 0 || now < _candidateExpirations[slot]) continue;
            ReleaseCandidate(slot, releaseConnection: true);
            _candidateTimeouts = SaturatingIncrement(_candidateTimeouts);
        }
    }

    private void ReleaseCandidate(int slot, bool releaseConnection)
    {
        if (_candidateStates[slot] == 0) return;
        int nonceOffset = checked(slot * ProtocolLoopbackHandshake.NonceBytes);
        int keyOffset = checked(slot * ProtocolLoopbackHandshake.MaximumClientPublicKeyBytes);
        CryptographicOperations.ZeroMemory(
            _candidateNonces.AsSpan(nonceOffset, ProtocolLoopbackHandshake.NonceBytes));
        CryptographicOperations.ZeroMemory(_candidatePublicKeys.AsSpan(
            keyOffset, ProtocolLoopbackHandshake.MaximumClientPublicKeyBytes));
        if (_promotionEnabled)
        {
            if (releaseConnection && _candidateConnections[slot].IsValid)
                CloseAndReleaseConnection(_candidateConnections[slot]);
            _candidateServerKeys[slot]?.Dispose();
            _candidateServerKeys[slot] = null;
            CryptographicOperations.ZeroMemory(CandidateCookie(slot));
            CryptographicOperations.ZeroMemory(CandidateServerPublic(slot));
            CryptographicOperations.ZeroMemory(CandidateTranscript(slot));
            CryptographicOperations.ZeroMemory(CandidateMaster(slot));
            CryptographicOperations.ZeroMemory(CandidateServerFlight(slot));
            _candidateConnections[slot] = default;
            _candidateSecurityEpochs[slot] = default;
            _candidateServerPublicKeyLengths[slot] = 0;
            _candidateTranscriptLengths[slot] = 0;
            _candidateServerFlightLengths[slot] = 0;
        }
        if (_candidateStates[slot] == (byte)HostedCandidateState.Promoting)
            _promotingCandidateCount--;
        _candidatePaths[slot] = 0;
        _candidateExpirations[slot] = 0;
        _candidatePublicKeyLengths[slot] = 0;
        _candidateStates[slot] = 0;
        _candidateFree[_candidateFreeCount++] = slot;
        _candidateCount--;
    }

    private void CloseAndReleaseConnection(ConnectionId connection)
    {
        if (!_promotionEnabled || !connection.IsValid) return;
        if (_connections!.TryGetState(connection, out NetworkConnectionState state) &&
            state != NetworkConnectionState.Closed)
            _ = _connections.TryAdvance(connection, NetworkConnectionState.Closed);
        _ = _connections.TryReleaseClosed(connection);
    }

    private Span<byte> CandidateCookie(int slot) => _candidateCookies.AsSpan(
        checked(slot * ProtocolLoopbackHandshake.CookieBytes),
        ProtocolLoopbackHandshake.CookieBytes);

    private Span<byte> CandidateServerPublic(int slot) => _candidateServerPublicKeys.AsSpan(
        checked(slot * ProtocolLoopbackHandshake.MaximumClientPublicKeyBytes),
        ProtocolLoopbackHandshake.MaximumClientPublicKeyBytes);

    private Span<byte> CandidateTranscript(int slot) => _candidateTranscripts.AsSpan(
        checked(slot * ProtocolLoopbackHandshake.MaximumTranscriptBytes),
        ProtocolLoopbackHandshake.MaximumTranscriptBytes);

    private Span<byte> CandidateMaster(int slot) => _candidateMasterSecrets.AsSpan(
        checked(slot * ProtocolLoopbackHandshake.MasterSecretBytes),
        ProtocolLoopbackHandshake.MasterSecretBytes);

    private Span<byte> CandidateServerFlight(int slot) => _candidateServerFlights.AsSpan(
        checked(slot * _udpLimits.MaximumDatagramBytes),
        _udpLimits.MaximumDatagramBytes);

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

    private static IPEndPoint UnpackPath(ulong packed)
    {
        if (packed == 0) throw new ArgumentOutOfRangeException(nameof(packed));
        ulong value = packed - 1UL;
        uint ipv4 = checked((uint)(value >> 16));
        int port = (ushort)(value & ushort.MaxValue);
        Span<byte> address = stackalloc byte[4]
        {
            (byte)((ipv4 >> 24) & byte.MaxValue),
            (byte)((ipv4 >> 16) & byte.MaxValue),
            (byte)((ipv4 >> 8) & byte.MaxValue),
            (byte)(ipv4 & byte.MaxValue),
        };
        return new IPEndPoint(new IPAddress(address), port);
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

/// <summary>Cold diagnostics for one externally authenticated server session.</summary>
public readonly record struct HostedUdpProtocolSessionSnapshot(
    UdpProtocolSessionState State,
    ConnectionId Connection,
    HostedCandidateId Candidate,
    NetworkProtocolSnapshot Server,
    UdpDatagramEndpointSnapshot Datagrams);

/// <summary>
/// One externally authenticated connection realized on the hosted shared socket.
/// </summary>
public sealed class HostedUdpProtocolSession : IDisposable
{
    private bool _disposed;

    internal HostedUdpProtocolSession(
        HostedUdpAdmissionHost owner,
        SharedUdpServerEndpoint endpoint,
        NetworkProtocolPeer server,
        ConnectionId connection,
        HostedCandidateId candidate,
        byte[] clientFinish,
        byte[] serverFinish,
        ulong now)
    {
        Owner = owner;
        Endpoint = endpoint;
        Server = server;
        Connection = connection;
        Candidate = candidate;
        ClientFinish = clientFinish;
        ServerFinish = serverFinish;
        RawState = UdpProtocolSessionState.Established;
        LastActivityMilliseconds = now;
        LastKeepAliveMilliseconds = now;
    }

    internal HostedUdpAdmissionHost Owner { get; }
    internal SharedUdpServerEndpoint Endpoint { get; }
    internal byte[] ClientFinish { get; }
    internal byte[] ServerFinish { get; }
    internal UdpProtocolSessionState RawState { get; set; }
    internal ulong LastActivityMilliseconds { get; set; }
    internal ulong LastKeepAliveMilliseconds { get; set; }
    internal ulong DrainDeadlineMilliseconds { get; set; }

    public ConnectionId Connection { get; }
    public HostedCandidateId Candidate { get; }
    public NetworkProtocolPeer Server { get; }
    public UdpProtocolSessionState State => _disposed
        ? UdpProtocolSessionState.Closed
        : Owner.GetState(this);

    public void BeginDrain()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Owner.BeginDrain(this);
    }

    public HostedUdpProtocolSessionSnapshot GetSnapshot()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Owner.GetSessionSnapshot(this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        Owner.Retire(this);
    }

    internal void MarkDisposed()
    {
        CryptographicOperations.ZeroMemory(ClientFinish);
        CryptographicOperations.ZeroMemory(ServerFinish);
        _disposed = true;
    }
}

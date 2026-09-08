namespace UltraSim.Network;

public enum NetworkConnectionState : byte
{
    Free = 0,
    CookieValidated = 1,
    KeyExchange = 2,
    ProtocolValidated = 3,
    Established = 4,
    Draining = 5,
    Closed = 6,
}

public readonly record struct NetworkConnectionSnapshot(
    int Capacity,
    int Active,
    int Available,
    int Retired,
    long ReservedBytes);

/// <summary>Single-owner fixed generational connection/session table.</summary>
public sealed class NetworkConnectionTable
{
    private readonly int _ownerThreadId;
    private readonly uint[] _generations;
    private readonly NetworkConnectionState[] _states;
    private readonly uint[] _securityEpochs;
    private readonly ulong[] _laneSequences;
    private readonly int[] _free;
    private int _freeCount;
    private int _active;
    private int _retired;

    public NetworkConnectionTable(int maximumConnections)
    {
        if (maximumConnections is <= 0 or > 1_048_576)
            throw new ArgumentOutOfRangeException(nameof(maximumConnections));
        _ownerThreadId = Environment.CurrentManagedThreadId;
        _generations = new uint[maximumConnections];
        _states = new NetworkConnectionState[maximumConnections];
        _securityEpochs = new uint[maximumConnections];
        _laneSequences = new ulong[checked(maximumConnections * NetworkLaneContracts.Count)];
        _free = new int[maximumConnections];
        for (int index = 0; index < maximumConnections; index++) _free[index] = maximumConnections - 1 - index;
        _freeCount = maximumConnections;
    }

    /// <summary>Allocates a slot only after external cookie validation.</summary>
    public bool TryOpenValidated(out ConnectionId connection)
    {
        EnsureOwnerThread();
        while (_freeCount != 0)
        {
            int slot = _free[--_freeCount];
            uint generation = _generations[slot];
            if (generation == uint.MaxValue)
            {
                _retired++;
                continue;
            }
            generation++;
            _generations[slot] = generation;
            _states[slot] = NetworkConnectionState.CookieValidated;
            _securityEpochs[slot] = 1;
            _laneSequences.AsSpan(slot * NetworkLaneContracts.Count, NetworkLaneContracts.Count).Clear();
            _active++;
            connection = new ConnectionId((uint)slot, generation);
            return true;
        }
        connection = default;
        return false;
    }

    public bool TryGetState(ConnectionId connection, out NetworkConnectionState state)
    {
        EnsureOwnerThread();
        if (!TryResolve(connection, out int slot)) { state = NetworkConnectionState.Free; return false; }
        state = _states[slot];
        return true;
    }

    public bool TryGetSecurityEpoch(ConnectionId connection, out SecurityEpoch epoch)
    {
        EnsureOwnerThread();
        if (!TryResolve(connection, out int slot)) { epoch = default; return false; }
        epoch = new SecurityEpoch(_securityEpochs[slot]);
        return true;
    }

    public bool TryAdvance(ConnectionId connection, NetworkConnectionState next)
    {
        EnsureOwnerThread();
        if (!TryResolve(connection, out int slot) || !IsAllowed(_states[slot], next)) return false;
        _states[slot] = next;
        return true;
    }

    public bool TryRotateSecurityEpoch(ConnectionId connection, out SecurityEpoch epoch)
    {
        EnsureOwnerThread();
        if (!TryResolve(connection, out int slot) || _securityEpochs[slot] == uint.MaxValue)
        {
            epoch = default;
            return false;
        }
        uint value = ++_securityEpochs[slot];
        epoch = new SecurityEpoch(value);
        return true;
    }

    public bool TryTakeLaneSequence(ConnectionId connection, NetworkLane lane, out LaneSequence sequence)
    {
        EnsureOwnerThread();
        if ((uint)lane >= NetworkLaneContracts.Count || !TryResolve(connection, out int slot))
        {
            sequence = default;
            return false;
        }
        int index = slot * NetworkLaneContracts.Count + (int)lane;
        ulong value = _laneSequences[index];
        if (value == ulong.MaxValue) { sequence = default; return false; }
        sequence = new LaneSequence(value);
        _laneSequences[index] = value + 1;
        return true;
    }

    /// <summary>Returns a completely closed slot; retained IDs become stale immediately.</summary>
    public bool TryReleaseClosed(ConnectionId connection)
    {
        EnsureOwnerThread();
        if (!TryResolve(connection, out int slot) || _states[slot] != NetworkConnectionState.Closed) return false;
        _states[slot] = NetworkConnectionState.Free;
        _securityEpochs[slot] = 0;
        _laneSequences.AsSpan(slot * NetworkLaneContracts.Count, NetworkLaneContracts.Count).Clear();
        _free[_freeCount++] = slot;
        _active--;
        return true;
    }

    public NetworkConnectionSnapshot GetSnapshot()
    {
        EnsureOwnerThread();
        return new(
            _states.Length,
            _active,
            _freeCount,
            _retired,
            checked((long)_generations.Length * sizeof(uint) + _states.Length +
                (long)_securityEpochs.Length * sizeof(uint) +
                (long)_laneSequences.Length * sizeof(ulong) + (long)_free.Length * sizeof(int)));
    }

    private bool TryResolve(ConnectionId connection, out int slot)
    {
        if (!connection.IsValid || connection.Slot >= (uint)_states.Length) { slot = -1; return false; }
        slot = (int)connection.Slot;
        return
            _generations[slot] == connection.Generation && _states[slot] != NetworkConnectionState.Free;
    }

    private static bool IsAllowed(NetworkConnectionState current, NetworkConnectionState next) =>
        (current, next) switch
        {
            (NetworkConnectionState.CookieValidated, NetworkConnectionState.KeyExchange) => true,
            (NetworkConnectionState.KeyExchange, NetworkConnectionState.ProtocolValidated) => true,
            (NetworkConnectionState.ProtocolValidated, NetworkConnectionState.Established) => true,
            (NetworkConnectionState.Established, NetworkConnectionState.Draining) => true,
            (NetworkConnectionState.CookieValidated, NetworkConnectionState.Closed) => true,
            (NetworkConnectionState.KeyExchange, NetworkConnectionState.Closed) => true,
            (NetworkConnectionState.ProtocolValidated, NetworkConnectionState.Closed) => true,
            (NetworkConnectionState.Established, NetworkConnectionState.Closed) => true,
            (NetworkConnectionState.Draining, NetworkConnectionState.Closed) => true,
            _ => false,
        };

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The connection table is bound to its creating owner thread.");
    }
}

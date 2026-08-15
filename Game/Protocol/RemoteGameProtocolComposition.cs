using UltraSim.Network;

namespace UltraSim.Game.Protocol;

/// <summary>
/// Small cold-composed owner for the bounded set of game protocol adapters used
/// by one application session.
/// </summary>
/// <remarks>
/// This is deliberately not a message bus. Network identifies the message;
/// game composition orders a small fixed set of mutually exclusive owners. The
/// first handler that claims a message consumes it. Handlers remain owned and
/// disposed by their creating game/client composition.
/// </remarks>
public sealed class RemoteGameProtocolComposition : IRemoteGameProtocolHandler
{
    private readonly IRemoteGameProtocolHandler?[] _handlers;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private int _count;

    public RemoteGameProtocolComposition(int maximumHandlers = 4)
    {
        if (maximumHandlers <= 0 || maximumHandlers > 16)
            throw new ArgumentOutOfRangeException(nameof(maximumHandlers));
        _handlers = new IRemoteGameProtocolHandler[maximumHandlers];
    }

    public int Count
    {
        get { EnsureOwnerThread(); return _count; }
    }

    public void Add(IRemoteGameProtocolHandler handler)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(handler);
        for (int index = 0; index < _count; index++)
            if (ReferenceEquals(_handlers[index], handler))
                throw new ArgumentException("The protocol handler is already composed.", nameof(handler));
        if (_count == _handlers.Length)
            throw new InvalidOperationException("The bounded game protocol composition is full.");
        _handlers[_count++] = handler;
    }

    public bool TryProcess(in NetworkDeliveredMessage message, ReadOnlySpan<byte> payload)
    {
        EnsureOwnerThread();
        for (int index = 0; index < _count; index++)
            if (_handlers[index]!.TryProcess(in message, payload)) return true;
        return false;
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The game protocol composition is owner-thread-bound.");
    }
}

namespace UltraSim.Client;

/// <summary>
/// Tracks locally issued input samples and their pending/acknowledged lifecycle.
/// </summary>
public sealed class ClientInputSequencer<TInput>
{
    private readonly List<ClientInputSample<TInput>> _pending = new();
    private ushort _nextSequence = 1;

    public ushort NextSequence => _nextSequence;
    public int PendingCount => _pending.Count;

    public ClientInputSample<TInput> EnqueueInput(in TInput input, uint clientTick)
    {
        ushort sequence = _nextSequence++;
        if (_nextSequence == 0)
            _nextSequence = 1;

        var sample = new ClientInputSample<TInput>(sequence, clientTick, input);
        _pending.Add(sample);
        return sample;
    }

    public int AcknowledgeUpTo(ushort acknowledgedSequence)
    {
        int removed = 0;
        while (removed < _pending.Count && SequenceLte(_pending[removed].Sequence, acknowledgedSequence))
            removed++;

        if (removed > 0)
            _pending.RemoveRange(0, removed);

        return removed;
    }

    public int CopyPendingTo(Span<ClientInputSample<TInput>> destination)
    {
        int count = Math.Min(destination.Length, _pending.Count);
        for (int i = 0; i < count; i++)
            destination[i] = _pending[i];
        return count;
    }

    private static bool SequenceLte(ushort left, ushort right)
    {
        return unchecked((short)(left - right)) <= 0;
    }
}

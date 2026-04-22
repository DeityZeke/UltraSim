namespace UltraSim.Client;

/// <summary>
/// Stores locally predicted states keyed by input sequence.
/// </summary>
public sealed class ClientPredictionStore<TState>
{
    private readonly List<PredictedStateSample<TState>> _samples = new();

    public int Count => _samples.Count;

    public void Record(ushort sequence, uint clientTick, in TState state)
    {
        _samples.Add(new PredictedStateSample<TState>(sequence, clientTick, state));
    }

    public bool TryGet(ushort sequence, out PredictedStateSample<TState> sample)
    {
        for (int i = 0; i < _samples.Count; i++)
        {
            if (_samples[i].Sequence == sequence)
            {
                sample = _samples[i];
                return true;
            }
        }

        sample = default;
        return false;
    }

    public int RemoveAcknowledgedUpTo(ushort authoritativeSequence)
    {
        int removed = 0;
        while (removed < _samples.Count && SequenceLte(_samples[removed].Sequence, authoritativeSequence))
            removed++;

        if (removed > 0)
            _samples.RemoveRange(0, removed);

        return removed;
    }

    public void Clear()
    {
        _samples.Clear();
    }

    private static bool SequenceLte(ushort left, ushort right)
    {
        return unchecked((short)(left - right)) <= 0;
    }
}

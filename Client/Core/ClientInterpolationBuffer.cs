namespace UltraSim.Client;

/// <summary>
/// Stores authoritative state samples for interpolation on the client.
/// </summary>
public sealed class ClientInterpolationBuffer<TState>
{
    private readonly List<ClientInterpolationSample<TState>> _samples;

    public ClientInterpolationBuffer(int initialCapacity = 8)
    {
        if (initialCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(initialCapacity));

        _samples = new List<ClientInterpolationSample<TState>>(initialCapacity);
    }

    public int Count => _samples.Count;

    public void AddSample(ulong serverTick, in TState state)
    {
        int index = _samples.Count;
        while (index > 0 && _samples[index - 1].ServerTick > serverTick)
            index--;

        if (index > 0 && _samples[index - 1].ServerTick == serverTick)
        {
            _samples[index - 1] = new ClientInterpolationSample<TState>(serverTick, state);
            return;
        }

        if (index < _samples.Count && _samples[index].ServerTick == serverTick)
        {
            _samples[index] = new ClientInterpolationSample<TState>(serverTick, state);
            return;
        }

        _samples.Insert(index, new ClientInterpolationSample<TState>(serverTick, state));
    }

    public int PruneBefore(ulong minServerTick)
    {
        int removed = 0;
        while (removed < _samples.Count && _samples[removed].ServerTick < minServerTick)
            removed++;

        if (removed > 0)
            _samples.RemoveRange(0, removed);

        return removed;
    }

    public bool TrySample(ulong targetServerTick, InterpolationLerp<TState> lerp, out TState state)
    {
        ArgumentNullException.ThrowIfNull(lerp);

        if (_samples.Count == 0)
        {
            state = default!;
            return false;
        }

        if (_samples.Count == 1 || targetServerTick <= _samples[0].ServerTick)
        {
            state = _samples[0].State;
            return true;
        }

        ClientInterpolationSample<TState> previous = _samples[0];
        for (int i = 1; i < _samples.Count; i++)
        {
            ClientInterpolationSample<TState> next = _samples[i];
            if (targetServerTick <= next.ServerTick)
            {
                if (next.ServerTick == previous.ServerTick)
                {
                    state = next.State;
                    return true;
                }

                float alpha = (targetServerTick - previous.ServerTick) / (float)(next.ServerTick - previous.ServerTick);
                TState previousState = previous.State;
                TState nextState = next.State;
                state = lerp(in previousState, in nextState, alpha);
                return true;
            }

            previous = next;
        }

        state = _samples[^1].State;
        return true;
    }

    public void Clear()
    {
        _samples.Clear();
    }
}

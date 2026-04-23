namespace UltraSim.Server.Network;

public sealed class ServerEcsChunkAoiRingCadencePolicy
{
    private readonly uint[] _cadenceTicks;

    public ServerEcsChunkAoiRingCadencePolicy(params uint[] cadenceTicks)
    {
        ArgumentNullException.ThrowIfNull(cadenceTicks);
        if (cadenceTicks.Length == 0)
            throw new ArgumentOutOfRangeException(nameof(cadenceTicks));

        _cadenceTicks = new uint[cadenceTicks.Length];
        for (int i = 0; i < cadenceTicks.Length; i++)
        {
            if (cadenceTicks[i] == 0)
                throw new ArgumentOutOfRangeException(nameof(cadenceTicks));
            _cadenceTicks[i] = cadenceTicks[i];
        }
    }

    public static ServerEcsChunkAoiRingCadencePolicy EveryTick { get; } = new(1);

    public int Count => _cadenceTicks.Length;

    public uint GetCadenceTicks(int ringIndex)
    {
        if (ringIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(ringIndex));

        return ringIndex < _cadenceTicks.Length
            ? _cadenceTicks[ringIndex]
            : _cadenceTicks[^1];
    }

    public bool ShouldPublish(uint tick, int ringIndex)
    {
        uint cadence = GetCadenceTicks(ringIndex);
        return tick == 0 || (tick % cadence) == 0;
    }
}

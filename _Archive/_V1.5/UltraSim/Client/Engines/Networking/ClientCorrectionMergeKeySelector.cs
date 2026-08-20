namespace UltraSim.Client.Engines.Networking
{
    public delegate bool ClientCorrectionMergeKeySelector<TCorrection>(
        in TCorrection correction,
        out ulong mergeKey);
}

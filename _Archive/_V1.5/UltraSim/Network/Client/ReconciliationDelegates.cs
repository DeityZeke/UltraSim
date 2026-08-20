namespace UltraSim.Network
{
    public delegate void ReconciliationHandler<TCorrection>(in ReconciliationEntry<TCorrection> correction);
}

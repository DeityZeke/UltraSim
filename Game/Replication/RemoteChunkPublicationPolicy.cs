namespace UltraSim.Game.Replication;

/// <summary>
/// Physical representation for ordinary selected-profile chunk publications. This never changes
/// publication ownership, semantic identity, proof requirements, or snapshot recovery.
/// </summary>
public enum RemoteChunkPublicationRepresentation : byte
{
    Standalone = 1,
    Message129WhenBeneficial = 2,
}

/// <summary>
/// Bounded selected-profile publication policy. Cadence controls when current truth is eligible;
/// representation controls only how already-due Delta/Absence messages share an envelope.
/// </summary>
public readonly record struct RemoteChunkPublicationPolicy(
    OfflineDirectChunkCadence Cadence,
    RemoteChunkPublicationRepresentation Representation)
{
    public static RemoteChunkPublicationPolicy CurrentStandalone { get; } = new(
        OfflineDirectChunkCadence.EvidenceProfile,
        RemoteChunkPublicationRepresentation.Standalone);

    public static RemoteChunkPublicationPolicy CurrentBundled { get; } = new(
        OfflineDirectChunkCadence.EvidenceProfile,
        RemoteChunkPublicationRepresentation.Message129WhenBeneficial);

    public static RemoteChunkPublicationPolicy CandidateStandalone { get; } = new(
        new OfflineDirectChunkCadence(1, 3, 6),
        RemoteChunkPublicationRepresentation.Standalone);

    public static RemoteChunkPublicationPolicy CandidateBundled { get; } = new(
        new OfflineDirectChunkCadence(1, 3, 6),
        RemoteChunkPublicationRepresentation.Message129WhenBeneficial);

    public bool IsValid => Cadence.IsValid && Enum.IsDefined(Representation);

    public void Validate()
    {
        if (!IsValid) throw new ArgumentOutOfRangeException(nameof(RemoteChunkPublicationPolicy));
    }
}

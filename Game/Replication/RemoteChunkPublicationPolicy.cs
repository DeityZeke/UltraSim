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
/// Optional physical compression of an already-composed canonical message 129. This policy may
/// change neither bundle membership nor the delivery/proof transaction.
/// </summary>
public readonly record struct RemoteChunkBundleCompressionPolicy(
    bool Enabled,
    int MinimumCanonicalBytes,
    int MinimumNetSavingsBytes)
{
    public const int MaximumCanonicalBytes = 1_405;
    public static RemoteChunkBundleCompressionPolicy Disabled => default;

    public static RemoteChunkBundleCompressionPolicy Selected { get; } = new(
        true,
        MinimumCanonicalBytes: 384,
        MinimumNetSavingsBytes: 32);

    public bool IsValid => !Enabled ||
        MinimumCanonicalBytes >= 1 &&
        MinimumCanonicalBytes <= MaximumCanonicalBytes &&
        MinimumNetSavingsBytes >= 1 &&
        MinimumNetSavingsBytes < MinimumCanonicalBytes;

    public void Validate()
    {
        if (!IsValid)
            throw new ArgumentOutOfRangeException(nameof(RemoteChunkBundleCompressionPolicy));
    }
}

/// <summary>
/// Bounded selected-profile publication policy. Cadence controls when current truth is eligible;
/// representation controls only how already-due Delta/Absence messages share an envelope.
/// </summary>
public readonly record struct RemoteChunkPublicationPolicy(
    OfflineDirectChunkCadence Cadence,
    RemoteChunkPublicationRepresentation Representation,
    RemoteChunkBundleCompressionPolicy Compression = default)
{
    public static RemoteChunkPublicationPolicy CurrentStandalone { get; } = new(
        OfflineDirectChunkCadence.EvidenceProfile,
        RemoteChunkPublicationRepresentation.Standalone);

    public static RemoteChunkPublicationPolicy CurrentBundled { get; } = new(
        OfflineDirectChunkCadence.EvidenceProfile,
        RemoteChunkPublicationRepresentation.Message129WhenBeneficial,
        RemoteChunkBundleCompressionPolicy.Selected);

    public static RemoteChunkPublicationPolicy CurrentBundledCanonical { get; } = new(
        OfflineDirectChunkCadence.EvidenceProfile,
        RemoteChunkPublicationRepresentation.Message129WhenBeneficial,
        RemoteChunkBundleCompressionPolicy.Disabled);

    public static RemoteChunkPublicationPolicy CandidateStandalone { get; } = new(
        new OfflineDirectChunkCadence(1, 3, 6),
        RemoteChunkPublicationRepresentation.Standalone);

    public static RemoteChunkPublicationPolicy CandidateBundled { get; } = new(
        new OfflineDirectChunkCadence(1, 3, 6),
        RemoteChunkPublicationRepresentation.Message129WhenBeneficial);

    public bool IsValid => Cadence.IsValid && Enum.IsDefined(Representation) &&
        Compression.IsValid &&
        (!Compression.Enabled ||
         Representation == RemoteChunkPublicationRepresentation.Message129WhenBeneficial);

    public void Validate()
    {
        if (!IsValid) throw new ArgumentOutOfRangeException(nameof(RemoteChunkPublicationPolicy));
    }
}

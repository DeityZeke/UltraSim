namespace UltraSim.Network;

public readonly record struct HandshakeValidationResult(
    bool Accepted,
    HandshakeRejectReason RejectReason,
    FeatureFlags MissingFeatures)
{
    public static HandshakeValidationResult AcceptedResult() =>
        new(true, HandshakeRejectReason.None, FeatureFlags.None);

    public static HandshakeValidationResult Rejected(
        HandshakeRejectReason rejectReason,
        FeatureFlags missingFeatures = FeatureFlags.None) =>
        new(false, rejectReason, missingFeatures);
}

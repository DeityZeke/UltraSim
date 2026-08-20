namespace UltraSim.Network
{
    public readonly struct HandshakeValidationResult
    {
        public readonly bool Accepted;
        public readonly HandshakeRejectReason RejectReason;
        public readonly FeatureFlags MissingFeatures;

        private HandshakeValidationResult(
            bool accepted,
            HandshakeRejectReason rejectReason,
            FeatureFlags missingFeatures)
        {
            Accepted = accepted;
            RejectReason = rejectReason;
            MissingFeatures = missingFeatures;
        }

        public static HandshakeValidationResult AcceptedResult() =>
            new HandshakeValidationResult(true, HandshakeRejectReason.None, FeatureFlags.None);

        public static HandshakeValidationResult Rejected(
            HandshakeRejectReason reason,
            FeatureFlags missingFeatures = FeatureFlags.None) =>
            new HandshakeValidationResult(false, reason, missingFeatures);
    }
}

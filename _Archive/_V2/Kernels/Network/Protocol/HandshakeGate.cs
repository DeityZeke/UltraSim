namespace UltraSim.Network;

public static class HandshakeGate
{
    public static HandshakeValidationResult Validate(
        in HandshakeHelloPacket hello,
        in HandshakePolicy policy)
    {
        if (hello.ProtocolVersion != policy.ProtocolVersion)
            return HandshakeValidationResult.Rejected(HandshakeRejectReason.ProtocolVersionMismatch);

        if (hello.SchemaHash != policy.SchemaHash)
            return HandshakeValidationResult.Rejected(HandshakeRejectReason.SchemaHashMismatch);

        if (hello.ContentHash != policy.ContentHash)
            return HandshakeValidationResult.Rejected(HandshakeRejectReason.ContentHashMismatch);

        FeatureFlags missing = policy.RequiredFeatures & ~hello.Features;
        if (missing != FeatureFlags.None)
            return HandshakeValidationResult.Rejected(
                HandshakeRejectReason.MissingRequiredFeatures,
                missing);

        return HandshakeValidationResult.AcceptedResult();
    }
}

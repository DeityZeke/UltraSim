using System.Runtime.CompilerServices;

namespace UltraSim.Network
{
    public static class HandshakeGate
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

            FeatureFlags missingFeatures = policy.RequiredFeatures & ~hello.Features;
            if (missingFeatures != FeatureFlags.None)
                return HandshakeValidationResult.Rejected(
                    HandshakeRejectReason.MissingRequiredFeatures,
                    missingFeatures);

            return HandshakeValidationResult.AcceptedResult();
        }
    }
}

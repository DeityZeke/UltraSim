namespace UltraSim.Shared.Engines.Networking
{
    public static class MovementProtocolConstants
    {
        public const float PositionQuantizationScale = 100.0f;
        public const float InputDirectionQuantizationScale = 32767.0f;
        public const float PlayerMoveSpeedMetersPerSecond = 3.5f;
        public const int FixedSimulationTickRateHz = 100;
        public const float FixedSimulationTickSeconds = 1.0f / FixedSimulationTickRateHz;
        public const float PositionChangeEpsilonMeters = 0.005f;
        public const float YawChangeEpsilonRadians = 0.01f;
    }
}

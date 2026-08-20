namespace UltraSim.Client.Engines.Networking
{
    public readonly struct EntityFloat3PredictedState
    {
        public EntityFloat3PredictedState(float x, float y, float z, float yawRadians)
        {
            X = x;
            Y = y;
            Z = z;
            YawRadians = yawRadians;
        }

        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public float YawRadians { get; }
    }
}

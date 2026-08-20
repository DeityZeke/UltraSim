using UltraSim.Client.Runtime;

namespace UltraSim.Client.Engines.Networking
{
    public readonly struct EntityFloat3InterpolatedPose
    {
        public EntityFloat3InterpolatedPose(in ClientWorldPose pose)
        {
            Pose = pose;
        }

        public ClientWorldPose Pose { get; }
    }
}

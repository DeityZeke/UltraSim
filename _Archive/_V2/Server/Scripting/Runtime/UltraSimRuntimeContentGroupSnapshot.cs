namespace UltraSim.Server.Scripting;

public sealed record UltraSimRuntimeContentGroupSnapshot(
    UltraSimRuntimeContentGroupDescriptor Descriptor,
    UltraSimRuntimeContentGroupMountState MountState,
    DateTimeOffset UpdatedUtc)
{
    public bool IsMounted => MountState == UltraSimRuntimeContentGroupMountState.Mounted;
}

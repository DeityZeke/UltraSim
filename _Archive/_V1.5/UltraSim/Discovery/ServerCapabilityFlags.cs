using System;

namespace UltraSim.Discovery
{
    [Flags]
    public enum ServerCapabilityFlags : ushort
    {
        None = 0,
        SupportsDirectConnect = 1 << 0,
        SupportsPassword = 1 << 1,
        SupportsGatewayAuth = 1 << 2,
        SupportsCharacterCreationAtJoin = 1 << 3,
        SupportsMods = 1 << 4,
        RequiresLauncher = 1 << 5,
        SupportsVoice = 1 << 6,
    }
}

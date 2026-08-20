using System.Runtime.CompilerServices;

namespace UltraSim.ECS;

public readonly struct SystemDescriptor
{
    public string Name { get; }
    public SystemStage Stage { get; }
    public int DefaultPeriodTicks { get; }
    public int DefaultStartOffset { get; }
    public ComponentMask ReadMask { get; }
    public ComponentMask WriteMask { get; }
    public Type[] Requires { get; }
    public bool Manual { get; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SystemDescriptor(
        string name,
        SystemStage stage,
        int defaultPeriodTicks,
        int defaultStartOffset,
        ComponentMask readMask,
        ComponentMask writeMask,
        Type[]? requires = null,
        bool manual = false)
    {
        Name = name;
        Stage = stage;
        DefaultPeriodTicks = defaultPeriodTicks;
        DefaultStartOffset = defaultStartOffset;
        ReadMask = readMask;
        WriteMask = writeMask;
        Requires = requires ?? Array.Empty<Type>();
        Manual = manual;
    }
}

using System;
using System.Runtime.CompilerServices;

namespace UltraSim.ECS
{
    public readonly struct SystemDescriptor
    {
        public readonly string Name;
        public readonly SystemStage Stage;
        public readonly int DefaultPeriodTicks;
        public readonly int DefaultStartOffset;
        public readonly ComponentMask ReadMask;
        public readonly ComponentMask WriteMask;
        public readonly Type[] Requires;
        public readonly bool Manual;

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
}

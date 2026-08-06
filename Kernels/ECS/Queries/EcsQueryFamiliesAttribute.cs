using System.ComponentModel;

namespace UltraSim.ECS;

/// <summary>Build marker requesting the kernel-owned generated query families.</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class EcsQueryFamiliesAttribute : Attribute
{
}

namespace UltraSim.ECS;

internal static class EntityGeneration
{
    internal const uint First = 1;

    /// <summary>
    /// Advances a live slot generation. Returning false retires the slot rather
    /// than allowing a stale handle to become valid after wrap-around.
    /// </summary>
    internal static bool TryAdvance(uint current, out uint next)
    {
        if (current == 0 || current == uint.MaxValue)
        {
            next = 0;
            return false;
        }

        next = current + 1;
        return true;
    }
}

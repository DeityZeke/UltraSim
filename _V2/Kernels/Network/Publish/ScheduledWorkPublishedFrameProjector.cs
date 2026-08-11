namespace UltraSim.Network;

public static class ScheduledWorkPublishedFrameProjector
{
    public static int Project(
        ReadOnlySpan<ScheduledWorkItem> items,
        ReadOnlySpan<SubscriptionKey> scopeKeys,
        Span<ScopedPublishedFrame> destination)
    {
        if (items.Length != scopeKeys.Length)
            throw new ArgumentException("Items and scope key spans must have the same length.");

        int count = Math.Min(items.Length, destination.Length);
        for (int i = 0; i < count; i++)
        {
            SubscriptionKey scopeKey = scopeKeys[i];
            if (!scopeKey.IsValid)
                throw new ArgumentOutOfRangeException(nameof(scopeKeys));

            destination[i] = new ScopedPublishedFrame(scopeKey, SharedFramePublisher.Publish(items[i]));
        }

        return count;
    }
}

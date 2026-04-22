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

            ScheduledWorkItem item = items[i];
            PublishedFrame frame = item.Kind switch
            {
                ScheduledWorkKind.LatestState => SharedFramePublisher.PublishLatestState(item.Sequence, item.ItemKey, item.Payload),
                ScheduledWorkKind.OrderedEvent => SharedFramePublisher.PublishOrderedEvent(item.Sequence, item.Payload),
                _ => throw new InvalidOperationException($"Unsupported scheduled work kind: {item.Kind}."),
            };

            destination[i] = new ScopedPublishedFrame(scopeKey, frame);
        }

        return count;
    }
}

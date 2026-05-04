using UltraSim.Network;

namespace UltraSim.Server.Network;

public sealed class ServerChatService
{
    private readonly OrderedEventStream _orderedEvents;
    private readonly ServerCommandRegistry _commands;
    private readonly ServerChatOptions _options;
    private readonly Dictionary<long, long> _lastSubmitTicksBySession = new();
    private ulong _nextMessageId = 1;

    public ServerChatService(
        OrderedEventStream orderedEvents,
        ServerCommandRegistry? commands = null,
        ServerChatOptions? options = null)
    {
        _orderedEvents = orderedEvents ?? throw new ArgumentNullException(nameof(orderedEvents));
        _commands = commands ?? new ServerCommandRegistry();
        _options = options ?? ServerChatOptions.Default;
        if (_options.MaxTextLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(options));
        if (_options.MinTicksBetweenMessages < 0)
            throw new ArgumentOutOfRangeException(nameof(options));
    }

    public ServerCommandRegistry Commands => _commands;
    public long ReceivedMessages { get; private set; }
    public long PublishedMessages { get; private set; }
    public long CommandDispatches { get; private set; }
    public long RateLimitedMessages { get; private set; }
    public long RejectedMessages { get; private set; }

    public bool TryHandleSubmit(
        in ServerChatSenderContext sender,
        string rawText,
        SubscriptionKey localScope,
        long nowTicks,
        ulong serverTick)
    {
        string text = Sanitize(rawText);
        if (text.Length == 0)
        {
            RejectedMessages++;
            return false;
        }

        if (_lastSubmitTicksBySession.TryGetValue(sender.SessionId, out long lastTicks) &&
            nowTicks - lastTicks < _options.MinTicksBetweenMessages)
        {
            RateLimitedMessages++;
            return false;
        }

        _lastSubmitTicksBySession[sender.SessionId] = nowTicks;
        ReceivedMessages++;

        if (_options.EnableCommands)
        {
            var request = new ServerCommandRequest(
                ServerCommandSource.ChatInput,
                text,
                sender.AccessLevel,
                sender.SessionId,
                sender.ConnectionId,
                sender.EntityId,
                sender.DisplayName,
                AccessRuleKeys: sender.AccessRuleKeys,
                DirectPermissionGrants: sender.DirectPermissionGrants,
                DirectPermissionDenies: sender.DirectPermissionDenies,
                Reply: reply => PublishSystemTo(localScope, reply, serverTick));

            if (_commands.TryDispatch(in request, out ServerCommandDispatchResult result))
            {
                CommandDispatches++;
                if (result.Status is not ServerCommandDispatchStatus.Executed &&
                    !string.IsNullOrWhiteSpace(result.Error))
                {
                    PublishSystemTo(localScope, result.Error, serverTick);
                }

                return true;
            }
        }

        return PublishLocal(localScope, sender.EntityId, sender.DisplayName, text, serverTick);
    }

    public bool PublishLocal(
        SubscriptionKey scope,
        ulong senderEntityId,
        string senderName,
        string text,
        ulong serverTick)
    {
        if (!scope.IsValid)
        {
            RejectedMessages++;
            return false;
        }

        return Publish(
            scope,
            ServerChatChannelIds.Local,
            senderEntityId,
            senderName,
            text,
            serverTick,
            ChatMessageFlags.Local | ChatMessageFlags.Overhead);
    }

    public bool PublishSystemAnnouncement(string text, ulong serverTick = 0)
    {
        return Publish(
            ServerChatScopes.System,
            ServerChatChannelIds.System,
            senderEntityId: 0,
            senderName: "server",
            text,
            serverTick,
            ChatMessageFlags.System | ChatMessageFlags.Announcement);
    }

    public ServerChatMetricsSnapshot GetMetricsSnapshot()
    {
        return new ServerChatMetricsSnapshot(
            ReceivedMessages,
            PublishedMessages,
            CommandDispatches,
            RateLimitedMessages,
            RejectedMessages);
    }

    private bool PublishSystemTo(SubscriptionKey scope, string text, ulong serverTick)
    {
        if (!scope.IsValid)
            scope = ServerChatScopes.System;

        return Publish(
            scope,
            ServerChatChannelIds.System,
            senderEntityId: 0,
            senderName: "server",
            text,
            serverTick,
            ChatMessageFlags.System);
    }

    private bool Publish(
        SubscriptionKey scope,
        ushort channelId,
        ulong senderEntityId,
        string senderName,
        string text,
        ulong serverTick,
        ChatMessageFlags flags)
    {
        string clean = Sanitize(text);
        if (clean.Length == 0)
        {
            RejectedMessages++;
            return false;
        }

        var packet = new ChatMessageEventPacket(
            _nextMessageId++,
            channelId,
            senderEntityId,
            senderName ?? string.Empty,
            clean,
            serverTick,
            flags);

        // Reliable-event payloads are shared by gameplay events and chat, so chat keeps
        // its own packet envelope inside the ordered-event body for client-side routing.
        _orderedEvents.Publish(scope, PacketClass.ReliableEvent, PacketCodec.Encode(packet));
        PublishedMessages++;
        return true;
    }

    private string Sanitize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        string trimmed = text.Trim();
        return trimmed.Length <= _options.MaxTextLength
            ? trimmed
            : trimmed[.._options.MaxTextLength];
    }
}

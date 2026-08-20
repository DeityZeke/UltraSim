using System;
using System.Collections.Generic;
using UltraSim.Network.Protocol;
using UltraSim.Network;

namespace UltraSim.Server.Chat
{
    public sealed class ServerChatService
    {
        public readonly struct SenderContext
        {
            public SenderContext(
                long sessionId,
                int connectionId,
                ulong senderEntityId,
                string senderName,
                int accessLevel = 0)
            {
                SessionId = sessionId;
                ConnectionId = connectionId;
                SenderEntityId = senderEntityId;
                SenderName = senderName ?? string.Empty;
                AccessLevel = accessLevel;
            }

            public long SessionId { get; }
            public int ConnectionId { get; }
            public ulong SenderEntityId { get; }
            public string SenderName { get; }
            public int AccessLevel { get; }
        }

        private readonly INetworkTransport _transport;
        private readonly NetworkSessionManager _sessions;
        private readonly Func<long, string>? _displayNameResolver;
        private readonly Func<long, SenderContext?>? _senderContextResolver;
        private readonly Func<ulong, List<int>, int>? _localRecipientCollector;
        private readonly ServerChatCommandRegistry? _commandRegistry;
        private readonly Dictionary<long, long> _lastChatTicksBySession = new();
        private readonly Dictionary<ulong, long> _lastDenseLocalChatTickBySender = new();
        private readonly Dictionary<ulong, int> _localChatCursorBySender = new();
        private readonly Dictionary<int, List<byte[]>> _pendingByConnection = new();
        private readonly Stack<List<byte[]>> _pendingListPool = new();
        private readonly List<int> _connectionScratch = new(64);
        private readonly List<int> _selectionScratch = new(64);

        public ServerChatService(
            INetworkTransport transport,
            NetworkSessionManager sessions,
            Func<long, string>? displayNameResolver = null,
            long minTicksBetweenMessages = 8)
            : this(
                transport,
                sessions,
                displayNameResolver,
                senderContextResolver: null,
                localRecipientCollector: null,
                commandRegistry: null,
                new ServerChatOptions(MinTicksBetweenMessages: minTicksBetweenMessages))
        {
        }

        public ServerChatService(
            INetworkTransport transport,
            NetworkSessionManager sessions,
            Func<long, string>? displayNameResolver,
            Func<long, SenderContext?>? senderContextResolver,
            Func<ulong, List<int>, int>? localRecipientCollector,
            ServerChatCommandRegistry? commandRegistry,
            ServerChatOptions? options = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            _displayNameResolver = displayNameResolver;
            _senderContextResolver = senderContextResolver;
            _localRecipientCollector = localRecipientCollector;
            _commandRegistry = commandRegistry;

            ServerChatOptions resolvedOptions = options ?? ServerChatOptions.Default;
            if (resolvedOptions.MinTicksBetweenMessages < 0)
                throw new ArgumentOutOfRangeException(nameof(options));

            Options = resolvedOptions;
            MinTicksBetweenMessages = resolvedOptions.MinTicksBetweenMessages;
        }

        public ServerChatOptions Options { get; }
        public long MinTicksBetweenMessages { get; }
        public long ReceivedMessageCount { get; private set; }
        public long SentMessageCount { get; private set; }

        public bool TryHandleInboundDatagram(long sessionId, in InboundDatagram datagram, long nowTicks)
        {
            if (datagram.Channel != ChannelType.ReliableOrdered)
                return false;

            if (!NetworkControlMessageCodec.TryDecodeClientChat(datagram.Payload.Span, out string text))
                return false;

            text = text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
                return true;

            if (!_sessions.TryGetSnapshot(sessionId, out SessionSnapshot snapshot))
                return true;

            if (_lastChatTicksBySession.TryGetValue(sessionId, out long lastTick))
            {
                if ((nowTicks - lastTick) < MinTicksBetweenMessages)
                    return true;
            }

            _lastChatTicksBySession[sessionId] = nowTicks;
            ReceivedMessageCount++;

            SenderContext? senderContext = _senderContextResolver?.Invoke(sessionId);
            if (senderContext.HasValue &&
                TryDispatchCommand(text, senderContext.Value))
                return true;

            if (senderContext.HasValue)
            {
                BroadcastLocal(senderContext.Value.SenderEntityId, $"{senderContext.Value.SenderName}: {text}", nowTicks);
                return true;
            }

            string senderName = _displayNameResolver?.Invoke(sessionId) ?? $"Player {sessionId}";
            BroadcastSystem($"{senderName}: {text}");
            return true;
        }

        public bool SendSystemToSession(long sessionId, string text)
        {
            if (!_sessions.TryGetSnapshot(sessionId, out SessionSnapshot snapshot))
                return false;

            return QueueToConnection(snapshot.ConnectionId, ServerChatCodec.ChatScopeSystem, 0, text);
        }

        public int BroadcastSystem(string text) =>
            Broadcast(ServerChatCodec.ChatScopeSystem, 0, text, nowTicks: 0);

        public int BroadcastLocal(ulong senderEntityId, string text, long nowTicks = 0) =>
            Broadcast(ServerChatCodec.ChatScopeLocal, senderEntityId, text, nowTicks);

        public void FlushPending()
        {
            if (_pendingByConnection.Count == 0)
                return;

            foreach ((int connectionId, List<byte[]> pending) in _pendingByConnection)
            {
                if (pending.Count == 1)
                {
                    byte[] singlePayload = pending[0];
                    _transport.EnqueueOutbound(new OutboundDatagram(
                        connectionId,
                        ChannelType.ReliableOrdered,
                        singlePayload));
                    continue;
                }

                if (Options.EnableBatching)
                {
                    int totalBytes = ServerChatCodec.ServerChatBatchHeaderBytes;
                    for (int i = 0; i < pending.Count; i++)
                        totalBytes += 2 + pending[i].Length;

                    byte[] batchPayload = new byte[totalBytes];
                    if (ServerChatCodec.TryEncodeChatBatch(batchPayload, pending, out int batchWritten) && batchWritten > 0)
                    {
                        _transport.EnqueueOutbound(new OutboundDatagram(
                            connectionId,
                            ChannelType.ReliableOrdered,
                            new ReadOnlyMemory<byte>(batchPayload, 0, batchWritten)));
                        continue;
                    }
                }

                for (int i = 0; i < pending.Count; i++)
                {
                    _transport.EnqueueOutbound(new OutboundDatagram(
                        connectionId,
                        ChannelType.ReliableOrdered,
                        pending[i]));
                }
            }

            foreach ((int _, List<byte[]> pending) in _pendingByConnection)
                ReturnPendingList(pending);

            _pendingByConnection.Clear();
        }

        public void HandleSessionDisconnected(long sessionId, int connectionId)
        {
            _lastChatTicksBySession.Remove(sessionId);
            SenderContext? senderContext = _senderContextResolver?.Invoke(sessionId);
            if (senderContext.HasValue)
            {
                _lastDenseLocalChatTickBySender.Remove(senderContext.Value.SenderEntityId);
                _localChatCursorBySender.Remove(senderContext.Value.SenderEntityId);
            }

            if (_pendingByConnection.TryGetValue(connectionId, out List<byte[]>? pending))
            {
                ReturnPendingList(pending);
                _pendingByConnection.Remove(connectionId);
            }
        }

        public void Reset()
        {
            _lastChatTicksBySession.Clear();
            _lastDenseLocalChatTickBySender.Clear();
            _localChatCursorBySender.Clear();
            foreach ((int _, List<byte[]> pending) in _pendingByConnection)
                ReturnPendingList(pending);
            _pendingByConnection.Clear();
            ReceivedMessageCount = 0;
            SentMessageCount = 0;
        }

        private int Broadcast(byte scope, ulong senderEntityId, string text, long nowTicks)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            CollectRecipients(scope, senderEntityId, _connectionScratch);
            if (_connectionScratch.Count == 0)
                return 0;

            if (scope == ServerChatCodec.ChatScopeLocal && senderEntityId != 0)
            {
                if (Options.EnableDenseLocalChatRateCap &&
                    _connectionScratch.Count >= Math.Max(1, Options.DenseLocalChatRateCapStartRecipients))
                {
                    int minTicks = Math.Max(1, Options.DenseLocalChatRateCapMinTicks);
                    if (_lastDenseLocalChatTickBySender.TryGetValue(senderEntityId, out long lastTick) &&
                        (nowTicks - lastTick) < minTicks)
                    {
                        return 0;
                    }

                    _lastDenseLocalChatTickBySender[senderEntityId] = nowTicks;
                }

                if (Options.EnableDenseLocalChatRecipientCap)
                {
                    int capStart = Math.Max(1, Options.DenseLocalChatCapStartRecipients);
                    int capMax = Math.Max(1, Options.DenseLocalChatCapMaxRecipients);
                    if (_connectionScratch.Count >= capStart && capMax < _connectionScratch.Count)
                    {
                        int originalCount = _connectionScratch.Count;
                        int startIndex = 0;
                        if (_localChatCursorBySender.TryGetValue(senderEntityId, out int cursor))
                            startIndex = cursor;
                        if ((uint)startIndex >= (uint)originalCount)
                            startIndex = 0;

                        _selectionScratch.Clear();
                        for (int offset = 0; offset < originalCount && _selectionScratch.Count < capMax; offset++)
                        {
                            int idx = startIndex + offset;
                            if (idx >= originalCount)
                                idx -= originalCount;
                            _selectionScratch.Add(_connectionScratch[idx]);
                        }

                        _connectionScratch.Clear();
                        _connectionScratch.AddRange(_selectionScratch);

                        int nextCursor = startIndex + _connectionScratch.Count;
                        if (nextCursor >= originalCount)
                            nextCursor %= originalCount;
                        _localChatCursorBySender[senderEntityId] = nextCursor;
                    }
                }
            }

            int sent = 0;
            for (int i = 0; i < _connectionScratch.Count; i++)
            {
                if (QueueToConnection(_connectionScratch[i], scope, senderEntityId, text))
                    sent++;
            }

            return sent;
        }

        private void CollectRecipients(byte scope, ulong senderEntityId, List<int> destination)
        {
            destination.Clear();
            if (scope == ServerChatCodec.ChatScopeLocal && senderEntityId != 0 && _localRecipientCollector != null)
            {
                _localRecipientCollector(senderEntityId, destination);
                return;
            }

            Dictionary<int, long> connections = _sessions.CopyConnectionSnapshot();
            foreach (int connectionId in connections.Keys)
                destination.Add(connectionId);
        }

        private bool TryDispatchCommand(string text, in SenderContext senderContext)
        {
            if (_commandRegistry == null || !Options.EnableCommands)
                return false;

            long sessionId = senderContext.SessionId;
            int connectionId = senderContext.ConnectionId;
            ulong senderEntityId = senderContext.SenderEntityId;
            int accessLevel = senderContext.AccessLevel;
            string senderName = senderContext.SenderName;

            var commandContext = new ChatCommandContext(
                sessionId,
                connectionId,
                senderEntityId,
                accessLevel,
                senderName,
                reply: message =>
                {
                    if (!string.IsNullOrWhiteSpace(message))
                        QueueToConnection(connectionId, ServerChatCodec.ChatScopeSystem, 0, $"[server] {message.Trim()}");
                });

            if (!_commandRegistry.TryDispatch(text, in commandContext, out ChatCommandDispatchResult result))
                return false;

            if (result.Status != ChatCommandDispatchStatus.Executed)
            {
                string error = string.IsNullOrWhiteSpace(result.Error) ? "Command failed." : result.Error.Trim();
                QueueToConnection(senderContext.ConnectionId, ServerChatCodec.ChatScopeSystem, 0, $"[server] {error}");
            }

            return true;
        }

        private bool QueueToConnection(int connectionId, byte scope, ulong senderEntityId, string text)
        {
            byte[] payload = new byte[ServerChatCodec.ServerChatHeaderBytes + ServerChatCodec.ChatMaxUtf8Bytes];
            if (!ServerChatCodec.TryEncodeChat(payload, scope, senderEntityId, text, out int written) || written <= 0)
                return false;

            var exactPayload = new byte[written];
            payload.AsSpan(0, written).CopyTo(exactPayload);

            if (!_pendingByConnection.TryGetValue(connectionId, out List<byte[]>? pending))
            {
                pending = RentPendingList();
                _pendingByConnection[connectionId] = pending;
            }

            pending.Add(exactPayload);
            SentMessageCount++;
            return true;
        }

        private List<byte[]> RentPendingList()
        {
            if (_pendingListPool.Count > 0)
            {
                List<byte[]> pooled = _pendingListPool.Pop();
                pooled.Clear();
                return pooled;
            }

            return new List<byte[]>(4);
        }

        private void ReturnPendingList(List<byte[]> pending)
        {
            pending.Clear();
            _pendingListPool.Push(pending);
        }
    }
}

using System.Net;
using System.Net.Sockets;
using UltraSim.Network;

internal static class N010SharedUdpListenerTests
{
    private static readonly NetworkMessageId s_reliable = new(1);
    private static readonly NetworkMessageId s_latest = new(2);
    private static readonly NetworkMessageId s_independent = new(3);
    private static readonly NetworkProtocolManifest s_manifest = NetworkProtocolManifest.Create(
        8,
        [
            new NetworkMessageDescriptor(s_reliable, "ultrasim.test.shared-control", 1,
                NetworkLane.Control, 32, 0x1010),
            new NetworkMessageDescriptor(s_latest, "ultrasim.test.shared-owner", 1,
                NetworkLane.OwnerState, 32, 0x2020),
            new NetworkMessageDescriptor(s_independent, "ultrasim.test.shared-world", 1,
                NetworkLane.WorldState, 32, 0x3030),
        ]);
    private static readonly NetworkProtocolLimits s_protocol = new(
        8, 8, 64, 1, 64, 16, 16, 2, 64, 4, 32, 4);
    private static readonly UdpProtocolLimits s_udp = new(
        512, 16 * 1024, 16 * 1024, 4_096, 30_000, 10_000, 1_000, 5_000);

    internal static void RunAll()
    {
        Run("SharedUdpListenerRoutesIndependentSecureSessions", SharedUdpListenerRoutesIndependentSecureSessions);
        Run("SharedUdpListenerCapacityAndUnknownPathsAreBounded", SharedUdpListenerCapacityAndUnknownPathsAreBounded);
        Run("SharedUdpTrustAndAggregateSocketBudgetsAreExplicit", SharedUdpTrustAndAggregateSocketBudgetsAreExplicit);
        Run("SharedUdpFailedHandshakeReleasesCapacity", SharedUdpFailedHandshakeReleasesCapacity);
        Run("SharedUdpSessionDrainDoesNotRetirePeerSession", SharedUdpSessionDrainDoesNotRetirePeerSession);
        Run("SharedUdpRetiredCapacityAdvancesGeneration", SharedUdpRetiredCapacityAdvancesGeneration);
        Run("SharedUdpAggregateListenerCapacityPreservesBurst", SharedUdpAggregateListenerCapacityPreservesBurst);
        Run("WarmedSharedUdpDeliveryAllocatesZero", WarmedSharedUdpDeliveryAllocatesZero);
        Run("SharedUdpNativeSendBatchPreservesIndependentDatagrams", SharedUdpNativeSendBatchPreservesIndependentDatagrams);
        Run("SharedUdpNativeSendBatchBackpressureIsPerDatagram", SharedUdpNativeSendBatchBackpressureIsPerDatagram);
        Run("SharedUdpNativeSendBatchRetirementPurgesQueuedPath", SharedUdpNativeSendBatchRetirementPurgesQueuedPath);
        Run("WarmedSharedUdpNativeSendBatchAllocatesZero", WarmedSharedUdpNativeSendBatchAllocatesZero);
        Run("SharedUdpNativeReceiveBatchPreservesIndependentDatagrams", SharedUdpNativeReceiveBatchPreservesIndependentDatagrams);
        Run("SharedUdpNativeReceiveBatchBackpressureRemainsPerSession", SharedUdpNativeReceiveBatchBackpressureRemainsPerSession);
        Run("WarmedSharedUdpNativeReceiveBatchAllocatesZero", WarmedSharedUdpNativeReceiveBatchAllocatesZero);
        Run("SharedUdpBackgroundScalarCopiesAndSendsIndependentDatagrams", SharedUdpBackgroundScalarCopiesAndSendsIndependentDatagrams);
        Run("SharedUdpBackgroundNativeBatchCopiesAndSendsIndependentDatagrams", SharedUdpBackgroundNativeBatchCopiesAndSendsIndependentDatagrams);
        Run("SharedUdpBackgroundModesPreserveSecureSessions", SharedUdpBackgroundModesPreserveSecureSessions);
        Run("SharedUdpBackgroundQueueRefusalIsBounded", SharedUdpBackgroundQueueRefusalIsBounded);
        Run("SharedUdpBackgroundQueueRefusalDoesNotAdvanceProtocolSend", SharedUdpBackgroundQueueRefusalDoesNotAdvanceProtocolSend);
        Run("SharedUdpBackgroundRetirementFencesQueuedPath", SharedUdpBackgroundRetirementFencesQueuedPath);
        Run("SharedUdpBackgroundRebindFencesOldQueuedPath", SharedUdpBackgroundRebindFencesOldQueuedPath);
        Run("SharedUdpBackgroundSecurityEpochRotationPreservesQueueOrder", SharedUdpBackgroundSecurityEpochRotationPreservesQueueOrder);
        Run("WarmedSharedUdpBackgroundProducerAllocatesZero", WarmedSharedUdpBackgroundProducerAllocatesZero);
        Run("SharedUdpBackgroundNativePartialAndWouldBlockRetainSuffix", SharedUdpBackgroundNativePartialAndWouldBlockRetainSuffix);
        Run("SharedUdpBackgroundPhysicalReliableLossRetransmits", SharedUdpBackgroundPhysicalReliableLossRetransmits);
        Run("SharedUdpBackgroundShutdownDrainsAndIsIdempotent", SharedUdpBackgroundShutdownDrainsAndIsIdempotent);
        Run("SharedUdpBackgroundWorkerFailureSurvivesCleanup", SharedUdpBackgroundWorkerFailureSurvivesCleanup);
    }

    private static void SharedUdpTrustAndAggregateSocketBudgetsAreExplicit()
    {
        using NetworkServerIdentity identity = NetworkServerIdentity.CreateEphemeral();
        bool rejectedUnpinned = false;
        try
        {
            using var _ = new SharedUdpProtocolHost(
                s_protocol, s_udp, new SharedUdpProtocolLimits(1, 4, 16),
                s_manifest, identity);
        }
        catch (ArgumentException)
        {
            rejectedUnpinned = true;
        }
        True(rejectedUnpinned, "The ordinary host constructor silently self-trusted an identity.");

        var shared = new SharedUdpProtocolLimits(
            MaximumSessions: 2,
            MaximumQueuedDatagramsPerSession: 4,
            MaximumInboundDatagramsPerPump: 32,
            AggregateSocketSendBufferBytes: 32 * 1024,
            AggregateSocketReceiveBufferBytes: 64 * 1024);
        using SharedUdpProtocolHost host = SharedUdpProtocolHost.CreateSelfTrustedLoopback(
            s_protocol, s_udp, shared, s_manifest);
        True(host.TryAddLoopbackSession(out _, out _));
        SharedUdpProtocolHostSnapshot snapshot = host.GetSnapshot();
        Equal(96L * 1024, snapshot.ConfiguredListenerSocketBufferBytes);
        Equal(32L * 1024, snapshot.ConfiguredClientSocketBufferBytes);
        Equal(128L * 1024, snapshot.ConfiguredSocketBufferBytes);
        Equal(snapshot.KnownReservedBytes,
            checked(snapshot.KnownServerReservedBytes + snapshot.KnownClientReservedBytes));
        True(snapshot.KnownServerReservedBytes > 0 && snapshot.KnownClientReservedBytes > 0);
        True(snapshot.Listener.ActualSocketBufferBytes > 0);
    }

    private static void SharedUdpListenerRoutesIndependentSecureSessions()
    {
        using SharedUdpProtocolHost host = CreateHost(maximumSessions: 2);
        True(host.TryAddLoopbackSession(out SharedUdpProtocolSession? first, out ProtocolHandshakeSnapshot firstHandshake));
        True(host.TryAddLoopbackSession(out SharedUdpProtocolSession? second, out ProtocolHandshakeSnapshot secondHandshake));
        True(first is not null && second is not null);
        SharedUdpProtocolSession firstSession = first!;
        SharedUdpProtocolSession secondSession = second!;
        True(firstHandshake.Established && secondHandshake.Established);
        False(firstSession.Connection == secondSession.Connection,
            "A shared server connection table reused one live identity.");

        byte[] firstPayload = [1, 2, 3];
        byte[] secondPayload = [9, 8, 7, 6];
        Equal(NetworkQueueStatus.Accepted, firstSession.Client.TryQueue(s_reliable, firstPayload));
        Equal(NetworkQueueStatus.Accepted, secondSession.Server.TryQueue(s_reliable, secondPayload));
        Drive(host, () => firstSession.Server.ReceivedQueued == 1 && secondSession.Client.ReceivedQueued == 1);
        AssertReceive(firstSession.Server, firstPayload);
        AssertReceive(secondSession.Client, secondPayload);
        Drive(host, () => firstSession.Client.ReliableQueued == 0 && secondSession.Server.ReliableQueued == 0);

        SharedUdpProtocolHostSnapshot snapshot = host.GetSnapshot();
        Equal(2, snapshot.Sessions);
        Equal(2, snapshot.Listener.RegisteredPaths);
        Equal(2, snapshot.Connections.Active);
        Equal(4L * (s_udp.SocketSendBufferBytes + s_udp.SocketReceiveBufferBytes),
            snapshot.ConfiguredSocketBufferBytes,
            "The listener must retain the aggregate configured socket capacity that the two server sockets replaced.");
        True(snapshot.Listener.Sent > 0 && snapshot.Listener.Received > 0);
        True(snapshot.Listener.SentBytes > 0 && snapshot.Listener.ReceivedBytes > 0);
        True(snapshot.Listener.SendCalls > 0);
        True(snapshot.Listener.ReceivePollCalls >= snapshot.Listener.ReceiveCalls);
        True(snapshot.Listener.ReceiveCalls > 0);
        True(snapshot.Listener.MaximumSentDatagramBytes > 0);
        True(snapshot.Listener.MaximumReceivedDatagramBytes > 0);
    }

    private static void SharedUdpListenerCapacityAndUnknownPathsAreBounded()
    {
        using SharedUdpProtocolHost host = CreateHost(maximumSessions: 1);
        True(host.TryAddLoopbackSession(out SharedUdpProtocolSession? admitted, out _));
        True(admitted is not null);
        SharedUdpProtocolSession admittedSession = admitted!;
        False(host.TryAddLoopbackSession(out SharedUdpProtocolSession? rejected, out _));
        True(rejected is null);
        Equal(1, host.GetSnapshot().Connections.Active);

        using var hostile = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        hostile.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        _ = hostile.SendTo(new byte[96], host.ListenerLocalEndPoint);
        host.Pump();
        SharedUdpProtocolHostSnapshot snapshot = host.GetSnapshot();
        Equal(1UL, snapshot.Listener.UnknownPathDatagrams);
        Equal(0UL, admittedSession.GetSnapshot().Server.AuthenticationFailures,
            "An unknown source path entered an admitted secure peer.");
    }

    private static void SharedUdpSessionDrainDoesNotRetirePeerSession()
    {
        using SharedUdpProtocolHost host = CreateHost(maximumSessions: 2);
        True(host.TryAddLoopbackSession(out SharedUdpProtocolSession? first, out _));
        True(host.TryAddLoopbackSession(out SharedUdpProtocolSession? second, out _));
        True(first is not null && second is not null);
        SharedUdpProtocolSession firstSession = first!;
        SharedUdpProtocolSession secondSession = second!;
        firstSession.BeginDrain();
        Equal(NetworkQueueStatus.NotAccepting,
            firstSession.Client.TryQueue(s_reliable, stackalloc byte[] { 1 }));
        Drive(host, () => firstSession.State == UdpProtocolSessionState.Closed);
        Equal(UdpProtocolSessionState.Established, secondSession.State);
        Equal(NetworkQueueStatus.Accepted,
            secondSession.Client.TryQueue(s_reliable, stackalloc byte[] { 4, 5 }));
        Drive(host, () => secondSession.Server.ReceivedQueued == 1);
        AssertReceive(secondSession.Server, stackalloc byte[] { 4, 5 });
        firstSession.Dispose();
        Equal(1, host.GetSnapshot().Connections.Active);
    }

    private static void SharedUdpFailedHandshakeReleasesCapacity()
    {
        using NetworkServerIdentity serverIdentity = NetworkServerIdentity.CreateEphemeral();
        using NetworkServerIdentity unrelatedIdentity = NetworkServerIdentity.CreateEphemeral();
        using var host = new SharedUdpProtocolHost(
            s_protocol,
            s_udp with { MaximumHandshakePumps = 64 },
            new SharedUdpProtocolLimits(1, 4, 16),
            s_manifest,
            serverIdentity,
            unrelatedIdentity.PublicKey.ToArray());

        False(host.TryAddLoopbackSession(out SharedUdpProtocolSession? rejected, out _));
        True(rejected is null);
        SharedUdpProtocolHostSnapshot snapshot = host.GetSnapshot();
        Equal(0, snapshot.Sessions);
        Equal(0, snapshot.Listener.RegisteredPaths);
        Equal(0, snapshot.Connections.Active);
        Equal(1, snapshot.Connections.Available);
    }

    private static void WarmedSharedUdpDeliveryAllocatesZero()
    {
        using SharedUdpProtocolHost host = CreateHost(maximumSessions: 4);
        var sessions = new SharedUdpProtocolSession[4];
        for (int index = 0; index < sessions.Length; index++)
        {
            True(host.TryAddLoopbackSession(out SharedUdpProtocolSession? session, out _));
            sessions[index] = session!;
        }
        for (int iteration = 0; iteration < 32; iteration++)
            RoundTrip(iteration);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 128; iteration++)
            RoundTrip(iteration);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Equal(0L, allocated,
            "The warmed shared-listener secure-delivery path allocated managed memory.");

        void RoundTrip(int value)
        {
            Span<byte> payload = stackalloc byte[4];
            BitConverter.TryWriteBytes(payload, value);
            for (int index = 0; index < sessions.Length; index++)
                Equal(NetworkQueueStatus.Accepted,
                    sessions[index].Server.TryQueue(s_latest, payload));
            int step = 0;
            while (!AllClientsReceived() && step++ < 50_000) host.Pump();
            True(AllClientsReceived(), "Shared UDP latest state did not converge.");
            for (int index = 0; index < sessions.Length; index++)
            {
                True(sessions[index].Client.TryPeekReceived(out NetworkDeliveredMessage message,
                    out ReadOnlySpan<byte> received));
                Equal(s_latest, message.MessageId);
                True(received.SequenceEqual(payload));
                True(sessions[index].Client.TryConsumeReceived());
            }

            bool AllClientsReceived()
            {
                for (int index = 0; index < sessions.Length; index++)
                    if (sessions[index].Client.ReceivedQueued != 1) return false;
                return true;
            }
        }
    }

    private static void SharedUdpRetiredCapacityAdvancesGeneration()
    {
        using SharedUdpProtocolHost host = CreateHost(maximumSessions: 1);
        True(host.TryAddLoopbackSession(out SharedUdpProtocolSession? first, out _));
        ConnectionId stale = first!.Connection;
        first.Dispose();
        Equal(0, host.SessionCount);
        True(host.TryAddLoopbackSession(out SharedUdpProtocolSession? second, out _));
        True(second is not null);
        Equal(stale.Slot, second!.Connection.Slot);
        True(second.Connection.Generation > stale.Generation);
        False(second.Connection == stale);
    }

    private static void SharedUdpAggregateListenerCapacityPreservesBurst()
    {
        const int sessionCount = 64;
        using SharedUdpProtocolHost host = CreateHost(sessionCount);
        var sessions = new SharedUdpProtocolSession[sessionCount];
        Span<byte> payload = stackalloc byte[2];
        for (int index = 0; index < sessions.Length; index++)
        {
            True(host.TryAddLoopbackSession(out SharedUdpProtocolSession? session, out _));
            sessions[index] = session!;
            for (byte message = 0; message < 4; message++)
            {
                payload[0] = (byte)index;
                payload[1] = message;
                Equal(NetworkQueueStatus.Accepted,
                    session!.Client.TryQueue(s_reliable, payload));
            }
        }

        Drive(host, AllDelivered);
        for (int index = 0; index < sessions.Length; index++)
        {
            Equal(0UL, sessions[index].Client.GetSnapshot().ReliableRetransmissions,
                "The shared listener dropped an admitted initial burst that fit the aggregate socket contract.");
            for (byte message = 0; message < 4; message++)
            {
                payload[0] = (byte)index;
                payload[1] = message;
                AssertReceive(sessions[index].Server, payload);
            }
        }

        SharedUdpProtocolHostSnapshot snapshot = host.GetSnapshot();
        Equal(0UL, snapshot.Listener.CapacityDrops);
        Equal(2L * sessionCount *
            (s_udp.SocketSendBufferBytes + s_udp.SocketReceiveBufferBytes),
            snapshot.ConfiguredSocketBufferBytes);

        bool AllDelivered()
        {
            for (int index = 0; index < sessions.Length; index++)
                if (sessions[index].Server.ReceivedQueued != 4) return false;
            return true;
        }
    }

    private static void SharedUdpNativeSendBatchPreservesIndependentDatagrams()
    {
        if (!OperatingSystem.IsLinux()) return;
        const int sessionCount = 8;
        using SharedUdpProtocolHost host = CreateHost(
            sessionCount, SharedUdpSocketIoMode.PlatformBatch);
        var sessions = new SharedUdpProtocolSession[sessionCount];
        for (int index = 0; index < sessions.Length; index++)
        {
            True(host.TryAddLoopbackSession(out SharedUdpProtocolSession? session, out _));
            sessions[index] = session!;
            byte[] queued = [(byte)index];
            Equal(NetworkQueueStatus.Accepted,
                sessions[index].Server.TryQueue(s_latest, queued));
        }

        host.Pump();
        SharedUdpListenerSnapshot submitted = host.GetSnapshot().Listener;
        True(submitted.UsesNativeSendBatching);
        Equal(1UL, submitted.NativeSendCalls);
        Equal((ulong)sessionCount, submitted.NativeSentDatagrams);
        Equal(sessionCount, submitted.MaximumNativeSendBatch);
        Equal(sessionCount, submitted.NativeSendBatchP50);
        Equal(sessionCount, submitted.NativeSendBatchP95);
        Equal(sessionCount, submitted.MaximumOutboundQueuedDatagrams);
        Equal(0UL, submitted.PartialNativeSendCalls);

        Drive(host, AllDelivered);
        for (int index = 0; index < sessions.Length; index++)
        {
            True(sessions[index].Client.TryPeekReceived(out NetworkDeliveredMessage message,
                out ReadOnlySpan<byte> payload));
            Equal(s_latest, message.MessageId);
            byte[] expected = [(byte)index];
            True(payload.SequenceEqual(expected));
            True(sessions[index].Client.TryConsumeReceived());
        }

        bool AllDelivered()
        {
            for (int index = 0; index < sessions.Length; index++)
                if (sessions[index].Client.ReceivedQueued != 1) return false;
            return true;
        }
    }

    private static void SharedUdpNativeSendBatchBackpressureIsPerDatagram()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var host = SharedUdpProtocolHost.CreateSelfTrustedLoopback(
            s_protocol,
            s_udp,
            new SharedUdpProtocolLimits(
                1, 1, 16, SharedUdpSocketIoMode.PlatformBatch, 8),
            s_manifest);
        True(host.TryAddLoopbackSession(out SharedUdpProtocolSession? created, out _));
        SharedUdpProtocolSession session = created!;
        for (byte value = 0; value < 4; value++)
        {
            byte[] payload = [value];
            Equal(NetworkQueueStatus.Accepted,
                session.Server.TryQueue(s_independent, payload));
        }

        Drive(host, () => session.Client.ReceivedQueued == 4);
        for (byte value = 0; value < 4; value++)
        {
            byte[] expected = [value];
            True(session.Client.TryPeekReceived(out NetworkDeliveredMessage message,
                out ReadOnlySpan<byte> payload));
            Equal(s_independent, message.MessageId);
            True(payload.SequenceEqual(expected));
            True(session.Client.TryConsumeReceived());
        }

        SharedUdpListenerSnapshot listener = host.GetSnapshot().Listener;
        True(listener.UsesNativeSendBatching);
        True(listener.CapacityDrops > 0,
            "The one-datagram listener queue never exercised per-datagram backpressure.");
        Equal(4UL, listener.NativeSentDatagrams);
        Equal(1, listener.MaximumNativeSendBatch);
    }

    private static void WarmedSharedUdpNativeSendBatchAllocatesZero()
    {
        if (!OperatingSystem.IsLinux()) return;
        using SharedUdpProtocolHost host = CreateHost(4, SharedUdpSocketIoMode.PlatformBatch);
        var sessions = new SharedUdpProtocolSession[4];
        for (int index = 0; index < sessions.Length; index++)
        {
            True(host.TryAddLoopbackSession(out SharedUdpProtocolSession? session, out _));
            sessions[index] = session!;
        }
        for (int iteration = 0; iteration < 32; iteration++) RoundTrip(iteration);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 128; iteration++) RoundTrip(iteration);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Equal(0L, allocated,
            $"The warmed native send-batch path allocated {allocated} managed bytes.");

        void RoundTrip(int value)
        {
            Span<byte> payload = stackalloc byte[4];
            BitConverter.TryWriteBytes(payload, value);
            for (int index = 0; index < sessions.Length; index++)
                Equal(NetworkQueueStatus.Accepted,
                    sessions[index].Server.TryQueue(s_latest, payload));
            int step = 0;
            while (!AllDelivered() && step++ < 50_000) host.Pump();
            True(AllDelivered(), "Shared UDP native-batch latest state did not converge.");
            for (int index = 0; index < sessions.Length; index++)
            {
                True(sessions[index].Client.TryConsumeReceived());
            }

            bool AllDelivered()
            {
                for (int index = 0; index < sessions.Length; index++)
                    if (sessions[index].Client.ReceivedQueued != 1) return false;
                return true;
            }
        }
    }

    private static void SharedUdpNativeSendBatchRetirementPurgesQueuedPath()
    {
        if (!OperatingSystem.IsLinux()) return;
        using SharedUdpProtocolHost host = CreateHost(1, SharedUdpSocketIoMode.PlatformBatch);
        True(host.TryAddLoopbackSession(out SharedUdpProtocolSession? created, out _));
        SharedUdpProtocolSession session = created!;
        ulong sentBefore = host.GetSnapshot().Listener.Sent;
        Equal(DatagramSendStatus.Accepted,
            session.ServerEndpoint.TrySend(stackalloc byte[] { 1, 2, 3 }));
        session.Dispose();
        host.Pump();
        Equal(sentBefore, host.GetSnapshot().Listener.Sent,
            "Retiring an endpoint submitted a queued datagram to its stale path.");

        True(host.TryAddLoopbackSession(out SharedUdpProtocolSession? replacement, out _));
        Equal(NetworkQueueStatus.Accepted,
            replacement!.Server.TryQueue(s_latest, stackalloc byte[] { 9 }));
        Drive(host, () => replacement.Client.ReceivedQueued == 1);
    }

    private static void SharedUdpNativeReceiveBatchPreservesIndependentDatagrams()
    {
        if (!OperatingSystem.IsLinux()) return;
        const int sessionCount = 8;
        using SharedUdpProtocolHost host = CreateHost(
            sessionCount, SharedUdpSocketIoMode.PlatformSendReceiveBatch);
        var sessions = new SharedUdpProtocolSession[sessionCount];
        for (int index = 0; index < sessions.Length; index++)
        {
            True(host.TryAddLoopbackSession(out SharedUdpProtocolSession? session, out _));
            sessions[index] = session!;
        }
        host.ResetListenerStageTiming(enabled: true);
        Span<byte> payload = stackalloc byte[1];
        for (int index = 0; index < sessions.Length; index++)
        {
            payload[0] = checked((byte)index);
            Equal(NetworkQueueStatus.Accepted,
                sessions[index].Client.TryQueue(s_latest, payload));
            sessions[index].Client.Emit(0);
        }

        host.Pump();
        SharedUdpReceiveCensusSnapshot census = host.GetSnapshot().Listener.ReceiveCensus;
        True(census.UsesNativeReceiveBatching);
        Equal(1UL, census.NativeReceiveCalls);
        Equal((ulong)sessionCount, census.NativeReceivedDatagrams);
        Equal(sessionCount, census.NativeReceiveBatchP50);
        Equal(sessionCount, census.NativeReceiveBatchP95);
        Equal(sessionCount, census.MaximumNativeReceiveBatch);
        for (int index = 0; index < sessions.Length; index++)
        {
            True(sessions[index].Server.TryPeekReceived(
                out NetworkDeliveredMessage message, out ReadOnlySpan<byte> receivedPayload));
            Equal(s_latest, message.MessageId);
            Equal(1, receivedPayload.Length);
            Equal(checked((byte)index), receivedPayload[0]);
            True(sessions[index].Server.TryConsumeReceived());
        }
    }

    private static void WarmedSharedUdpNativeReceiveBatchAllocatesZero()
    {
        if (!OperatingSystem.IsLinux()) return;
        using SharedUdpProtocolHost host = CreateHost(
            4, SharedUdpSocketIoMode.PlatformSendReceiveBatch);
        var sessions = new SharedUdpProtocolSession[4];
        for (int index = 0; index < sessions.Length; index++)
        {
            True(host.TryAddLoopbackSession(out SharedUdpProtocolSession? session, out _));
            sessions[index] = session!;
        }
        for (int iteration = 0; iteration < 32; iteration++) RoundTrip(iteration);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 128; iteration++) RoundTrip(iteration);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Equal(0L, allocated,
            $"The warmed native receive-batch path allocated {allocated} managed bytes.");

        void RoundTrip(int value)
        {
            Span<byte> payload = stackalloc byte[4];
            BitConverter.TryWriteBytes(payload, value);
            for (int index = 0; index < sessions.Length; index++)
            {
                Equal(NetworkQueueStatus.Accepted,
                    sessions[index].Client.TryQueue(s_latest, payload));
                sessions[index].Client.Emit(checked((ulong)value + 1));
            }
            for (int step = 0; step < 50_000 && !AllDelivered(); step++) host.Pump();
            True(AllDelivered(), "Shared UDP native receive-batch state did not converge.");
            for (int index = 0; index < sessions.Length; index++)
                True(sessions[index].Server.TryConsumeReceived());

            bool AllDelivered()
            {
                for (int index = 0; index < sessions.Length; index++)
                    if (sessions[index].Server.ReceivedQueued != 1) return false;
                return true;
            }
        }
    }

    private static void SharedUdpNativeReceiveBatchBackpressureRemainsPerSession()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var host = SharedUdpProtocolHost.CreateSelfTrustedLoopback(
            s_protocol,
            s_udp,
            new SharedUdpProtocolLimits(
                1, 1, 16, SharedUdpSocketIoMode.PlatformSendReceiveBatch, 8, 8),
            s_manifest);
        True(host.TryAddLoopbackSession(out SharedUdpProtocolSession? created, out _));
        SharedUdpProtocolSession session = created!;
        host.ResetListenerStageTiming(enabled: true);
        Span<byte> payload = stackalloc byte[1];
        for (byte value = 0; value < 4; value++)
        {
            payload[0] = value;
            Equal(NetworkQueueStatus.Accepted,
                session.Client.TryQueue(s_independent, payload));
            session.Client.Emit(checked((ulong)value + 1));
        }

        host.Pump();
        SharedUdpListenerSnapshot listener = host.GetSnapshot().Listener;
        True(listener.ReceiveCensus.UsesNativeReceiveBatching);
        Equal(4UL, listener.ReceiveCensus.NativeReceivedDatagrams);
        Equal(3UL, listener.CapacityDrops);
        Equal(1, session.Server.ReceivedQueued);
    }

    private static SharedUdpProtocolHost CreateHost(
        int maximumSessions,
        SharedUdpSocketIoMode mode = SharedUdpSocketIoMode.Scalar) =>
        SharedUdpProtocolHost.CreateSelfTrustedLoopback(
            s_protocol,
            s_udp,
            new SharedUdpProtocolLimits(
                maximumSessions, 4, checked(maximumSessions * 16), mode, 32),
            s_manifest);

    private static void SharedUdpBackgroundScalarCopiesAndSendsIndependentDatagrams() =>
        AssertBackgroundCopiesAndSends(SharedUdpSocketIoMode.BackgroundScalar);

    private static void SharedUdpBackgroundNativeBatchCopiesAndSendsIndependentDatagrams()
    {
        if (!LinuxUdpBatch.IsSupported) return;
        AssertBackgroundCopiesAndSends(SharedUdpSocketIoMode.BackgroundPlatformBatch);
    }

    private static void AssertBackgroundCopiesAndSends(SharedUdpSocketIoMode mode)
    {
        using var remote = CreateBoundSocket();
        using var gate = new BlockingEgressHook();
        using var listener = new SharedUdpServerListener(
            s_udp,
            new SharedUdpProtocolLimits(1, 4, 16, mode, 8),
            new IPEndPoint(IPAddress.Loopback, 0),
            unknownDatagramHandler: null,
            gate);
        True(listener.TryRegister((IPEndPoint)remote.LocalEndPoint!, out SharedUdpServerEndpoint? endpoint));
        Span<byte> source = stackalloc byte[] { 1, 2, 3, 4 };
        Equal(DatagramSendStatus.Accepted, listener.TrySend(endpoint!.Slot, source));
        True(gate.Entered.Wait(TimeSpan.FromSeconds(2)), "The background sender did not observe queued work.");
        source.Fill(99);
        gate.Release.Set();

        Span<byte> received = stackalloc byte[32];
        int length = ReceiveBounded(remote, received);
        Equal(4, length);
        True(received[..length].SequenceEqual(stackalloc byte[] { 1, 2, 3, 4 }),
            "Background egress retained the caller's mutable source rather than its own copy.");
        SpinUntil(() => listener.GetSnapshot().BackgroundEgress.CurrentDatagrams == 0);
        SharedUdpBackgroundEgressSnapshot snapshot = listener.GetSnapshot().BackgroundEgress;
        Equal(SharedUdpBackgroundEgressState.Running, snapshot.State);
        Equal(1, snapshot.SenderThreads);
        Equal(1UL, snapshot.AcceptedDatagrams);
        Equal(1UL, snapshot.PhysicallySentDatagrams);
        Equal(0, snapshot.CurrentDatagrams);
        True(snapshot.ProducerEnqueue.Count == 1 && snapshot.QueueResidence.Count == 1);
        endpoint.Dispose();
    }

    private static void SharedUdpBackgroundModesPreserveSecureSessions()
    {
        AssertMode(SharedUdpSocketIoMode.BackgroundScalar);
        if (LinuxUdpBatch.IsSupported)
            AssertMode(SharedUdpSocketIoMode.BackgroundPlatformBatch);

        static void AssertMode(SharedUdpSocketIoMode mode)
        {
            using SharedUdpProtocolHost host = CreateHost(4, mode);
            var sessions = new SharedUdpProtocolSession[4];
            Span<byte> queuedPayload = stackalloc byte[2];
            for (int index = 0; index < sessions.Length; index++)
            {
                True(host.TryAddLoopbackSession(out SharedUdpProtocolSession? session, out _));
                sessions[index] = session!;
                queuedPayload[0] = checked((byte)index);
                queuedPayload[1] = 0x5a;
                Equal(NetworkQueueStatus.Accepted,
                    sessions[index].Server.TryQueue(s_latest, queuedPayload));
            }
            Drive(host, AllDelivered);
            for (int index = 0; index < sessions.Length; index++)
            {
                True(sessions[index].Client.TryPeekReceived(out NetworkDeliveredMessage message,
                    out ReadOnlySpan<byte> payload));
                Equal(s_latest, message.MessageId);
                Equal(checked((byte)index), payload[0]);
                True(sessions[index].Client.TryConsumeReceived());
            }
            SharedUdpBackgroundEgressSnapshot snapshot = host.GetSnapshot().Listener.BackgroundEgress;
            True(snapshot.PhysicallySentDatagrams >= 4);
            True(snapshot.QueueResidence.Count >= 4);

            bool AllDelivered()
            {
                for (int index = 0; index < sessions.Length; index++)
                    if (sessions[index].Client.ReceivedQueued != 1) return false;
                return true;
            }
        }
    }

    private static void SharedUdpBackgroundQueueRefusalIsBounded()
    {
        using var remote = CreateBoundSocket();
        using var gate = new BlockingEgressHook();
        using var listener = new SharedUdpServerListener(
            s_udp,
            new SharedUdpProtocolLimits(1, 1, 16,
                SharedUdpSocketIoMode.BackgroundScalar, 8),
            new IPEndPoint(IPAddress.Loopback, 0), null, gate);
        True(listener.TryRegister((IPEndPoint)remote.LocalEndPoint!, out SharedUdpServerEndpoint? endpoint));
        Equal(DatagramSendStatus.Accepted,
            listener.TrySend(endpoint!.Slot, stackalloc byte[] { 7 }));
        True(gate.Entered.Wait(TimeSpan.FromSeconds(2)));
        Equal(DatagramSendStatus.CapacityExceeded,
            listener.TrySend(endpoint.Slot, stackalloc byte[] { 8 }));
        SharedUdpBackgroundEgressSnapshot full = listener.GetSnapshot().BackgroundEgress;
        Equal(1, full.CurrentDatagrams);
        Equal(1, full.HighWaterDatagrams);
        Equal(1UL, full.CapacityRefusals);
        True(full.OldestQueuedStopwatchTicks > 0,
            "A stalled sender did not expose queue age.");
        True(full.ReservedBytes >= full.CapacityBytes && full.CapacityBytes == 512,
            "The fixed queue reservation was not reported explicitly.");
        gate.Release.Set();
        Span<byte> received = stackalloc byte[8];
        Equal(1, ReceiveBounded(remote, received));
        Equal((byte)7, received[0]);
        endpoint.Dispose();
    }

    private static void SharedUdpBackgroundQueueRefusalDoesNotAdvanceProtocolSend()
    {
        using var gate = new BlockingEgressHook();
        using var host = new SharedUdpProtocolHost(
            s_protocol,
            s_udp,
            new SharedUdpProtocolLimits(1, 1, 16,
                SharedUdpSocketIoMode.BackgroundScalar, 8),
            StopwatchNetworkMonotonicClock.Instance,
            s_manifest,
            serverIdentity: null,
            trustedServerPublicKey: default,
            serverManifest: null,
            allowSelfTrust: true,
            backgroundEgressTestHook: gate);
        True(host.TryAddLoopbackSession(out SharedUdpProtocolSession? created, out _));
        SharedUdpProtocolSession session = created!;
        Equal(NetworkQueueStatus.Accepted,
            session.Server.TryQueue(s_reliable, stackalloc byte[] { 1 }));
        session.Server.Emit(1);
        True(gate.Entered.Wait(TimeSpan.FromSeconds(2)));
        NetworkProtocolSnapshot before = session.Server.GetSnapshot();
        Equal(1UL, before.SentPackets);
        Equal(NetworkQueueStatus.Accepted,
            session.Server.TryQueue(s_reliable, stackalloc byte[] { 2 }));
        session.Server.Emit(1_000);
        NetworkProtocolSnapshot refused = session.Server.GetSnapshot();
        Equal(before.SentPackets, refused.SentPackets,
            "A refused listener enqueue consumed a protocol packet number.");
        Equal(2, refused.ReliableQueued,
            "A refused listener enqueue consumed reliable application work.");
        True(host.GetSnapshot().Listener.BackgroundEgress.CapacityRefusals > 0);
        gate.Release.Set();
        Drive(host, () => session.Client.ReceivedQueued == 2);
        AssertReceive(session.Client, stackalloc byte[] { 1 });
        AssertReceive(session.Client, stackalloc byte[] { 2 });
    }

    private static void SharedUdpBackgroundRetirementFencesQueuedPath()
    {
        using var remote = CreateBoundSocket();
        using var gate = new BlockingEgressHook();
        using var listener = new SharedUdpServerListener(
            s_udp,
            new SharedUdpProtocolLimits(1, 2, 16,
                SharedUdpSocketIoMode.BackgroundScalar, 8),
            new IPEndPoint(IPAddress.Loopback, 0), null, gate);
        True(listener.TryRegister((IPEndPoint)remote.LocalEndPoint!, out SharedUdpServerEndpoint? endpoint));
        Equal(DatagramSendStatus.Accepted,
            listener.TrySend(endpoint!.Slot, stackalloc byte[] { 3, 2, 1 }));
        True(gate.Entered.Wait(TimeSpan.FromSeconds(2)));
        endpoint.Dispose();
        gate.Release.Set();
        SpinUntil(() => listener.GetSnapshot().BackgroundEgress.CurrentDatagrams == 0);
        SharedUdpBackgroundEgressSnapshot snapshot = listener.GetSnapshot().BackgroundEgress;
        Equal(0UL, snapshot.PhysicallySentDatagrams);
        Equal(1UL, snapshot.StalePathDrops);
        False(remote.Poll(20_000, SelectMode.SelectRead),
            "A queued datagram escaped after its endpoint lifetime retired.");
    }

    private static void SharedUdpBackgroundRebindFencesOldQueuedPath()
    {
        using var oldRemote = CreateBoundSocket();
        using var newRemote = CreateBoundSocket();
        using var gate = new BlockingEgressHook();
        using var listener = new SharedUdpServerListener(
            s_udp,
            new SharedUdpProtocolLimits(1, 2, 16,
                SharedUdpSocketIoMode.BackgroundScalar, 8),
            new IPEndPoint(IPAddress.Loopback, 0), null, gate);
        True(listener.TryRegister((IPEndPoint)oldRemote.LocalEndPoint!,
            out SharedUdpServerEndpoint? endpoint));
        Equal(DatagramSendStatus.Accepted,
            listener.TrySend(endpoint!.Slot, stackalloc byte[] { 1 }));
        True(gate.Entered.Wait(TimeSpan.FromSeconds(2)));
        True(listener.TryRebind(endpoint, (IPEndPoint)newRemote.LocalEndPoint!));
        gate.Release.Set();
        SpinUntil(() => listener.GetSnapshot().BackgroundEgress.CurrentDatagrams == 0);
        Equal(DatagramSendStatus.Accepted,
            listener.TrySend(endpoint.Slot, stackalloc byte[] { 2 }));
        Span<byte> received = stackalloc byte[8];
        Equal(1, ReceiveBounded(newRemote, received));
        Equal((byte)2, received[0]);
        False(oldRemote.Poll(20_000, SelectMode.SelectRead),
            "A pre-rebind datagram escaped to the retired path.");
        True(listener.GetSnapshot().BackgroundEgress.StalePathDrops >= 1);
        endpoint.Dispose();
    }

    private static void SharedUdpBackgroundSecurityEpochRotationPreservesQueueOrder()
    {
        using var gate = new BlockingEgressHook();
        using var host = new SharedUdpProtocolHost(
            s_protocol,
            s_udp,
            new SharedUdpProtocolLimits(1, 4, 16,
                SharedUdpSocketIoMode.BackgroundScalar, 8),
            StopwatchNetworkMonotonicClock.Instance,
            s_manifest,
            serverIdentity: null,
            trustedServerPublicKey: default,
            serverManifest: null,
            allowSelfTrust: true,
            backgroundEgressTestHook: gate);
        True(host.TryAddLoopbackSession(out SharedUdpProtocolSession? created, out _));
        SharedUdpProtocolSession session = created!;
        Equal(NetworkQueueStatus.Accepted,
            session.Server.TryQueue(s_latest, stackalloc byte[] { 1 }));
        session.Server.Emit(1);
        True(gate.Entered.Wait(TimeSpan.FromSeconds(2)));
        True(session.Server.TryRotateSendSecurityEpoch());
        Equal(NetworkQueueStatus.Accepted,
            session.Server.TryQueue(s_latest, stackalloc byte[] { 2 }));
        session.Server.Emit(2);
        gate.Release.Set();
        Drive(host, () => session.Client.GetSnapshot().ReceivedPackets >= 2);
        byte latest = 0;
        int applied = 0;
        while (session.Client.TryPeekReceived(out NetworkDeliveredMessage message,
            out ReadOnlySpan<byte> payload))
        {
            Equal(s_latest, message.MessageId);
            latest = payload[0];
            applied++;
            True(session.Client.TryConsumeReceived());
        }
        True(applied >= 1);
        Equal((byte)2, latest);
        Equal(0UL, session.Client.GetSnapshot().AuthenticationFailures);
    }

    private static void WarmedSharedUdpBackgroundProducerAllocatesZero()
    {
        using var remote = CreateBoundSocket();
        using var listener = new SharedUdpServerListener(
            s_udp,
            new SharedUdpProtocolLimits(1, 4, 16,
                SharedUdpSocketIoMode.BackgroundScalar, 8),
            new IPEndPoint(IPAddress.Loopback, 0), null);
        True(listener.TryRegister((IPEndPoint)remote.LocalEndPoint!, out SharedUdpServerEndpoint? endpoint));
        byte[] payload = new byte[4];
        for (int iteration = 0; iteration < 32; iteration++) SendAndDrain(iteration);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 128; iteration++) SendAndDrain(iteration);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Equal(0L, allocated,
            $"The warmed background-egress producer allocated {allocated} managed bytes.");
        endpoint!.Dispose();

        void SendAndDrain(int value)
        {
            BitConverter.TryWriteBytes(payload, value);
            Equal(DatagramSendStatus.Accepted, listener.TrySend(endpoint!.Slot, payload));
            WaitBackgroundEmpty(listener);
        }
    }

    private static void SharedUdpBackgroundNativePartialAndWouldBlockRetainSuffix()
    {
        if (!LinuxUdpBatch.IsSupported) return;
        using var remote = CreateBoundSocket();
        using var hook = new ScriptedEgressHook(
            new SendResult(-1, 11),
            new SendResult(1, 0),
            new SendResult(int.MaxValue, 0));
        using var listener = new SharedUdpServerListener(
            s_udp,
            new SharedUdpProtocolLimits(1, 4, 16,
                SharedUdpSocketIoMode.BackgroundPlatformBatch, 8),
            new IPEndPoint(IPAddress.Loopback, 0), null, hook);
        True(listener.TryRegister((IPEndPoint)remote.LocalEndPoint!, out SharedUdpServerEndpoint? endpoint));
        hook.Block();
        Span<byte> payload = stackalloc byte[1];
        for (byte value = 1; value <= 3; value++)
        {
            payload[0] = value;
            Equal(DatagramSendStatus.Accepted,
                listener.TrySend(endpoint!.Slot, payload));
        }
        hook.Release();
        SpinUntil(() => listener.GetSnapshot().BackgroundEgress.CurrentDatagrams == 0);
        SharedUdpBackgroundEgressSnapshot snapshot = listener.GetSnapshot().BackgroundEgress;
        Equal(1UL, snapshot.WouldBlockEvents);
        Equal(1UL, snapshot.PartialNativeSendCalls);
        Equal(3UL, snapshot.PhysicallySentDatagrams);
        True(hook.RequestCount >= 3);
        Equal(3, hook.GetRequested(0));
        Equal(3, hook.GetRequested(1));
        Equal(2, hook.GetRequested(2),
            "A partial native send did not retain the unsent suffix as the next batch.");
        endpoint!.Dispose();
    }

    private static void SharedUdpBackgroundWorkerFailureSurvivesCleanup()
    {
        using var remote = CreateBoundSocket();
        using var hook = new FaultingEgressHook();
        var listener = new SharedUdpServerListener(
            s_udp,
            new SharedUdpProtocolLimits(1, 2, 16,
                SharedUdpSocketIoMode.BackgroundScalar, 8),
            new IPEndPoint(IPAddress.Loopback, 0), null, hook);
        True(listener.TryRegister((IPEndPoint)remote.LocalEndPoint!, out SharedUdpServerEndpoint? endpoint));
        Equal(DatagramSendStatus.Accepted,
            listener.TrySend(endpoint!.Slot, stackalloc byte[] { 4 }));
        SpinUntil(() => listener.GetSnapshot().BackgroundEgress.State ==
            SharedUdpBackgroundEgressState.Faulted);
        bool surfacedDuringService = false;
        try
        {
            listener.PumpOutbound();
        }
        catch (InvalidOperationException exception) when (exception.Message == FaultingEgressHook.Message)
        {
            surfacedDuringService = true;
        }
        True(surfacedDuringService,
            "A failed background sender was not surfaced to ordinary listener service.");
        bool preserved = false;
        try
        {
            listener.Dispose();
        }
        catch (InvalidOperationException exception) when (exception.Message == FaultingEgressHook.Message)
        {
            preserved = true;
        }
        True(preserved, "Listener disposal swallowed the background worker failure.");
        listener.Dispose();
    }

    private static void SharedUdpBackgroundPhysicalReliableLossRetransmits()
    {
        var clock = new ManualEgressClock(1);
        using var hook = new ScriptedEgressHook(
            new SendResult(-1, (int)SocketError.ConnectionReset),
            new SendResult(0, 0, Override: false));
        using var host = new SharedUdpProtocolHost(
            s_protocol,
            s_udp,
            new SharedUdpProtocolLimits(1, 4, 16,
                SharedUdpSocketIoMode.BackgroundScalar, 8),
            clock,
            s_manifest,
            serverIdentity: null,
            trustedServerPublicKey: default,
            serverManifest: null,
            allowSelfTrust: true,
            backgroundEgressTestHook: hook);
        True(host.TryAddLoopbackSession(out SharedUdpProtocolSession? created, out _));
        SharedUdpProtocolSession session = created!;
        Equal(NetworkQueueStatus.Accepted,
            session.Server.TryQueue(s_reliable, stackalloc byte[] { 4, 2 }));
        session.Server.Emit(clock.Milliseconds);
        for (int attempt = 0; attempt < 100_000 &&
             host.GetSnapshot().Listener.BackgroundEgress.CurrentDatagrams != 0; attempt++)
            if ((attempt & 31) == 0) Thread.Yield();
        Equal(0, host.GetSnapshot().Listener.BackgroundEgress.CurrentDatagrams);
        Equal(1UL, host.GetSnapshot().Listener.BackgroundEgress.PhysicalSendFailures);

        for (int step = 0; step < 2_000 && session.Client.ReceivedQueued == 0; step++)
        {
            clock.Advance(10);
            host.Pump();
        }
        Equal(1, session.Client.ReceivedQueued,
            "Reliable debt did not recover after post-admission physical loss.");
        AssertReceive(session.Client, stackalloc byte[] { 4, 2 });
        True(session.Server.GetSnapshot().ReliableRetransmissions > 0);
    }

    private static void SharedUdpBackgroundShutdownDrainsAndIsIdempotent()
    {
        using var remote = CreateBoundSocket();
        var listener = new SharedUdpServerListener(
            s_udp,
            new SharedUdpProtocolLimits(1, 4, 16,
                SharedUdpSocketIoMode.BackgroundScalar, 8),
            new IPEndPoint(IPAddress.Loopback, 0), null);
        True(listener.TryRegister((IPEndPoint)remote.LocalEndPoint!, out SharedUdpServerEndpoint? endpoint));
        Equal(DatagramSendStatus.Accepted,
            listener.TrySend(endpoint!.Slot, stackalloc byte[] { 9, 7, 5 }));
        listener.Dispose();
        listener.Dispose();
        Span<byte> received = stackalloc byte[8];
        Equal(3, ReceiveBounded(remote, received));
        True(received[..3].SequenceEqual(stackalloc byte[] { 9, 7, 5 }));
    }

    private static Socket CreateBoundSocket()
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        socket.Blocking = false;
        return socket;
    }

    private static int ReceiveBounded(Socket socket, Span<byte> destination)
    {
        for (int attempt = 0; attempt < 20_000; attempt++)
        {
            try
            {
                return socket.Receive(destination);
            }
            catch (SocketException exception) when (exception.SocketErrorCode == SocketError.WouldBlock)
            {
                if ((attempt & 31) == 0) Thread.Yield();
            }
        }
        throw new InvalidOperationException("The background UDP datagram did not arrive.");
    }

    private static void SpinUntil(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 100_000 && !condition(); attempt++)
            if ((attempt & 31) == 0) Thread.Yield();
        True(condition(), "The bounded background-egress condition did not converge.");
    }

    private static void WaitBackgroundEmpty(SharedUdpServerListener listener)
    {
        for (int attempt = 0; attempt < 100_000; attempt++)
        {
            if (listener.GetSnapshot().BackgroundEgress.CurrentDatagrams == 0) return;
            if ((attempt & 31) == 0) Thread.Yield();
        }
        throw new InvalidOperationException("The bounded background-egress queue did not drain.");
    }

    private sealed class BlockingEgressHook : ISharedUdpBackgroundEgressTestHook, IDisposable
    {
        private int _block = 1;
        internal ManualResetEventSlim Entered { get; } = new(false);
        internal ManualResetEventSlim Release { get; } = new(false);

        public void BeforePhysicalSend()
        {
            if (Interlocked.Exchange(ref _block, 0) == 0) return;
            Entered.Set();
            if (!Release.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("The controlled egress gate was not released.");
        }

        public bool TryOverrideSend(int requested, out int sent, out int error)
        {
            sent = 0;
            error = 0;
            return false;
        }

        public void Dispose()
        {
            Release.Set();
            Entered.Dispose();
            Release.Dispose();
        }
    }

    private readonly record struct SendResult(int Sent, int Error, bool Override = true);

    private sealed class ScriptedEgressHook : ISharedUdpBackgroundEgressTestHook, IDisposable
    {
        private readonly SendResult[] _results;
        private readonly int[] _requested = new int[16];
        private readonly ManualResetEventSlim _entered = new(false);
        private readonly ManualResetEventSlim _release = new(false);
        private int _block;
        private int _calls;

        internal ScriptedEgressHook(params SendResult[] results) => _results = results;
        internal int RequestCount => Volatile.Read(ref _calls);
        internal int GetRequested(int index) => _requested[index];
        internal void Block() => Volatile.Write(ref _block, 1);
        internal void Release()
        {
            True(_entered.Wait(TimeSpan.FromSeconds(2)));
            _release.Set();
        }

        public void BeforePhysicalSend()
        {
            if (Interlocked.Exchange(ref _block, 0) == 0) return;
            _entered.Set();
            if (!_release.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("The scripted egress gate was not released.");
        }

        public bool TryOverrideSend(int requested, out int sent, out int error)
        {
            int call = Interlocked.Increment(ref _calls) - 1;
            _requested[call] = requested;
            SendResult result = _results[Math.Min(call, _results.Length - 1)];
            if (!result.Override)
            {
                sent = 0;
                error = 0;
                return false;
            }
            sent = result.Sent == int.MaxValue ? requested : result.Sent;
            error = result.Error;
            return true;
        }

        public void Dispose()
        {
            _release.Set();
            _entered.Dispose();
            _release.Dispose();
        }
    }

    private sealed class FaultingEgressHook : ISharedUdpBackgroundEgressTestHook, IDisposable
    {
        internal const string Message = "PF5-R3 injected worker failure";
        public void BeforePhysicalSend() => throw new InvalidOperationException(Message);
        public bool TryOverrideSend(int requested, out int sent, out int error) =>
            throw new InvalidOperationException(Message);
        public void Dispose() { }
    }

    private sealed class ManualEgressClock(ulong milliseconds) : INetworkMonotonicClock
    {
        private long _milliseconds = checked((long)milliseconds);
        public ulong Milliseconds => checked((ulong)Volatile.Read(ref _milliseconds));
        internal void Advance(ulong milliseconds) => Interlocked.Add(
            ref _milliseconds, checked((long)milliseconds));
    }

    private static void Drive(SharedUdpProtocolHost host, Func<bool> done)
    {
        for (int step = 0; step < 50_000 && !done(); step++) host.Pump();
        True(done(), "Shared UDP protocol traffic did not converge.");
    }

    private static void AssertReceive(NetworkProtocolPeer peer, ReadOnlySpan<byte> expected)
    {
        True(peer.TryPeekReceived(out NetworkDeliveredMessage message, out ReadOnlySpan<byte> payload));
        Equal(s_reliable, message.MessageId);
        True(payload.SequenceEqual(expected));
        True(peer.TryConsumeReceived());
    }

    private static void Run(string name, Action body)
    {
        body();
        Console.WriteLine($"PASS {name}");
    }

    private static void True(bool condition, string? message = null)
    {
        if (!condition) throw new InvalidOperationException(message ?? "Expected true.");
    }

    private static void False(bool condition, string? message = null) => True(!condition, message);

    private static void Equal<T>(T expected, T actual, string? message = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(message ?? $"Expected {expected}, got {actual}.");
    }
}

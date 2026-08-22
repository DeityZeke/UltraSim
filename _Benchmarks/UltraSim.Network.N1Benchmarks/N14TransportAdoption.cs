using System.Diagnostics;
using UltraSim.Client.Core;
using UltraSim.Client.Network;
using UltraSim.ECS;
using UltraSim.Game.Composition;
using UltraSim.Game.Protocol;
using UltraSim.Game.Replication;
using UltraSim.Game.World;
using UltraSim.Network;
using UltraSim.Spatial;

internal static class N14TransportAdoption
{
    private const ulong ApplicationSessionEpoch = 0x14_4001;
    private static readonly WorldContentFingerprint s_content = new(0x14_4001, 0xA0D0_7100);

    internal static TransportAdoptionEvidence Capture(
        int replayRecipients,
        int replayWarmupTicks,
        int replayTicks,
        int townChunks,
        int townRowsPerChunk,
        int subscriptionCapacity)
    {
        ProtocolReplayEvidence replay;
        MixedTrafficEvidence mixed;
        using (var fixture = new ProtocolReplayFixture(replayRecipients, subscriptionCapacity))
        {
            replay = fixture.Measure(replayWarmupTicks, replayTicks);
            mixed = fixture.MeasureMixedTraffic(Math.Max(1, replayWarmupTicks / 2), replayTicks);
        }
        DenseTownEntryEvidence town;
        using (var fixture = new DenseTownEntryFixture(
            townChunks, townRowsPerChunk, subscriptionCapacity))
            town = fixture.Measure();
        SharedSocketReplayEvidence sockets;
        using (var fixture = new SharedSocketReplayFixture(
            replayRecipients, subscriptionCapacity))
            sockets = fixture.Measure(replayWarmupTicks, replayTicks);
        return new TransportAdoptionEvidence(
            replay,
            mixed,
            town,
            sockets,
            "Evidence level 2 uses secure deterministic ProtocolLoopback: real Network framing, " +
            "security, packetization, client decode/apply, and application proof, but no sockets. " +
            "The shared-socket row uses real connected loopback clients and one real shared server " +
            "UDP listener in one process. It is not a sharded-client or public-WAN CCU claim.");
    }

    private sealed class ProtocolReplayFixture : IDisposable
    {
        private readonly int _recipientCount;
        private readonly EcsWorld _world;
        private readonly Entity _entity;
        private readonly ChunkPublicationStore _source;
        private readonly ChunkPublicationWorkScheduler _scheduler;
        private readonly RemotePair[] _pairs;
        private readonly WorldPublicChange[] _change = new WorldPublicChange[1];
        private readonly WorldPublicDescriptor _descriptor = new(
            GameWorldPublicPrefabIds.Character,
            WorldYaw.Zero,
            WorldPublicStateFlags.Visible);
        private bool _side;
        private ulong _step;
        private readonly ulong[] _lastOwnerTicks;
        private readonly ulong[] _eventApplications;
        private readonly ulong[] _actionApplications;
        private readonly ulong[] _eventProofs;
        private readonly byte[] _ownerScratch = new byte[RemoteOwnerStateMessageCodec.EncodedSize];
        private readonly byte[] _eventScratch = new byte[RemoteApplicationEventMessageCodec.EncodedSize];
        private readonly byte[] _actionScratch = new byte[RemoteUiActionHeaderCodec.EncodedSize];
        private readonly byte[] _eventProofScratch =
            new byte[RemoteApplicationEventAppliedMessageCodec.EncodedSize];
        private ulong _disclosureViolations;
        private ulong _mixedStep;

        internal ProtocolReplayFixture(int recipients, int subscriptionCapacity)
        {
            if (recipients <= 0) throw new ArgumentOutOfRangeException(nameof(recipients));
            _recipientCount = recipients;
            _lastOwnerTicks = new ulong[recipients];
            _eventApplications = new ulong[recipients];
            _actionApplications = new ulong[recipients];
            _eventProofs = new ulong[recipients];
            _world = CreateEcs(1, out ArchetypeHandle empty);
            Check(_world.TryCreate(empty, out _entity));
            SpatialChunkKey key = Key(0, 0);
            WorldPosition position = new(0, 0, 8_000, 8_000, 0);
            _source = new ChunkPublicationStore(
                PublicationEpoch.Create(),
                new ChunkPublicationLimits(1, 2, 1, 256));
            Check(_source.TryCommit([
                WorldPublicChange.Present(_entity, position, _descriptor)
            ], out _));
            BoundWorldPack world = CreateWorld([key]);
            _scheduler = new ChunkPublicationWorkScheduler(
                _source,
                new ChunkPublicationWorkSchedulerLimits(
                    recipients,
                    checked(recipients * subscriptionCapacity)));
            _pairs = new RemotePair[recipients];
            var changes = new OfflineDirectChunkSubscriptionChange[subscriptionCapacity];
            for (int index = 0; index < recipients; index++)
            {
                var recipient = new OfflineDirectChunkRecipient(
                    new OfflineDirectChunkRecipientLimits(subscriptionCapacity, 64, 64),
                    GameChunkInterestProfile.Default,
                    new OfflineDirectChunkCadence(1, 1, 1));
                Check(_scheduler.TryRegister(recipient, out ChunkReplicationRecipientId id));
                Check(recipient.Reconcile(
                    _source,
                    GameWorldState.DefaultSpace,
                    position,
                    changes,
                    out OfflineDirectChunkReconcileResult result) ==
                    OfflineDirectChunkReconcileStatus.Reconciled);
                Check(_scheduler.TryApplyReconciliation(
                    id, changes.AsSpan(0, result.ChangeCount), result));
                var cache = new ClientWorldSemanticCache(
                    world,
                    new ClientWorldSemanticCacheLimits(1, 0, 0, 64, 1));
                Check(cache.TryRequest(key, out ClientSemanticChunkHandle handle, out _));
                Check(cache.TryStageFromBaseWorld(handle, [], default, out _));
                Check(cache.TryPublish(handle, out _));
                int subscription = FindSubscription(recipient.Subscriptions, key);
                Check(subscription >= 0);
                Check(recipient.TryBeginRead(
                    _source, subscription, 0, out OfflineDirectChunkRead read) ==
                    OfflineDirectChunkReadStatus.Snapshot);
                Check(cache.TryApplySnapshot(handle, read.Batch, read.Snapshot, out _));
                recipient.CommitRead();
                ProtocolLoopback loopback = CreateLoopback();
                var server = new RemoteChunkWorldStateServer(
                    loopback.Server,
                    recipient,
                    _source,
                    ApplicationSessionEpoch,
                    new RemoteChunkWorldStateTransportLimits(8));
                var client = new RemoteChunkWorldStateClient(
                    loopback.Client,
                    cache,
                    ApplicationSessionEpoch,
                    maximumDeltaRecords: 64,
                    maximumPendingProofs: 8);
                _pairs[index] = new RemotePair(
                    loopback, recipient, id, cache, handle, server, client);
            }
            while (_scheduler.TryDequeueDue(0, out _)) { }
        }

        internal ProtocolReplayEvidence Measure(int warmupTicks, int measuredTicks)
        {
            for (int tick = 0; tick < warmupTicks; tick++) _ = RunTick(measure: false);
            ulong appliedBefore = SumAppliedProofs();
            var total = new double[measuredTicks];
            var semantic = new double[measuredTicks];
            var serverOffer = new double[measuredTicks];
            var protocol = new double[measuredTicks];
            var client = new double[measuredTicks];
            var proof = new double[measuredTicks];
            ulong datagramsBefore = SumDatagrams();
            ulong serverPlaintextBefore = SumServerPlaintext();
            ulong clientPlaintextBefore = SumClientPlaintext();
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            int maximumPumps = 0;
            for (int tick = 0; tick < measuredTicks; tick++)
            {
                ProtocolTick item = RunTick(measure: true);
                total[tick] = item.TotalMicroseconds;
                semantic[tick] = item.SemanticMicroseconds;
                serverOffer[tick] = item.ServerOfferMicroseconds;
                protocol[tick] = item.ProtocolMicroseconds;
                client[tick] = item.ClientMicroseconds;
                proof[tick] = item.ProofMicroseconds;
                maximumPumps = Math.Max(maximumPumps, item.Pumps);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            Check(allocated == 0);
            ulong applied = 0;
            ulong rejected = 0;
            long reserved = _source.GetSnapshot().ReservedBytes + _scheduler.GetSnapshot().ReservedBytes;
            for (int index = 0; index < _pairs.Length; index++)
            {
                RemoteChunkWorldStateServerSnapshot server = _pairs[index].Server.GetSnapshot();
                RemoteChunkWorldStateClientSnapshot remote = _pairs[index].Client.GetSnapshot();
                applied += server.AppliedProofs;
                rejected += remote.RejectedDeliveries + remote.MalformedDeliveries +
                    remote.ContradictoryDeliveries;
                reserved = checked(reserved + server.ReservedBytes + remote.ReservedBytes +
                    _pairs[index].Recipient.GetSnapshot().ReservedBytes +
                    _pairs[index].Cache.GetSnapshot().ReservedBytes +
                    _pairs[index].Loopback.GetSnapshot().Client.ReservedBytes +
                    _pairs[index].Loopback.GetSnapshot().Server.ReservedBytes +
                    _pairs[index].Loopback.GetSnapshot().Datagram.ReservedBytes);
            }
            Check(rejected == 0);
            return new ProtocolReplayEvidence(
                _recipientCount,
                warmupTicks,
                measuredTicks,
                applied - appliedBefore,
                rejected,
                maximumPumps,
                SumDatagrams() - datagramsBefore,
                SumServerPlaintext() - serverPlaintextBefore,
                SumClientPlaintext() - clientPlaintextBefore,
                reserved,
                allocated,
                Metric(total),
                Metric(semantic),
                Metric(serverOffer),
                Metric(protocol),
                Metric(client),
                Metric(proof));
        }

        internal MixedTrafficEvidence MeasureMixedTraffic(int warmupTicks, int measuredTicks)
        {
            for (int tick = 0; tick < warmupTicks; tick++) _ = RunMixedTick(measure: false);
            var total = new double[measuredTicks];
            var protocol = new double[measuredTicks];
            var application = new double[measuredTicks];
            int maximumPumps = 0;
            ulong publicBefore = SumAppliedProofs();
            ulong eventsBefore = Sum(_eventApplications);
            ulong actionsBefore = Sum(_actionApplications);
            ulong eventProofsBefore = Sum(_eventProofs);
            ulong disclosureBefore = _disclosureViolations;
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            for (int tick = 0; tick < measuredTicks; tick++)
            {
                MixedTrafficTick sample = RunMixedTick(measure: true);
                total[tick] = sample.TotalMicroseconds;
                protocol[tick] = sample.ProtocolMicroseconds;
                application[tick] = sample.ApplicationMicroseconds;
                maximumPumps = Math.Max(maximumPumps, sample.Pumps);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            Check(allocated == 0);
            ulong expected = checked((ulong)_recipientCount * (ulong)measuredTicks);
            ulong publicApplied = SumAppliedProofs() - publicBefore;
            ulong eventsApplied = Sum(_eventApplications) - eventsBefore;
            ulong actionsApplied = Sum(_actionApplications) - actionsBefore;
            ulong proofsApplied = Sum(_eventProofs) - eventProofsBefore;
            ulong disclosure = _disclosureViolations - disclosureBefore;
            Check(publicApplied == expected && eventsApplied == expected &&
                actionsApplied == expected && proofsApplied == expected && disclosure == 0);
            return new MixedTrafficEvidence(
                _recipientCount,
                warmupTicks,
                measuredTicks,
                publicApplied,
                expected,
                eventsApplied,
                actionsApplied,
                proofsApplied,
                disclosure,
                maximumPumps,
                allocated,
                Metric(total),
                Metric(protocol),
                Metric(application),
                "Every recipient receives one chunk delta, one owner-private state, and one " +
                "retained Event while sending one Control action. Recipient-specific owner/event " +
                "identity is validated before counting; all four streams converge before the next tick.");
        }

        private MixedTrafficTick RunMixedTick(bool measure)
        {
            _step++;
            _mixedStep++;
            _side = !_side;
            _change[0] = WorldPublicChange.Present(
                _entity,
                new WorldPosition(0, 0, checked((ushort)(_side ? 8_001 : 8_000)), 8_000, 0),
                _descriptor);
            long started = Stopwatch.GetTimestamp();
            Check(_source.TryCommit(_change, out _));
            Check(_scheduler.ObserveCommit(_source) ==
                ChunkPublicationCommitObservationStatus.Observed);
            int offered = 0;
            while (_scheduler.TryDequeueDue(_step, out ChunkPublicationWork work))
            {
                RemotePair pair = _pairs[work.Recipient.Index];
                Check(pair.Id == work.Recipient);
                Check(pair.Server.TrySchedule(work.Key, _step) ==
                    RemoteChunkWorldStateScheduleStatus.QueuedDelta);
                offered++;
            }
            Check(offered == _recipientCount);
            for (int index = 0; index < _pairs.Length; index++) QueueMixedMessages(index);

            double protocolMicros = 0;
            double applicationMicros = 0;
            int pumps = 0;
            while (!MixedTickComplete())
            {
                pumps++;
                Check(pumps <= 64);
                for (int index = 0; index < _pairs.Length; index++)
                    PumpMixed(index, ref protocolMicros, ref applicationMicros);
            }
            long stopped = Stopwatch.GetTimestamp();
            if (!measure) return default;
            return new MixedTrafficTick(
                Microseconds(stopped - started), protocolMicros, applicationMicros, pumps);
        }

        private void QueueMixedMessages(int index)
        {
            RemotePair pair = _pairs[index];
            var owner = new RemoteOwnerStateMessage
            {
                ApplicationSessionEpoch = ApplicationSessionEpoch,
                StateEpoch = 1,
                EntityIndex = checked((uint)index + 1),
                EntityGeneration = 1,
                SimulationTick = _step,
                FrameSequence = _mixedStep,
                LastProcessedInputSequence = _mixedStep,
                TickMarker = checked((int)_mixedStep),
                InputVelocity = index,
                ClockRevision = 1,
                IsPresent = true,
            };
            Check(RemoteOwnerStateMessageCodec.TryWriteExact(_ownerScratch, in owner));
            Check(pair.Loopback.Server.TryQueue(
                RemoteOwnerStateMessageCodec.Descriptor.Id, _ownerScratch) ==
                NetworkQueueStatus.Accepted);

            var applicationEvent = new RemoteApplicationEventMessage
            {
                ApplicationSessionEpoch = ApplicationSessionEpoch,
                StateEpoch = 1,
                Sequence = _mixedStep,
                ParticipantId = checked((ulong)index + 1),
                Kind = 1,
                Subject = _mixedStep,
                PayloadHash = checked((ulong)index * 1_000_003UL + _mixedStep),
            };
            Check(RemoteApplicationEventMessageCodec.TryWriteExact(
                _eventScratch, in applicationEvent));
            Check(pair.Loopback.Server.TryQueue(
                RemoteApplicationEventMessageCodec.Descriptor.Id, _eventScratch) ==
                NetworkQueueStatus.Accepted);

            var action = new RemoteUiActionHeader
            {
                ApplicationSessionEpoch = ApplicationSessionEpoch,
            };
            Check(RemoteUiActionHeaderCodec.TryWriteExact(_actionScratch, in action));
            Check(pair.Loopback.Client.TryQueue(
                RemoteUiActionHeaderCodec.Descriptor.Id, _actionScratch) ==
                NetworkQueueStatus.Accepted);
        }

        private bool MixedTickComplete()
        {
            ulong targetPublic = checked(_step * (ulong)_recipientCount);
            if (SumAppliedProofs() < targetPublic) return false;
            for (int index = 0; index < _pairs.Length; index++)
            {
                if (_lastOwnerTicks[index] < _step ||
                    _eventApplications[index] < _mixedStep ||
                    _actionApplications[index] < _mixedStep ||
                    _eventProofs[index] < _mixedStep)
                    return false;
            }
            return true;
        }

        private void PumpMixed(
            int index,
            ref double protocolMicroseconds,
            ref double applicationMicroseconds)
        {
            RemotePair pair = _pairs[index];
            _ = pair.Server.TryProgress();
            long started = Stopwatch.GetTimestamp();
            pair.Loopback.Pump();
            protocolMicroseconds += Microseconds(Stopwatch.GetTimestamp() - started);

            started = Stopwatch.GetTimestamp();
            while (pair.Loopback.Client.TryPeekReceived(
                       out NetworkDeliveredMessage message, out ReadOnlySpan<byte> payload))
            {
                bool handled = pair.Client.TryProcess(message, payload);
                if (!handled && message.MessageId == RemoteOwnerStateMessageCodec.Descriptor.Id)
                {
                    Check(RemoteOwnerStateMessageCodec.TryReadExact(
                        payload, out RemoteOwnerStateMessage owner));
                    if (owner.ApplicationSessionEpoch != ApplicationSessionEpoch ||
                        owner.EntityIndex != checked((uint)index + 1))
                        _disclosureViolations++;
                    else
                        _lastOwnerTicks[index] = Math.Max(_lastOwnerTicks[index], owner.SimulationTick);
                    handled = true;
                }
                if (!handled && message.MessageId == RemoteApplicationEventMessageCodec.Descriptor.Id)
                {
                    Check(RemoteApplicationEventMessageCodec.TryReadExact(
                        payload, out RemoteApplicationEventMessage applicationEvent));
                    if (applicationEvent.ApplicationSessionEpoch != ApplicationSessionEpoch ||
                        applicationEvent.ParticipantId != checked((ulong)index + 1))
                        _disclosureViolations++;
                    else
                        _eventApplications[index]++;
                    var proof = new RemoteApplicationEventAppliedMessage
                    {
                        ApplicationSessionEpoch = ApplicationSessionEpoch,
                        StateEpoch = applicationEvent.StateEpoch,
                        NextSequence = checked(applicationEvent.Sequence + 1),
                        Status = RemoteApplicationEventApplyStatus.Applied,
                    };
                    Check(RemoteApplicationEventAppliedMessageCodec.TryWriteExact(
                        _eventProofScratch, in proof));
                    Check(pair.Loopback.Client.TryQueue(
                        RemoteApplicationEventAppliedMessageCodec.Descriptor.Id,
                        _eventProofScratch) == NetworkQueueStatus.Accepted);
                    handled = true;
                }
                Check(handled);
                Check(pair.Loopback.Client.TryConsumeReceived());
            }
            _ = pair.Client.TryProgress();
            applicationMicroseconds += Microseconds(Stopwatch.GetTimestamp() - started);

            started = Stopwatch.GetTimestamp();
            pair.Loopback.Pump();
            protocolMicroseconds += Microseconds(Stopwatch.GetTimestamp() - started);
            started = Stopwatch.GetTimestamp();
            while (pair.Loopback.Server.TryPeekReceived(
                       out NetworkDeliveredMessage message, out ReadOnlySpan<byte> payload))
            {
                bool handled = pair.Server.TryProcess(message, payload);
                if (!handled && message.MessageId == RemoteUiActionHeaderCodec.Descriptor.Id)
                {
                    Check(RemoteUiActionHeaderCodec.TryReadExact(
                        payload, out RemoteUiActionHeader action));
                    if (action.ApplicationSessionEpoch != ApplicationSessionEpoch)
                        _disclosureViolations++;
                    else
                        _actionApplications[index]++;
                    handled = true;
                }
                if (!handled &&
                    message.MessageId == RemoteApplicationEventAppliedMessageCodec.Descriptor.Id)
                {
                    Check(RemoteApplicationEventAppliedMessageCodec.TryReadExact(
                        payload, out RemoteApplicationEventAppliedMessage proof));
                    if (proof.ApplicationSessionEpoch != ApplicationSessionEpoch ||
                        proof.StateEpoch != 1 || proof.Status != RemoteApplicationEventApplyStatus.Applied)
                        _disclosureViolations++;
                    else
                        _eventProofs[index]++;
                    handled = true;
                }
                Check(handled);
                Check(pair.Loopback.Server.TryConsumeReceived());
            }
            applicationMicroseconds += Microseconds(Stopwatch.GetTimestamp() - started);
        }

        private static ulong Sum(ulong[] values)
        {
            ulong result = 0;
            for (int index = 0; index < values.Length; index++) result += values[index];
            return result;
        }

        private ProtocolTick RunTick(bool measure)
        {
            _step++;
            _side = !_side;
            _change[0] = WorldPublicChange.Present(
                _entity,
                new WorldPosition(0, 0, checked((ushort)(_side ? 8_001 : 8_000)), 8_000, 0),
                _descriptor);
            long started = Stopwatch.GetTimestamp();
            Check(_source.TryCommit(_change, out _));
            Check(_scheduler.ObserveCommit(_source) ==
                ChunkPublicationCommitObservationStatus.Observed);
            long semantic = Stopwatch.GetTimestamp();
            int offered = 0;
            while (_scheduler.TryDequeueDue(_step, out ChunkPublicationWork work))
            {
                RemotePair pair = _pairs[work.Recipient.Index];
                Check(pair.Id == work.Recipient);
                Check(pair.Server.TrySchedule(work.Key, _step) ==
                    RemoteChunkWorldStateScheduleStatus.QueuedDelta);
                offered++;
            }
            Check(offered == _recipientCount);
            long offeredAt = Stopwatch.GetTimestamp();
            ulong targetProofs = checked(_step * (ulong)_recipientCount);
            double protocolMicros = 0;
            double clientMicros = 0;
            double proofMicros = 0;
            int pumps = 0;
            while (SumAppliedProofs() < targetProofs)
            {
                pumps++;
                Check(pumps <= 32);
                for (int index = 0; index < _pairs.Length; index++)
                    Pump(_pairs[index], ref protocolMicros, ref clientMicros, ref proofMicros);
            }
            long stopped = Stopwatch.GetTimestamp();
            if (!measure) return default;
            return new ProtocolTick(
                Microseconds(stopped - started),
                Microseconds(semantic - started),
                Microseconds(offeredAt - semantic),
                protocolMicros,
                clientMicros,
                proofMicros,
                pumps);
        }

        private ulong SumAppliedProofs()
        {
            ulong result = 0;
            for (int index = 0; index < _pairs.Length; index++)
                result += _pairs[index].Server.GetSnapshot().AppliedProofs;
            return result;
        }

        private ulong SumDatagrams()
        {
            ulong result = 0;
            for (int index = 0; index < _pairs.Length; index++)
                result += _pairs[index].Loopback.GetSnapshot().Datagram.Submitted;
            return result;
        }

        private ulong SumServerPlaintext()
        {
            ulong result = 0;
            for (int index = 0; index < _pairs.Length; index++)
                result += _pairs[index].Loopback.GetSnapshot().Server.EncryptedPlaintextBytes;
            return result;
        }

        private ulong SumClientPlaintext()
        {
            ulong result = 0;
            for (int index = 0; index < _pairs.Length; index++)
                result += _pairs[index].Loopback.GetSnapshot().Client.EncryptedPlaintextBytes;
            return result;
        }

        public void Dispose()
        {
            for (int index = 0; index < _pairs.Length; index++) _pairs[index]?.Dispose();
            _world.Dispose();
        }
    }

    private sealed class DenseTownEntryFixture : IDisposable
    {
        private readonly int _chunkCount;
        private readonly int _rowsPerChunk;
        private readonly EcsWorld _world;
        private readonly ChunkPublicationStore _source;
        private readonly OfflineDirectChunkRecipient _recipient;
        private readonly ClientWorldSemanticCache _cache;
        private readonly ClientSemanticChunkHandle[] _handles;
        private readonly SpatialChunkKey[] _keys;
        private readonly ProtocolLoopback _loopback;
        private readonly RemoteChunkWorldStateServer _server;
        private readonly RemoteChunkWorldStateClient _client;
        private readonly RemotePair _pair;

        internal DenseTownEntryFixture(
            int chunks,
            int rowsPerChunk,
            int subscriptionCapacity)
        {
            if (chunks <= 1 || rowsPerChunk is <= 0 or > 64)
                throw new ArgumentOutOfRangeException(nameof(chunks));
            _chunkCount = chunks;
            _rowsPerChunk = rowsPerChunk;
            _keys = TownKeys(chunks);
            int entityCount = checked(chunks * rowsPerChunk);
            _world = CreateEcs(entityCount, out ArchetypeHandle empty);
            var changes = new WorldPublicChange[entityCount];
            var descriptor = new WorldPublicDescriptor(
                GameWorldPublicPrefabIds.Character,
                WorldYaw.Zero,
                WorldPublicStateFlags.Visible);
            for (int index = 0; index < entityCount; index++)
            {
                Check(_world.TryCreate(empty, out Entity entity));
                SpatialChunkKey key = _keys[index / rowsPerChunk];
                int row = index % rowsPerChunk;
                var position = new WorldPosition(
                    key.Coord.X,
                    key.Coord.Z,
                    checked((ushort)(500 + row % 8 * 1_000)),
                    checked((ushort)(500 + row / 8 * 1_000)),
                    0);
                changes[index] = WorldPublicChange.Present(entity, position, descriptor);
            }
            _source = new ChunkPublicationStore(
                PublicationEpoch.Create(),
                new ChunkPublicationLimits(
                    entityCount,
                    checked(entityCount * 2),
                    64,
                    checked(entityCount * 2)));
            Check(_source.TryCommit(changes, out _));
            _recipient = new OfflineDirectChunkRecipient(
                new OfflineDirectChunkRecipientLimits(subscriptionCapacity, 64, 64),
                GameChunkInterestProfile.Default,
                new OfflineDirectChunkCadence(1, 1, 1));
            var reconcileChanges = new OfflineDirectChunkSubscriptionChange[subscriptionCapacity];
            Check(_recipient.Reconcile(
                _source,
                GameWorldState.DefaultSpace,
                new WorldPosition(0, 0, 8_000, 8_000, 0),
                reconcileChanges,
                out _) == OfflineDirectChunkReconcileStatus.Reconciled);
            BoundWorldPack baseWorld = CreateWorld(_keys);
            _cache = new ClientWorldSemanticCache(
                baseWorld,
                new ClientWorldSemanticCacheLimits(
                    chunks, 0, 0, 64, entityCount));
            _handles = new ClientSemanticChunkHandle[chunks];
            for (int index = 0; index < chunks; index++)
            {
                Check(_cache.TryRequest(_keys[index], out _handles[index], out _));
                Check(_cache.TryStageFromBaseWorld(_handles[index], [], default, out _));
                Check(_cache.TryPublish(_handles[index], out _));
            }
            EstablishAbsentKnowledge();
            _loopback = CreateLoopback(slotCapacity: 256);
            _server = new RemoteChunkWorldStateServer(
                _loopback.Server,
                _recipient,
                _source,
                ApplicationSessionEpoch + 1,
                new RemoteChunkWorldStateTransportLimits(8));
            _client = new RemoteChunkWorldStateClient(
                _loopback.Client,
                _cache,
                ApplicationSessionEpoch + 1,
                maximumDeltaRecords: 64,
                maximumPendingProofs: 8);
            _pair = new RemotePair(
                _loopback, _recipient, default, _cache, default, _server, _client,
                ownsLoopback: false);
        }

        internal DenseTownEntryEvidence Measure()
        {
            ulong datagramsBefore = _loopback.GetSnapshot().Datagram.Submitted;
            long started = Stopwatch.GetTimestamp();
            int totalPumps = 0;
            int maximumPumps = 0;
            int busy = 0;
            for (int index = 0; index < _keys.Length; index++)
            {
                int subscription = FindSubscription(_recipient.Subscriptions, _keys[index]);
                Check(subscription >= 0);
                Check(_server.TrySchedule(subscription, checked((ulong)index + 1)) ==
                    RemoteChunkWorldStateScheduleStatus.SnapshotTransferStarted);
                if (index + 1 < _keys.Length)
                {
                    int next = FindSubscription(_recipient.Subscriptions, _keys[index + 1]);
                    Check(_server.TrySchedule(next, checked((ulong)index + 1)) ==
                        RemoteChunkWorldStateScheduleStatus.SnapshotTransferBusy);
                    busy++;
                }
                ulong expected = checked((ulong)index + 1);
                int pumps = 0;
                double protocol = 0;
                double client = 0;
                double proof = 0;
                while (_server.GetSnapshot().AppliedProofs < expected)
                {
                    pumps++;
                    Check(pumps <= 64);
                    Pump(_pair, ref protocol, ref client, ref proof);
                }
                totalPumps += pumps;
                maximumPumps = Math.Max(maximumPumps, pumps);
                Check(_cache.TryGetResidentView(_handles[index], out ClientSemanticChunkView view));
                Check(view.PublicRecords.Length == _rowsPerChunk);
            }
            long stopped = Stopwatch.GetTimestamp();
            RemoteChunkWorldStateServerSnapshot server = _server.GetSnapshot();
            RemoteChunkWorldStateClientSnapshot clientSnapshot = _client.GetSnapshot();
            Check(server.AppliedProofs == checked((ulong)_chunkCount));
            Check(clientSnapshot.AppliedSnapshots == checked((ulong)_chunkCount));
            Check(clientSnapshot.RejectedSnapshots == 0 && clientSnapshot.RejectedDeliveries == 0);
            return new DenseTownEntryEvidence(
                _chunkCount,
                _rowsPerChunk,
                checked(_chunkCount * _rowsPerChunk),
                _recipient.GetSnapshot().SubscriptionCount,
                busy,
                totalPumps,
                maximumPumps,
                Microseconds(stopped - started),
                server.SnapshotsQueued,
                server.SnapshotSegmentsQueued,
                clientSnapshot.AppliedSnapshots,
                _loopback.GetSnapshot().Datagram.Submitted - datagramsBefore,
                server.ReservedBytes,
                clientSnapshot.ReservedBytes,
                _cache.GetSnapshot().ReservedBytes,
                "All absent subscriptions are pre-established packet-free so this observation " +
                "isolates populated incoming-chunk recovery. The current one-candidate client " +
                "serializes snapshots and returns SnapshotTransferBusy for the next populated key.");
        }

        private void EstablishAbsentKnowledge()
        {
            for (int index = 0; index < _recipient.Subscriptions.Length; index++)
            {
                SpatialChunkKey key = _recipient.Subscriptions[index].Interest.Key;
                if (Array.BinarySearch(_keys, key) >= 0) continue;
                Check(_recipient.TryBeginRead(_source, index, 0, out _) ==
                    OfflineDirectChunkReadStatus.ChunkAbsent);
                _recipient.CommitRead();
            }
        }

        public void Dispose()
        {
            _loopback.Dispose();
            _world.Dispose();
        }
    }

    private sealed class SharedSocketReplayFixture : IDisposable
    {
        private readonly int _recipientCount;
        private readonly EcsWorld _world;
        private readonly Entity _entity;
        private readonly ChunkPublicationStore _source;
        private readonly ChunkPublicationWorkScheduler _scheduler;
        private readonly SharedUdpProtocolHost _host;
        private readonly SharedSocketPair[] _pairs;
        private readonly WorldPublicChange[] _change = new WorldPublicChange[1];
        private readonly WorldPublicDescriptor _descriptor = new(
            GameWorldPublicPrefabIds.Character,
            WorldYaw.Zero,
            WorldPublicStateFlags.Visible);
        private bool _side;
        private ulong _step;

        internal SharedSocketReplayFixture(int recipients, int subscriptionCapacity)
        {
            _recipientCount = recipients;
            _world = CreateEcs(1, out ArchetypeHandle empty);
            Check(_world.TryCreate(empty, out _entity));
            SpatialChunkKey key = Key(0, 0);
            var position = new WorldPosition(0, 0, 8_000, 8_000, 0);
            _source = new ChunkPublicationStore(
                PublicationEpoch.Create(),
                new ChunkPublicationLimits(1, 2, 1, 256));
            Check(_source.TryCommit([
                WorldPublicChange.Present(_entity, position, _descriptor)
            ], out _));
            BoundWorldPack world = CreateWorld([key]);
            _scheduler = new ChunkPublicationWorkScheduler(
                _source,
                new ChunkPublicationWorkSchedulerLimits(
                    recipients,
                    checked(recipients * subscriptionCapacity)));

            var protocol = new NetworkProtocolLimits(
                MaximumReliableMessages: 32,
                MaximumReceivedMessages: 32,
                MaximumMessageBytes: 1_472,
                MaximumFragmentsPerMessage: 2,
                SentPacketCapacity: 256,
                MaximumInboundPacketsPerPump: 64,
                MaximumPacketsPerPump: 64,
                MinimumRetransmissionSteps: 2,
                MaximumRetransmissionSteps: 128,
                InitialCongestionWindowPackets: 8,
                MaximumCongestionWindowPackets: 128,
                MaximumIndependentUnreliableMessages: 8,
                ReliablePayloadByteCapacity: 65_536,
                ReceivedPayloadByteCapacity: 65_536);
            var udp = new UdpProtocolLimits(
                MaximumDatagramBytes: 1_472,
                SocketSendBufferBytes: 65_536,
                SocketReceiveBufferBytes: 65_536,
                MaximumHandshakePumps: 16_384,
                CookieBucketMilliseconds: 30_000,
                IdleTimeoutMilliseconds: 30_000,
                DrainTimeoutMilliseconds: 5_000,
                KeepAliveIntervalMilliseconds: 5_000);
            var shared = new SharedUdpProtocolLimits(
                MaximumSessions: recipients,
                MaximumQueuedDatagramsPerSession: 16,
                MaximumInboundDatagramsPerPump: Math.Max(4_096, recipients * 32),
                SocketIoMode: SharedUdpSocketIoMode.PlatformSendReceiveBatch,
                MaximumNativeSendBatchDatagrams: 32,
                MaximumNativeReceiveBatchDatagrams: 32);
            _host = SharedUdpProtocolHost.CreateSelfTrustedLoopback(
                protocol, udp, shared, UltraSimGameProtocol.Manifest);
            _pairs = new SharedSocketPair[recipients];
            var changes = new OfflineDirectChunkSubscriptionChange[subscriptionCapacity];
            for (int index = 0; index < recipients; index++)
            {
                Check(_host.TryAddLoopbackSession(
                    out SharedUdpProtocolSession? session, out _));
                var recipient = new OfflineDirectChunkRecipient(
                    new OfflineDirectChunkRecipientLimits(subscriptionCapacity, 64, 64),
                    GameChunkInterestProfile.Default,
                    new OfflineDirectChunkCadence(1, 1, 1));
                Check(_scheduler.TryRegister(recipient, out ChunkReplicationRecipientId id));
                Check(recipient.Reconcile(
                    _source,
                    GameWorldState.DefaultSpace,
                    position,
                    changes,
                    out OfflineDirectChunkReconcileResult result) ==
                    OfflineDirectChunkReconcileStatus.Reconciled);
                Check(_scheduler.TryApplyReconciliation(
                    id, changes.AsSpan(0, result.ChangeCount), result));
                var cache = new ClientWorldSemanticCache(
                    world,
                    new ClientWorldSemanticCacheLimits(1, 0, 0, 64, 1));
                Check(cache.TryRequest(key, out ClientSemanticChunkHandle handle, out _));
                Check(cache.TryStageFromBaseWorld(handle, [], default, out _));
                Check(cache.TryPublish(handle, out _));
                int subscription = FindSubscription(recipient.Subscriptions, key);
                Check(subscription >= 0);
                Check(recipient.TryBeginRead(
                    _source, subscription, 0, out OfflineDirectChunkRead read) ==
                    OfflineDirectChunkReadStatus.Snapshot);
                Check(cache.TryApplySnapshot(handle, read.Batch, read.Snapshot, out _));
                recipient.CommitRead();
                var server = new RemoteChunkWorldStateServer(
                    session!.Server,
                    recipient,
                    _source,
                    ApplicationSessionEpoch + 2,
                    new RemoteChunkWorldStateTransportLimits(8));
                var client = new RemoteChunkWorldStateClient(
                    session.Client,
                    cache,
                    ApplicationSessionEpoch + 2,
                    maximumDeltaRecords: 64,
                    maximumPendingProofs: 8);
                _pairs[index] = new SharedSocketPair(
                    session, recipient, id, cache, server, client);
            }
            while (_scheduler.TryDequeueDue(0, out _)) { }
        }

        internal SharedSocketReplayEvidence Measure(int warmupTicks, int measuredTicks)
        {
            for (int tick = 0; tick < warmupTicks; tick++) _ = RunTick(measure: false);
            var total = new double[measuredTicks];
            var sockets = new double[measuredTicks];
            int maximumPumps = 0;
            ulong proofsBefore = SumProofs();
            SharedUdpProtocolHostSnapshot before = _host.GetSnapshot();
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            for (int tick = 0; tick < measuredTicks; tick++)
            {
                SharedSocketTick sample = RunTick(measure: true);
                total[tick] = sample.TotalMicroseconds;
                sockets[tick] = sample.SocketPumpMicroseconds;
                maximumPumps = Math.Max(maximumPumps, sample.Pumps);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            Check(allocated == 0);
            SharedUdpProtocolHostSnapshot after = _host.GetSnapshot();
            ulong applied = SumProofs() - proofsBefore;
            Check(applied == checked((ulong)_recipientCount * (ulong)measuredTicks));
            return new SharedSocketReplayEvidence(
                _recipientCount,
                warmupTicks,
                measuredTicks,
                applied,
                maximumPumps,
                after.KnownServerReservedBytes,
                after.KnownClientReservedBytes,
                after.ConfiguredListenerSocketBufferBytes,
                after.ConfiguredClientSocketBufferBytes,
                after.Listener.SendCalls - before.Listener.SendCalls,
                after.Listener.ReceiveCalls - before.Listener.ReceiveCalls,
                after.Listener.NativeSendCalls - before.Listener.NativeSendCalls,
                after.Listener.ReceiveCensus.NativeReceiveCalls -
                    before.Listener.ReceiveCensus.NativeReceiveCalls,
                after.Listener.UsesNativeSendBatching,
                after.Listener.ReceiveCensus.UsesNativeReceiveBatching,
                allocated,
                Metric(total),
                Metric(sockets),
                "One process owns one real shared UDP server listener and real connected loopback " +
                "client sockets. This isolates the intended socket topology but not sharded client CPU.");
        }

        private SharedSocketTick RunTick(bool measure)
        {
            _step++;
            _side = !_side;
            _change[0] = WorldPublicChange.Present(
                _entity,
                new WorldPosition(0, 0, checked((ushort)(_side ? 8_001 : 8_000)), 8_000, 0),
                _descriptor);
            long started = Stopwatch.GetTimestamp();
            Check(_source.TryCommit(_change, out _));
            Check(_scheduler.ObserveCommit(_source) ==
                ChunkPublicationCommitObservationStatus.Observed);
            int offered = 0;
            while (_scheduler.TryDequeueDue(_step, out ChunkPublicationWork work))
            {
                SharedSocketPair pair = _pairs[work.Recipient.Index];
                Check(pair.Id == work.Recipient);
                Check(pair.Server.TrySchedule(work.Key, _step) ==
                    RemoteChunkWorldStateScheduleStatus.QueuedDelta);
                offered++;
            }
            Check(offered == _recipientCount);
            double socketMicros = 0;
            int pumps = 0;
            ulong target = checked(_step * (ulong)_recipientCount);
            while (SumProofs() < target)
            {
                pumps++;
                Check(pumps <= 256);
                for (int index = 0; index < _pairs.Length; index++)
                    _ = _pairs[index].Server.TryProgress();
                long socketStarted = Stopwatch.GetTimestamp();
                _host.Pump();
                socketMicros += Microseconds(Stopwatch.GetTimestamp() - socketStarted);
                for (int index = 0; index < _pairs.Length; index++)
                {
                    SharedSocketPair pair = _pairs[index];
                    while (pair.Session.Client.TryPeekReceived(
                               out NetworkDeliveredMessage message,
                               out ReadOnlySpan<byte> payload))
                    {
                        Check(pair.Client.TryProcess(message, payload));
                        Check(pair.Session.Client.TryConsumeReceived());
                    }
                    _ = pair.Client.TryProgress();
                }
                socketStarted = Stopwatch.GetTimestamp();
                _host.Pump();
                socketMicros += Microseconds(Stopwatch.GetTimestamp() - socketStarted);
                for (int index = 0; index < _pairs.Length; index++)
                {
                    SharedSocketPair pair = _pairs[index];
                    while (pair.Session.Server.TryPeekReceived(
                               out NetworkDeliveredMessage message,
                               out ReadOnlySpan<byte> payload))
                    {
                        Check(pair.Server.TryProcess(message, payload));
                        Check(pair.Session.Server.TryConsumeReceived());
                    }
                }
            }
            long stopped = Stopwatch.GetTimestamp();
            return measure
                ? new SharedSocketTick(
                    Microseconds(stopped - started), socketMicros, pumps)
                : default;
        }

        private ulong SumProofs()
        {
            ulong result = 0;
            for (int index = 0; index < _pairs.Length; index++)
                result += _pairs[index].Server.GetSnapshot().AppliedProofs;
            return result;
        }

        public void Dispose()
        {
            _host.Dispose();
            _world.Dispose();
        }
    }

    private sealed class SharedSocketPair
    {
        internal SharedSocketPair(
            SharedUdpProtocolSession session,
            OfflineDirectChunkRecipient recipient,
            ChunkReplicationRecipientId id,
            ClientWorldSemanticCache cache,
            RemoteChunkWorldStateServer server,
            RemoteChunkWorldStateClient client)
        {
            Session = session;
            Recipient = recipient;
            Id = id;
            Cache = cache;
            Server = server;
            Client = client;
        }

        internal SharedUdpProtocolSession Session { get; }
        internal OfflineDirectChunkRecipient Recipient { get; }
        internal ChunkReplicationRecipientId Id { get; }
        internal ClientWorldSemanticCache Cache { get; }
        internal RemoteChunkWorldStateServer Server { get; }
        internal RemoteChunkWorldStateClient Client { get; }
    }

    private sealed class RemotePair : IDisposable
    {
        private readonly bool _ownsLoopback;

        internal RemotePair(
            ProtocolLoopback loopback,
            OfflineDirectChunkRecipient recipient,
            ChunkReplicationRecipientId id,
            ClientWorldSemanticCache cache,
            ClientSemanticChunkHandle handle,
            RemoteChunkWorldStateServer server,
            RemoteChunkWorldStateClient client,
            bool ownsLoopback = true)
        {
            Loopback = loopback;
            Recipient = recipient;
            Id = id;
            Cache = cache;
            Handle = handle;
            Server = server;
            Client = client;
            _ownsLoopback = ownsLoopback;
        }

        internal ProtocolLoopback Loopback { get; }
        internal OfflineDirectChunkRecipient Recipient { get; }
        internal ChunkReplicationRecipientId Id { get; }
        internal ClientWorldSemanticCache Cache { get; }
        internal ClientSemanticChunkHandle Handle { get; }
        internal RemoteChunkWorldStateServer Server { get; }
        internal RemoteChunkWorldStateClient Client { get; }

        public void Dispose()
        {
            if (_ownsLoopback) Loopback.Dispose();
        }
    }

    private static void Pump(
        RemotePair pair,
        ref double protocolMicroseconds,
        ref double clientMicroseconds,
        ref double proofMicroseconds)
    {
        _ = pair.Server.TryProgress();
        long started = Stopwatch.GetTimestamp();
        pair.Loopback.Pump();
        protocolMicroseconds += Microseconds(Stopwatch.GetTimestamp() - started);
        started = Stopwatch.GetTimestamp();
        while (pair.Loopback.Client.TryPeekReceived(
                   out NetworkDeliveredMessage message, out ReadOnlySpan<byte> payload))
        {
            _ = pair.Client.TryProcess(message, payload);
            Check(pair.Loopback.Client.TryConsumeReceived());
        }
        _ = pair.Client.TryProgress();
        clientMicroseconds += Microseconds(Stopwatch.GetTimestamp() - started);
        started = Stopwatch.GetTimestamp();
        pair.Loopback.Pump();
        protocolMicroseconds += Microseconds(Stopwatch.GetTimestamp() - started);
        started = Stopwatch.GetTimestamp();
        while (pair.Loopback.Server.TryPeekReceived(
                   out NetworkDeliveredMessage message, out ReadOnlySpan<byte> payload))
        {
            _ = pair.Server.TryProcess(message, payload);
            Check(pair.Loopback.Server.TryConsumeReceived());
        }
        proofMicroseconds += Microseconds(Stopwatch.GetTimestamp() - started);
    }

    private static EcsWorld CreateEcs(int entities, out ArchetypeHandle empty)
    {
        var world = new EcsWorld(
            GameSchema.CreateSchema(),
            new EcsWorldOptions(
                new EntityCapacity(entities),
                2,
                4,
                lifecycleRecordCapacity: checked(entities * 2)));
        empty = world.RegisterArchetype(
            ReadOnlySpan<ComponentId>.Empty,
            ChunkLayoutOptions.KiB16,
            Math.Max(1, (entities + 255) / 256));
        world.Seal();
        world.Initialize();
        return world;
    }

    private static BoundWorldPack CreateWorld(ReadOnlySpan<SpatialChunkKey> keys)
    {
        var builder = new WorldPackBuilder(
            GameWorldState.DefaultSpace,
            s_content,
            new WorldPackLimits(64 * 1024 * 1024, keys.Length, 8, 0, 0, 0));
        var materials = new ushort[TerrainChunkFormat.MaterialCellCount];
        Array.Fill(materials, (ushort)1);
        var offsets = new TerrainHeightOffset[TerrainChunkFormat.HeightVertexCount];
        Check(TerrainHeightPlaneBase.TryCreate(0, out TerrainHeightPlaneBase heightBase));
        for (int index = 0; index < keys.Length; index++)
            Check(builder.TryAddBaseTerrain(keys[index], materials, heightBase, offsets, out _));
        Check(builder.TryBuild(out WorldPackArtifact? artifact, out _));
        WorldPack pack = artifact!.Open();
        Check(pack.TryBind(new WorldContentBinding(s_content, [1]),
            out BoundWorldPack? bound, out _));
        return bound!;
    }

    private static SpatialChunkKey[] TownKeys(int count)
    {
        var result = new SpatialChunkKey[count];
        int width = checked((int)Math.Ceiling(Math.Sqrt(count)));
        int offset = width / 2;
        for (int index = 0; index < count; index++)
            result[index] = Key(index / width - offset, index % width - offset);
        Array.Sort(result);
        return result;
    }

    private static ProtocolLoopback CreateLoopback(int slotCapacity = 64)
    {
        Check(ProtocolLoopback.TryCreate(
            NetworkProtocolLimits.Default,
            new DeterministicDatagramLimits(slotCapacity, 1_472),
            default,
            out ProtocolLoopback? loopback,
            out _,
            UltraSimGameProtocol.Manifest));
        return loopback!;
    }

    private static SpatialChunkKey Key(int x, int z) =>
        new(GameWorldState.DefaultSpace, x, z);

    private static int FindSubscription(
        OfflineDirectChunkSubscriptionView subscriptions,
        SpatialChunkKey key)
    {
        int low = 0;
        int high = subscriptions.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            int comparison = subscriptions[middle].Interest.Key.CompareTo(key);
            if (comparison == 0) return middle;
            if (comparison < 0) low = middle + 1;
            else high = middle - 1;
        }
        return -1;
    }

    private static N14Metric Metric(double[] values)
    {
        Array.Sort(values);
        return new N14Metric(
            values.Average(),
            values[Percentile(values.Length, .50)],
            values[Percentile(values.Length, .95)],
            values[Percentile(values.Length, .99)],
            values[^1]);
    }

    private static int Percentile(int count, double fraction) =>
        Math.Clamp((int)Math.Ceiling(count * fraction) - 1, 0, count - 1);

    private static double Microseconds(long ticks) => ticks * 1_000_000d / Stopwatch.Frequency;

    private static void Check(bool condition)
    {
        if (!condition)
            throw new InvalidOperationException("N1.4 transport-adoption guard failed.");
    }

    private readonly record struct ProtocolTick(
        double TotalMicroseconds,
        double SemanticMicroseconds,
        double ServerOfferMicroseconds,
        double ProtocolMicroseconds,
        double ClientMicroseconds,
        double ProofMicroseconds,
        int Pumps);

    private readonly record struct MixedTrafficTick(
        double TotalMicroseconds,
        double ProtocolMicroseconds,
        double ApplicationMicroseconds,
        int Pumps);

    private readonly record struct SharedSocketTick(
        double TotalMicroseconds,
        double SocketPumpMicroseconds,
        int Pumps);
}

internal readonly record struct ProtocolReplayEvidence(
    int Recipients,
    int WarmupTicks,
    int MeasuredTicks,
    ulong AppliedProofs,
    ulong RejectedDeliveries,
    int MaximumPumpRoundsPerTick,
    ulong DatagramSubmissions,
    ulong ServerPlaintextBytes,
    ulong ClientProofPlaintextBytes,
    long ReservedBytes,
    long AllocatedBytes,
    N14Metric TotalMicroseconds,
    N14Metric ServerSemanticMicroseconds,
    N14Metric ServerOfferMicroseconds,
    N14Metric ProtocolPumpMicroseconds,
    N14Metric ClientApplyMicroseconds,
    N14Metric ServerProofMicroseconds);

internal readonly record struct DenseTownEntryEvidence(
    int PopulatedChunks,
    int RecordsPerChunk,
    int PublicRecords,
    int TotalInterestedChunks,
    int SnapshotBusyObservations,
    int TotalPumpRounds,
    int MaximumPumpRoundsPerSnapshot,
    double TotalConvergenceMicroseconds,
    ulong SnapshotsQueued,
    int SnapshotSegmentsQueued,
    ulong SnapshotsApplied,
    ulong DatagramSubmissions,
    long ServerAdapterReservedBytes,
    long ClientAdapterReservedBytes,
    long ClientSemanticCacheReservedBytes,
    string Notes);

internal readonly record struct MixedTrafficEvidence(
    int Recipients,
    int WarmupTicks,
    int MeasuredTicks,
    ulong PublicDeltasApplied,
    ulong OwnerStatesApplied,
    ulong EventsApplied,
    ulong ActionsApplied,
    ulong EventProofsApplied,
    ulong DisclosureViolations,
    int MaximumPumpRoundsPerTick,
    long AllocatedBytes,
    N14Metric TotalMicroseconds,
    N14Metric ProtocolPumpMicroseconds,
    N14Metric ApplicationMicroseconds,
    string Notes);

internal readonly record struct SharedSocketReplayEvidence(
    int Recipients,
    int WarmupTicks,
    int MeasuredTicks,
    ulong PublicDeltasApplied,
    int MaximumPumpRoundsPerTick,
    long KnownServerReservedBytes,
    long KnownClientReservedBytes,
    long ConfiguredListenerSocketBufferBytes,
    long ConfiguredClientSocketBufferBytes,
    ulong ListenerSendCalls,
    ulong ListenerReceiveCalls,
    ulong NativeSendCalls,
    ulong NativeReceiveCalls,
    bool NativeSendBatching,
    bool NativeReceiveBatching,
    long AllocatedBytes,
    N14Metric TotalMicroseconds,
    N14Metric SocketPumpMicroseconds,
    string Notes);

internal readonly record struct TransportAdoptionEvidence(
    ProtocolReplayEvidence ProtocolReplay,
    MixedTrafficEvidence MixedTraffic,
    DenseTownEntryEvidence DenseTownEntry,
    SharedSocketReplayEvidence SharedSocketReplay,
    string Scope);

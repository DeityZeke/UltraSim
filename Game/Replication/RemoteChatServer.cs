using System.Runtime.CompilerServices;
using UltraSim.ECS;
using UltraSim.Game.Admission;
using UltraSim.Game.Chat;
using UltraSim.Game.Composition;
using UltraSim.Game.Protocol;
using UltraSim.Game.World;
using UltraSim.Network;
using UltraSim.Server.Runtime;

namespace UltraSim.Game.Replication;

public readonly record struct RemoteChatServerLimits(
    int MaximumSessions,
    int MaximumPendingDeliveries,
    int MaximumPendingDeliveriesPerSession,
    int MaximumSubmissionsPerRateWindow,
    long RateWindowMilliseconds,
    long ApplicationRetryMilliseconds)
{
    public static RemoteChatServerLimits Default => new(256, 16_384, 64, 8, 2_000, 1_000);

    public void Validate(GameChatLimits chat)
    {
        if (MaximumSessions <= 0 || MaximumSessions > chat.MaximumParticipants ||
            MaximumPendingDeliveries <= 0 || MaximumPendingDeliveriesPerSession <= 0 ||
            MaximumPendingDeliveriesPerSession > MaximumPendingDeliveries ||
            MaximumSubmissionsPerRateWindow <= 0 || RateWindowMilliseconds <= 0 ||
            ApplicationRetryMilliseconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(RemoteChatServerLimits));
    }
}

public readonly record struct RemoteChatDeliveryServiceSnapshot(
    int RegisteredSessions,
    int PendingDeliveries,
    int PendingHighWater,
    ulong AcceptedSubmissions,
    ulong DuplicateSubmissions,
    ulong RejectedSubmissions,
    ulong RateRejections,
    ulong DeliveriesQueued,
    ulong DeliveriesApplied,
    ulong DeliveryRetries,
    ulong MalformedProofs,
    ulong HandOffBackpressure,
    long KnownArrayReservedBytes);

/// <summary>
/// Game-owned bounded handoff from C0.0 occurrences to independent per-session
/// application debt. It owns no socket or transport acknowledgement semantics.
/// </summary>
public sealed class RemoteChatDeliveryService : IDisposable
{
    private readonly ServerGameFacet _runtime;
    private readonly GameChatService _chat;
    private readonly RemoteChatServerLimits _limits;
    private readonly RemoteChatServerSession?[] _sessions;
    private readonly DeliveryRow[] _deliveries;
    private readonly ParticipantId[] _recipientScratch;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private int _sessionCount;
    private int _deliveryCount;
    private int _deliveryHighWater;
    private ulong _nextDeliveryId = 1;
    private ulong _acceptedSubmissions;
    private ulong _duplicateSubmissions;
    private ulong _rejectedSubmissions;
    private ulong _rateRejections;
    private ulong _deliveriesQueued;
    private ulong _deliveriesApplied;
    private ulong _deliveryRetries;
    private ulong _malformedProofs;
    private ulong _handoffBackpressure;
    private bool _disposed;

    public RemoteChatDeliveryService(
        ServerGameFacet runtime,
        GameChatService chat,
        RemoteChatServerLimits limits = default)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _chat = chat ?? throw new ArgumentNullException(nameof(chat));
        if (limits == default) limits = RemoteChatServerLimits.Default with
        {
            MaximumSessions = chat.Limits.MaximumParticipants,
            MaximumPendingDeliveries = checked(
                chat.Limits.MaximumParticipants *
                RemoteChatServerLimits.Default.MaximumPendingDeliveriesPerSession),
        };
        limits.Validate(chat.Limits);
        _limits = limits;
        _sessions = new RemoteChatServerSession[limits.MaximumSessions];
        _deliveries = new DeliveryRow[limits.MaximumPendingDeliveries];
        _recipientScratch = new ParticipantId[chat.Limits.MaximumRecipientsPerSpeech];
        _runtime.GenerationPublished += HandleGenerationPublished;
    }

    public RemoteChatServerSession Register(
        NetworkProtocolPeer peer,
        ParticipantId participant,
        ulong applicationSessionEpoch,
        Func<long>? monotonicMilliseconds = null)
        => RegisterCore(peer, participant, applicationSessionEpoch, null, monotonicMilliseconds);

    /// <summary>
    /// Registers a hosted application session and adds its concrete Admission
    /// owner to the shared bounded Chat census for exactly this session lifetime.
    /// </summary>
    public RemoteChatServerSession Register(
        NetworkProtocolPeer peer,
        AdmissionService admission,
        ParticipantId participant,
        ulong applicationSessionEpoch,
        Func<long>? monotonicMilliseconds = null)
    {
        EnsureOperational();
        ArgumentNullException.ThrowIfNull(admission);
        GameChatAdmissionRegistration registration = _chat.AttachAdmission(admission);
        try
        {
            return RegisterCore(
                peer, participant, applicationSessionEpoch, registration, monotonicMilliseconds);
        }
        catch
        {
            registration.Dispose();
            throw;
        }
    }

    private RemoteChatServerSession RegisterCore(
        NetworkProtocolPeer peer,
        ParticipantId participant,
        ulong applicationSessionEpoch,
        GameChatAdmissionRegistration? admissionRegistration,
        Func<long>? monotonicMilliseconds)
    {
        EnsureOperational();
        ArgumentNullException.ThrowIfNull(peer);
        if (!participant.IsValid || applicationSessionEpoch == 0 ||
            !_chat.TryGetEnteredParticipant(participant, out _))
            throw new ArgumentException("Chat registration requires a current entered participant.");
        if (FindSession(participant) >= 0)
            throw new InvalidOperationException("The participant already owns a Chat delivery session.");
        int slot = FindFreeSession();
        if (slot < 0) throw new InvalidOperationException("The bounded Chat session table is full.");
        var session = new RemoteChatServerSession(
            this, peer, participant, applicationSessionEpoch,
            monotonicMilliseconds ?? (() => Environment.TickCount64),
            admissionRegistration);
        _sessions[slot] = session;
        _sessionCount++;
        return session;
    }

    internal GameChatService Chat => _chat;
    internal RemoteChatServerLimits Limits => _limits;

    public bool TryHandoffPendingSpeech()
    {
        EnsureOperational();
        if (!_chat.TryPeekNextSpeech(out GameSpeechFact fact, out int recipientCount))
            return true;
        if (recipientCount > _recipientScratch.Length ||
            !_chat.TryCopyRecipients(fact.Id, _recipientScratch, out int copied) || copied != recipientCount)
            throw new InvalidOperationException("C0.0 changed one retained audience during remote preflight.");
        if (_deliveryCount > _deliveries.Length - recipientCount ||
            _nextDeliveryId == 0 || _nextDeliveryId > ulong.MaxValue - (ulong)recipientCount)
            return HandOffBlocked();
        for (int index = 0; index < recipientCount; index++)
        {
            int session = FindSession(_recipientScratch[index]);
            if (session < 0 || _sessions[session]!.IsNonviable ||
                CountDeliveries(_recipientScratch[index]) >= _limits.MaximumPendingDeliveriesPerSession ||
                _sessions[session]!.NextRecipientSequence == 0)
                return HandOffBlocked();
        }
        if (!TryResolvePresentation(in fact, out string displayLabel, out WorldPublicEntityId anchor))
            return HandOffBlocked();
        if (!_chat.TryConsumeNextSpeech(
                _recipientScratch, out GameSpeechFact consumed, out int consumedRecipients) ||
            consumed != fact || consumedRecipients != recipientCount)
            throw new InvalidOperationException("C0.0 occurrence handoff was not transactional.");

        for (int index = 0; index < recipientCount; index++)
        {
            RemoteChatServerSession session = _sessions[FindSession(_recipientScratch[index])]!;
            AddDelivery(new DeliveryRow(
                session,
                _nextDeliveryId++,
                session.TakeRecipientSequence(),
                fact.Id.Value,
                fact.GenerationId,
                fact.SimulationTick,
                RemoteChatOccurrenceKind.SpatialSpeech,
                (byte)fact.Mode,
                anchor,
                displayLabel,
                fact.Text));
        }
        return true;
    }

    internal bool QueuePrivate(RemoteChatServerSession session, string text, bool error)
    {
        EnsureOperational();
        if (session.IsNonviable || string.IsNullOrWhiteSpace(text) ||
            CountDeliveries(session.Participant) >= _limits.MaximumPendingDeliveriesPerSession ||
            _deliveryCount == _deliveries.Length || _nextDeliveryId == 0 ||
            session.NextRecipientSequence == 0 || !TryReadGeneration(out ulong generationId))
            return false;
        AddDelivery(new DeliveryRow(
            session,
            _nextDeliveryId++,
            session.TakeRecipientSequence(),
            0,
            generationId,
            0,
            error ? RemoteChatOccurrenceKind.PrivateError : RemoteChatOccurrenceKind.PrivateSystem,
            0,
            default,
            "System",
            text));
        return true;
    }

    internal bool TrySchedule(RemoteChatServerSession session, long now)
    {
        EnsureOperational();
        _ = TryHandoffPendingSpeech();
        int slot = FindOldestDelivery(session);
        if (slot < 0) return true;
        ref DeliveryRow row = ref _deliveries[slot];
        if (row.AwaitingApplication && now - row.LastQueuedMilliseconds < _limits.ApplicationRetryMilliseconds)
            return true;
        var header = new RemoteChatOccurrenceHeader
        {
            ApplicationSessionEpoch = session.ApplicationSessionEpoch,
            ServerGenerationId = row.ServerGenerationId,
            DeliveryId = row.DeliveryId,
            RecipientSequence = row.RecipientSequence,
            SpeechFactId = row.SpeechFactId,
            SimulationTick = row.SimulationTick,
            PublicEntityIndex = row.PublicAnchor.Index,
            PublicEntityGeneration = row.PublicAnchor.Generation,
            Kind = row.Kind,
            SpeechMode = row.SpeechMode,
        };
        if (!UltraSimGameProtocol.TryWriteChatOccurrence(
                session.MessageScratch, in header, row.DisplayLabel, row.Text, out int written))
        {
            session.MarkNonviable();
            return false;
        }
        if (session.Peer.TryQueue(RemoteChatOccurrenceHeaderCodec.Descriptor.Id,
                session.MessageScratch.AsSpan(0, written)) != NetworkQueueStatus.Accepted)
            return false;
        if (row.AwaitingApplication) SaturatingIncrement(ref _deliveryRetries);
        row.AwaitingApplication = true;
        row.LastQueuedMilliseconds = now;
        SaturatingIncrement(ref _deliveriesQueued);
        return true;
    }

    internal bool ProcessProof(RemoteChatServerSession session, in RemoteChatAppliedMessage proof)
    {
        EnsureOperational();
        int slot = FindDelivery(session, proof.DeliveryId);
        if (slot < 0 || proof.ApplicationSessionEpoch != session.ApplicationSessionEpoch)
            return MalformedProof();
        ref DeliveryRow row = ref _deliveries[slot];
        if (proof.ServerGenerationId != row.ServerGenerationId ||
            proof.RecipientSequence != row.RecipientSequence ||
            proof.Status is < RemoteChatApplyStatus.Applied or > RemoteChatApplyStatus.CapacityExceeded)
            return MalformedProof();
        if (proof.Status == RemoteChatApplyStatus.Applied)
        {
            row = default;
            _deliveryCount--;
            SaturatingIncrement(ref _deliveriesApplied);
        }
        else
        {
            row.AwaitingApplication = false;
            row.LastQueuedMilliseconds = 0;
            if (proof.Status == RemoteChatApplyStatus.CapacityExceeded)
                session.MarkNonviable();
        }
        return true;
    }

    internal void RecordAccepted() => SaturatingIncrement(ref _acceptedSubmissions);
    internal void RecordDuplicate() => SaturatingIncrement(ref _duplicateSubmissions);
    internal void RecordRejected() => SaturatingIncrement(ref _rejectedSubmissions);
    internal void RecordRateRejected() => SaturatingIncrement(ref _rateRejections);
    internal void RecordMalformedProof() => SaturatingIncrement(ref _malformedProofs);

    internal void Unregister(RemoteChatServerSession session)
    {
        EnsureOwnerThread();
        for (int index = 0; index < _deliveries.Length; index++)
        {
            if (!ReferenceEquals(_deliveries[index].Session, session)) continue;
            _deliveries[index] = default;
            _deliveryCount--;
        }
        for (int index = 0; index < _sessions.Length; index++)
        {
            if (!ReferenceEquals(_sessions[index], session)) continue;
            _sessions[index] = null;
            _sessionCount--;
            session.ReleaseAdmissionRegistration();
            return;
        }
    }

    public RemoteChatDeliveryServiceSnapshot GetSnapshot()
    {
        EnsureOperational();
        long bytes = checked(
            (long)_sessions.Length * IntPtr.Size +
            (long)_deliveries.Length * Unsafe.SizeOf<DeliveryRow>() +
            (long)_recipientScratch.Length * Unsafe.SizeOf<ParticipantId>());
        return new RemoteChatDeliveryServiceSnapshot(
            _sessionCount, _deliveryCount, _deliveryHighWater,
            _acceptedSubmissions, _duplicateSubmissions, _rejectedSubmissions, _rateRejections,
            _deliveriesQueued, _deliveriesApplied, _deliveryRetries, _malformedProofs,
            _handoffBackpressure, bytes);
    }

    public void Dispose()
    {
        EnsureOwnerThread();
        if (_disposed) return;
        _runtime.GenerationPublished -= HandleGenerationPublished;
        _disposed = true;
        for (int index = 0; index < _sessions.Length; index++)
            _sessions[index]?.DetachFromService();
        Array.Clear(_sessions);
        Array.Clear(_deliveries);
        _sessionCount = 0;
        _deliveryCount = 0;
    }

    private bool TryResolvePresentation(
        in GameSpeechFact fact,
        out string displayLabel,
        out WorldPublicEntityId anchor)
    {
        GameSpeechFact staged = fact;
        PresentationResolution resolution = default;
        bool ran = _runtime.TryUseActiveGeneration<IGameGenerationStateProvider, PresentationResolution>(
            (world, generationId, game) =>
            {
                if (generationId != staged.GenerationId || !world.IsAlive(staged.SpeakerEntity))
                    return default;
                string label = $"Player {staged.Speaker.Value}";
                if (_chat.TryGetEnteredParticipant(staged.Speaker, out AdmissionResult entered) &&
                    entered.GenerationId == generationId && entered.CharacterId.IsValid &&
                    game.Accounting.TryGetCharacter(entered.CharacterId, out var character))
                    label = character.DisplayName;
                _ = game.WorldState.Publications.TryGetPublicEntityId(staged.SpeakerEntity, out WorldPublicEntityId id);
                return new PresentationResolution(label, id, true);
            }, out resolution);
        displayLabel = resolution.Label ?? string.Empty;
        anchor = resolution.Anchor;
        return ran && resolution.Valid;
    }

    private bool TryReadGeneration(out ulong generationId) =>
        _runtime.TryUseActiveGeneration(static (_, generation) => generation, out generationId) &&
        generationId != 0;

    private void HandleGenerationPublished(ulong generationId)
    {
        if (_disposed) return;
        Array.Clear(_deliveries);
        _deliveryCount = 0;
        for (int index = 0; index < _sessions.Length; index++)
            _sessions[index]?.ResetDeliveryLineage();
    }

    private void AddDelivery(in DeliveryRow row)
    {
        for (int index = 0; index < _deliveries.Length; index++)
        {
            if (_deliveries[index].DeliveryId != 0) continue;
            _deliveries[index] = row;
            _deliveryCount++;
            _deliveryHighWater = Math.Max(_deliveryHighWater, _deliveryCount);
            return;
        }
        throw new InvalidOperationException("A preflighted Chat delivery slot disappeared.");
    }

    private int FindSession(ParticipantId participant)
    {
        for (int index = 0; index < _sessions.Length; index++)
            if (_sessions[index]?.Participant == participant) return index;
        return -1;
    }

    private int FindFreeSession()
    {
        for (int index = 0; index < _sessions.Length; index++)
            if (_sessions[index] is null) return index;
        return -1;
    }

    private int CountDeliveries(ParticipantId participant)
    {
        int count = 0;
        for (int index = 0; index < _deliveries.Length; index++)
            if (_deliveries[index].Session?.Participant == participant) count++;
        return count;
    }

    private int FindOldestDelivery(RemoteChatServerSession session)
    {
        int best = -1;
        ulong sequence = ulong.MaxValue;
        for (int index = 0; index < _deliveries.Length; index++)
        {
            ref readonly DeliveryRow row = ref _deliveries[index];
            if (!ReferenceEquals(row.Session, session) || row.RecipientSequence >= sequence) continue;
            best = index;
            sequence = row.RecipientSequence;
        }
        return best;
    }

    private int FindDelivery(RemoteChatServerSession session, ulong deliveryId)
    {
        for (int index = 0; index < _deliveries.Length; index++)
            if (_deliveries[index].DeliveryId == deliveryId &&
                ReferenceEquals(_deliveries[index].Session, session)) return index;
        return -1;
    }

    private bool HandOffBlocked()
    {
        SaturatingIncrement(ref _handoffBackpressure);
        return false;
    }

    private bool MalformedProof()
    {
        SaturatingIncrement(ref _malformedProofs);
        return true;
    }

    private void EnsureOperational()
    {
        EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Remote Chat delivery is owner-thread-bound.");
    }

    private static void SaturatingIncrement(ref ulong value)
    {
        if (value != ulong.MaxValue) value++;
    }

    private readonly record struct PresentationResolution(
        string? Label, WorldPublicEntityId Anchor, bool Valid);

    private struct DeliveryRow
    {
        internal DeliveryRow(
            RemoteChatServerSession session,
            ulong deliveryId,
            ulong recipientSequence,
            ulong speechFactId,
            ulong serverGenerationId,
            ulong simulationTick,
            RemoteChatOccurrenceKind kind,
            byte speechMode,
            WorldPublicEntityId publicAnchor,
            string displayLabel,
            string text)
        {
            Session = session;
            DeliveryId = deliveryId;
            RecipientSequence = recipientSequence;
            SpeechFactId = speechFactId;
            ServerGenerationId = serverGenerationId;
            SimulationTick = simulationTick;
            Kind = kind;
            SpeechMode = speechMode;
            PublicAnchor = publicAnchor;
            DisplayLabel = displayLabel;
            Text = text;
        }
        internal RemoteChatServerSession? Session;
        internal ulong DeliveryId;
        internal ulong RecipientSequence;
        internal ulong SpeechFactId;
        internal ulong ServerGenerationId;
        internal ulong SimulationTick;
        internal RemoteChatOccurrenceKind Kind;
        internal byte SpeechMode;
        internal WorldPublicEntityId PublicAnchor;
        internal string DisplayLabel;
        internal string Text;
        internal long LastQueuedMilliseconds;
        internal bool AwaitingApplication;
    }
}

/// <summary>One authenticated application's Chat ingress and proof owner.</summary>
public sealed class RemoteChatServerSession : IRemoteGameProtocolHandler, IRemoteGameProtocolProgressor, IDisposable
{
    private readonly RemoteChatDeliveryService _service;
    private readonly Func<long> _monotonicMilliseconds;
    private GameChatAdmissionRegistration? _admissionRegistration;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private ulong _nextSubmissionSequence = 1;
    private ulong _nextRecipientSequence = 1;
    private long _rateWindowStart;
    private int _rateWindowCount;
    private bool _hasRateWindow;
    private long _lastMonotonicMilliseconds;
    private bool _hasMonotonicSample;
    private bool _nonviable;
    private bool _disposed;
    private bool _attached = true;

    internal RemoteChatServerSession(
        RemoteChatDeliveryService service,
        NetworkProtocolPeer peer,
        ParticipantId participant,
        ulong applicationSessionEpoch,
        Func<long> monotonicMilliseconds,
        GameChatAdmissionRegistration? admissionRegistration)
    {
        _service = service;
        Peer = peer;
        Participant = participant;
        ApplicationSessionEpoch = applicationSessionEpoch;
        _monotonicMilliseconds = monotonicMilliseconds;
        _admissionRegistration = admissionRegistration;
        MessageScratch = new byte[UltraSimGameProtocol.MaximumChatMessageBytes];
    }

    internal NetworkProtocolPeer Peer { get; }
    internal ParticipantId Participant { get; }
    internal ulong ApplicationSessionEpoch { get; }
    internal byte[] MessageScratch { get; }
    internal ulong NextRecipientSequence => _nextRecipientSequence;
    internal bool IsNonviable => _nonviable;

    public bool TryProcess(in NetworkDeliveredMessage message, ReadOnlySpan<byte> payload)
    {
        EnsureOperational();
        if (message.MessageId == RemoteChatSubmitHeaderCodec.Descriptor.Id)
        {
            ProcessSubmission(payload);
            return true;
        }
        if (message.MessageId == RemoteChatAppliedMessageCodec.Descriptor.Id)
        {
            if (!RemoteChatAppliedMessageCodec.TryReadExact(payload, out RemoteChatAppliedMessage proof))
                _service.RecordMalformedProof();
            else
                _ = _service.ProcessProof(this, in proof);
            return true;
        }
        return false;
    }

    public bool TryProgress()
    {
        EnsureOperational();
        return !_nonviable && _service.TrySchedule(this, ReadNow());
    }

    bool IRemoteGameProtocolProgressor.TryProgress() => TryProgress();

    public void Dispose()
    {
        EnsureOwnerThread();
        if (_disposed) return;
        _disposed = true;
        if (_attached) _service.Unregister(this);
        _attached = false;
        Array.Clear(MessageScratch);
    }

    internal ulong TakeRecipientSequence()
    {
        ulong value = _nextRecipientSequence;
        _nextRecipientSequence = value == ulong.MaxValue ? 0 : value + 1;
        return value;
    }

    internal void ResetDeliveryLineage()
    {
        _nextRecipientSequence = 1;
        _nonviable = false;
    }

    internal void MarkNonviable() => _nonviable = true;

    internal void DetachFromService()
    {
        _attached = false;
        _nonviable = true;
        ReleaseAdmissionRegistration();
    }

    internal void ReleaseAdmissionRegistration()
    {
        _admissionRegistration?.Dispose();
        _admissionRegistration = null;
    }

    private void ProcessSubmission(ReadOnlySpan<byte> payload)
    {
        if (!UltraSimGameProtocol.TryReadChatSubmission(
                payload, out RemoteChatSubmitHeader header, out string text) ||
            header.ApplicationSessionEpoch != ApplicationSessionEpoch)
        {
            _service.RecordRejected();
            return;
        }
        if (header.SubmissionSequence < _nextSubmissionSequence)
        {
            _service.RecordDuplicate();
            return;
        }
        if (header.SubmissionSequence != _nextSubmissionSequence || _nextSubmissionSequence == 0)
        {
            _service.RecordRejected();
            return;
        }
        _nextSubmissionSequence = _nextSubmissionSequence == ulong.MaxValue
            ? 0
            : _nextSubmissionSequence + 1;
        if (!ConsumeRate(ReadNow()))
        {
            _service.RecordRateRejected();
            if (!_service.QueuePrivate(this, "Chat rate limit exceeded.", error: true))
                MarkNonviable();
            return;
        }

        GameChatSubmissionResult result = _service.Chat.Submit(
            Participant, text, (GameSpeechMode)header.SpeechMode);
        if (result.AcceptedSpeech)
        {
            _service.RecordAccepted();
            _ = _service.TryHandoffPendingSpeech();
            return;
        }
        if (result.CommandSucceeded)
            _service.RecordAccepted();
        else
            _service.RecordRejected();
        if (!_service.QueuePrivate(
                this,
                result.PrivateReply ?? "Chat submission was rejected.",
                error: !result.CommandSucceeded))
            MarkNonviable();
    }

    private bool ConsumeRate(long now)
    {
        if (!_hasRateWindow || now - _rateWindowStart >= _service.Limits.RateWindowMilliseconds)
        {
            _hasRateWindow = true;
            _rateWindowStart = now;
            _rateWindowCount = 0;
        }
        if (_rateWindowCount == _service.Limits.MaximumSubmissionsPerRateWindow)
            return false;
        _rateWindowCount++;
        return true;
    }

    private long ReadNow()
    {
        long now = _monotonicMilliseconds();
        if (now < 0 || _hasMonotonicSample && now < _lastMonotonicMilliseconds)
            throw new InvalidOperationException("The Chat monotonic clock moved backwards.");
        _hasMonotonicSample = true;
        _lastMonotonicMilliseconds = now;
        return now;
    }

    private void EnsureOperational()
    {
        EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Remote Chat session is owner-thread-bound.");
    }
}

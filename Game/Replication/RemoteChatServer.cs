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
    int FreeDeliveryRows,
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
    ulong DeliveryRowsExamined,
    ulong HandoffPreflights,
    ulong TerminalAbandonments,
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
    private readonly HandoffRecipient[] _handoffRecipients;
    private readonly TerminalRecipient[] _terminalRecipients;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private int _freeDeliveryHead;
    private int _freeDeliveryCount;
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
    private ulong _deliveryRowsExamined;
    private ulong _handoffPreflights;
    private ulong _terminalAbandonments;
    private GameSpeechFact _handoffFact;
    private int _handoffRecipientCount;
    private ulong _lastObservedSpeechSequence;
    private bool _handoffCandidateValid;
    private bool _handoffDirty = true;
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
        _handoffRecipients = new HandoffRecipient[chat.Limits.MaximumRecipientsPerSpeech];
        _terminalRecipients = new TerminalRecipient[limits.MaximumSessions];
        for (int index = 0; index < _deliveries.Length; index++)
            _deliveries[index].Next = index + 1 < _deliveries.Length ? index + 1 : -1;
        _freeDeliveryHead = 0;
        _freeDeliveryCount = _deliveries.Length;
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
        RebindPendingRecipient(session);
        _handoffDirty = true;
        return session;
    }

    internal GameChatService Chat => _chat;
    internal RemoteChatServerLimits Limits => _limits;

    public bool TryHandoffPendingSpeech()
    {
        EnsureOperational();
        _handoffDirty = true;
        return ProgressHandoff();
    }

    internal bool QueuePrivate(RemoteChatServerSession session, string text, bool error)
    {
        EnsureOperational();
        if (session.IsNonviable || string.IsNullOrWhiteSpace(text) ||
            session.PendingDeliveryCount >= _limits.MaximumPendingDeliveriesPerSession ||
            _freeDeliveryCount == 0 || _nextDeliveryId == 0 ||
            session.NextRecipientSequence == 0 || !TryReadGeneration(out ulong generationId))
            return false;
        AddDelivery(session, new DeliveryRow(
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
        ObserveNewSpeech();
        if (_handoffDirty) _ = ProgressHandoff();
        int slot = session.DeliveryHead;
        if (slot < 0) return true;
        SaturatingIncrement(ref _deliveryRowsExamined);
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
        int slot = session.DeliveryHead;
        if (slot < 0 || proof.ApplicationSessionEpoch != session.ApplicationSessionEpoch)
            return MalformedProof();
        ref DeliveryRow row = ref _deliveries[slot];
        if (proof.DeliveryId != row.DeliveryId ||
            proof.ServerGenerationId != row.ServerGenerationId ||
            proof.RecipientSequence != row.RecipientSequence ||
            proof.Status is < RemoteChatApplyStatus.Applied or > RemoteChatApplyStatus.CapacityExceeded)
            return MalformedProof();
        if (proof.Status == RemoteChatApplyStatus.Applied)
        {
            ReleaseHead(session, abandoned: false);
            SaturatingIncrement(ref _deliveriesApplied);
            _handoffDirty = true;
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
        RecordTerminalRecipient(session.Participant);
        MarkPendingRecipientTerminal(session);
        while (session.DeliveryHead >= 0)
            ReleaseHead(session, abandoned: true);
        for (int index = 0; index < _sessions.Length; index++)
        {
            if (!ReferenceEquals(_sessions[index], session)) continue;
            _sessions[index] = null;
            _sessionCount--;
            session.ReleaseAdmissionRegistration();
            _handoffDirty = true;
            return;
        }
    }

    public RemoteChatDeliveryServiceSnapshot GetSnapshot()
    {
        EnsureOperational();
        long bytes = checked(
            (long)_sessions.Length * IntPtr.Size +
            (long)_deliveries.Length * Unsafe.SizeOf<DeliveryRow>() +
            (long)_recipientScratch.Length * Unsafe.SizeOf<ParticipantId>() +
            (long)_handoffRecipients.Length * Unsafe.SizeOf<HandoffRecipient>() +
            (long)_terminalRecipients.Length * Unsafe.SizeOf<TerminalRecipient>());
        return new RemoteChatDeliveryServiceSnapshot(
            _sessionCount, _deliveryCount, _freeDeliveryCount, _deliveryHighWater,
            _acceptedSubmissions, _duplicateSubmissions, _rejectedSubmissions, _rateRejections,
            _deliveriesQueued, _deliveriesApplied, _deliveryRetries, _malformedProofs,
            _handoffBackpressure, _deliveryRowsExamined, _handoffPreflights,
            _terminalAbandonments, bytes);
    }

    public void Dispose()
    {
        EnsureOwnerThread();
        if (_disposed) return;
        _disposed = true;
        for (int index = 0; index < _sessions.Length; index++)
            _sessions[index]?.DetachFromService();
        Array.Clear(_sessions);
        Array.Clear(_deliveries);
        _sessionCount = 0;
        _deliveryCount = 0;
        _freeDeliveryCount = 0;
    }

    private bool TryReadGeneration(out ulong generationId) =>
        _runtime.TryUseActiveGeneration(static (_, generation) => generation, out generationId) &&
        generationId != 0;

    private void ObserveNewSpeech()
    {
        ulong latest = _chat.LatestAcceptedSpeechSequence;
        if (latest == _lastObservedSpeechSequence) return;
        _lastObservedSpeechSequence = latest;
        _handoffDirty = true;
    }

    private bool ProgressHandoff()
    {
        ObserveNewSpeech();
        while (_handoffDirty)
        {
            _handoffDirty = false;
            if (!EnsureHandoffCandidate()) return true;

            int liveRecipients = 0;
            for (int index = 0; index < _handoffRecipientCount; index++)
            {
                ref HandoffRecipient recipient = ref _handoffRecipients[index];
                if (recipient.Terminal) continue;
                if (recipient.Session is null)
                    recipient.Session = TryFindSession(recipient.Participant);
                if (recipient.Session is null || recipient.Session.IsNonviable ||
                    recipient.Session.PendingDeliveryCount >=
                        _limits.MaximumPendingDeliveriesPerSession ||
                    recipient.Session.NextRecipientSequence == 0)
                    return HandOffBlocked();
                liveRecipients++;
            }
            if (_freeDeliveryCount < liveRecipients || _nextDeliveryId == 0 ||
                _nextDeliveryId > ulong.MaxValue - (ulong)liveRecipients)
                return HandOffBlocked();

            if (!_chat.TryConsumeNextSpeech(
                    _recipientScratch, out GameSpeechFact consumed, out int consumedRecipients) ||
                consumed != _handoffFact || consumedRecipients != _handoffRecipientCount)
                throw new InvalidOperationException("C0.0 occurrence handoff was not transactional.");

            for (int index = 0; index < _handoffRecipientCount; index++)
            {
                ref HandoffRecipient recipient = ref _handoffRecipients[index];
                if (recipient.Terminal) continue;
                RemoteChatServerSession session = recipient.Session ??
                    throw new InvalidOperationException("A preflighted Chat recipient lost its session.");
                AddDelivery(session, new DeliveryRow(
                    _nextDeliveryId++,
                    session.TakeRecipientSequence(),
                    consumed.Id.Value,
                    consumed.GenerationId,
                    consumed.SimulationTick,
                    RemoteChatOccurrenceKind.SpatialSpeech,
                    (byte)consumed.Mode,
                    consumed.PublicAnchor,
                    consumed.DisplayLabel,
                    consumed.Text));
            }
            ClearTerminalRecipientsThrough(consumed.Id.Value);
            ClearHandoffCandidate();
            _handoffDirty = true;
        }
        return false;
    }

    private bool EnsureHandoffCandidate()
    {
        if (_handoffCandidateValid) return true;
        if (!_chat.TryPeekNextSpeech(out GameSpeechFact fact, out int recipientCount))
            return false;
        if (recipientCount > _handoffRecipients.Length ||
            !_chat.TryCopyRecipients(fact.Id, _recipientScratch, out int copied) ||
            copied != recipientCount)
            throw new InvalidOperationException("C0.0 changed one retained audience during remote preflight.");
        SaturatingIncrement(ref _handoffPreflights);
        _handoffFact = fact;
        _handoffRecipientCount = recipientCount;
        _handoffCandidateValid = true;
        for (int index = 0; index < recipientCount; index++)
        {
            ParticipantId participant = _recipientScratch[index];
            bool terminal = IsTerminalFor(participant, fact.Id.Value);
            _handoffRecipients[index] = new HandoffRecipient(
                participant,
                terminal ? null : TryFindSession(participant),
                terminal);
            if (terminal) SaturatingIncrement(ref _terminalAbandonments);
        }
        return true;
    }

    private void ClearHandoffCandidate()
    {
        _handoffRecipients.AsSpan(0, _handoffRecipientCount).Clear();
        _handoffFact = default;
        _handoffRecipientCount = 0;
        _handoffCandidateValid = false;
    }

    private void AddDelivery(RemoteChatServerSession session, in DeliveryRow row)
    {
        if (_freeDeliveryHead < 0)
            throw new InvalidOperationException("A preflighted Chat delivery slot disappeared.");
        int slot = _freeDeliveryHead;
        _freeDeliveryHead = _deliveries[slot].Next;
        _freeDeliveryCount--;
        _deliveries[slot] = row;
        _deliveries[slot].Next = -1;
        if (session.DeliveryTail < 0)
            session.DeliveryHead = slot;
        else
            _deliveries[session.DeliveryTail].Next = slot;
        session.DeliveryTail = slot;
        session.PendingDeliveryCount++;
        _deliveryCount++;
        _deliveryHighWater = Math.Max(_deliveryHighWater, _deliveryCount);
    }

    private void ReleaseHead(RemoteChatServerSession session, bool abandoned)
    {
        int slot = session.DeliveryHead;
        if (slot < 0)
            throw new InvalidOperationException("Chat attempted to release an empty session FIFO.");
        int next = _deliveries[slot].Next;
        session.DeliveryHead = next;
        if (next < 0) session.DeliveryTail = -1;
        session.PendingDeliveryCount--;
        _deliveries[slot] = default;
        _deliveries[slot].Next = _freeDeliveryHead;
        _freeDeliveryHead = slot;
        _freeDeliveryCount++;
        _deliveryCount--;
        if (abandoned) SaturatingIncrement(ref _terminalAbandonments);
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

    private RemoteChatServerSession? TryFindSession(ParticipantId participant)
    {
        int session = FindSession(participant);
        return session < 0 ? null : _sessions[session];
    }

    private void RebindPendingRecipient(RemoteChatServerSession session)
    {
        if (!_handoffCandidateValid) return;
        for (int index = 0; index < _handoffRecipientCount; index++)
        {
            ref HandoffRecipient recipient = ref _handoffRecipients[index];
            if (recipient.Participant == session.Participant && !recipient.Terminal &&
                recipient.Session is null)
                recipient.Session = session;
        }
    }

    private void MarkPendingRecipientTerminal(RemoteChatServerSession session)
    {
        if (!_handoffCandidateValid) return;
        for (int index = 0; index < _handoffRecipientCount; index++)
        {
            ref HandoffRecipient recipient = ref _handoffRecipients[index];
            if (!ReferenceEquals(recipient.Session, session) || recipient.Terminal) continue;
            recipient.Session = null;
            recipient.Terminal = true;
            SaturatingIncrement(ref _terminalAbandonments);
        }
    }

    private void RecordTerminalRecipient(ParticipantId participant)
    {
        if (!_chat.TryPeekNextSpeech(out _, out _))
            return;
        ulong through = _chat.LatestAcceptedSpeechSequence;
        for (int index = 0; index < _terminalRecipients.Length; index++)
        {
            ref TerminalRecipient entry = ref _terminalRecipients[index];
            if (entry.Participant == participant)
            {
                entry.ThroughSpeechFactId = Math.Max(entry.ThroughSpeechFactId, through);
                return;
            }
            if (entry.Participant.IsValid) continue;
            entry = new TerminalRecipient(participant, through);
            return;
        }
        throw new InvalidOperationException("The bounded Chat terminal-recipient table is full.");
    }

    private bool IsTerminalFor(ParticipantId participant, ulong speechFactId)
    {
        for (int index = 0; index < _terminalRecipients.Length; index++)
        {
            ref readonly TerminalRecipient entry = ref _terminalRecipients[index];
            if (entry.Participant == participant)
                return speechFactId <= entry.ThroughSpeechFactId;
        }
        return false;
    }

    private void ClearTerminalRecipientsThrough(ulong speechFactId)
    {
        for (int index = 0; index < _terminalRecipients.Length; index++)
            if (_terminalRecipients[index].Participant.IsValid &&
                _terminalRecipients[index].ThroughSpeechFactId <= speechFactId)
                _terminalRecipients[index] = default;
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

    private struct HandoffRecipient
    {
        internal HandoffRecipient(
            ParticipantId participant,
            RemoteChatServerSession? session,
            bool terminal) =>
            (Participant, Session, Terminal) = (participant, session, terminal);
        internal ParticipantId Participant;
        internal RemoteChatServerSession? Session;
        internal bool Terminal;
    }

    private struct TerminalRecipient
    {
        internal TerminalRecipient(ParticipantId participant, ulong throughSpeechFactId) =>
            (Participant, ThroughSpeechFactId) = (participant, throughSpeechFactId);
        internal ParticipantId Participant;
        internal ulong ThroughSpeechFactId;
    }

    private struct DeliveryRow
    {
        internal DeliveryRow(
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
            Next = -1;
        }
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
        internal int Next;
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
    internal int DeliveryHead { get; set; } = -1;
    internal int DeliveryTail { get; set; } = -1;
    internal int PendingDeliveryCount { get; set; }

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

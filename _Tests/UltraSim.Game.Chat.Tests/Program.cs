using System.Reflection;
using UltraSim.Client.Core;
using UltraSim.Client.Network;
using UltraSim.ECS;
using UltraSim.Game.Accounting;
using UltraSim.Game.Admission;
using UltraSim.Game.Chat;
using UltraSim.Game.Composition;
using UltraSim.Game.Protocol;
using UltraSim.Game.Replication;
using UltraSim.Game.World;
using UltraSim.Network;
using UltraSim.Server.Runtime;

var tests = new (string Name, Action Body)[]
{
    ("DefaultsAndAuthoritySurfaceAreExplicit", DefaultsAndAuthoritySurfaceAreExplicit),
    ("DefaultAndMalformedSuccessResultsFailClosed", DefaultAndMalformedSuccessResultsFailClosed),
    ("PlainTextBecomesSayAndOccurrencesRemainOrdered", PlainTextBecomesSayAndOccurrencesRemainOrdered),
    ("HelpAndWhereArePrivateCommands", HelpAndWhereArePrivateCommands),
    ("UnknownAndMalformedCommandsNeverBecomeSpeech", UnknownAndMalformedCommandsNeverBecomeSpeech),
    ("WhisperSayAndYellProduceNestedExactAudiences", WhisperSayAndYellProduceNestedExactAudiences),
    ("NegativeCornerAndCrossChunkRoutingAreExact", NegativeCornerAndCrossChunkRoutingAreExact),
    ("CurrentAuthoritativeMovementChangesLaterAudience", CurrentAuthoritativeMovementChangesLaterAudience),
    ("MissingAndPositionlessSpeakersReject", MissingAndPositionlessSpeakersReject),
    ("TextValidationRejectsWithoutTruncation", TextValidationRejectsWithoutTruncation),
    ("RecipientCapacityFailurePublishesNoPartialFact", RecipientCapacityFailurePublishesNoPartialFact),
    ("CandidateCapacityFailurePublishesNoPartialFact", CandidateCapacityFailurePublishesNoPartialFact),
    ("RetentionCapacityNeverOverwritesAnOccurrence", RetentionCapacityNeverOverwritesAnOccurrence),
    ("RejectedAdmissionOperationCannotEvictEnteredParticipant", RejectedAdmissionOperationCannotEvictEnteredParticipant),
    ("ActualAdmissionRejectionRemovesEnteredParticipant", ActualAdmissionRejectionRemovesEnteredParticipant),
    ("ExistingAdmissionsAreCensusedAndWorldMismatchRejects", ExistingAdmissionsAreCensusedAndWorldMismatchRejects),
    ("MultipleAdmissionOwnersFormOneBoundedCensus", MultipleAdmissionOwnersFormOneBoundedCensus),
    ("WarmedSpeechDiscoveryAllocatesZero", WarmedSpeechDiscoveryAllocatesZero),
    ("RemoteChatCodecAndClientStateFailClosed", RemoteChatCodecAndClientStateFailClosed),
    ("RemoteSpeechAndPrivateCommandsConverge", RemoteSpeechAndPrivateCommandsConverge),
    ("RemoteRecipientProofsAdvanceIndependently", RemoteRecipientProofsAdvanceIndependently),
    ("RemoteDuplicateSubmissionCannotRepeatAuthority", RemoteDuplicateSubmissionCannotRepeatAuthority),
    ("LostApplicationProofRetriesSameOccurrence", LostApplicationProofRetriesSameOccurrence),
    ("RemoteAudienceHandoffCapacityIsAtomic", RemoteAudienceHandoffCapacityIsAtomic),
    ("RemoteRateLimitRejectsBeforeSecondSpeech", RemoteRateLimitRejectsBeforeSecondSpeech),
    ("RemoteSpeechConvergesUnderLossDuplicateAndReorder", RemoteSpeechConvergesUnderLossDuplicateAndReorder),
    ("AcceptedOccurrenceAndDebtSurviveGenerationReplacement", AcceptedOccurrenceAndDebtSurviveGenerationReplacement),
    ("DelayedHandoffUsesAcceptanceTimePresentation", DelayedHandoffUsesAcceptanceTimePresentation),
    ("ClientFeedAndRecipientSequenceCrossGeneration", ClientFeedAndRecipientSequenceCrossGeneration),
    ("TerminalRecipientCannotBlockSpeechFifo", TerminalRecipientCannotBlockSpeechFifo),
    ("NewApplicationSessionRejectsOldLineage", NewApplicationSessionRejectsOldLineage),
    ("DeliveryLedgerIdleAndOneActiveWorkAreConstant", DeliveryLedgerIdleAndOneActiveWorkAreConstant),
};

foreach ((string name, Action body) in tests)
{
    body();
    Console.WriteLine($"PASS {name}");
}
Console.WriteLine($"All {tests.Length} Chat C0.0-C0.1 tests passed.");

static void RemoteChatCodecAndClientStateFailClosed()
{
    byte[] bytes = new byte[UltraSimGameProtocol.MaximumChatMessageBytes];
    Assert.True(UltraSimGameProtocol.TryWriteChatSubmission(
        bytes, 7, 1, (byte)GameSpeechMode.Say, "hello", out int written));
    Assert.True(UltraSimGameProtocol.TryReadChatSubmission(
        bytes.AsSpan(0, written), out RemoteChatSubmitHeader submission, out string text));
    Assert.Equal(7UL, submission.ApplicationSessionEpoch);
    Assert.Equal("hello", text);
    bytes[written++] = 0;
    Assert.False(UltraSimGameProtocol.TryReadChatSubmission(
        bytes.AsSpan(0, written), out _, out _));

    var invalidAnchor = new RemoteChatOccurrenceHeader
    {
        ApplicationSessionEpoch = 7,
        ServerGenerationId = 10,
        DeliveryId = 1,
        RecipientSequence = 1,
        SpeechFactId = 1,
        PublicEntityIndex = 9,
        PublicEntityGeneration = 0,
        Kind = RemoteChatOccurrenceKind.SpatialSpeech,
        SpeechMode = (byte)GameSpeechMode.Say,
    };
    Assert.False(UltraSimGameProtocol.TryWriteChatOccurrence(
        bytes, in invalidAnchor, "Player 1", "invalid anchor", out _));

    var state = new ClientChatState(7, new ClientChatLimits(1, 256, 128));
    var first = new ClientChatOccurrence(
        10, 1, 1, 1, 20,
        ClientChatOccurrenceKind.SpatialSpeech, ClientChatSpeechMode.Say,
        default, "Player 1", "first");
    Assert.Equal(ClientChatApplyStatus.Applied, state.TryApply(in first));
    Assert.Equal(ClientChatApplyStatus.Duplicate, state.TryApply(in first));
    var gap = first with { DeliveryId = 2, RecipientSequence = 3, Text = "gap" };
    Assert.Equal(ClientChatApplyStatus.Rejected, state.TryApply(in gap));
    var newer = first with
    {
        ServerGenerationId = 11,
        DeliveryId = 3,
        RecipientSequence = 2,
        SpeechFactId = 2,
        Text = "new generation",
    };
    Assert.Equal(ClientChatApplyStatus.Applied, state.TryApply(in newer));
    Assert.Equal(1, state.GetSnapshot().RetainedOccurrences);
    var delayedOldOrigin = first with
    {
        DeliveryId = 4,
        RecipientSequence = 3,
        SpeechFactId = 3,
        Text = "old-origin delayed",
    };
    Assert.Equal(ClientChatApplyStatus.Applied, state.TryApply(in delayedOldOrigin));
    Assert.Equal(4UL, state.GetSnapshot().NextRecipientSequence);
    Assert.Equal(11UL, state.GetSnapshot().ServerGenerationId);
    Assert.False(delayedOldOrigin.CanPresentOverhead(11));
}

static void MultipleAdmissionOwnersFormOneBoundedCensus()
{
    using var fixture = new ChatFixture(
        GameChatLimits.Default with
        {
            MaximumParticipants = 4,
            MaximumRecipientsPerSpeech = 4,
            MaximumCandidateEntities = 16,
        },
        entityCapacity: 16);
    ParticipantId first = fixture.Enter(1, At(0, 0));
    using AdmissionService secondAdmission = fixture.CreateAdmission();
    AdmissionResult second = secondAdmission.JoinAccountless(new ParticipantId(2));
    Assert.True(second.IsEntered);
    TestRuntime.SetPosition(fixture.Runtime, second.Entity, At(1_000, 0));
    fixture.Runtime.Tick();
    using GameChatAdmissionRegistration registration = fixture.Chat.AttachAdmission(secondAdmission);
    Assert.Equal(2, fixture.Chat.GetSnapshot().EnteredParticipants);
    GameChatSubmissionResult speech = fixture.Chat.Submit(first, "two admissions");
    Assert.True(speech.AcceptedSpeech);
    Assert.Equal(2, speech.RecipientCount);
}

static void RemoteSpeechAndPrivateCommandsConverge()
{
    using var fixture = new RemoteChatFixture();
    Assert.True(fixture.FirstClient.TrySubmit("hello nearby", ClientChatSpeechMode.Say));
    fixture.DriveUntil(() => fixture.Service.GetSnapshot().DeliveriesApplied == 2);
    ClientChatOccurrence[] first = fixture.CopyFirst();
    ClientChatOccurrence[] second = fixture.CopySecond();
    Assert.Equal(1, first.Length);
    Assert.Equal(1, second.Length);
    Assert.Equal("hello nearby", first[0].Text);
    Assert.Equal(ClientChatOccurrenceKind.SpatialSpeech, first[0].Kind);
    Assert.True(first[0].PublicAnchor.IsValid);
    Assert.Equal(first[0].SpeechFactId, second[0].SpeechFactId);

    Assert.True(fixture.FirstClient.TrySubmit("/where", ClientChatSpeechMode.Say));
    fixture.DriveUntil(() => fixture.Service.GetSnapshot().DeliveriesApplied == 3);
    first = fixture.CopyFirst();
    second = fixture.CopySecond();
    Assert.Equal(2, first.Length);
    Assert.Equal(1, second.Length);
    Assert.Equal(ClientChatOccurrenceKind.PrivateSystem, first[1].Kind);
    Assert.True(first[1].Text.StartsWith("Where:", StringComparison.Ordinal));

    Assert.True(fixture.FirstClient.TrySubmit("/not-a-command", ClientChatSpeechMode.Say));
    fixture.DriveUntil(() => fixture.Service.GetSnapshot().DeliveriesApplied == 4);
    first = fixture.CopyFirst();
    Assert.Equal(ClientChatOccurrenceKind.PrivateError, first[2].Kind);
    Assert.Equal(1, fixture.Authority.Chat.GetSnapshot().AcceptedSpeech);
}

static void RemoteRecipientProofsAdvanceIndependently()
{
    using var fixture = new RemoteChatFixture();
    Assert.True(fixture.FirstClient.TrySubmit("independent", ClientChatSpeechMode.Yell));
    fixture.DriveFirstUntil(() => fixture.FirstState.GetSnapshot().RetainedOccurrences == 1 &&
        fixture.Service.GetSnapshot().PendingDeliveries == 1);
    Assert.Equal(0, fixture.SecondState.GetSnapshot().RetainedOccurrences);
    Assert.Equal(1UL, fixture.Service.GetSnapshot().DeliveriesApplied);
    fixture.DriveUntil(() => fixture.Service.GetSnapshot().DeliveriesApplied == 2);
    Assert.Equal(1, fixture.SecondState.GetSnapshot().RetainedOccurrences);
}

static void RemoteDuplicateSubmissionCannotRepeatAuthority()
{
    using var fixture = new RemoteChatFixture();
    Assert.True(fixture.FirstClient.TrySubmit("once", ClientChatSpeechMode.Say));
    fixture.DriveUntil(() => fixture.Service.GetSnapshot().DeliveriesApplied == 2);
    byte[] payload = new byte[UltraSimGameProtocol.MaximumChatMessageBytes];
    Assert.True(UltraSimGameProtocol.TryWriteChatSubmission(
        payload, fixture.FirstEpoch, 1, (byte)GameSpeechMode.Say, "once", out int written));
    Assert.Equal(NetworkQueueStatus.Accepted, fixture.FirstLoop.Client.TryQueue(
        RemoteChatSubmitHeaderCodec.Descriptor.Id, payload.AsSpan(0, written)));
    fixture.DriveUntil(() => fixture.Service.GetSnapshot().DuplicateSubmissions == 1);
    Assert.Equal(1, fixture.Authority.Chat.GetSnapshot().AcceptedSpeech);
    Assert.Equal(2UL, fixture.Service.GetSnapshot().DeliveriesApplied);
}

static void LostApplicationProofRetriesSameOccurrence()
{
    using var fixture = new RemoteChatFixture();
    Assert.True(fixture.FirstClient.TrySubmit("retry same fact", ClientChatSpeechMode.Say));
    fixture.ReceiveFirstSubmissionOnly();
    Assert.Equal(2, fixture.Service.GetSnapshot().PendingDeliveries);

    fixture.DeliverFirstWithoutReturningProof();
    Assert.Equal(1, fixture.FirstState.GetSnapshot().RetainedOccurrences);
    Assert.Equal(0UL, fixture.Service.GetSnapshot().DeliveriesApplied);
    ClientChatOccurrence original = fixture.CopyFirst()[0];

    fixture.AdvanceTime(1_001);
    fixture.QueueFirstRetry();
    Assert.Equal(1UL, fixture.Service.GetSnapshot().DeliveryRetries);
    fixture.DriveUntil(() => fixture.Service.GetSnapshot().DeliveriesApplied == 2);

    ClientChatOccurrence[] rows = fixture.CopyFirst();
    Assert.Equal(1, rows.Length);
    Assert.Equal(original.DeliveryId, rows[0].DeliveryId);
    Assert.Equal(original.SpeechFactId, rows[0].SpeechFactId);
    Assert.True(fixture.FirstState.GetSnapshot().Duplicates >= 1);
}

static void RemoteAudienceHandoffCapacityIsAtomic()
{
    RemoteChatServerLimits limits = RemoteChatServerLimits.Default with
    {
        MaximumSessions = 2,
        MaximumPendingDeliveries = 1,
        MaximumPendingDeliveriesPerSession = 1,
    };
    using var fixture = new RemoteChatFixture(limits);
    Assert.True(fixture.FirstClient.TrySubmit("all or none", ClientChatSpeechMode.Say));
    fixture.DriveSteps(64);
    Assert.Equal(1, fixture.Authority.Chat.PendingSpeechCount);
    Assert.Equal(0, fixture.Service.GetSnapshot().PendingDeliveries);
    Assert.Equal(0, fixture.FirstState.GetSnapshot().RetainedOccurrences);
    Assert.Equal(0, fixture.SecondState.GetSnapshot().RetainedOccurrences);
}

static void RemoteRateLimitRejectsBeforeSecondSpeech()
{
    RemoteChatServerLimits limits = RemoteChatServerLimits.Default with
    {
        MaximumSessions = 2,
        MaximumPendingDeliveries = 8,
        MaximumPendingDeliveriesPerSession = 4,
        MaximumSubmissionsPerRateWindow = 1,
        RateWindowMilliseconds = 10_000,
    };
    using var fixture = new RemoteChatFixture(limits);
    Assert.True(fixture.FirstClient.TrySubmit("first", ClientChatSpeechMode.Say));
    Assert.True(fixture.FirstClient.TrySubmit("second", ClientChatSpeechMode.Say));
    fixture.DriveUntil(() => fixture.Service.GetSnapshot().DeliveriesApplied == 3);
    Assert.Equal(1, fixture.Authority.Chat.GetSnapshot().AcceptedSpeech);
    Assert.Equal(1UL, fixture.Service.GetSnapshot().RateRejections);
    ClientChatOccurrence[] rows = fixture.CopyFirst();
    Assert.Equal(2, rows.Length);
    Assert.Equal(ClientChatOccurrenceKind.PrivateError, rows[1].Kind);
}

static void RemoteSpeechConvergesUnderLossDuplicateAndReorder()
{
    using var fixture = new RemoteChatFixture(
        impairment: new NetworkImpairmentOptions(0xC001_0001, 220, 180, 0, 6));
    for (int index = 0; index < 4; index++)
        Assert.True(fixture.FirstClient.TrySubmit($"loss-{index}", ClientChatSpeechMode.Say));
    fixture.DriveUntil(() => fixture.Service.GetSnapshot().DeliveriesApplied == 8, 200_000);
    Assert.Equal(4, fixture.CopyFirst().Length);
    Assert.Equal(4, fixture.CopySecond().Length);
    Assert.True(fixture.FirstLoop.GetSnapshot().Datagram.Dropped > 0 ||
        fixture.SecondLoop.GetSnapshot().Datagram.Dropped > 0);
}

static void AcceptedOccurrenceAndDebtSurviveGenerationReplacement()
{
    using var fixture = new RemoteChatFixture();
    Assert.True(fixture.FirstClient.TrySubmit("cross-generation", ClientChatSpeechMode.Say));
    fixture.ReceiveFirstSubmissionOnly();
    fixture.DeliverFirstWithoutReturningProof();
    ClientChatOccurrence original = fixture.CopyFirst()[0];
    Assert.Equal(2, fixture.Service.GetSnapshot().PendingDeliveries);

    fixture.Authority.ReplaceGeneration();
    ulong replacementGeneration = fixture.Authority.CurrentGenerationId;
    Assert.True(replacementGeneration > original.ServerGenerationId);
    Assert.Equal(2, fixture.Service.GetSnapshot().PendingDeliveries);
    Assert.Equal(1, fixture.FirstState.GetSnapshot().RetainedOccurrences);
    Assert.False(original.CanPresentOverhead(replacementGeneration));

    fixture.AdvanceTime(1_001);
    fixture.QueueFirstRetry();
    fixture.DriveUntil(() => fixture.Service.GetSnapshot().DeliveriesApplied == 2);
    ClientChatOccurrence retained = fixture.CopyFirst()[0];
    Assert.Equal(original.DeliveryId, retained.DeliveryId);
    Assert.Equal(original.SpeechFactId, retained.SpeechFactId);
    Assert.Equal(2UL, fixture.FirstState.GetSnapshot().NextRecipientSequence);
}

static void DelayedHandoffUsesAcceptanceTimePresentation()
{
    RemoteChatServerLimits limits = RemoteChatServerLimits.Default with
    {
        MaximumSessions = 2,
        MaximumPendingDeliveries = 2,
        MaximumPendingDeliveriesPerSession = 2,
    };
    using var fixture = new RemoteChatFixture(limits);
    Assert.True(fixture.FirstClient.TrySubmit("occupy ledger", ClientChatSpeechMode.Say));
    fixture.ReceiveFirstSubmissionOnly();
    GameChatSubmissionResult delayed = fixture.Authority.Chat.Submit(
        fixture.FirstParticipant, "speaker vanished");
    Assert.True(delayed.AcceptedSpeech);
    Assert.False(fixture.Service.TryHandoffPendingSpeech());
    Assert.Equal(1, fixture.Authority.Chat.PendingSpeechCount);

    fixture.Authority.Destroy(fixture.FirstParticipant);
    fixture.Authority.ReplaceGeneration();
    ulong replacementGeneration = fixture.Authority.CurrentGenerationId;
    fixture.DriveUntil(() => fixture.Service.GetSnapshot().DeliveriesApplied == 4);
    ClientChatOccurrence first = fixture.CopyFirst()[1];
    ClientChatOccurrence second = fixture.CopySecond()[1];
    Assert.Equal(delayed.Speech.Id.Value, first.SpeechFactId);
    Assert.Equal(delayed.Speech.DisplayLabel, first.DisplayLabel);
    Assert.Equal(delayed.Speech.PublicAnchor, first.PublicAnchor);
    Assert.Equal(first.SpeechFactId, second.SpeechFactId);
    Assert.Equal("speaker vanished", first.Text);
    Assert.False(first.CanPresentOverhead(replacementGeneration));
}

static void ClientFeedAndRecipientSequenceCrossGeneration()
{
    var state = new ClientChatState(77, new ClientChatLimits(4, 256, 128));
    var first = new ClientChatOccurrence(
        10, 1, 1, 1, 1,
        ClientChatOccurrenceKind.SpatialSpeech, ClientChatSpeechMode.Say,
        new WorldPublicEntityId(1, 1), "Alice", "before");
    var second = first with
    {
        ServerGenerationId = 11,
        DeliveryId = 2,
        RecipientSequence = 2,
        SpeechFactId = 2,
        PublicAnchor = new WorldPublicEntityId(1, 2),
        Text = "after",
    };
    var delayed = first with
    {
        DeliveryId = 3,
        RecipientSequence = 3,
        SpeechFactId = 3,
        Text = "delayed old origin",
    };
    Assert.Equal(ClientChatApplyStatus.Applied, state.TryApply(in first));
    Assert.Equal(ClientChatApplyStatus.Applied, state.TryApply(in second));
    Assert.Equal(ClientChatApplyStatus.Applied, state.TryApply(in delayed));
    Assert.Equal(3, state.GetSnapshot().RetainedOccurrences);
    Assert.Equal(4UL, state.GetSnapshot().NextRecipientSequence);
    Assert.Equal(11UL, state.GetSnapshot().ServerGenerationId);
    Assert.False(delayed.CanPresentOverhead(11));
    Assert.True(second.CanPresentOverhead(11));
}

static void TerminalRecipientCannotBlockSpeechFifo()
{
    RemoteChatServerLimits limits = RemoteChatServerLimits.Default with
    {
        MaximumSessions = 2,
        MaximumPendingDeliveries = 2,
        MaximumPendingDeliveriesPerSession = 2,
    };
    using var fixture = new RemoteChatFixture(limits);
    Assert.True(fixture.FirstClient.TrySubmit("first", ClientChatSpeechMode.Say));
    fixture.ReceiveFirstSubmissionOnly();
    Assert.True(fixture.Authority.Chat.Submit(
        fixture.FirstParticipant, "survivor").AcceptedSpeech);
    Assert.False(fixture.Service.TryHandoffPendingSpeech());

    fixture.DisposeSecondServer();
    using RemoteChatServerSession reconnectedSecond = fixture.Service.Register(
        fixture.SecondLoop.Server, fixture.SecondParticipant, 0xC102, () => fixture.Now);
    Assert.True(fixture.Service.TryHandoffPendingSpeech());
    fixture.Authority.Move(fixture.SecondParticipant, At(100_000, 0));
    Assert.True(fixture.Authority.Chat.Submit(
        fixture.FirstParticipant, "later").AcceptedSpeech);
    Assert.False(fixture.Service.TryHandoffPendingSpeech());
    fixture.DriveFirstUntil(() => fixture.Service.GetSnapshot().DeliveriesApplied == 3);

    RemoteChatDeliveryServiceSnapshot snapshot = fixture.Service.GetSnapshot();
    Assert.Equal(3UL, snapshot.DeliveriesApplied);
    Assert.Equal(2UL, snapshot.TerminalAbandonments);
    Assert.Equal(0, snapshot.PendingDeliveries);
    Assert.Equal(0, fixture.Authority.Chat.PendingSpeechCount);
    Assert.SequenceEqual(
        new[] { "first", "survivor", "later" },
        fixture.CopyFirst().Select(static row => row.Text));
}

static void NewApplicationSessionRejectsOldLineage()
{
    using var fixture = new RemoteChatFixture();
    fixture.DisposeFirstServer();
    const ulong replacementEpoch = 0xD001;
    using RemoteChatServerSession replacement = fixture.Service.Register(
        fixture.FirstLoop.Server, fixture.FirstParticipant, replacementEpoch, () => fixture.Now);

    byte[] bytes = new byte[UltraSimGameProtocol.MaximumChatMessageBytes];
    Assert.True(UltraSimGameProtocol.TryWriteChatSubmission(
        bytes, fixture.FirstEpoch, 1, (byte)GameSpeechMode.Say, "stale", out int written));
    Assert.Equal(NetworkQueueStatus.Accepted, fixture.FirstLoop.Client.TryQueue(
        RemoteChatSubmitHeaderCodec.Descriptor.Id, bytes.AsSpan(0, written)));
    fixture.PumpIntoFirstServer(replacement);
    Assert.Equal(1UL, fixture.Service.GetSnapshot().RejectedSubmissions);
    Assert.Equal(0, fixture.Authority.Chat.PendingSpeechCount);

    fixture.Authority.Move(fixture.SecondParticipant, At(100_000, 0));
    GameChatSubmissionResult current = fixture.Authority.Chat.Submit(
        fixture.FirstParticipant, "current lineage");
    Assert.True(current.AcceptedSpeech);
    Assert.True(fixture.Service.TryHandoffPendingSpeech());
    var staleProof = new RemoteChatAppliedMessage
    {
        ApplicationSessionEpoch = fixture.FirstEpoch,
        ServerGenerationId = current.Speech.GenerationId,
        DeliveryId = 1,
        RecipientSequence = 1,
        Status = RemoteChatApplyStatus.Applied,
    };
    Assert.True(RemoteChatAppliedMessageCodec.TryWriteExact(
        bytes.AsSpan(0, RemoteChatAppliedMessageCodec.EncodedSize), in staleProof));
    Assert.Equal(NetworkQueueStatus.Accepted, fixture.FirstLoop.Client.TryQueue(
        RemoteChatAppliedMessageCodec.Descriptor.Id,
        bytes.AsSpan(0, RemoteChatAppliedMessageCodec.EncodedSize)));
    fixture.PumpIntoFirstServer(replacement);
    Assert.Equal(1UL, fixture.Service.GetSnapshot().MalformedProofs);
    Assert.Equal(1, fixture.Service.GetSnapshot().PendingDeliveries);
}

static void DeliveryLedgerIdleAndOneActiveWorkAreConstant()
{
    using var authority = new ChatFixture(entityCapacity: 512);
    var participants = new ParticipantId[256];
    for (int index = 0; index < participants.Length; index++)
        participants[index] = authority.Enter(
            (ulong)index + 1, At((long)index * 100_000, 0));
    Assert.True(ProtocolLoopback.TryCreate(
        NetworkProtocolLimits.Default,
        new DeterministicDatagramLimits(2_048, 1_472),
        default,
        out ProtocolLoopback? created,
        out _,
        UltraSimGameProtocol.Manifest));
    using ProtocolLoopback loop = created!;
    using var service = new RemoteChatDeliveryService(authority.Runtime.Game, authority.Chat);
    var sessions = new RemoteChatServerSession[participants.Length];
    try
    {
        for (int index = 0; index < sessions.Length; index++)
            sessions[index] = service.Register(
                loop.Server, participants[index], (ulong)index + 1, static () => 1_000);
        RemoteChatDeliveryServiceSnapshot before = service.GetSnapshot();
        for (int index = 0; index < sessions.Length; index++)
            Assert.True(sessions[index].TryProgress());
        RemoteChatDeliveryServiceSnapshot idle = service.GetSnapshot();
        Assert.Equal(before.DeliveryRowsExamined, idle.DeliveryRowsExamined);
        Assert.Equal(before.HandoffPreflights, idle.HandoffPreflights);

        Assert.True(authority.Chat.Submit(participants[0], "one active").AcceptedSpeech);
        Assert.True(service.TryHandoffPendingSpeech());
        RemoteChatDeliveryServiceSnapshot queued = service.GetSnapshot();
        Assert.Equal(1, queued.PendingDeliveries);
        for (int index = 0; index < sessions.Length; index++)
            Assert.True(sessions[index].TryProgress());
        RemoteChatDeliveryServiceSnapshot active = service.GetSnapshot();
        Assert.Equal(1UL, active.DeliveryRowsExamined - queued.DeliveryRowsExamined);
        Assert.Equal(1UL, active.HandoffPreflights);
        Assert.Equal(16_383, active.FreeDeliveryRows);
        Console.WriteLine(
            $"C0.1a CHAT LEDGER sessions={active.RegisteredSessions} " +
            $"capacity={active.PendingDeliveries + active.FreeDeliveryRows} " +
            $"idle_rows_examined={idle.DeliveryRowsExamined - before.DeliveryRowsExamined} " +
            $"one_active_rows_examined={active.DeliveryRowsExamined - queued.DeliveryRowsExamined} " +
            $"handoff_preflights={active.HandoffPreflights} " +
            $"reserved_array_bytes={active.KnownArrayReservedBytes}");
    }
    finally
    {
        for (int index = sessions.Length - 1; index >= 0; index--)
            sessions[index]?.Dispose();
    }
}

static void DefaultsAndAuthoritySurfaceAreExplicit()
{
    Assert.Equal(new GameSpeechDistanceProfile(2_000, 15_000, 30_000),
        GameSpeechDistanceProfile.Default);
    Assert.Equal(256, GameChatLimits.Default.MaximumTextUtf8Bytes);
    Assert.Equal(25, GameChatLimits.Default.MaximumCandidateChunks);
    Assert.Equal(4_096, GameChatLimits.Default.MaximumCandidateEntities);
    Assert.Equal(256, GameChatLimits.Default.MaximumRecipientsPerSpeech);
    Assert.Equal(64, GameChatLimits.Default.MaximumRetainedSpeechFacts);

    MethodInfo submit = typeof(GameChatService).GetMethod(nameof(GameChatService.Submit)) ??
        throw new InvalidOperationException("Chat Submit is missing.");
    Type[] parameters = submit.GetParameters().Select(static parameter => parameter.ParameterType).ToArray();
    Assert.SequenceEqual(
        new[] { typeof(ParticipantId), typeof(string), typeof(GameSpeechMode) },
        parameters);
    Assert.False(parameters.Contains(typeof(Entity)));
    Assert.False(parameters.Contains(typeof(WorldPosition)));
    Assert.False(parameters.Any(static type => type.IsArray));

    string[] remoteFields = typeof(RemoteChatSubmitHeader)
        .GetFields(BindingFlags.Instance | BindingFlags.Public)
        .Select(static field => field.Name)
        .OrderBy(static name => name, StringComparer.Ordinal)
        .ToArray();
    Assert.SequenceEqual(
        new[] { "ApplicationSessionEpoch", "SpeechMode", "SubmissionSequence" },
        remoteFields);

    using var fixture = new ChatFixture();
    Assert.Equal('/', fixture.Chat.PlayerCommandPrefix);
    Assert.Equal(2, fixture.Chat.CommandCount);
}

static void DefaultAndMalformedSuccessResultsFailClosed()
{
    GameChatSubmissionResult empty = default;
    Assert.Equal(GameChatSubmissionStatus.None, empty.Status);
    Assert.False(empty.AcceptedSpeech);
    Assert.False(empty.CommandSucceeded);
    Assert.False(empty.IsCommandResult);

    var malformedSpeech = new GameChatSubmissionResult(
        GameChatSubmissionStatus.SpeechAccepted,
        default,
        0,
        null);
    Assert.False(malformedSpeech.AcceptedSpeech);

    var malformedCommand = new GameChatSubmissionResult(
        GameChatSubmissionStatus.CommandSucceeded,
        default,
        0,
        null);
    Assert.False(malformedCommand.CommandSucceeded);
    Assert.False(malformedCommand.IsCommandResult);
}

static void PlainTextBecomesSayAndOccurrencesRemainOrdered()
{
    using var fixture = new ChatFixture();
    ParticipantId speaker = fixture.Enter(1, At(0, 0));
    ParticipantId listener = fixture.Enter(2, At(1_000, 0));

    GameChatSubmissionResult first = fixture.Chat.Submit(speaker, "first");
    GameChatSubmissionResult second = fixture.Chat.Submit(speaker, "second");
    Assert.True(first.AcceptedSpeech);
    Assert.True(second.AcceptedSpeech);
    Assert.Equal(GameSpeechMode.Say, first.Speech.Mode);
    Assert.Equal(1UL, first.Speech.Id.Value);
    Assert.Equal(2UL, second.Speech.Id.Value);
    Assert.Equal("first", first.Speech.Text);
    Assert.Equal("second", second.Speech.Text);
    Assert.SequenceEqual(new[] { speaker, listener }, fixture.CopyRecipients(first.Speech.Id));
    Assert.SequenceEqual(new[] { speaker, listener }, fixture.CopyRecipients(second.Speech.Id));

    Span<ParticipantId> recipients = stackalloc ParticipantId[4];
    Assert.True(fixture.Chat.TryConsumeNextSpeech(recipients, out GameSpeechFact consumedFirst, out int count));
    Assert.Equal(first.Speech.Id, consumedFirst.Id);
    Assert.Equal(2, count);
    Assert.True(fixture.Chat.TryConsumeNextSpeech(recipients, out GameSpeechFact consumedSecond, out count));
    Assert.Equal(second.Speech.Id, consumedSecond.Id);
    Assert.Equal(0, fixture.Chat.PendingSpeechCount);
}

static void HelpAndWhereArePrivateCommands()
{
    using var fixture = new ChatFixture();
    ParticipantId speaker = fixture.Enter(10, At(-16_001, 32_005, 777));

    GameChatSubmissionResult help = fixture.Chat.Submit(speaker, "/HELP");
    Assert.True(help.CommandSucceeded);
    Assert.True(help.PrivateReply!.Contains("/help", StringComparison.Ordinal));
    Assert.True(help.PrivateReply.Contains("/where", StringComparison.Ordinal));
    Assert.Equal(0, fixture.Chat.PendingSpeechCount);

    GameChatSubmissionResult where = fixture.Chat.Submit(speaker, "/where   ");
    Assert.True(where.CommandSucceeded);
    Assert.Equal($"Where: {At(-16_001, 32_005, 777)}", where.PrivateReply);
    Assert.Equal(0, fixture.Chat.PendingSpeechCount);
}

static void UnknownAndMalformedCommandsNeverBecomeSpeech()
{
    using var fixture = new ChatFixture();
    ParticipantId speaker = fixture.Enter(20, At(0, 0));

    Assert.Equal(GameChatSubmissionStatus.UnknownCommand,
        fixture.Chat.Submit(speaker, "/not-a-command").Status);
    Assert.Equal(GameChatSubmissionStatus.UnknownCommand,
        fixture.Chat.Submit(speaker, "/not-a-command value").Status);
    Assert.Equal(GameChatSubmissionStatus.InvalidCommandSyntax,
        fixture.Chat.Submit(speaker, "/").Status);
    Assert.Equal(GameChatSubmissionStatus.InvalidCommandSyntax,
        fixture.Chat.Submit(speaker, "/ help").Status);
    Assert.Equal(GameChatSubmissionStatus.InvalidCommandSyntax,
        fixture.Chat.Submit(speaker, "/where extra").Status);
    Assert.Equal(0, fixture.Chat.PendingSpeechCount);
}

static void WhisperSayAndYellProduceNestedExactAudiences()
{
    using var fixture = new ChatFixture(entityCapacity: 32);
    ParticipantId p1 = fixture.Enter(1, At(0, 0));
    ParticipantId p2 = fixture.Enter(2, At(2_000, 0));
    ParticipantId p3 = fixture.Enter(3, At(2_001, 0));
    ParticipantId p4 = fixture.Enter(4, At(15_000, 0));
    ParticipantId p5 = fixture.Enter(5, At(15_001, 0));
    ParticipantId p6 = fixture.Enter(6, At(30_000, 0));
    _ = fixture.Enter(7, At(30_001, 0));

    GameChatSubmissionResult whisper = fixture.Chat.Submit(p1, "whisper", GameSpeechMode.Whisper);
    GameChatSubmissionResult say = fixture.Chat.Submit(p1, "say", GameSpeechMode.Say);
    GameChatSubmissionResult yell = fixture.Chat.Submit(p1, "yell", GameSpeechMode.Yell);
    Assert.SequenceEqual(new[] { p1, p2 }, fixture.CopyRecipients(whisper.Speech.Id));
    Assert.SequenceEqual(new[] { p1, p2, p3, p4 }, fixture.CopyRecipients(say.Speech.Id));
    Assert.SequenceEqual(new[] { p1, p2, p3, p4, p5, p6 }, fixture.CopyRecipients(yell.Speech.Id));
}

static void NegativeCornerAndCrossChunkRoutingAreExact()
{
    using var fixture = new ChatFixture(entityCapacity: 32);
    ParticipantId p1 = fixture.Enter(1, At(-1, -1));
    ParticipantId p2 = fixture.Enter(2, At(1, 1));
    ParticipantId p3 = fixture.Enter(3, At(-1, 1));
    ParticipantId p4 = fixture.Enter(4, At(1, -1));
    ParticipantId p5 = fixture.Enter(5, At(-2_001, -1));
    _ = fixture.Enter(6, At(-2_002, -1));

    GameChatSubmissionResult result = fixture.Chat.Submit(p1, "corner", GameSpeechMode.Whisper);
    Assert.True(result.AcceptedSpeech);
    Assert.SequenceEqual(new[] { p1, p2, p3, p4, p5 }, fixture.CopyRecipients(result.Speech.Id));
}

static void CurrentAuthoritativeMovementChangesLaterAudience()
{
    using var fixture = new ChatFixture();
    ParticipantId speaker = fixture.Enter(1, At(0, 0));
    ParticipantId west = fixture.Enter(2, At(10_000, 0));
    ParticipantId east = fixture.Enter(3, At(40_000, 0));

    GameChatSubmissionResult before = fixture.Chat.Submit(speaker, "before");
    Assert.SequenceEqual(new[] { speaker, west }, fixture.CopyRecipients(before.Speech.Id));
    fixture.Move(speaker, At(30_000, 0));
    GameChatSubmissionResult after = fixture.Chat.Submit(speaker, "after");
    Assert.SequenceEqual(new[] { speaker, east }, fixture.CopyRecipients(after.Speech.Id));
}

static void MissingAndPositionlessSpeakersReject()
{
    using (var fixture = new ChatFixture())
    {
        ParticipantId speaker = fixture.Enter(1, At(0, 0));
        fixture.Destroy(speaker);
        Assert.Equal(GameChatSubmissionStatus.MissingSpeaker,
            fixture.Chat.Submit(speaker, "stale").Status);
        Assert.Equal(0, fixture.Chat.PendingSpeechCount);
    }

    var composition = new UltraSimGameComposition();
    using var runtime = TestRuntime.Create(composition, 16);
    using var admission = new AdmissionService(runtime.Game, new PositionlessBinder(composition));
    using var chat = new GameChatService(runtime.Game, admission);
    AdmissionResult entered = admission.JoinAccountless(new ParticipantId(2));
    Assert.True(entered.IsEntered);
    runtime.Tick();
    Assert.Equal(GameChatSubmissionStatus.MissingSpeakerPosition,
        chat.Submit(entered.Participant, "no position").Status);
    Assert.True(chat.Submit(entered.Participant, "/help").CommandSucceeded);
}

static void TextValidationRejectsWithoutTruncation()
{
    using var fixture = new ChatFixture();
    ParticipantId speaker = fixture.Enter(1, At(0, 0));

    Assert.Equal(GameChatSubmissionStatus.EmptyText, fixture.Chat.Submit(speaker, "").Status);
    Assert.Equal(GameChatSubmissionStatus.EmptyText, fixture.Chat.Submit(speaker, " \t\r\n").Status);
    Assert.Equal(GameChatSubmissionStatus.MalformedUnicode,
        fixture.Chat.Submit(speaker, "bad\uD800").Status);
    string overlong = new('x', fixture.Chat.Limits.MaximumTextUtf8Bytes + 1);
    GameChatSubmissionResult rejected = fixture.Chat.Submit(speaker, overlong);
    Assert.Equal(GameChatSubmissionStatus.TextTooLong, rejected.Status);
    Assert.Equal(0, fixture.Chat.PendingSpeechCount);

    string exact = new('x', fixture.Chat.Limits.MaximumTextUtf8Bytes);
    GameChatSubmissionResult accepted = fixture.Chat.Submit(speaker, exact);
    Assert.True(accepted.AcceptedSpeech);
    Assert.Equal(exact, accepted.Speech.Text);
}

static void RecipientCapacityFailurePublishesNoPartialFact()
{
    GameChatLimits limits = GameChatLimits.Default with
    {
        MaximumParticipants = 3,
        MaximumCandidateEntities = 8,
        MaximumRecipientsPerSpeech = 2,
        MaximumRetainedSpeechFacts = 4,
    };
    using var fixture = new ChatFixture(limits, entityCapacity: 16);
    ParticipantId speaker = fixture.Enter(1, At(0, 0));
    _ = fixture.Enter(2, At(100, 0));
    _ = fixture.Enter(3, At(200, 0));

    GameChatSubmissionResult rejected = fixture.Chat.Submit(speaker, "all or none");
    Assert.Equal(GameChatSubmissionStatus.RecipientCapacityExceeded, rejected.Status);
    Assert.Equal(0, fixture.Chat.PendingSpeechCount);
    Assert.Equal(0L, fixture.Chat.GetSnapshot().AcceptedSpeech);
    GameChatSubmissionResult help = fixture.Chat.Submit(speaker, "/help");
    Assert.True(help.CommandSucceeded);
}

static void CandidateCapacityFailurePublishesNoPartialFact()
{
    GameChatLimits limits = GameChatLimits.Default with
    {
        MaximumParticipants = 2,
        MaximumCandidateEntities = 2,
        MaximumRecipientsPerSpeech = 2,
        MaximumRetainedSpeechFacts = 4,
    };
    using var fixture = new ChatFixture(limits, entityCapacity: 16);
    ParticipantId speaker = fixture.Enter(1, At(0, 0));
    _ = fixture.Enter(2, At(100, 0));
    fixture.CreateAnimal(At(200, 0));

    GameChatSubmissionResult rejected = fixture.Chat.Submit(speaker, "bounded candidates");
    Assert.Equal(GameChatSubmissionStatus.CandidateEntityCapacityExceeded, rejected.Status);
    Assert.Equal(0, fixture.Chat.PendingSpeechCount);
}

static void RetentionCapacityNeverOverwritesAnOccurrence()
{
    GameChatLimits limits = GameChatLimits.Default with
    {
        MaximumParticipants = 2,
        MaximumCandidateEntities = 2,
        MaximumRecipientsPerSpeech = 2,
        MaximumRetainedSpeechFacts = 2,
    };
    using var fixture = new ChatFixture(limits);
    ParticipantId speaker = fixture.Enter(1, At(0, 0));
    GameChatSubmissionResult first = fixture.Chat.Submit(speaker, "first");
    GameChatSubmissionResult second = fixture.Chat.Submit(speaker, "second");

    Assert.Equal(GameChatSubmissionStatus.SpeechRetentionCapacityExceeded,
        fixture.Chat.Submit(speaker, "third").Status);
    Assert.Equal(2, fixture.Chat.PendingSpeechCount);
    Span<ParticipantId> recipients = stackalloc ParticipantId[2];
    Assert.True(fixture.Chat.TryConsumeNextSpeech(recipients, out GameSpeechFact consumed, out _));
    Assert.Equal(first.Speech.Id, consumed.Id);
    Assert.True(fixture.Chat.TryConsumeNextSpeech(recipients, out consumed, out _));
    Assert.Equal(second.Speech.Id, consumed.Id);
}

static void RejectedAdmissionOperationCannotEvictEnteredParticipant()
{
    using var fixture = new ChatFixture();
    ParticipantId speaker = fixture.Enter(1, At(0, 0));
    ParticipantId listener = fixture.Enter(2, At(1_000, 0));

    AdmissionResult duplicate = fixture.Admission.JoinAccountless(speaker);
    Assert.True(duplicate.IsRejected);
    Assert.Equal(AdmissionRejection.DuplicateParticipant, duplicate.Rejection);
    Assert.True(fixture.Admission.TryGetSession(speaker, out AdmissionResult retained));
    Assert.True(retained.IsEntered);
    Assert.Equal(2, fixture.Chat.GetSnapshot().EnteredParticipants);

    GameChatSubmissionResult speech = fixture.Chat.Submit(speaker, "still entered");
    Assert.True(speech.AcceptedSpeech);
    Assert.SequenceEqual(new[] { speaker, listener }, fixture.CopyRecipients(speech.Speech.Id));
}

static void ActualAdmissionRejectionRemovesEnteredParticipant()
{
    using var fixture = new ChatFixture();
    ParticipantId participant = fixture.Enter(1, At(0, 0));
    fixture.ReplaceGeneration();

    Assert.True(fixture.Admission.TryGetSession(participant, out AdmissionResult rejected));
    Assert.True(rejected.IsRejected);
    Assert.Equal(AdmissionRejection.GenerationReplaced, rejected.Rejection);
    Assert.Equal(0, fixture.Chat.GetSnapshot().EnteredParticipants);
    Assert.Equal(GameChatSubmissionStatus.ParticipantNotEntered,
        fixture.Chat.Submit(participant, "old authority").Status);
}

static void ExistingAdmissionsAreCensusedAndWorldMismatchRejects()
{
    var firstComposition = new UltraSimGameComposition();
    using var firstRuntime = TestRuntime.Create(firstComposition, 16);
    using var firstAdmission = new AdmissionService(firstRuntime.Game, firstComposition);
    AdmissionResult first = firstAdmission.JoinAccountless(new ParticipantId(1));
    Assert.True(first.IsEntered);
    TestRuntime.SetPosition(firstRuntime, first.Entity, At(0, 0));
    firstRuntime.Tick();
    using (var lateChat = new GameChatService(firstRuntime.Game, firstAdmission))
    {
        Assert.True(lateChat.Submit(first.Participant, "late census").AcceptedSpeech);
        Assert.Equal(1, lateChat.GetSnapshot().EnteredParticipants);
    }

    var secondComposition = new UltraSimGameComposition();
    using var secondRuntime = TestRuntime.Create(secondComposition, 16);
    using var secondAdmission = new AdmissionService(secondRuntime.Game, secondComposition);
    AdmissionResult foreign = secondAdmission.JoinAccountless(new ParticipantId(2));
    Assert.True(foreign.IsEntered);
    secondRuntime.Tick();
    using var mismatched = new GameChatService(firstRuntime.Game, secondAdmission);
    Assert.Equal(GameChatSubmissionStatus.GenerationMismatch,
        mismatched.Submit(foreign.Participant, "foreign world").Status);
    Assert.Equal(0, mismatched.PendingSpeechCount);
}

static void WarmedSpeechDiscoveryAllocatesZero()
{
    using var fixture = new ChatFixture();
    ParticipantId speaker = fixture.Enter(1, At(0, 0));
    _ = fixture.Enter(2, At(1_000, 0));
    Span<ParticipantId> recipients = stackalloc ParticipantId[4];

    for (int index = 0; index < 128; index++)
    {
        if (!fixture.Chat.Submit(speaker, "warm").AcceptedSpeech ||
            !fixture.Chat.TryConsumeNextSpeech(recipients, out _, out int count) || count != 2)
        {
            throw new InvalidOperationException("Chat warmup failed.");
        }
    }
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int index = 0; index < 1_000; index++)
    {
        if (!fixture.Chat.Submit(speaker, "warm").AcceptedSpeech ||
            !fixture.Chat.TryConsumeNextSpeech(recipients, out _, out int count) || count != 2)
        {
            throw new InvalidOperationException("Chat measured path failed.");
        }
    }
    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Assert.Equal(0L, allocated);
    GameChatSnapshot snapshot = fixture.Chat.GetSnapshot();
    Console.WriteLine(
        $"C0.0 CHAT METRICS submissions={snapshot.Submissions} accepted={snapshot.AcceptedSpeech} " +
        $"candidate_chunks={snapshot.CandidateChunksExamined} candidates={snapshot.CandidateEntitiesCopied} " +
        $"exact_tests={snapshot.ExactDistanceTests} deliveries={snapshot.RecipientDeliveries} " +
        $"reserved_array_bytes={snapshot.KnownArrayReservedBytes} warmed_alloc={allocated}B");
}

static WorldPosition At(long x, long z, int y = 0)
{
    Assert.True(WorldPosition.TryFromGlobalMillimeters(x, y, z, out WorldPosition position));
    return position;
}

sealed class ChatFixture : IDisposable
{
    private readonly Dictionary<ParticipantId, Entity> _entities = [];
    private readonly UltraSimGameComposition _composition;

    internal ChatFixture(GameChatLimits? limits = null, int entityCapacity = 64)
    {
        _composition = new UltraSimGameComposition();
        Runtime = TestRuntime.Create(_composition, entityCapacity);
        Admission = new AdmissionService(Runtime.Game, _composition);
        Chat = new GameChatService(Runtime.Game, Admission, limits ?? GameChatLimits.Default);
    }

    internal ServerRuntime Runtime { get; }
    internal AdmissionService Admission { get; }
    internal GameChatService Chat { get; }
    internal ulong CurrentGenerationId
    {
        get
        {
            Assert.True(Runtime.Game.TryUseActiveGeneration(
                static (_, generationId) => generationId, out ulong generationId));
            return generationId;
        }
    }

    internal AdmissionService CreateAdmission() => new(Runtime.Game, _composition);

    internal ParticipantId Enter(ulong value, in WorldPosition position)
    {
        var participant = new ParticipantId(value);
        AdmissionResult result = Admission.JoinAccountless(participant);
        Assert.True(result.IsEntered);
        _entities.Add(participant, result.Entity);
        TestRuntime.SetPosition(Runtime, result.Entity, position);
        Runtime.Tick();
        return participant;
    }

    internal void Move(ParticipantId participant, in WorldPosition position)
    {
        TestRuntime.SetPosition(Runtime, _entities[participant], position);
        Runtime.Tick();
    }

    internal void Destroy(ParticipantId participant)
    {
        Entity entity = _entities[participant];
        Assert.True(Runtime.Game.TryUseActiveGeneration(
            (world, _) => world.TryDestroy(entity), out bool destroyed) && destroyed);
        Runtime.Tick();
    }

    internal void CreateAnimal(in WorldPosition position)
    {
        WorldPosition requested = position;
        Assert.True(Runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
            (world, _, game) => world.TryInstantiate(game.Content.WoodlandAnimal, out Entity animal) &&
                game.WorldState.TrySetPosition(world, animal, requested),
            out bool created) && created);
        Runtime.Tick();
    }

    internal void ReplaceGeneration()
    {
        GameStaging staging = _composition.CreateFresh(
            64,
            new PersistenceIdentityOptions(new PersistentWorldId(0xC0_0001)));
        GameGenerationState? candidate = null;
        bool published = false;
        try
        {
            staging.World.Seal();
            staging.World.Initialize();
            staging.World.BeginRun();
            candidate = new GameGenerationState(
                new AccountingStore(),
                staging.Inventory,
                staging.Items,
                staging.Content,
                staging.WorldState,
                staging.SimulationLookup,
                staging.Replication,
                staging.WorldActions);
            Assert.True(Runtime.Game.TryPublishPreparedGeneration(staging.World, candidate, out _));
            published = true;
            candidate = null;
        }
        finally
        {
            if (!published)
            {
                candidate?.Dispose();
                staging.World.Dispose();
            }
        }
    }

    internal ParticipantId[] CopyRecipients(GameSpeechFactId fact)
    {
        var recipients = new ParticipantId[Chat.Limits.MaximumRecipientsPerSpeech];
        Assert.True(Chat.TryCopyRecipients(fact, recipients, out int count));
        return recipients[..count];
    }

    public void Dispose()
    {
        Chat.Dispose();
        Admission.Dispose();
        Runtime.Dispose();
    }
}

sealed class RemoteChatFixture : IDisposable
{
    private const ulong FirstApplicationEpoch = 0xC001;
    private const ulong SecondApplicationEpoch = 0xC002;
    private readonly RemoteChatServerSession _firstServer;
    private readonly RemoteChatServerSession _secondServer;
    private readonly ClientChatOccurrence[] _copyScratch = new ClientChatOccurrence[16];
    private long _now = 1_000;

    internal RemoteChatFixture(
        RemoteChatServerLimits? remoteLimits = null,
        NetworkImpairmentOptions impairment = default)
    {
        Authority = new ChatFixture(
            GameChatLimits.Default with
            {
                MaximumParticipants = 2,
                MaximumRecipientsPerSpeech = 2,
                MaximumCandidateEntities = 8,
            },
            entityCapacity: 16);
        FirstParticipant = Authority.Enter(1, Position(0, 0));
        SecondParticipant = Authority.Enter(2, Position(1_000, 0));
        Assert.True(ProtocolLoopback.TryCreate(
            NetworkProtocolLimits.Default,
            new DeterministicDatagramLimits(2_048, 1_472),
            impairment,
            out ProtocolLoopback? first,
            out _,
            UltraSimGameProtocol.Manifest));
        Assert.True(ProtocolLoopback.TryCreate(
            NetworkProtocolLimits.Default,
            new DeterministicDatagramLimits(2_048, 1_472),
            impairment,
            out ProtocolLoopback? second,
            out _,
            UltraSimGameProtocol.Manifest));
        FirstLoop = first!;
        SecondLoop = second!;
        Service = new RemoteChatDeliveryService(
            Authority.Runtime.Game,
            Authority.Chat,
            remoteLimits ?? RemoteChatServerLimits.Default with
            {
                MaximumSessions = 2,
                MaximumPendingDeliveries = 16,
                MaximumPendingDeliveriesPerSession = 8,
            });
        _firstServer = Service.Register(
            FirstLoop.Server, FirstParticipant, FirstApplicationEpoch, () => _now);
        _secondServer = Service.Register(
            SecondLoop.Server, SecondParticipant, SecondApplicationEpoch, () => _now);
        FirstState = new ClientChatState(
            FirstApplicationEpoch, new ClientChatLimits(16, 256, 128));
        SecondState = new ClientChatState(
            SecondApplicationEpoch, new ClientChatLimits(16, 256, 128));
        FirstClient = new RemoteChatClient(FirstLoop.Client, FirstApplicationEpoch, FirstState);
        SecondClient = new RemoteChatClient(SecondLoop.Client, SecondApplicationEpoch, SecondState);
    }

    internal ChatFixture Authority { get; }
    internal ProtocolLoopback FirstLoop { get; }
    internal ProtocolLoopback SecondLoop { get; }
    internal RemoteChatDeliveryService Service { get; }
    internal ParticipantId FirstParticipant { get; }
    internal ParticipantId SecondParticipant { get; }
    internal ClientChatState FirstState { get; }
    internal ClientChatState SecondState { get; }
    internal RemoteChatClient FirstClient { get; }
    internal RemoteChatClient SecondClient { get; }
    internal ulong FirstEpoch => FirstApplicationEpoch;
    internal long Now => _now;

    internal ClientChatOccurrence[] CopyFirst() => Copy(FirstState);
    internal ClientChatOccurrence[] CopySecond() => Copy(SecondState);

    internal void DriveUntil(Func<bool> complete, int maximumSteps = 20_000)
    {
        for (int step = 0; step < maximumSteps; step++)
        {
            if (complete()) return;
            PumpFirst();
            PumpSecond();
        }
        throw new InvalidOperationException("Remote Chat fixture did not converge.");
    }

    internal void DriveFirstUntil(Func<bool> complete, int maximumSteps = 20_000)
    {
        for (int step = 0; step < maximumSteps; step++)
        {
            if (complete()) return;
            PumpFirst();
        }
        throw new InvalidOperationException("First remote Chat recipient did not converge.");
    }

    internal void DriveSteps(int count)
    {
        for (int step = 0; step < count; step++)
        {
            PumpFirst();
            PumpSecond();
        }
    }

    internal void ReceiveFirstSubmissionOnly()
    {
        for (int step = 0; step < 1_000; step++)
        {
            FirstLoop.Pump();
            Drain(FirstLoop.Server, _firstServer);
            if (Service.GetSnapshot().PendingDeliveries != 0) return;
        }
        throw new InvalidOperationException("The first Chat submission did not reach authority.");
    }

    internal void DeliverFirstWithoutReturningProof()
    {
        Assert.True(_firstServer.TryProgress());
        for (int step = 0; step < 1_000; step++)
        {
            FirstLoop.Pump();
            Drain(FirstLoop.Client, FirstClient);
            if (FirstState.GetSnapshot().RetainedOccurrences != 0) return;
        }
        throw new InvalidOperationException("The first Chat occurrence did not reach Client Core.");
    }

    internal void AdvanceTime(long milliseconds) => _now = checked(_now + milliseconds);

    internal void QueueFirstRetry() => Assert.True(_firstServer.TryProgress());

    internal void DisposeFirstServer() => _firstServer.Dispose();
    internal void DisposeSecondServer() => _secondServer.Dispose();

    internal void PumpIntoFirstServer(RemoteChatServerSession server)
    {
        for (int step = 0; step < 1_000; step++)
        {
            FirstLoop.Pump();
            if (FirstLoop.Server.TryPeekReceived(
                    out NetworkDeliveredMessage message, out ReadOnlySpan<byte> payload))
            {
                Assert.True(server.TryProcess(in message, payload));
                Assert.True(FirstLoop.Server.TryConsumeReceived());
                return;
            }
        }
        throw new InvalidOperationException("A staged Chat message did not reach its replacement session.");
    }

    public void Dispose()
    {
        _firstServer.Dispose();
        _secondServer.Dispose();
        Service.Dispose();
        FirstLoop.Dispose();
        SecondLoop.Dispose();
        Authority.Dispose();
    }

    private void PumpFirst()
    {
        _ = _firstServer.TryProgress();
        _ = FirstClient.TryProgress();
        FirstLoop.Pump();
        Drain(FirstLoop.Server, _firstServer);
        Drain(FirstLoop.Client, FirstClient);
    }

    private void PumpSecond()
    {
        _ = _secondServer.TryProgress();
        _ = SecondClient.TryProgress();
        SecondLoop.Pump();
        Drain(SecondLoop.Server, _secondServer);
        Drain(SecondLoop.Client, SecondClient);
    }

    private ClientChatOccurrence[] Copy(ClientChatState state)
    {
        int count = state.CopyRecent(_copyScratch);
        return _copyScratch[..count].ToArray();
    }

    private static void Drain(NetworkProtocolPeer peer, IRemoteGameProtocolHandler handler)
    {
        while (peer.TryPeekReceived(out NetworkDeliveredMessage message, out ReadOnlySpan<byte> payload))
        {
            Assert.True(handler.TryProcess(in message, payload));
            Assert.True(peer.TryConsumeReceived());
        }
    }

    private static WorldPosition Position(long x, long z)
    {
        Assert.True(WorldPosition.TryFromGlobalMillimeters(x, 0, z, out WorldPosition position));
        return position;
    }
}

static class TestRuntime
{
    internal static ServerRuntime Create(UltraSimGameComposition composition, int entityCapacity)
    {
        var runtime = new ServerRuntime(
            composition,
            new ServerRuntimeOptions(
                UltraSimGameComposition.CreateWorldOptions(
                    entityCapacity,
                    new PersistenceIdentityOptions(new PersistentWorldId(0xC0_0001))),
                WorldClockOptions.Default));
        runtime.Initialize();
        runtime.Start();
        return runtime;
    }

    internal static void SetPosition(
        ServerRuntime runtime,
        Entity entity,
        in WorldPosition position)
    {
        WorldPosition requested = position;
        Assert.True(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
            (world, _, game) => game.WorldState.TrySetPosition(world, entity, requested),
            out bool changed) && changed);
    }
}

sealed class PositionlessBinder : IAdmissionEntityBinder
{
    private readonly UltraSimGameComposition _composition;

    internal PositionlessBinder(UltraSimGameComposition composition) => _composition = composition;

    public bool TryCreatePlayable(EcsWorld world, ParticipantId participant, out Entity entity)
    {
        entity = Entity.Null;
        return _composition.TryGetContent(world, out GameContent content) &&
            world.TryInstantiate(content.Bread.Handle, out entity);
    }

    public bool IsPlayable(EcsWorld world, in CharacterRecord character, Entity resolvedEntity) => true;
}

static class Assert
{
    internal static void True(bool value)
    {
        if (!value)
            throw new InvalidOperationException("Expected true.");
    }

    internal static void False(bool value)
    {
        if (value)
            throw new InvalidOperationException("Expected false.");
    }

    internal static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    internal static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException(
                $"Expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}].");
        }
    }
}

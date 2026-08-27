using System.Reflection;
using UltraSim.ECS;
using UltraSim.Game.Accounting;
using UltraSim.Game.Admission;
using UltraSim.Game.Chat;
using UltraSim.Game.Composition;
using UltraSim.Game.World;
using UltraSim.Server.Runtime;

var tests = new (string Name, Action Body)[]
{
    ("DefaultsAndAuthoritySurfaceAreExplicit", DefaultsAndAuthoritySurfaceAreExplicit),
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
    ("ExistingAdmissionsAreCensusedAndWorldMismatchRejects", ExistingAdmissionsAreCensusedAndWorldMismatchRejects),
    ("WarmedSpeechDiscoveryAllocatesZero", WarmedSpeechDiscoveryAllocatesZero),
};

foreach ((string name, Action body) in tests)
{
    body();
    Console.WriteLine($"PASS {name}");
}
Console.WriteLine($"All {tests.Length} Chat C0.0 tests passed.");

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

    using var fixture = new ChatFixture();
    Assert.Equal('/', fixture.Chat.PlayerCommandPrefix);
    Assert.Equal(2, fixture.Chat.CommandCount);
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

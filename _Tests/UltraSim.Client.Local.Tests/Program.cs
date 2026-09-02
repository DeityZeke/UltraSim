using UltraSim.Client.Core;
using UltraSim.Client.Local;
using UltraSim.Game.Composition;
using UltraSim.Game.Input;
using UltraSim.Game.UI;
using UltraSim.Game.World;
using UltraSim.Spatial;
using UltraSim.UI;

var tests = new (string Name, Action Body)[]
{
    ("AuthorityTicksWhilePresentationIsStalled", AuthorityTicksWhilePresentationIsStalled),
    ("PublicDebtWaitsForClientApplicationProof", PublicDebtWaitsForClientApplicationProof),
    ("SequencedCommandsCrossOwnerThread", SequencedCommandsCrossOwnerThread),
    ("ChatOccurrencesApplyWithoutProtocolLoopback", ChatOccurrencesApplyWithoutProtocolLoopback),
    ("ChatIngressRejectsUnboundedText", ChatIngressRejectsUnboundedText),
    ("RejectedClientApplicationRetainsChatDebt", RejectedClientApplicationRetainsChatDebt),
    ("UiOperationsAndActionsUseBoundedLocalBridge", UiOperationsAndActionsUseBoundedLocalBridge),
    ("SaveLoadReplacesGenerationAndRebuildsPublicLineage", SaveLoadReplacesGenerationAndRebuildsPublicLineage),
};

foreach ((string name, Action body) in tests)
{
    body();
    Console.WriteLine($"PASS {name}");
}
Console.WriteLine($"All {tests.Length} local client-session tests passed.");

static void AuthorityTicksWhilePresentationIsStalled()
{
    using Fixture fixture = Fixture.Create();
    ulong before = fixture.Session.GetSnapshot().AuthorityTicks;
    Thread.Sleep(90);
    LocalPlayableSessionSnapshot snapshot = fixture.Session.GetSnapshot();
    ulong after = snapshot.AuthorityTicks;
    True(after >= before + 3, $"Authority did not advance independently: {before}->{after}.");
    Console.WriteLine(
        $"EVIDENCE F0.0b authorityTicks={before}->{after} " +
        $"knownReservedBytes={snapshot.KnownReservedBytes}");
}

static void PublicDebtWaitsForClientApplicationProof()
{
    using Fixture fixture = Fixture.Create();
    Thread.Sleep(80);
    LocalPlayableSessionSnapshot stalled = fixture.Session.GetSnapshot();
    True(stalled.PendingWorldDeliveries > 0, "No bounded public debt accumulated while the client was stalled.");
    Equal(0UL, stalled.WorldDeliveriesApplied);

    ClientWorldSemanticCache cache = fixture.CreateCache();
    DriveUntil(() =>
    {
        fixture.Session.PumpWorld(cache);
        return fixture.Session.GetSnapshot().WorldDeliveriesApplied != 0 &&
            ContainsPrefab(cache, GameWorldPublicPrefabIds.Character) &&
            ContainsPrefab(cache, GameWorldPublicPrefabIds.CabinDoor) &&
            ContainsPrefab(cache, GameWorldPublicPrefabIds.WorldChest);
    }, failure: () => fixture.Session.GetSnapshot().ToString());
}

static void SequencedCommandsCrossOwnerThread()
{
    using Fixture fixture = Fixture.Create();
    True(fixture.Session.TryGetLatestView(out LocalPlayableAuthorityView before));
    var sequencer = new ClientWorldInputSequencer(fixture.Session.ControlSession);
    True(sequencer.TryCreateSemanticCommand(
        new ClientWorldCommandIntent(
            GameLocomotionUpdate.CreateDirect(GamePlayerCommand.MaximumAxisMagnitude, 0),
            GamePlayerActions.None,
            true),
        out GamePlayerCommand command));
    True(fixture.Session.TrySubmit(new LocalPlayableCommand(
        command, LocalPlayableSelection.None, false, default, false)));
    DriveUntil(() => fixture.Session.TryGetLatestView(out LocalPlayableAuthorityView view) &&
        view.LastCommandSequence == command.Sequence &&
        view.PlayerPosition.GlobalXMillimeters > before.PlayerPosition.GlobalXMillimeters);
}

static void ChatOccurrencesApplyWithoutProtocolLoopback()
{
    using Fixture fixture = Fixture.Create();
    var chat = new ClientChatState(fixture.Session.ApplicationSessionEpoch);
    True(fixture.Session.TrySubmitChat(1, ClientChatSpeechMode.Say, "local hello"));
    DriveUntil(() =>
    {
        fixture.Session.PumpChat(chat);
        return chat.GetSnapshot().Applied == 1;
    });
    var rows = new ClientChatOccurrence[4];
    Equal(1, chat.CopyRecent(rows));
    Equal("local hello", rows[0].Text);
    True(rows[0].IsSpatial);

    True(fixture.Session.TrySubmitChat(2, ClientChatSpeechMode.Say, "/where"));
    DriveUntil(() =>
    {
        fixture.Session.PumpChat(chat);
        return chat.GetSnapshot().Applied == 2;
    });
    Equal(2, chat.CopyRecent(rows));
    True(rows[1].Text.StartsWith("Where:", StringComparison.Ordinal));
    Equal(ClientChatOccurrenceKind.PrivateSystem, rows[1].Kind);
}

static void RejectedClientApplicationRetainsChatDebt()
{
    using Fixture fixture = Fixture.Create();
    var wrongLineage = new ClientChatState(fixture.Session.ApplicationSessionEpoch + 1);
    True(fixture.Session.TrySubmitChat(1, ClientChatSpeechMode.Say, "retain me"));
    DriveUntil(() =>
    {
        fixture.Session.PumpChat(wrongLineage, maximumDeliveries: 1);
        return fixture.Session.GetSnapshot().ChatDeliveriesRejected != 0;
    });
    Equal(0UL, fixture.Session.GetSnapshot().ChatDeliveriesApplied);

    var correctLineage = new ClientChatState(fixture.Session.ApplicationSessionEpoch);
    DriveUntil(() =>
    {
        fixture.Session.PumpChat(correctLineage, maximumDeliveries: 1);
        return correctLineage.GetSnapshot().Applied == 1 &&
            fixture.Session.GetSnapshot().ChatDeliveriesApplied == 1;
    });
    var rows = new ClientChatOccurrence[1];
    Equal(1, correctLineage.CopyRecent(rows));
    Equal("retain me", rows[0].Text);
}

static void ChatIngressRejectsUnboundedText()
{
    using Fixture fixture = Fixture.Create();
    True(!fixture.Session.TrySubmitChat(
        1, ClientChatSpeechMode.Say, new string('x', 257)));
    True(!fixture.Session.TrySubmitChat(
        2, ClientChatSpeechMode.Say, "\ud800"));
    Equal(0, fixture.Session.GetSnapshot().PendingChatDeliveries);
}

static void UiOperationsAndActionsUseBoundedLocalBridge()
{
    using Fixture fixture = Fixture.Create();
    var renderer = new MemoryRenderer();
    using var client = new UiClientRuntime(renderer, new UiClientLimits(8, 8));
    using LocalQueuedUiClient bridge = fixture.Session.AttachUiClient(client);
    True(fixture.Session.TryRequestOpenInventory(1));
    DriveUntil(() =>
    {
        bridge.Pump();
        return client.OpenViewCount == 1 && fixture.Session.GetSnapshot().UiOperationsApplied != 0;
    }, failure: () =>
    {
        fixture.Session.TryGetLatestView(out LocalPlayableAuthorityView view);
        return $"session={fixture.Session.GetSnapshot()} authority={view} views={client.OpenViewCount} created={renderer.Created}";
    });
    Equal(1, renderer.Created);

    UiViewInstanceId view = renderer.LastInstance;
    ReadOnlySpan<UiListRow> items = client.GetRows(view, InventoryUi.Items);
    True(items.Length != 0);
    client.SubmitAction(view, InventoryUi.Select, items[0].Id);
    DriveUntil(() =>
    {
        bridge.Pump();
        return string.Equals(
            client.GetValue(view, InventoryUi.Status).Text,
            "Item selected.",
            StringComparison.Ordinal);
    });
}

static void SaveLoadReplacesGenerationAndRebuildsPublicLineage()
{
    using Fixture fixture = Fixture.Create();
    True(fixture.Session.TryGetLatestView(out LocalPlayableAuthorityView initial));
    True(fixture.Session.TryRequestSave(1));
    DriveUntil(() =>
        fixture.Session.TryGetLatestView(out LocalPlayableAuthorityView view) &&
        view.LastAdministrativeSequence == 1 && view.LastAdministrativeSucceeded);

    True(fixture.Session.TryRequestRelocation(2, GamePlayableWorld.ClearCrossChunkPosition));
    DriveUntil(() =>
        fixture.Session.TryGetLatestView(out LocalPlayableAuthorityView view) &&
        view.LastAdministrativeSequence == 2 && view.LastAdministrativeSucceeded &&
        view.PlayerPosition.Chunk == GamePlayableWorld.ClearCrossChunkPosition.Chunk);

    True(fixture.Session.TryRequestLoad(3));
    DriveUntil(() =>
        fixture.Session.TryGetLatestView(out LocalPlayableAuthorityView view) &&
        view.LastAdministrativeSequence == 3 && view.LastAdministrativeSucceeded &&
        view.GenerationId > initial.GenerationId);
    True(fixture.Session.TryGetLatestView(out LocalPlayableAuthorityView restored));
    Equal(GamePlayableWorld.PlayerStart, restored.PlayerPosition);

    ClientWorldSemanticCache replacement = fixture.CreateCache();
    DriveUntil(() =>
    {
        fixture.Session.PumpWorld(replacement);
        return ContainsPrefab(replacement, GameWorldPublicPrefabIds.Character) &&
            ContainsPrefab(replacement, GameWorldPublicPrefabIds.CabinDoor) &&
            ContainsPrefab(replacement, GameWorldPublicPrefabIds.WorldChest);
    }, failure: () => fixture.Session.GetSnapshot().ToString());
}

static bool ContainsPrefab(ClientWorldSemanticCache cache, uint prefab)
{
    var chunks = new ClientSemanticChunkInfo[25];
    if (!cache.TryCopyResidentInfo(chunks, out int count, out _)) return false;
    for (int index = 0; index < count; index++)
    {
        if (!cache.TryGetResidentView(chunks[index].Handle, out ClientSemanticChunkView view)) continue;
        foreach (ref readonly WorldPublicRecord row in view.PublicRecords)
            if (row.PrefabId == prefab) return true;
    }
    return false;
}

static void DriveUntil(Func<bool> condition, int milliseconds = 5_000, Func<string>? failure = null)
{
    DateTime deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);
    while (DateTime.UtcNow < deadline)
    {
        if (condition()) return;
        Thread.Sleep(2);
    }
    throw new InvalidOperationException(
        $"The local-session condition did not converge. {failure?.Invoke()}");
}

static void True(bool value, string? message = null)
{
    if (!value) throw new InvalidOperationException(message ?? "Expected true.");
}

static void Equal<T>(T expected, T actual) where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
}

sealed class Fixture : IDisposable
{
    private readonly string _saveRoot;

    private Fixture(string saveRoot, LocalPlayableSession session)
    {
        _saveRoot = saveRoot;
        Session = session;
    }

    internal LocalPlayableSession Session { get; }

    internal static Fixture Create()
    {
        string saveRoot = Path.Combine(Path.GetTempPath(), $"ultrasim-local-{Guid.NewGuid():N}");
        Directory.CreateDirectory(saveRoot);
        LocalPlayableSession session = LocalPlayableSession.CreateCabin(saveRoot);
        session.WaitUntilReady(TimeSpan.FromSeconds(10));
        return new Fixture(saveRoot, session);
    }

    internal ClientWorldSemanticCache CreateCache()
    {
        Require(Session.TryGetLatestView(out LocalPlayableAuthorityView view));
        var cache = new ClientWorldSemanticCache(
            view.PreparedWorld,
            new ClientWorldSemanticCacheLimits(25, 16, 16, 16, 64));
        for (int z = GamePlayableWorld.MinimumChunk; z <= GamePlayableWorld.MaximumChunk; z++)
        for (int x = GamePlayableWorld.MinimumChunk; x <= GamePlayableWorld.MaximumChunk; x++)
        {
            var key = new SpatialChunkKey(
                GameWorldState.DefaultSpace,
                new SpatialChunkCoord(x, z));
            Require(cache.TryRequest(key, out ClientSemanticChunkHandle handle, out _));
            Require(cache.TryStageFromBaseWorld(handle, [], default, out _));
            Require(cache.TryPublish(handle, out _));
        }
        return cache;
    }

    public void Dispose()
    {
        Session.Dispose();
        if (Directory.Exists(_saveRoot)) Directory.Delete(_saveRoot, recursive: true);
    }

    private static void Require(bool value)
    {
        if (!value) throw new InvalidOperationException("Fixture setup failed.");
    }
}

sealed class MemoryRenderer : IUiRenderer
{
    internal int Created;
    internal UiViewInstanceId LastInstance;

    public IUiRenderHandle Create(
        UiViewInstanceId instance,
        CompiledUiDefinition definition,
        UiModelView model,
        Action<UiActionRequest> submitAction)
    {
        Created++;
        LastInstance = instance;
        return new Handle();
    }

    public void Apply(
        IUiRenderHandle handle,
        CompiledUiDefinition definition,
        UiModelView previous,
        UiModelView current,
        UiModelDelta delta,
        Action<UiActionRequest> submitAction) { }

    public void Close(IUiRenderHandle handle) { }

    private sealed class Handle : IUiRenderHandle { }
}

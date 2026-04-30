using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using UltraSim.Client;
using UltraSim.Client.Content;
using UltraSim.Client.UI;
using UltraSim.Discovery;
using UltraSim.Network;
using UltraSim.UI;

TestRunner.Run();

internal static class TestRunner
{
    public static void Run()
    {
        Run(nameof(ClientConnectionState_PreservesExpectedValues), ClientConnectionState_PreservesExpectedValues);
        Run(nameof(ClientInputSequencer_QueuesCopiesAndAcknowledgesInputs), ClientInputSequencer_QueuesCopiesAndAcknowledgesInputs);
        Run(nameof(ClientInputFocusState_BlocksMovementForTextMenusAndExplicitCapture), ClientInputFocusState_BlocksMovementForTextMenusAndExplicitCapture);
        Run(nameof(ClientAppShellNavigation_NavigatesBackAndTogglesSessionMenu), ClientAppShellNavigation_NavigatesBackAndTogglesSessionMenu);
        Run(nameof(ClientNetworkSummaryFormatter_FormatsOfflineAndReadySummary), ClientNetworkSummaryFormatter_FormatsOfflineAndReadySummary);
        Run(nameof(ClientServerBrowserService_RefreshesRowsAndHandlesProviderFailure), ClientServerBrowserService_RefreshesRowsAndHandlesProviderFailure);
        Run(nameof(ClientDirectConnectRequest_ValidatesAndTrimsEndpointFields), ClientDirectConnectRequest_ValidatesAndTrimsEndpointFields);
        Run(nameof(ServerContentIdentity_ProducesPathSafeStableNamespace), ServerContentIdentity_ProducesPathSafeStableNamespace);
        Run(nameof(ClientContentCachePathPolicy_ResolvesServerScopedPathsAndRejectsTraversal), ClientContentCachePathPolicy_ResolvesServerScopedPathsAndRejectsTraversal);
        Run(nameof(UiSchemaCacheKey_RequiresExactServerPanelRevisionAndHashIdentity), UiSchemaCacheKey_RequiresExactServerPanelRevisionAndHashIdentity);
        Run(nameof(ServerPatchManifestValidator_RejectsExecutableContentAndUnsafePaths), ServerPatchManifestValidator_RejectsExecutableContentAndUnsafePaths);
        Run(nameof(ContentPathValidation_ValidatesSizeAndSha256), ContentPathValidation_ValidatesSizeAndSha256);
        Run(nameof(LayeredContentResolver_ResolvesHighestPriorityMapping), LayeredContentResolver_ResolvesHighestPriorityMapping);
        Run(nameof(ResourcePackManifestValidator_CreatesUserOverrideLayer), ResourcePackManifestValidator_CreatesUserOverrideLayer);
        Run(nameof(ContentCacheQuotaPlanner_PlansOldestUnpinnedEvictions), ContentCacheQuotaPlanner_PlansOldestUnpinnedEvictions);
        Run(nameof(ContentCacheIndex_TracksMetadataAccessPinningAndEviction), ContentCacheIndex_TracksMetadataAccessPinningAndEviction);
        Run(nameof(ContentCacheIndexStore_RoundTripsMetadataAndRejectsUnsafeEntries), ContentCacheIndexStore_RoundTripsMetadataAndRejectsUnsafeEntries);
        Run(nameof(ContentCacheEvictionExecutor_DeletesFilesAndMetadataUnderCacheRootOnly), ContentCacheEvictionExecutor_DeletesFilesAndMetadataUnderCacheRootOnly);
        Run(nameof(UiSchemaCacheStore_WritesReadsIndexesAndRemovesSchemaBytes), UiSchemaCacheStore_WritesReadsIndexesAndRemovesSchemaBytes);
        Run(nameof(UiSchemaCacheStore_RejectsHashMismatchedSchemaBytes), UiSchemaCacheStore_RejectsHashMismatchedSchemaBytes);
        Run(nameof(ClientUiPanelRegistry_OpensColdPanelCachesSchemaAndReopensHot), ClientUiPanelRegistry_OpensColdPanelCachesSchemaAndReopensHot);
        Run(nameof(ClientUiPanelRegistry_ReportsMissingAndSupersededDefinitionsWithoutOpening), ClientUiPanelRegistry_ReportsMissingAndSupersededDefinitionsWithoutOpening);
        Run(nameof(ClientUiPanelRegistry_RequiresCatalogIdentityBeforeCatalogBackedOpen), ClientUiPanelRegistry_RequiresCatalogIdentityBeforeCatalogBackedOpen);
        Run(nameof(ClientUiPanelRegistry_ValidatesActionsAgainstOpenInstanceContext), ClientUiPanelRegistry_ValidatesActionsAgainstOpenInstanceContext);
        Run(nameof(ClientUiPanelRegistry_RejectsMissingPanelAndInvalidActionRequests), ClientUiPanelRegistry_RejectsMissingPanelAndInvalidActionRequests);
        Run(nameof(ClientUiPanelRegistry_AppliesModelSlotUpdatesAndIgnoresStaleSequences), ClientUiPanelRegistry_AppliesModelSlotUpdatesAndIgnoresStaleSequences);
        Run(nameof(ClientUiPanelRegistry_AppliesRepeaterRowsSeparateFromFlatModel), ClientUiPanelRegistry_AppliesRepeaterRowsSeparateFromFlatModel);
        Run(nameof(ClientUiPanelRegistry_RejectsMissingInvalidAndSupersededModelUpdates), ClientUiPanelRegistry_RejectsMissingInvalidAndSupersededModelUpdates);
        Run(nameof(ClientUiStateBindingResolverRegistry_RegistersAndResolvesBySource), ClientUiStateBindingResolverRegistry_RegistersAndResolvesBySource);
        Run(nameof(ServerPatchManifestCacheStore_WritesReadsIndexesAndRemovesManifest), ServerPatchManifestCacheStore_WritesReadsIndexesAndRemovesManifest);
        Run(nameof(ServerPatchManifestCacheStore_RejectsInvalidAndMismatchedManifests), ServerPatchManifestCacheStore_RejectsInvalidAndMismatchedManifests);
        Run(nameof(ServerAssetCacheStore_WritesReadsIndexesAndRemovesManifestBackedAssets), ServerAssetCacheStore_WritesReadsIndexesAndRemovesManifestBackedAssets);
        Run(nameof(ServerAssetCacheStore_RejectsSizeHashAndUnsafePathMismatches), ServerAssetCacheStore_RejectsSizeHashAndUnsafePathMismatches);
        Run(nameof(ClientPredictionStore_RecordsLooksUpAndRemovesAcknowledgedStates), ClientPredictionStore_RecordsLooksUpAndRemovesAcknowledgedStates);
        Run(nameof(ClientInterpolationBuffer_SamplesInterpolatedAndLatestState), ClientInterpolationBuffer_SamplesInterpolatedAndLatestState);
        Run(nameof(MovementPackets_RoundTripExpectedFields), MovementPackets_RoundTripExpectedFields);
        Run(nameof(ClientObserverStateBridge_AppliesNewestRealtimeFramesOnly), ClientObserverStateBridge_AppliesNewestRealtimeFramesOnly);
        Run(nameof(ClientObserverStateBridge_AppliesRealtimeStateBatchFrames), ClientObserverStateBridge_AppliesRealtimeStateBatchFrames);
        Run(nameof(ClientObserverStateBridge_AppliesFullBaselineAndResetsLocalState), ClientObserverStateBridge_AppliesFullBaselineAndResetsLocalState);
        Run(nameof(ClientObserverStateBridge_AppliesDeltaFramesAfterCompletedBaseline), ClientObserverStateBridge_AppliesDeltaFramesAfterCompletedBaseline);
        Run(nameof(ClientObserverEventBridge_AppliesIncreasingReliableEventFramesOnly), ClientObserverEventBridge_AppliesIncreasingReliableEventFramesOnly);
        Run(nameof(ClientObserverEventBridge_BuffersOutOfOrderFramesUntilGapCloses), ClientObserverEventBridge_BuffersOutOfOrderFramesUntilGapCloses);
        Run(nameof(ClientObserverEventBridge_CountsDroppedOutOfOrderFramesWhenBufferIsFull), ClientObserverEventBridge_CountsDroppedOutOfOrderFramesWhenBufferIsFull);
        Run(nameof(ClientObserverEventBridge_AllowSequenceGaps_AppliesScopedFanoutGaps), ClientObserverEventBridge_AllowSequenceGaps_AppliesScopedFanoutGaps);
        Run(nameof(ClientObserverEventBridge_RoutesNestedChatEventsBeforeGenericApply), ClientObserverEventBridge_RoutesNestedChatEventsBeforeGenericApply);
        Run(nameof(ClientMovementNetworkBridge_LoopbackRoundTripsMovementInputAndAuthoritativeState), ClientMovementNetworkBridge_LoopbackRoundTripsMovementInputAndAuthoritativeState);
        Run(nameof(ClientMovementSessionHost_ControlPlaneHandshakeAndPing_RoundTrip), ClientMovementSessionHost_ControlPlaneHandshakeAndPing_RoundTrip);
        Run(nameof(ClientMovementSessionHost_Pump_EmitsReliableEventAckForContiguousObserverEvents), ClientMovementSessionHost_Pump_EmitsReliableEventAckForContiguousObserverEvents);
        Run(nameof(ClientMovementSessionHost_Pump_EmitsStateBaselineAckWhenBaselineCompletes), ClientMovementSessionHost_Pump_EmitsStateBaselineAckWhenBaselineCompletes);
        Run(nameof(ClientMovementSessionHost_BaselineCompletion_ResetsObserverEventSequenceFloor), ClientMovementSessionHost_BaselineCompletion_ResetsObserverEventSequenceFloor);
        Run(nameof(ClientMovementSessionHost_Pump_AggregatesObserverCountsAcrossPollBatches), ClientMovementSessionHost_Pump_AggregatesObserverCountsAcrossPollBatches);

        Console.WriteLine("All Client scaffold tests passed.");
    }

    private static void ClientConnectionState_PreservesExpectedValues()
    {
        Assert.Equal(0, (int)ClientConnectionState.Disconnected);
        Assert.Equal(1, (int)ClientConnectionState.Connecting);
        Assert.Equal(2, (int)ClientConnectionState.Handshaking);
        Assert.Equal(3, (int)ClientConnectionState.Ready);
        Assert.Equal(4, (int)ClientConnectionState.Faulted);
    }

    private static void ClientInputSequencer_QueuesCopiesAndAcknowledgesInputs()
    {
        var sequencer = new ClientInputSequencer<ClientMovementInput>();
        ClientInputSample<ClientMovementInput> first = sequencer.EnqueueInput(new ClientMovementInput(100, 0, 1), 10);
        ClientInputSample<ClientMovementInput> second = sequencer.EnqueueInput(new ClientMovementInput(0, 100, 2), 11);

        Assert.Equal((ushort)1, first.Sequence);
        Assert.Equal((ushort)2, second.Sequence);
        Assert.Equal(2, sequencer.PendingCount);

        var scratch = new ClientInputSample<ClientMovementInput>[4];
        int copied = sequencer.CopyPendingTo(scratch);
        Assert.Equal(2, copied);
        Assert.Equal(first, scratch[0]);
        Assert.Equal(second, scratch[1]);

        int removed = sequencer.AcknowledgeUpTo(1);
        Assert.Equal(1, removed);
        Assert.Equal(1, sequencer.PendingCount);
        copied = sequencer.CopyPendingTo(scratch);
        Assert.Equal(1, copied);
        Assert.Equal(second, scratch[0]);
    }

    private static void ClientInputFocusState_BlocksMovementForTextMenusAndExplicitCapture()
    {
        Assert.False(new ClientInputFocusState(
            TextInputFocused: false,
            MenuOpen: false,
            ModalInputCaptured: false,
            ExplicitGameplayBlock: false).BlocksGameplayMovement);

        Assert.True(new ClientInputFocusState(
            TextInputFocused: true,
            MenuOpen: false,
            ModalInputCaptured: false,
            ExplicitGameplayBlock: false).BlocksGameplayMovement);

        Assert.True(new ClientInputFocusState(
            TextInputFocused: false,
            MenuOpen: true,
            ModalInputCaptured: false,
            ExplicitGameplayBlock: false).BlocksGameplayMovement);

        Assert.True(new ClientInputFocusState(
            TextInputFocused: false,
            MenuOpen: false,
            ModalInputCaptured: true,
            ExplicitGameplayBlock: false).BlocksGameplayMovement);

        Assert.True(new ClientInputFocusState(
            TextInputFocused: false,
            MenuOpen: false,
            ModalInputCaptured: false,
            ExplicitGameplayBlock: true).BlocksGameplayMovement);
    }

    private static void ClientAppShellNavigation_NavigatesBackAndTogglesSessionMenu()
    {
        var navigation = new ClientAppShellNavigation();

        Assert.Equal(ClientAppScreen.Start, navigation.CurrentScreen);
        Assert.True(navigation.IsMenuVisible);

        navigation.NavigateTo(ClientAppScreen.ServerBrowser);
        navigation.NavigateTo(ClientAppScreen.DirectConnect);
        Assert.Equal(ClientAppScreen.DirectConnect, navigation.CurrentScreen);
        Assert.Equal(2, navigation.BackStackCount);

        Assert.True(navigation.TryGoBack());
        Assert.Equal(ClientAppScreen.ServerBrowser, navigation.CurrentScreen);

        navigation.EnterSession();
        Assert.True(navigation.IsInSession);
        Assert.False(navigation.IsMenuVisible);
        Assert.Equal(ClientAppScreen.SessionStatus, navigation.CurrentScreen);

        navigation.ShowInSessionOptions();
        Assert.True(navigation.IsMenuVisible);
        Assert.Equal(ClientAppScreen.InSessionOptions, navigation.CurrentScreen);

        navigation.HideMenuIfInSession();
        Assert.False(navigation.IsMenuVisible);

        navigation.LeaveSession();
        Assert.False(navigation.IsInSession);
        Assert.True(navigation.IsMenuVisible);
        Assert.Equal(ClientAppScreen.Disconnected, navigation.CurrentScreen);
    }

    private static void ClientNetworkSummaryFormatter_FormatsOfflineAndReadySummary()
    {
        Assert.Equal("Network: offline", ClientNetworkSummaryFormatter.FormatOffline());

        long halfMillisecondTicks = Stopwatch.Frequency / 2000;
        var profile = new ClientPumpProfileSnapshot(
            InboundDatagramCount: 12,
            PollElapsedTicks: halfMillisecondTicks,
            PollPendingDrainElapsedTicks: 0,
            PollSocketReceiveElapsedTicks: 0,
            PollBundleUnpackElapsedTicks: 0,
            PollBundleIndexElapsedTicks: 0,
            PollBundleMaterializeElapsedTicks: 0,
            PollBundleSliceElapsedTicks: 0,
            PollBundleDatagramCreateElapsedTicks: 0,
            PollBundleEnqueueElapsedTicks: 0,
            PollPayloadCopyElapsedTicks: 0,
            PollEnvelopeParseElapsedTicks: 0,
            PollAsyncReceiveElapsedTicks: 0,
            PollAsyncQueuedDatagramCount: 0,
            ControlElapsedTicks: 0,
            MovementElapsedTicks: halfMillisecondTicks * 2,
            ObserverStateElapsedTicks: halfMillisecondTicks * 3,
            ObserverStateDecodeElapsedTicks: 0,
            ObserverStateApplyElapsedTicks: 0,
            ObserverEventElapsedTicks: halfMillisecondTicks * 4);

        string summary = ClientNetworkSummaryFormatter.Format(
            isReady: true,
            sessionId: new SessionId(42),
            lastPingRoundTripMilliseconds: 12.34,
            profile);

        Assert.True(summary.Contains("Network: ready", StringComparison.Ordinal), summary);
        Assert.True(summary.Contains("session:42", StringComparison.Ordinal), summary);
        Assert.True(summary.Contains("ping:12.3ms", StringComparison.Ordinal), summary);
        Assert.True(summary.Contains("in:12", StringComparison.Ordinal), summary);
        Assert.True(summary.Contains("poll:0.50ms", StringComparison.Ordinal), summary);
        Assert.True(summary.Contains("move:1.00ms", StringComparison.Ordinal), summary);
        Assert.True(summary.Contains("obs:1.50ms", StringComparison.Ordinal), summary);
        Assert.True(summary.Contains("evt:2.00ms", StringComparison.Ordinal), summary);
    }

    private static void ClientServerBrowserService_RefreshesRowsAndHandlesProviderFailure()
    {
        var localRow = new ServerDiscoveryRow(
            ProviderId: "manual",
            ProviderKind: ServerDiscoveryProviderKind.Manual,
            ServerId: "local-dev",
            DisplayName: "Local Dev",
            Host: "127.0.0.1",
            Port: 42042,
            Region: "local",
            CurrentUsers: 1,
            MaxUsers: 64);
        var service = new ClientServerBrowserService(
        [
            new ServerBrowserProviderDefinition(
                "manual",
                "Manual",
                ServerDiscoveryProviderKind.Manual,
                new InMemoryServerDiscoveryQuery([localRow])),
            new ServerBrowserProviderDefinition(
                "broken",
                "Broken",
                ServerDiscoveryProviderKind.Custom,
                new ThrowingDiscoveryQuery("registry unavailable"))
        ]);

        ServerBrowserSnapshot initial = service.GetSnapshot();
        Assert.Equal("manual", initial.ActiveProviderId);
        Assert.Equal(2, initial.Providers.Count);
        Assert.True(initial.Providers[0].IsSelected);
        Assert.Equal(0, initial.Rows.Count);

        ServerBrowserSnapshot refreshed = service.RefreshAsync().AsTask().GetAwaiter().GetResult();
        Assert.Equal(1, refreshed.Rows.Count);
        Assert.Equal("Local Dev", refreshed.Rows[0].DisplayName);
        Assert.Equal("127.0.0.1", refreshed.Rows[0].Host);
        Assert.Equal(42042, refreshed.Rows[0].Port);
        Assert.True(refreshed.LastError is null);
        Assert.True(refreshed.LastRefreshUtc.HasValue);

        Assert.True(service.SelectProvider("broken"));
        ServerBrowserSnapshot failed = service.RefreshAsync().AsTask().GetAwaiter().GetResult();
        Assert.Equal("broken", failed.ActiveProviderId);
        Assert.Equal(0, failed.Rows.Count);
        Assert.True(string.Equals("registry unavailable", failed.LastError, StringComparison.Ordinal));

        Assert.False(service.SelectProvider("missing"));
        Assert.Equal("broken", service.ActiveProviderId);
    }

    private static void ClientDirectConnectRequest_ValidatesAndTrimsEndpointFields()
    {
        Assert.True(ClientDirectConnectRequest.TryCreate(
            " 127.0.0.1 ",
            "42042",
            out ClientDirectConnectRequest request,
            out string error));
        Assert.Equal("127.0.0.1", request.Host);
        Assert.Equal(42042, request.Port);
        Assert.Equal(string.Empty, error);
        Assert.True(request.TryResolve(out System.Net.IPEndPoint endPoint, out error));
        Assert.Equal(42042, endPoint.Port);

        Assert.False(ClientDirectConnectRequest.TryCreate("", "42042", out _, out error));
        Assert.Equal("Host is required.", error);

        Assert.False(ClientDirectConnectRequest.TryCreate("127.0.0.1", "99999", out _, out error));
        Assert.Equal("Port must be between 0 and 65535.", error);

        Assert.True(ClientDirectConnectRequest.TryCreate(
            new ServerDiscoveryRow(
                ProviderId: "manual",
                ProviderKind: ServerDiscoveryProviderKind.Manual,
                ServerId: "local",
                DisplayName: "Local",
                Host: "127.0.0.1",
                Port: 42042),
            out request,
            out error));
        Assert.Equal("127.0.0.1", request.Host);
        Assert.Equal(42042, request.Port);

        Assert.False(ClientDirectConnectRequest.TryCreate(
            new ServerDiscoveryRow(
                ProviderId: "relay",
                ProviderKind: ServerDiscoveryProviderKind.Custom,
                ServerId: "relay",
                DisplayName: "Relay",
                Host: "relay.example",
                Port: 1,
                ConnectTargetKind: ConnectTargetKind.Relay),
            out _,
            out error));
        Assert.Equal("Server 'Relay' does not expose a direct connect target.", error);
    }

    private static void ServerContentIdentity_ProducesPathSafeStableNamespace()
    {
        Assert.True(ServerContentIdentity.TryCreate(" official:shard/main ", out ServerContentIdentity first, out string error), error);
        Assert.True(ServerContentIdentity.TryCreate("official:shard/main", out ServerContentIdentity second, out error), error);
        Assert.True(ServerContentIdentity.TryCreate("official:shard/other", out ServerContentIdentity other, out error), error);

        Assert.Equal("official:shard/main", first.ServerId);
        Assert.Equal(first.CacheNamespace, second.CacheNamespace);
        Assert.False(string.Equals(first.CacheNamespace, other.CacheNamespace, StringComparison.Ordinal));
        Assert.True(first.CacheNamespace.StartsWith("server-", StringComparison.Ordinal), first.CacheNamespace);
        Assert.False(first.CacheNamespace.Contains('/', StringComparison.Ordinal), first.CacheNamespace);
        Assert.False(first.CacheNamespace.Contains('\\', StringComparison.Ordinal), first.CacheNamespace);

        Assert.False(ServerContentIdentity.TryCreate("", out _, out error));
        Assert.Equal("Server identity is required.", error);
    }

    private static void ClientContentCachePathPolicy_ResolvesServerScopedPathsAndRejectsTraversal()
    {
        Assert.True(ServerContentIdentity.TryCreate("local-dev", out ServerContentIdentity server, out string error), error);
        var policy = new ClientContentCachePathPolicy(Path.Combine(Path.GetTempPath(), "ultrasim-cache-test"));

        Assert.True(policy.TryGetServerAssetPath(server, "icons\\items/sword.png", out string assetPath, out error), error);
        Assert.True(assetPath.Contains(server.CacheNamespace, StringComparison.Ordinal), assetPath);
        Assert.True(assetPath.EndsWith(Path.Combine("Assets", "icons", "items", "sword.png"), StringComparison.Ordinal), assetPath);

        Assert.False(policy.TryGetServerAssetPath(server, "../outside.bin", out _, out error));
        Assert.Equal("Content path cannot contain traversal segments.", error);

        Assert.False(policy.TryGetServerAssetPath(server, "/tmp/outside.bin", out _, out error));
        Assert.Equal("Content path must be relative.", error);

        Assert.True(string.Equals(Path.Combine(policy.RootPath, "Mods"), policy.ModsRoot, StringComparison.Ordinal));
        Assert.True(policy.ModsRoot.Contains("Mods", StringComparison.Ordinal));
    }

    private static void UiSchemaCacheKey_RequiresExactServerPanelRevisionAndHashIdentity()
    {
        Assert.True(ServerContentIdentity.TryCreate("local-dev", out ServerContentIdentity server, out string error), error);
        string hash = new string('A', 64);
        Assert.True(UiSchemaCacheKey.TryCreate(server, "login.character_select", 7, hash, out UiSchemaCacheKey key, out error), error);

        Assert.Equal("login.character_select", key.PanelType);
        Assert.Equal(7, key.Revision);
        Assert.Equal(new string('a', 64), key.SchemaHashHex);

        var policy = new ClientContentCachePathPolicy(Path.Combine(Path.GetTempPath(), "ultrasim-cache-test"));
        string path = policy.GetSchemaPath(key);
        Assert.True(path.Contains(server.CacheNamespace, StringComparison.Ordinal), path);
        Assert.True(path.EndsWith(Path.Combine("Schemas", "login.character_select", "7", new string('a', 64) + ".uischema"), StringComparison.Ordinal), path);

        Assert.False(UiSchemaCacheKey.TryCreate(server, "../login", 7, hash, out _, out error));
        Assert.True(error.Contains("Panel type", StringComparison.Ordinal), error);

        Assert.False(UiSchemaCacheKey.TryCreate(server, "login", -1, hash, out _, out error));
        Assert.Equal("Schema revision cannot be negative.", error);
    }

    private static void ServerPatchManifestValidator_RejectsExecutableContentAndUnsafePaths()
    {
        string hash = new string('b', 64);
        var valid = new ServerPatchManifest(
            "patch-1",
            1,
            [
                new ServerPatchManifestEntry("icons/items/sword.png", 128, hash),
                new ServerPatchManifestEntry("ui/login.uischema", 256, new string('c', 64))
            ]);
        Assert.True(ServerPatchManifestValidator.TryValidate(valid, out string error), error);

        var executable = new ServerPatchManifest(
            "patch-1",
            1,
            [new ServerPatchManifestEntry("scripts/evil.gd", 1, hash)]);
        Assert.False(ServerPatchManifestValidator.TryValidate(executable, out error));
        Assert.True(error.Contains("executable/script", StringComparison.Ordinal), error);

        var traversal = new ServerPatchManifest(
            "patch-1",
            1,
            [new ServerPatchManifestEntry("../outside.png", 1, hash)]);
        Assert.False(ServerPatchManifestValidator.TryValidate(traversal, out error));
        Assert.Equal("Content path cannot contain traversal segments.", error);

        var duplicate = new ServerPatchManifest(
            "patch-1",
            1,
            [
                new ServerPatchManifestEntry("icons/a.png", 1, hash),
                new ServerPatchManifestEntry("icons\\a.png", 1, hash)
            ]);
        Assert.False(ServerPatchManifestValidator.TryValidate(duplicate, out error));
        Assert.True(error.Contains("duplicate path", StringComparison.Ordinal), error);
    }

    private static void ContentPathValidation_ValidatesSizeAndSha256()
    {
        byte[] bytes = [1, 2, 3, 4];
        string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        Assert.True(ContentPathValidation.ValidateSizeAndSha256(bytes, bytes.Length, hash, out string error), error);
        Assert.False(ContentPathValidation.ValidateSizeAndSha256(bytes, bytes.Length + 1, hash, out error));
        Assert.True(error.Contains("size mismatch", StringComparison.OrdinalIgnoreCase), error);
        Assert.False(ContentPathValidation.ValidateSizeAndSha256(bytes, bytes.Length, new string('0', 64), out error));
        Assert.Equal("Asset SHA-256 hash mismatch.", error);
    }

    private static void LayeredContentResolver_ResolvesHighestPriorityMapping()
    {
        Assert.True(ContentResolverLayer.TryCreate(
            "builtin.default",
            ContentTrustClass.BuiltIn,
            ContentResolverPriority.BuiltIn,
            new Dictionary<string, string>
            {
                ["mobile/default/body"] = "res://Assets/Avatars/default.tscn",
                ["icons/item/sword"] = "res://Assets/Icons/sword.png"
            },
            out ContentResolverLayer builtIn,
            out string error), error);
        Assert.True(ContentResolverLayer.TryCreate(
            "server.local",
            ContentTrustClass.ServerPatch,
            ContentResolverPriority.ServerPatch,
            new Dictionary<string, string>
            {
                ["mobile/default/body"] = "/cache/server/Assets/avatars/server-default.tscn"
            },
            out ContentResolverLayer serverPatch,
            out error), error);
        Assert.True(ContentResolverLayer.TryCreate(
            "resource-pack.noir",
            ContentTrustClass.UserResourcePack,
            ContentResolverPriority.UserResourcePack,
            new Dictionary<string, string>
            {
                ["mobile/default/body"] = "/packs/noir/default.tscn"
            },
            out ContentResolverLayer resourcePack,
            out error), error);

        var resolver = new LayeredContentResolver([builtIn, serverPatch, resourcePack]);

        Assert.True(resolver.TryResolve(" mobile/default/body ", out ContentResolution resolved));
        Assert.Equal("/cache/server/Assets/avatars/server-default.tscn", resolved.ResolvedPath);
        Assert.Equal(ContentTrustClass.ServerPatch, resolved.TrustClass);
        Assert.Equal("server.local", resolved.LayerId);

        Assert.True(resolver.TryResolve("icons/item/sword", out resolved));
        Assert.Equal("res://Assets/Icons/sword.png", resolved.ResolvedPath);
        Assert.Equal(ContentTrustClass.BuiltIn, resolved.TrustClass);

        Assert.False(resolver.TryResolve("missing", out _));
    }

    private static void ResourcePackManifestValidator_CreatesUserOverrideLayer()
    {
        var manifest = new ResourcePackManifest(
            "high-contrast",
            Priority: 12,
            new Dictionary<string, string>
            {
                ["icons/item/sword"] = "user://packs/high-contrast/icons/sword.png"
            });

        Assert.True(ResourcePackManifestValidator.TryCreateResolverLayer(manifest, out ContentResolverLayer layer, out string error), error);
        Assert.Equal("resource-pack.high-contrast", layer.LayerId);
        Assert.Equal(ContentTrustClass.UserResourcePack, layer.TrustClass);
        Assert.Equal(ContentResolverPriority.UserResourcePack + 12, layer.Priority);
        Assert.True(layer.TryResolve("icons/item/sword", out ContentResolution resolution));
        Assert.Equal("user://packs/high-contrast/icons/sword.png", resolution.ResolvedPath);

        var invalid = new ResourcePackManifest(
            "bad pack",
            Priority: 0,
            new Dictionary<string, string>
            {
                ["icons/item/sword"] = "user://packs/bad/icons/sword.png"
            });
        Assert.False(ResourcePackManifestValidator.TryCreateResolverLayer(invalid, out _, out error));
        Assert.True(error.Contains("layer id", StringComparison.Ordinal), error);
    }

    private static void ContentCacheQuotaPlanner_PlansOldestUnpinnedEvictions()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-04-29T00:00:00Z", CultureInfo.InvariantCulture);
        ContentCacheEntry pinnedOld = new(
            "pinned-old",
            "schemas/pinned.uischema",
            ContentCacheEntryKind.Schema,
            600,
            now.AddDays(-10),
            Pinned: true);
        ContentCacheEntry oldest = new(
            "oldest",
            "assets/old.bin",
            ContentCacheEntryKind.Asset,
            400,
            now.AddDays(-7));
        ContentCacheEntry middle = new(
            "middle",
            "assets/middle.bin",
            ContentCacheEntryKind.Asset,
            350,
            now.AddDays(-3));
        ContentCacheEntry newest = new(
            "newest",
            "assets/new.bin",
            ContentCacheEntryKind.Asset,
            300,
            now);

        ContentCacheQuotaPlan plan = ContentCacheQuotaPlanner.PlanEviction(
            [pinnedOld, newest, middle, oldest],
            quotaBytes: 1000,
            targetFillRatio: 0.80);

        Assert.Equal(1650L, plan.CurrentSizeBytes);
        Assert.Equal(1000L, plan.QuotaBytes);
        Assert.Equal(800L, plan.TargetSizeBytes);
        Assert.Equal(850L, plan.BytesToFree);
        Assert.SequenceEqual([oldest, middle, newest], plan.EvictionCandidates);

        ContentCacheQuotaPlan underQuota = ContentCacheQuotaPlanner.PlanEviction(
            [oldest, middle],
            quotaBytes: 1000);
        Assert.Equal(0, underQuota.EvictionCandidates.Count);
    }

    private static void ContentCacheIndex_TracksMetadataAccessPinningAndEviction()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-04-29T00:00:00Z", CultureInfo.InvariantCulture);
        var index = new ContentCacheIndex();
        Assert.True(index.TryUpsert(new ContentCacheEntry(
            "schema/login",
            "Schemas/login/1/hash.uischema",
            ContentCacheEntryKind.Schema,
            100,
            now.AddHours(-2)), out string error), error);
        Assert.True(index.TryUpsert(new ContentCacheEntry(
            "asset/sword",
            "Assets/icons/sword.png",
            ContentCacheEntryKind.Asset,
            500,
            now.AddHours(-4)), out error), error);
        Assert.Equal(2, index.Count);
        Assert.Equal(600L, index.TotalSizeBytes);

        Assert.True(index.TryMarkAccessed("asset/sword", now));
        Assert.True(index.TrySetPinned("asset/sword", true));
        Assert.True(index.TryGet("asset/sword", out ContentCacheEntry sword));
        Assert.Equal(now, sword.LastAccessUtc);
        Assert.True(sword.Pinned);

        ContentCacheQuotaPlan plan = index.PlanEviction(quotaBytes: 300, targetFillRatio: 0.5);
        Assert.SequenceEqual(["schema/login"], plan.EvictionCandidates.Select(static entry => entry.CacheKey).ToArray());
        IReadOnlyList<ContentCacheEntry> removed = index.ApplyEvictionPlan(plan);
        Assert.Equal(1, removed.Count);
        Assert.Equal("schema/login", removed[0].CacheKey);
        Assert.Equal(1, index.Count);
        Assert.True(index.TryGet("asset/sword", out _));

        Assert.False(index.TryUpsert(new ContentCacheEntry(
            "bad",
            "../outside.bin",
            ContentCacheEntryKind.Asset,
            1,
            now), out error));
        Assert.Equal("Content path cannot contain traversal segments.", error);
    }

    private static void ContentCacheIndexStore_RoundTripsMetadataAndRejectsUnsafeEntries()
    {
        string root = Path.Combine(Path.GetTempPath(), "ultrasim-cache-index-tests", Guid.NewGuid().ToString("N"));
        var pathPolicy = new ClientContentCachePathPolicy(root);
        var store = new ContentCacheIndexStore(pathPolicy.CacheIndexPath);
        DateTimeOffset now = DateTimeOffset.Parse("2026-04-29T00:00:00Z", CultureInfo.InvariantCulture);
        var index = new ContentCacheIndex();
        Assert.True(index.TryUpsert(new ContentCacheEntry(
            "schema/login",
            "Schemas/login/1/hash.uischema",
            ContentCacheEntryKind.Schema,
            100,
            now,
            Pinned: true), out string error), error);

        store.Save(index);
        ContentCacheIndex loaded = store.LoadOrCreate();

        Assert.Equal(1, loaded.Count);
        Assert.True(loaded.TryGet("schema/login", out ContentCacheEntry entry));
        Assert.Equal("Schemas/login/1/hash.uischema", entry.RelativePath);
        Assert.Equal(ContentCacheEntryKind.Schema, entry.Kind);
        Assert.Equal(100L, entry.SizeBytes);
        Assert.True(entry.Pinned);

        string json = ContentCacheIndexStore.Serialize(index);
        Assert.True(ContentCacheIndexStore.TryDeserialize(json, out ContentCacheIndex parsed, out error), error);
        Assert.True(parsed.TryGet("schema/login", out _));

        string unsafeJson = """
        {
          "Version": 1,
          "Entries": [
            {
              "CacheKey": "bad",
              "RelativePath": "../outside.bin",
              "Kind": 0,
              "SizeBytes": 1,
              "LastAccessUtc": "2026-04-29T00:00:00+00:00",
              "Pinned": false
            }
          ]
        }
        """;
        Assert.False(ContentCacheIndexStore.TryDeserialize(unsafeJson, out _, out error));
        Assert.Equal("Content path cannot contain traversal segments.", error);
    }

    private static void ContentCacheEvictionExecutor_DeletesFilesAndMetadataUnderCacheRootOnly()
    {
        string root = Path.Combine(Path.GetTempPath(), "ultrasim-cache-eviction-tests", Guid.NewGuid().ToString("N"));
        var pathPolicy = new ClientContentCachePathPolicy(root);
        var index = new ContentCacheIndex();
        DateTimeOffset now = DateTimeOffset.Parse("2026-04-29T00:00:00Z", CultureInfo.InvariantCulture);
        string relativePath = "Servers/server-a/Assets/icons/sword.png";
        string fullPath = Path.Combine(pathPolicy.RootPath, "Servers", "server-a", "Assets", "icons", "sword.png");
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, [1, 2, 3]);

        var entry = new ContentCacheEntry(
            "asset/server-a/icons/sword",
            relativePath,
            ContentCacheEntryKind.Asset,
            3,
            now);
        Assert.True(index.TryUpsert(entry, out string error), error);

        ContentCacheQuotaPlan plan = ContentCacheQuotaPlanner.PlanEviction(index.Entries, quotaBytes: 1, targetFillRatio: 0.0);
        var executor = new ContentCacheEvictionExecutor(pathPolicy, index);
        IReadOnlyList<ContentCacheEvictionResult> results = executor.Execute(plan);

        Assert.Equal(1, results.Count);
        Assert.True(results[0].DeletedFile, results[0].Error);
        Assert.True(results[0].RemovedMetadata, results[0].Error);
        Assert.True(results[0].Error is null, results[0].Error);
        Assert.False(File.Exists(fullPath));
        Assert.False(index.TryGet(entry.CacheKey, out _));

        var pinned = new ContentCacheEntry(
            "asset/pinned",
            "Servers/server-a/Assets/icons/pinned.png",
            ContentCacheEntryKind.Asset,
            10,
            now,
            Pinned: true);
        Assert.True(index.TryUpsert(pinned, out error), error);
        var badPlan = new ContentCacheQuotaPlan(10, 1, 0, [pinned]);
        results = executor.Execute(badPlan);
        Assert.Equal(1, results.Count);
        Assert.False(results[0].DeletedFile);
        Assert.False(results[0].RemovedMetadata);
        Assert.True(results[0].Error?.Contains("Pinned", StringComparison.Ordinal) == true, results[0].Error);
        Assert.True(index.TryGet(pinned.CacheKey, out _));
    }

    private static void UiSchemaCacheStore_WritesReadsIndexesAndRemovesSchemaBytes()
    {
        Assert.True(ServerContentIdentity.TryCreate("local-dev", out ServerContentIdentity server, out string error), error);
        byte[] bytes = [10, 20, 30, 40];
        string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        Assert.True(UiSchemaCacheKey.TryCreate(server, "login", 1, hash, out UiSchemaCacheKey key, out error), error);

        string root = Path.Combine(Path.GetTempPath(), "ultrasim-schema-cache-tests", Guid.NewGuid().ToString("N"));
        var pathPolicy = new ClientContentCachePathPolicy(root);
        var index = new ContentCacheIndex();
        var store = new UiSchemaCacheStore(pathPolicy, index);
        DateTimeOffset writtenAt = DateTimeOffset.Parse("2026-04-29T00:00:00Z", CultureInfo.InvariantCulture);
        DateTimeOffset readAt = writtenAt.AddMinutes(5);

        Assert.False(store.Has(key));
        Assert.True(store.TryWrite(key, bytes, writtenAt, pinned: true, out error), error);
        Assert.True(store.Has(key));
        Assert.True(File.Exists(pathPolicy.GetSchemaPath(key)));

        string cacheKey = UiSchemaCacheStore.CreateCacheKey(key);
        Assert.True(index.TryGet(cacheKey, out ContentCacheEntry entry));
        Assert.Equal(ContentCacheEntryKind.Schema, entry.Kind);
        Assert.Equal(bytes.Length, entry.SizeBytes);
        Assert.Equal(writtenAt, entry.LastAccessUtc);
        Assert.True(entry.Pinned);
        Assert.True(entry.RelativePath.StartsWith("Servers/", StringComparison.Ordinal), entry.RelativePath);

        Assert.True(store.TryRead(key, readAt, out byte[] readBytes, out error), error);
        Assert.SequenceEqual(bytes, readBytes);
        Assert.True(index.TryGet(cacheKey, out entry));
        Assert.Equal(readAt, entry.LastAccessUtc);

        Assert.True(store.Remove(key));
        Assert.False(File.Exists(pathPolicy.GetSchemaPath(key)));
        Assert.False(index.TryGet(cacheKey, out _));
    }

    private static void UiSchemaCacheStore_RejectsHashMismatchedSchemaBytes()
    {
        Assert.True(ServerContentIdentity.TryCreate("local-dev", out ServerContentIdentity server, out string error), error);
        string wrongHash = new string('0', 64);
        Assert.True(UiSchemaCacheKey.TryCreate(server, "login", 1, wrongHash, out UiSchemaCacheKey key, out error), error);

        string root = Path.Combine(Path.GetTempPath(), "ultrasim-schema-cache-tests", Guid.NewGuid().ToString("N"));
        var pathPolicy = new ClientContentCachePathPolicy(root);
        var index = new ContentCacheIndex();
        var store = new UiSchemaCacheStore(pathPolicy, index);

        Assert.False(store.TryWrite(key, [1, 2, 3, 4], DateTimeOffset.UtcNow, pinned: false, out error));
        Assert.Equal("Asset SHA-256 hash mismatch.", error);
        Assert.False(File.Exists(pathPolicy.GetSchemaPath(key)));
        Assert.Equal(0, index.Count);
    }

    private static void ClientUiPanelRegistry_OpensColdPanelCachesSchemaAndReopensHot()
    {
        UiSchemaDocument schema = CreateClientUiSchema();
        UiModelDocument model = CreateClientUiModel(schema);
        var registry = new ClientUiPanelRegistry();

        var coldOpen = new UiPanelOpenDocument("panel/status/1", schema.Identity, schema.CatalogIdentity, model, schema);
        ClientUiPanelOpenResult cold = registry.OpenPanel(coldOpen, rendererCapabilities: CreateClientRendererCapabilities());
        Assert.Equal(ClientUiPanelOpenStatus.ColdOpened, cold.Status);
        Assert.True(cold.Opened);
        Assert.True(cold.Panel is not null);
        Assert.Equal(1, registry.Count);
        Assert.Equal(schema.Metadata, cold.Panel!.Metadata);
        Assert.Equal(schema, cold.Panel.Schema);

        var hotOpen = new UiPanelOpenDocument("panel/status/2", schema.Identity, schema.CatalogIdentity, model, SchemaDefinition: null);
        ClientUiPanelOpenResult hot = registry.OpenPanel(hotOpen, rendererCapabilities: CreateClientRendererCapabilities());
        Assert.Equal(ClientUiPanelOpenStatus.HotOpened, hot.Status);
        Assert.True(hot.Panel is not null);
        Assert.Equal(2, registry.Count);
        Assert.True(registry.TryGetPanel("panel/status/2", out ClientUiPanelInstance panel));
        Assert.Equal(schema.Identity, panel.SchemaIdentity);
        Assert.Equal(schema, panel.Schema);
        Assert.True(hot.Panel!.OpenSequence > cold.Panel.OpenSequence);
    }

    private static void ClientUiPanelRegistry_ReportsMissingAndSupersededDefinitionsWithoutOpening()
    {
        UiSchemaDocument schemaV1 = CreateClientUiSchema();
        UiSchemaDocument schemaV2 = CreateClientSchemaRevision(schemaV1, 2, 'd');
        UiModelDocument modelV2 = CreateClientUiModel(schemaV2);
        var registry = new ClientUiPanelRegistry();

        var missingOpen = new UiPanelOpenDocument(
            "panel/status/missing",
            schemaV1.Identity,
            schemaV1.CatalogIdentity,
            CreateClientUiModel(schemaV1),
            SchemaDefinition: null);
        ClientUiPanelOpenResult missing = registry.OpenPanel(missingOpen);
        Assert.Equal(ClientUiPanelOpenStatus.MissingSchema, missing.Status);
        Assert.Equal(0, registry.Count);

        UiValidationResult cacheResult = registry.CacheSchema(schemaV1, rendererCapabilities: CreateClientRendererCapabilities());
        Assert.True(cacheResult.IsValid, cacheResult.Error);

        var supersededOpen = new UiPanelOpenDocument(
            "panel/status/superseded",
            schemaV2.Identity,
            schemaV2.CatalogIdentity,
            modelV2,
            SchemaDefinition: null);
        ClientUiPanelOpenResult superseded = registry.OpenPanel(supersededOpen);
        Assert.Equal(ClientUiPanelOpenStatus.SupersededSchema, superseded.Status);
        Assert.Equal(schemaV1.Identity, superseded.SupersededSchemaIdentity!.Value);
        Assert.Equal(0, registry.Count);
    }

    private static void ClientUiPanelRegistry_RequiresCatalogIdentityBeforeCatalogBackedOpen()
    {
        UiCatalogIdentity catalog = CreateClientCatalogIdentity(revision: 1, 'a');
        UiSchemaDocument schema = CreateClientUiSchema(catalog);
        UiModelDocument model = CreateClientUiModel(schema);
        var registry = new ClientUiPanelRegistry();

        var coldOpen = new UiPanelOpenDocument("panel/crafting/1", schema.Identity, catalog, model, schema);
        ClientUiPanelOpenResult missingCatalog = registry.OpenPanel(coldOpen, rendererCapabilities: CreateClientRendererCapabilities());
        Assert.Equal(ClientUiPanelOpenStatus.MissingCatalog, missingCatalog.Status);
        Assert.Equal(0, registry.Count);

        UiValidationResult catalogResult = registry.CacheCatalogIdentity(catalog);
        Assert.True(catalogResult.IsValid, catalogResult.Error);

        var hotOpen = coldOpen with { SchemaDefinition = null };
        ClientUiPanelOpenResult opened = registry.OpenPanel(hotOpen, rendererCapabilities: CreateClientRendererCapabilities());
        Assert.Equal(ClientUiPanelOpenStatus.HotOpened, opened.Status);
        Assert.True(opened.Panel is not null);
        Assert.Equal(catalog, opened.Panel!.CatalogIdentity!.Value);
        Assert.Equal(1, registry.Count);
    }

    private static void ClientUiPanelRegistry_ValidatesActionsAgainstOpenInstanceContext()
    {
        UiSchemaDocument schemaV1 = CreateClientUiSchema();
        UiSchemaDocument schemaV2 = CreateClientSchemaRevision(schemaV1, 2, 'd');
        UiModelDocument model = CreateClientUiModel(schemaV1);
        var registry = new ClientUiPanelRegistry();

        var open = new UiPanelOpenDocument("panel/status/action", schemaV1.Identity, null, model, schemaV1);
        ClientUiPanelOpenResult opened = registry.OpenPanel(open, rendererCapabilities: CreateClientRendererCapabilities());
        Assert.Equal(ClientUiPanelOpenStatus.ColdOpened, opened.Status);
        Assert.True(opened.Panel is not null);

        UiValidationResult supersedeCache = registry.CacheSchema(schemaV2, rendererCapabilities: CreateClientRendererCapabilities());
        Assert.True(supersedeCache.IsValid, supersedeCache.Error);

        var request = new UiActionRequestDocument(
            "panel/status/action",
            schemaV1.Identity,
            CatalogIdentity: null,
            ClientSequence: 10,
            "hud.open_character",
            UiActionContextKind.Panel,
            ContextId: null,
            FieldValues: [UiModelSlotValue.Integer("levelValue", 13)]);
        ClientUiActionRouteResult routed = registry.ValidateActionRequest(request);
        Assert.Equal(ClientUiActionRouteStatus.Validated, routed.Status);
        Assert.True(routed.ReadyToSend);
        Assert.Equal(request, routed.Request!);
        Assert.Equal(opened.Panel!.PanelInstanceId, routed.Panel!.PanelInstanceId);
        Assert.Equal(schemaV1.Identity, routed.Panel.CompatibilityContext.SchemaIdentity);
    }

    private static void ClientUiPanelRegistry_RejectsMissingPanelAndInvalidActionRequests()
    {
        UiSchemaDocument schema = CreateClientUiSchema();
        UiModelDocument model = CreateClientUiModel(schema);
        var registry = new ClientUiPanelRegistry();

        var missingPanelRequest = new UiActionRequestDocument(
            "panel/missing",
            schema.Identity,
            CatalogIdentity: null,
            ClientSequence: 1,
            "hud.open_character",
            UiActionContextKind.Panel,
            ContextId: null,
            FieldValues: []);
        ClientUiActionRouteResult missing = registry.ValidateActionRequest(missingPanelRequest);
        Assert.Equal(ClientUiActionRouteStatus.MissingPanel, missing.Status);

        var open = new UiPanelOpenDocument("panel/status/action", schema.Identity, null, model, schema);
        Assert.Equal(ClientUiPanelOpenStatus.ColdOpened, registry.OpenPanel(open, rendererCapabilities: CreateClientRendererCapabilities()).Status);

        ClientUiActionRouteResult unknown = registry.ValidateActionRequest(missingPanelRequest with
        {
            PanelInstanceId = "panel/status/action",
            ActionId = "missing.action"
        });
        Assert.Equal(ClientUiActionRouteStatus.Rejected, unknown.Status);
        Assert.True(unknown.Error.Contains("unknown action", StringComparison.OrdinalIgnoreCase), unknown.Error);

        ClientUiActionRouteResult wrongContext = registry.ValidateActionRequest(missingPanelRequest with
        {
            PanelInstanceId = "panel/status/action",
            ContextKind = UiActionContextKind.Item
        });
        Assert.Equal(ClientUiActionRouteStatus.Rejected, wrongContext.Status);
        Assert.True(wrongContext.Error.Contains("context kind", StringComparison.OrdinalIgnoreCase), wrongContext.Error);
    }

    private static void ClientUiPanelRegistry_AppliesModelSlotUpdatesAndIgnoresStaleSequences()
    {
        UiSchemaDocument schema = CreateClientUiSchema();
        UiModelDocument model = CreateClientUiModel(schema);
        var registry = new ClientUiPanelRegistry();
        Assert.Equal(
            ClientUiPanelOpenStatus.ColdOpened,
            registry.OpenPanel(
                new UiPanelOpenDocument("panel/status/update", schema.Identity, null, model, schema),
                rendererCapabilities: CreateClientRendererCapabilities()).Status);

        var patch = new UiModelUpdateDocument(
            "panel/status/update",
            schema.Identity,
            CatalogIdentity: null,
            Sequence: 1,
            UiModelUpdateKind.SlotPatch,
            ScopeId: null,
            SlotValues: [UiModelSlotValue.Integer("levelValue", 20)],
            ClearedSlotIds: [],
            RowDeltas: []);
        ClientUiModelUpdateApplyResult applied = registry.ApplyModelUpdate(patch);
        Assert.Equal(ClientUiModelUpdateApplyStatus.Applied, applied.Status);
        Assert.True(applied.Panel is not null);
        Assert.Equal(20L, GetModelValue(applied.Panel!.Model, "levelValue").IntegerValue);
        Assert.Equal(1L, applied.Panel.LastModelUpdateSequence);

        ClientUiModelUpdateApplyResult stale = registry.ApplyModelUpdate(patch with
        {
            SlotValues = [UiModelSlotValue.Integer("levelValue", 21)]
        });
        Assert.Equal(ClientUiModelUpdateApplyStatus.IgnoredStale, stale.Status);
        Assert.True(registry.TryGetPanel("panel/status/update", out ClientUiPanelInstance panel));
        Assert.Equal(20L, GetModelValue(panel.Model, "levelValue").IntegerValue);

        var full = new UiModelUpdateDocument(
            "panel/status/update",
            schema.Identity,
            CatalogIdentity: null,
            Sequence: 2,
            UiModelUpdateKind.FullModel,
            ScopeId: null,
            SlotValues: [UiModelSlotValue.String("titleText", "Fresh")],
            ClearedSlotIds: [],
            RowDeltas: []);
        ClientUiModelUpdateApplyResult fullApplied = registry.ApplyModelUpdate(full);
        Assert.Equal(ClientUiModelUpdateApplyStatus.Applied, fullApplied.Status);
        Assert.Equal("Fresh", GetModelValue(fullApplied.Panel!.Model, "titleText").StringValue ?? string.Empty);
        Assert.False(TryGetModelValue(fullApplied.Panel.Model, "levelValue", out _));
    }

    private static void ClientUiPanelRegistry_AppliesRepeaterRowsSeparateFromFlatModel()
    {
        UiSchemaDocument schema = CreateClientUiSchema();
        UiModelDocument model = CreateClientUiModel(schema);
        var registry = new ClientUiPanelRegistry();
        Assert.Equal(
            ClientUiPanelOpenStatus.ColdOpened,
            registry.OpenPanel(
                new UiPanelOpenDocument("panel/status/rows", schema.Identity, null, model, schema),
                rendererCapabilities: CreateClientRendererCapabilities()).Status);

        var addRows = new UiModelUpdateDocument(
            "panel/status/rows",
            schema.Identity,
            CatalogIdentity: null,
            Sequence: 1,
            UiModelUpdateKind.RepeaterRows,
            ScopeId: null,
            SlotValues: [],
            ClearedSlotIds: [],
            RowDeltas:
            [
                new UiRepeaterRowDelta(
                    "characters",
                    "character/1",
                    UiRepeaterRowUpdateKind.Add,
                    [UiModelSlotValue.String("name", "Ari"), UiModelSlotValue.Integer("level", 4)]),
            ]);
        ClientUiModelUpdateApplyResult added = registry.ApplyModelUpdate(addRows);
        Assert.Equal(ClientUiModelUpdateApplyStatus.Applied, added.Status);
        Assert.Equal("Ari", added.Panel!.RepeaterRows["characters"]["character/1"].Values[0].StringValue ?? string.Empty);
        Assert.Equal(2, added.Panel.RepeaterRows["characters"]["character/1"].Values.Count);
        Assert.Equal("Status", GetModelValue(added.Panel.Model, "titleText").StringValue ?? string.Empty);

        var updateRow = addRows with
        {
            Sequence = 2,
            RowDeltas =
            [
                new UiRepeaterRowDelta(
                    "characters",
                    "character/1",
                    UiRepeaterRowUpdateKind.Update,
                    [UiModelSlotValue.Integer("level", 5)])
            ]
        };
        ClientUiModelUpdateApplyResult updated = registry.ApplyModelUpdate(updateRow);
        Assert.Equal(ClientUiModelUpdateApplyStatus.Applied, updated.Status);
        ClientUiRepeaterRowState row = updated.Panel!.RepeaterRows["characters"]["character/1"];
        Assert.Equal("Ari", GetRowValue(row, "name").StringValue ?? string.Empty);
        Assert.Equal(5L, GetRowValue(row, "level").IntegerValue);

        var removeRow = addRows with
        {
            Sequence = 3,
            RowDeltas =
            [
                new UiRepeaterRowDelta(
                    "characters",
                    "character/1",
                    UiRepeaterRowUpdateKind.Remove,
                    [])
            ]
        };
        ClientUiModelUpdateApplyResult removed = registry.ApplyModelUpdate(removeRow);
        Assert.Equal(ClientUiModelUpdateApplyStatus.Applied, removed.Status);
        Assert.False(removed.Panel!.RepeaterRows.ContainsKey("characters"));
    }

    private static void ClientUiPanelRegistry_RejectsMissingInvalidAndSupersededModelUpdates()
    {
        UiSchemaDocument schema = CreateClientUiSchema();
        UiModelDocument model = CreateClientUiModel(schema);
        var registry = new ClientUiPanelRegistry();

        var missingPanel = new UiModelUpdateDocument(
            "panel/missing",
            schema.Identity,
            CatalogIdentity: null,
            Sequence: 1,
            UiModelUpdateKind.SlotPatch,
            ScopeId: null,
            SlotValues: [UiModelSlotValue.Integer("levelValue", 20)],
            ClearedSlotIds: [],
            RowDeltas: []);
        Assert.Equal(ClientUiModelUpdateApplyStatus.MissingPanel, registry.ApplyModelUpdate(missingPanel).Status);

        Assert.Equal(
            ClientUiPanelOpenStatus.ColdOpened,
            registry.OpenPanel(
                new UiPanelOpenDocument("panel/status/update", schema.Identity, null, model, schema),
                rendererCapabilities: CreateClientRendererCapabilities()).Status);

        ClientUiModelUpdateApplyResult invalid = registry.ApplyModelUpdate(missingPanel with
        {
            PanelInstanceId = "panel/status/update",
            SlotValues = [UiModelSlotValue.Integer("unknownSlot", 20)]
        });
        Assert.Equal(ClientUiModelUpdateApplyStatus.Rejected, invalid.Status);
        Assert.True(invalid.Error.Contains("unknown slot", StringComparison.OrdinalIgnoreCase), invalid.Error);

        var superseded = missingPanel with
        {
            PanelInstanceId = "panel/status/update",
            Sequence = 2,
            Kind = UiModelUpdateKind.SchemaSuperseded,
            SlotValues = []
        };
        ClientUiModelUpdateApplyResult result = registry.ApplyModelUpdate(superseded);
        Assert.Equal(ClientUiModelUpdateApplyStatus.SchemaSuperseded, result.Status);
        Assert.True(registry.TryGetPanel("panel/status/update", out ClientUiPanelInstance panel));
        Assert.Equal(2L, panel.LastModelUpdateSequence);
    }

    private static void ClientUiStateBindingResolverRegistry_RegistersAndResolvesBySource()
    {
        var registry = new ClientUiStateBindingResolverRegistry();
        var resolver = new FakeStateBindingResolver("EntityState", UiModelSlotValue.Integer("healthText", 42));

        Assert.True(registry.TryRegister(resolver, out string error), error);
        Assert.False(registry.TryRegister(resolver, out error));
        Assert.True(error.Contains("already registered", StringComparison.Ordinal), error);

        var binding = new UiStateBindingDescriptor(
            "healthText",
            "EntityState",
            "entity:local",
            "Health.Current",
            new UiStateBindingFallback(
                UiBindingFallbackBehavior.Stale,
                UiBindingFallbackBehavior.Hidden,
                UiBindingFallbackBehavior.Stale,
                1500));
        Assert.True(registry.TryResolve(binding, out UiModelSlotValue value, out error), error);
        Assert.Equal("healthText", value.SlotId);
        Assert.Equal(42L, value.IntegerValue);

        var missing = binding with { SourceId = "PartyState" };
        Assert.False(registry.TryResolve(missing, out _, out error));
        Assert.True(error.Contains("No UI state binding resolver", StringComparison.Ordinal), error);
    }

    private static void ServerPatchManifestCacheStore_WritesReadsIndexesAndRemovesManifest()
    {
        Assert.True(ServerContentIdentity.TryCreate("local-dev", out ServerContentIdentity server, out string error), error);
        var manifest = new ServerPatchManifest(
            "patch-1",
            3,
            [
                new ServerPatchManifestEntry("icons/items/sword.png", 128, new string('a', 64)),
                new ServerPatchManifestEntry("ui/login.uischema", 256, new string('b', 64))
            ]);

        string root = Path.Combine(Path.GetTempPath(), "ultrasim-patch-manifest-cache-tests", Guid.NewGuid().ToString("N"));
        var pathPolicy = new ClientContentCachePathPolicy(root);
        var index = new ContentCacheIndex();
        var store = new ServerPatchManifestCacheStore(pathPolicy, index);
        DateTimeOffset writtenAt = DateTimeOffset.Parse("2026-04-29T00:00:00Z", CultureInfo.InvariantCulture);
        DateTimeOffset readAt = writtenAt.AddMinutes(4);

        Assert.False(store.Has(server, manifest.PatchId, manifest.Revision));
        Assert.True(store.TryWrite(server, manifest, writtenAt, pinned: true, out error), error);
        Assert.True(store.Has(server, manifest.PatchId, manifest.Revision));
        Assert.True(store.TryGetPath(server, manifest.PatchId, manifest.Revision, out string manifestPath, out error), error);
        Assert.True(File.Exists(manifestPath));

        string cacheKey = ServerPatchManifestCacheStore.CreateCacheKey(server, manifest.PatchId, manifest.Revision);
        Assert.True(index.TryGet(cacheKey, out ContentCacheEntry entry));
        Assert.Equal(ContentCacheEntryKind.PatchManifest, entry.Kind);
        Assert.True(entry.SizeBytes > 0);
        Assert.Equal(writtenAt, entry.LastAccessUtc);
        Assert.True(entry.Pinned);
        Assert.True(entry.RelativePath.EndsWith(".json", StringComparison.Ordinal), entry.RelativePath);

        Assert.True(store.TryRead(server, manifest.PatchId, manifest.Revision, readAt, out ServerPatchManifest readManifest, out error), error);
        Assert.Equal(manifest.PatchId, readManifest.PatchId);
        Assert.Equal(manifest.Revision, readManifest.Revision);
        Assert.Equal(2, readManifest.Entries.Count);
        Assert.Equal("icons/items/sword.png", readManifest.Entries[0].RelativePath);
        Assert.True(index.TryGet(cacheKey, out entry));
        Assert.Equal(readAt, entry.LastAccessUtc);

        Assert.True(store.Remove(server, manifest.PatchId, manifest.Revision));
        Assert.False(File.Exists(manifestPath));
        Assert.False(index.TryGet(cacheKey, out _));
    }

    private static void ServerPatchManifestCacheStore_RejectsInvalidAndMismatchedManifests()
    {
        Assert.True(ServerContentIdentity.TryCreate("local-dev", out ServerContentIdentity server, out string error), error);
        string root = Path.Combine(Path.GetTempPath(), "ultrasim-patch-manifest-cache-tests", Guid.NewGuid().ToString("N"));
        var pathPolicy = new ClientContentCachePathPolicy(root);
        var index = new ContentCacheIndex();
        var store = new ServerPatchManifestCacheStore(pathPolicy, index);

        var executable = new ServerPatchManifest(
            "patch-evil",
            1,
            [new ServerPatchManifestEntry("scripts/evil.gd", 1, new string('a', 64))]);
        Assert.False(store.TryWrite(server, executable, DateTimeOffset.UtcNow, pinned: false, out error));
        Assert.True(error.Contains("executable/script", StringComparison.Ordinal), error);
        Assert.Equal(0, index.Count);

        Assert.True(store.TryGetPath(server, "patch-1", 3, out string manifestPath, out error), error);
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath) ?? ".");
        File.WriteAllText(
            manifestPath,
            """
            {
              "PatchId": "patch-other",
              "Revision": 3,
              "Entries": []
            }
            """);

        Assert.False(store.TryRead(server, "patch-1", 3, DateTimeOffset.UtcNow, out _, out error));
        Assert.Equal("Cached patch manifest identity does not match requested key.", error);
    }

    private static void ServerAssetCacheStore_WritesReadsIndexesAndRemovesManifestBackedAssets()
    {
        Assert.True(ServerContentIdentity.TryCreate("local-dev", out ServerContentIdentity server, out string error), error);
        byte[] bytes = [3, 1, 4, 1, 5];
        string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var manifestEntry = new ServerPatchManifestEntry("icons/items/sword.png", bytes.Length, hash);

        string root = Path.Combine(Path.GetTempPath(), "ultrasim-asset-cache-tests", Guid.NewGuid().ToString("N"));
        var pathPolicy = new ClientContentCachePathPolicy(root);
        var index = new ContentCacheIndex();
        var store = new ServerAssetCacheStore(pathPolicy, index);
        DateTimeOffset writtenAt = DateTimeOffset.Parse("2026-04-29T00:00:00Z", CultureInfo.InvariantCulture);
        DateTimeOffset readAt = writtenAt.AddMinutes(3);

        Assert.False(store.Has(server, manifestEntry));
        Assert.True(store.TryWrite(server, manifestEntry, bytes, writtenAt, pinned: false, out error), error);
        Assert.True(store.Has(server, manifestEntry));
        Assert.True(pathPolicy.TryGetServerAssetPath(server, manifestEntry.RelativePath, out string assetPath, out error), error);
        Assert.True(File.Exists(assetPath));

        string cacheKey = ServerAssetCacheStore.CreateCacheKey(server, "icons/items/sword.png", hash);
        Assert.True(index.TryGet(cacheKey, out ContentCacheEntry entry));
        Assert.Equal(ContentCacheEntryKind.Asset, entry.Kind);
        Assert.Equal(bytes.Length, entry.SizeBytes);
        Assert.Equal(writtenAt, entry.LastAccessUtc);
        Assert.False(entry.Pinned);
        Assert.True(entry.RelativePath.EndsWith("Assets/icons/items/sword.png", StringComparison.Ordinal), entry.RelativePath);

        Assert.True(store.TryRead(server, manifestEntry, readAt, out byte[] readBytes, out error), error);
        Assert.SequenceEqual(bytes, readBytes);
        Assert.True(index.TryGet(cacheKey, out entry));
        Assert.Equal(readAt, entry.LastAccessUtc);

        Assert.True(store.Remove(server, manifestEntry));
        Assert.False(File.Exists(assetPath));
        Assert.False(index.TryGet(cacheKey, out _));
    }

    private static void ServerAssetCacheStore_RejectsSizeHashAndUnsafePathMismatches()
    {
        Assert.True(ServerContentIdentity.TryCreate("local-dev", out ServerContentIdentity server, out string error), error);
        byte[] bytes = [1, 2, 3, 4];
        string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        string root = Path.Combine(Path.GetTempPath(), "ultrasim-asset-cache-tests", Guid.NewGuid().ToString("N"));
        var pathPolicy = new ClientContentCachePathPolicy(root);
        var index = new ContentCacheIndex();
        var store = new ServerAssetCacheStore(pathPolicy, index);

        var wrongSize = new ServerPatchManifestEntry("icons/items/sword.png", bytes.Length + 1, hash);
        Assert.False(store.TryWrite(server, wrongSize, bytes, DateTimeOffset.UtcNow, pinned: false, out error));
        Assert.True(error.Contains("size mismatch", StringComparison.OrdinalIgnoreCase), error);

        var wrongHash = new ServerPatchManifestEntry("icons/items/sword.png", bytes.Length, new string('0', 64));
        Assert.False(store.TryWrite(server, wrongHash, bytes, DateTimeOffset.UtcNow, pinned: false, out error));
        Assert.Equal("Asset SHA-256 hash mismatch.", error);

        var unsafePath = new ServerPatchManifestEntry("../outside.bin", bytes.Length, hash);
        Assert.False(store.TryWrite(server, unsafePath, bytes, DateTimeOffset.UtcNow, pinned: false, out error));
        Assert.Equal("Content path cannot contain traversal segments.", error);
        Assert.Equal(0, index.Count);
    }

    private static void ClientPredictionStore_RecordsLooksUpAndRemovesAcknowledgedStates()
    {
        var store = new ClientPredictionStore<ClientMovementState>();
        store.Record(1, 10, new ClientMovementState(1, 2, 3, 4));
        store.Record(2, 11, new ClientMovementState(5, 6, 7, 8));

        Assert.Equal(2, store.Count);
        Assert.True(store.TryGet(2, out PredictedStateSample<ClientMovementState> second));
        Assert.Equal((ushort)2, second.Sequence);
        Assert.Equal(new ClientMovementState(5, 6, 7, 8), second.State);

        int removed = store.RemoveAcknowledgedUpTo(1);
        Assert.Equal(1, removed);
        Assert.Equal(1, store.Count);
        Assert.False(store.TryGet(1, out _));
        Assert.True(store.TryGet(2, out _));
    }

    private static void ClientInterpolationBuffer_SamplesInterpolatedAndLatestState()
    {
        var buffer = new ClientInterpolationBuffer<ClientMovementState>();
        buffer.AddSample(100, new ClientMovementState(0, 0, 0, 0));
        buffer.AddSample(110, new ClientMovementState(10, 20, 0, 0));

        Assert.True(buffer.TrySample(105, static (in ClientMovementState a, in ClientMovementState b, float alpha) =>
        {
            return new ClientMovementState(
                a.PositionX + (b.PositionX - a.PositionX) * alpha,
                a.PositionY + (b.PositionY - a.PositionY) * alpha,
                a.VelocityX + (b.VelocityX - a.VelocityX) * alpha,
                a.VelocityY + (b.VelocityY - a.VelocityY) * alpha);
        }, out ClientMovementState sampled));

        Assert.Equal(new ClientMovementState(5, 10, 0, 0), sampled);

        Assert.True(buffer.TrySample(999, static (in ClientMovementState a, in ClientMovementState b, float alpha) => a, out ClientMovementState latest));
        Assert.Equal(new ClientMovementState(10, 20, 0, 0), latest);
    }

    private static void MovementPackets_RoundTripExpectedFields()
    {
        var input = new MovementInputPacket(12, 345, -100, 200, 7);
        ImmutablePayloadBuffer encodedInput = PacketCodec.Encode(input);
        Assert.True(PacketCodec.TryDecodeEnvelope(encodedInput.Span, out PacketEnvelopeHeader inputHeader, out ReadOnlySpan<byte> inputPayload));
        Assert.Equal(MovementInputPacket.PacketId, inputHeader.PacketId);
        var inputReader = new BitReader(inputPayload);
        MovementInputPacket decodedInput = MovementInputPacket.Deserialize(ref inputReader);
        Assert.Equal(input, decodedInput);

        var state = new MovementStatePacket(77, 500, 12, 1.25f, 2.5f, 3.75f, 4.5f);
        ImmutablePayloadBuffer encodedState = PacketCodec.Encode(state);
        Assert.True(PacketCodec.TryDecodeEnvelope(encodedState.Span, out PacketEnvelopeHeader stateHeader, out ReadOnlySpan<byte> statePayload));
        Assert.Equal(MovementStatePacket.PacketId, stateHeader.PacketId);
        var stateReader = new BitReader(statePayload);
        MovementStatePacket decodedState = MovementStatePacket.Deserialize(ref stateReader);
        Assert.Equal(state, decodedState);
    }

    private static void ClientObserverStateBridge_AppliesNewestRealtimeFramesOnly()
    {
        int applyCount = 0;
        ulong lastItemKey = 0;
        ulong lastSequence = 0;
        byte[]? lastPayload = null;

        var bridge = new ClientObserverStateBridge(
            new ConnectionId(7),
            (itemKey, sequence, payload) =>
            {
                applyCount++;
                lastItemKey = itemKey;
                lastSequence = sequence;
                lastPayload = payload.Span.ToArray();
            });

        ImmutablePayloadBuffer frame1 = PacketCodec.Encode(new RealtimeStateFramePacket(10, 1001, ImmutablePayloadBuffer.CopyFrom([1, 2])));
        ImmutablePayloadBuffer frame2 = PacketCodec.Encode(new RealtimeStateFramePacket(9, 1001, ImmutablePayloadBuffer.CopyFrom([3, 4])));
        ImmutablePayloadBuffer frame3 = PacketCodec.Encode(new RealtimeStateFramePacket(11, 1001, ImmutablePayloadBuffer.CopyFrom([5, 6])));
        InboundDatagram[] inbound =
        [
            new(new ConnectionId(7), NetworkPlane.State, PacketClass.Realtime, ChannelType.UnreliableSequenced, frame1.Memory),
            new(new ConnectionId(7), NetworkPlane.State, PacketClass.Realtime, ChannelType.UnreliableSequenced, frame2.Memory),
            new(new ConnectionId(7), NetworkPlane.State, PacketClass.Realtime, ChannelType.UnreliableSequenced, frame3.Memory),
        ];

        int processed = bridge.ProcessInbound(inbound);

        Assert.Equal(3, processed);
        Assert.Equal(3, bridge.LastReceivedFrameCount);
        Assert.Equal(2, bridge.LastAppliedFrameCount);
        Assert.Equal(2, applyCount);
        Assert.Equal((ulong)1001, lastItemKey);
        Assert.Equal((ulong)11, lastSequence);
        Assert.True(lastPayload is not null);
        Assert.Equal((byte)5, lastPayload![0]);
        Assert.Equal(1, bridge.LatestItemCount);
    }

    private static void ClientObserverStateBridge_AppliesRealtimeStateBatchFrames()
    {
        int applyCount = 0;
        ulong lastItemKey = 0;
        ulong lastSequence = 0;
        byte[]? lastPayload = null;

        var bridge = new ClientObserverStateBridge(
            new ConnectionId(7),
            (itemKey, sequence, payload) =>
            {
                applyCount++;
                lastItemKey = itemKey;
                lastSequence = sequence;
                lastPayload = payload.Span.ToArray();
            });

        RealtimeStateBatchItem[] items =
        [
            new(10, 1001, ImmutablePayloadBuffer.CopyFrom([1, 2])),
            new(11, 1002, ImmutablePayloadBuffer.CopyFrom([3, 4]))
        ];
        ImmutablePayloadBuffer batch = PacketCodec.Encode(new RealtimeStateBatchPacket(items));
        InboundDatagram[] inbound =
        [
            new(new ConnectionId(7), NetworkPlane.State, PacketClass.Realtime, ChannelType.UnreliableSequenced, batch.Memory),
        ];

        int processed = bridge.ProcessInbound(inbound);

        Assert.Equal(1, processed);
        Assert.Equal(2, bridge.LastReceivedFrameCount);
        Assert.Equal(2, bridge.LastAppliedFrameCount);
        Assert.Equal(2, applyCount);
        Assert.Equal((ulong)1002, lastItemKey);
        Assert.Equal((ulong)11, lastSequence);
        Assert.True(lastPayload is not null);
        Assert.Equal((byte)3, lastPayload![0]);
        Assert.Equal(2, bridge.LatestItemCount);
    }

    private static void ClientObserverEventBridge_AppliesIncreasingReliableEventFramesOnly()
    {
        int applyCount = 0;
        ulong lastSequence = 0;
        byte[]? lastPayload = null;

        var bridge = new ClientObserverEventBridge(
            new ConnectionId(9),
            (sequence, payload) =>
            {
                applyCount++;
                lastSequence = sequence;
                lastPayload = payload.Span.ToArray();
            });

        ImmutablePayloadBuffer frame1 = PacketCodec.Encode(new ReliableEventFramePacket(10, ImmutablePayloadBuffer.CopyFrom([1, 2])));
        ImmutablePayloadBuffer frame2 = PacketCodec.Encode(new ReliableEventFramePacket(9, ImmutablePayloadBuffer.CopyFrom([3, 4])));
        ImmutablePayloadBuffer frame3 = PacketCodec.Encode(new ReliableEventFramePacket(11, ImmutablePayloadBuffer.CopyFrom([5, 6])));
        InboundDatagram[] inbound =
        [
            new(new ConnectionId(9), NetworkPlane.State, PacketClass.ReliableEvent, ChannelType.ReliableOrdered, frame1.Memory),
            new(new ConnectionId(9), NetworkPlane.State, PacketClass.ReliableEvent, ChannelType.ReliableOrdered, frame2.Memory),
            new(new ConnectionId(9), NetworkPlane.State, PacketClass.ReliableEvent, ChannelType.ReliableOrdered, frame3.Memory),
        ];

        int processed = bridge.ProcessInbound(inbound);

        Assert.Equal(3, processed);
        Assert.Equal(3, bridge.LastReceivedEventCount);
        Assert.Equal(2, bridge.LastAppliedEventCount);
        Assert.Equal(2, applyCount);
        Assert.Equal((ulong)11, lastSequence);
        Assert.True(lastPayload is not null);
        Assert.Equal((byte)5, lastPayload![0]);
        Assert.True(bridge.TryDequeueAckPacket(out ReliableEventAckPacket ack));
        Assert.Equal((ulong)11, ack.Sequence);
        Assert.False(bridge.TryDequeueAckPacket(out _));
    }

    private static void ClientObserverStateBridge_AppliesFullBaselineAndResetsLocalState()
    {
        int applyCount = 0;
        int resetCount = 0;
        ulong lastItemKey = 0;
        ulong lastSequence = 0;
        var bridge = new ClientObserverStateBridge(
            new ConnectionId(7),
            (itemKey, sequence, _) =>
            {
                applyCount++;
                lastItemKey = itemKey;
                lastSequence = sequence;
            },
            () => resetCount++);

        InboundDatagram[] inbound =
        [
            new(new ConnectionId(7), NetworkPlane.State, PacketClass.Bulk, ChannelType.ReliableOrdered, PacketCodec.Encode(new StateBaselineBeginPacket(new StateBaselineDescriptor(5, 22, 41, 2))).Memory),
            new(new ConnectionId(7), NetworkPlane.State, PacketClass.Bulk, ChannelType.ReliableOrdered, PacketCodec.Encode(new StateBaselineFramePacket(5, 1001, 21, ImmutablePayloadBuffer.CopyFrom([1]))).Memory),
            new(new ConnectionId(7), NetworkPlane.State, PacketClass.Bulk, ChannelType.ReliableOrdered, PacketCodec.Encode(new StateBaselineFramePacket(5, 1002, 22, ImmutablePayloadBuffer.CopyFrom([2]))).Memory),
            new(new ConnectionId(7), NetworkPlane.State, PacketClass.Bulk, ChannelType.ReliableOrdered, PacketCodec.Encode(new StateBaselineCompletePacket(5)).Memory),
        ];

        int processed = bridge.ProcessInbound(inbound);

        Assert.Equal(4, processed);
        Assert.Equal(1, resetCount);
        Assert.Equal(2, bridge.LastReceivedBaselineFrameCount);
        Assert.Equal(2, bridge.LastAppliedBaselineFrameCount);
        Assert.Equal(2, applyCount);
        Assert.Equal((ulong)1002, lastItemKey);
        Assert.Equal((ulong)22, lastSequence);
        Assert.Equal(2, bridge.LatestItemCount);
        Assert.True(bridge.TryDequeueCompletedBaseline(out StateBaselineDescriptor baseline));
        Assert.Equal((uint)5, baseline.BaselineId);
        Assert.Equal((ulong)41, baseline.LastOrderedEventSequence);
        Assert.False(bridge.TryDequeueCompletedBaseline(out _));
    }

    private static void ClientObserverStateBridge_AppliesDeltaFramesAfterCompletedBaseline()
    {
        int applyCount = 0;
        ulong lastItemKey = 0;
        ulong lastSequence = 0;
        var bridge = new ClientObserverStateBridge(
            new ConnectionId(7),
            (itemKey, sequence, _) =>
            {
                applyCount++;
                lastItemKey = itemKey;
                lastSequence = sequence;
            });

        InboundDatagram[] inbound =
        [
            new(new ConnectionId(7), NetworkPlane.State, PacketClass.Bulk, ChannelType.ReliableOrdered, PacketCodec.Encode(new StateBaselineBeginPacket(new StateBaselineDescriptor(5, 22, 41, 1))).Memory),
            new(new ConnectionId(7), NetworkPlane.State, PacketClass.Bulk, ChannelType.ReliableOrdered, PacketCodec.Encode(new StateBaselineFramePacket(5, 1001, 21, ImmutablePayloadBuffer.CopyFrom([1]))).Memory),
            new(new ConnectionId(7), NetworkPlane.State, PacketClass.Bulk, ChannelType.ReliableOrdered, PacketCodec.Encode(new StateBaselineCompletePacket(5)).Memory),
            new(new ConnectionId(7), NetworkPlane.State, PacketClass.Bulk, ChannelType.ReliableOrdered, PacketCodec.Encode(new StateDeltaFramePacket(5, 1001, 23, ImmutablePayloadBuffer.CopyFrom([2]))).Memory),
        ];

        int processed = bridge.ProcessInbound(inbound);

        Assert.Equal(4, processed);
        Assert.Equal(1, bridge.LastReceivedBaselineFrameCount);
        Assert.Equal(1, bridge.LastAppliedBaselineFrameCount);
        Assert.Equal(1, bridge.LastReceivedDeltaFrameCount);
        Assert.Equal(1, bridge.LastAppliedDeltaFrameCount);
        Assert.Equal(2, applyCount);
        Assert.Equal((ulong)1001, lastItemKey);
        Assert.Equal((ulong)23, lastSequence);
        Assert.Equal(1, bridge.LatestItemCount);
    }

    private static void ClientObserverEventBridge_BuffersOutOfOrderFramesUntilGapCloses()
    {
        var applied = new List<ulong>();
        var bridge = new ClientObserverEventBridge(
            new ConnectionId(9),
            (sequence, _) => applied.Add(sequence));

        InboundDatagram[] inbound =
        [
            new(new ConnectionId(9), NetworkPlane.State, PacketClass.ReliableEvent, ChannelType.ReliableOrdered, PacketCodec.Encode(new ReliableEventFramePacket(10, ImmutablePayloadBuffer.CopyFrom([1]))).Memory),
            new(new ConnectionId(9), NetworkPlane.State, PacketClass.ReliableEvent, ChannelType.ReliableOrdered, PacketCodec.Encode(new ReliableEventFramePacket(12, ImmutablePayloadBuffer.CopyFrom([2]))).Memory),
            new(new ConnectionId(9), NetworkPlane.State, PacketClass.ReliableEvent, ChannelType.ReliableOrdered, PacketCodec.Encode(new ReliableEventFramePacket(11, ImmutablePayloadBuffer.CopyFrom([3]))).Memory),
        ];

        int processed = bridge.ProcessInbound(inbound);

        Assert.Equal(3, processed);
        Assert.Equal(3, bridge.LastReceivedEventCount);
        Assert.Equal(3, bridge.LastAppliedEventCount);
        Assert.Equal(0, bridge.BufferedEventCount);
        Assert.SequenceEqual([10UL, 11UL, 12UL], applied);
        Assert.True(bridge.TryDequeueAckPacket(out ReliableEventAckPacket ack));
        Assert.Equal((ulong)12, ack.Sequence);
    }

    private static void ClientObserverEventBridge_CountsDroppedOutOfOrderFramesWhenBufferIsFull()
    {
        var bridge = new ClientObserverEventBridge(new ConnectionId(9), maxBufferedEvents: 1);

        InboundDatagram[] inbound =
        [
            new(new ConnectionId(9), NetworkPlane.State, PacketClass.ReliableEvent, ChannelType.ReliableOrdered, PacketCodec.Encode(new ReliableEventFramePacket(10, ImmutablePayloadBuffer.CopyFrom([1]))).Memory),
            new(new ConnectionId(9), NetworkPlane.State, PacketClass.ReliableEvent, ChannelType.ReliableOrdered, PacketCodec.Encode(new ReliableEventFramePacket(12, ImmutablePayloadBuffer.CopyFrom([2]))).Memory),
            new(new ConnectionId(9), NetworkPlane.State, PacketClass.ReliableEvent, ChannelType.ReliableOrdered, PacketCodec.Encode(new ReliableEventFramePacket(14, ImmutablePayloadBuffer.CopyFrom([3]))).Memory),
        ];

        int processed = bridge.ProcessInbound(inbound);

        Assert.Equal(3, processed);
        Assert.Equal(1, bridge.BufferedEventCount);
        Assert.Equal(1L, bridge.DroppedBufferedEventCount);
    }

    private static void ClientObserverEventBridge_AllowSequenceGaps_AppliesScopedFanoutGaps()
    {
        var applied = new List<ulong>();
        var bridge = new ClientObserverEventBridge(
            new ConnectionId(9),
            (sequence, _) => applied.Add(sequence))
        {
            AllowSequenceGaps = true,
        };

        InboundDatagram[] inbound =
        [
            new(new ConnectionId(9), NetworkPlane.State, PacketClass.ReliableEvent, ChannelType.ReliableOrdered, PacketCodec.Encode(new ReliableEventFramePacket(10, ImmutablePayloadBuffer.CopyFrom([1]))).Memory),
            new(new ConnectionId(9), NetworkPlane.State, PacketClass.ReliableEvent, ChannelType.ReliableOrdered, PacketCodec.Encode(new ReliableEventFramePacket(12, ImmutablePayloadBuffer.CopyFrom([2]))).Memory),
        ];

        Assert.Equal(2, bridge.ProcessInbound(inbound));
        Assert.Equal(2, bridge.LastAppliedEventCount);
        Assert.Equal(0, bridge.BufferedEventCount);
        Assert.SequenceEqual([10UL, 12UL], applied);
    }

    private static void ClientObserverEventBridge_RoutesNestedChatEventsBeforeGenericApply()
    {
        ChatMessageEventPacket? applied = null;
        int genericApplyCount = 0;
        var chat = new ClientChatEventBridge(packet => applied = packet);
        var bridge = ClientObserverEventBridge.CreateSpanFastPath(
            new ConnectionId(9),
            (_, _) => genericApplyCount++);
        bridge.ChatEvents = chat;

        ImmutablePayloadBuffer chatPayload = PacketCodec.Encode(new ChatMessageEventPacket(
            MessageId: 7,
            ChannelId: 1,
            SenderEntityId: 22,
            SenderName: "Player 9",
            Text: "hello",
            ServerTick: 123,
            Flags: ChatMessageFlags.Local | ChatMessageFlags.Overhead));
        ImmutablePayloadBuffer frame = PacketCodec.Encode(new ReliableEventFramePacket(10, chatPayload));
        InboundDatagram[] inbound =
        [
            new(new ConnectionId(9), NetworkPlane.State, PacketClass.ReliableEvent, ChannelType.ReliableOrdered, frame.Memory),
        ];

        Assert.Equal(1, bridge.ProcessInbound(inbound));
        Assert.Equal(1, chat.LastReceivedMessageCount);
        Assert.Equal(1, chat.LastAppliedMessageCount);
        Assert.Equal(7UL, chat.LastMessageId);
        Assert.True(applied.HasValue);
        Assert.Equal("hello", applied.GetValueOrDefault().Text);
        Assert.Equal(0, genericApplyCount);
    }

    private static void ClientMovementSessionHost_Pump_EmitsReliableEventAckForContiguousObserverEvents()
    {
        using var pair = new LoopbackTransportPair();
        pair.Client.Start();
        pair.Server.Start();

        var observerEvents = new ClientObserverEventBridge(new ConnectionId(1));
        var host = new ClientMovementSessionHost(
            pair.Client,
            new ConnectionId(1),
            default,
            observerEvents: observerEvents,
            maxPollBatch: 4);

        pair.Server.EnqueueOutbound(new OutboundDatagram(
            new ConnectionId(1),
            ReliableEventFramePacket.Plane,
            ReliableEventFramePacket.PacketClass,
            ReliableEventFramePacket.Channel,
            PacketCodec.Encode(new ReliableEventFramePacket(20, ImmutablePayloadBuffer.CopyFrom([5, 6])))));
        pair.Server.EnqueueOutbound(new OutboundDatagram(
            new ConnectionId(1),
            ReliableEventFramePacket.Plane,
            ReliableEventFramePacket.PacketClass,
            ReliableEventFramePacket.Channel,
            PacketCodec.Encode(new ReliableEventFramePacket(21, ImmutablePayloadBuffer.CopyFrom([7, 8])))));
        pair.Server.FlushOutbound();

        int inboundCount = host.Pump(1000);
        Assert.Equal(2, inboundCount);
        Assert.Equal((ulong)21, observerEvents.LastAppliedSequence);

        var ackInbound = new InboundDatagram[4];
        int ackCount = pair.Server.PollInbound(ackInbound);
        Assert.Equal(1, ackCount);
        Assert.True(PacketCodec.TryDecodeEnvelope(ackInbound[0].Payload.Span, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> payload));
        Assert.Equal(ReliableEventAckPacket.PacketId, header.PacketId);
        var reader = new BitReader(payload);
        ReliableEventAckPacket ack = ReliableEventAckPacket.Deserialize(ref reader);
        Assert.Equal((ulong)21, ack.Sequence);
    }

    private static void ClientMovementSessionHost_Pump_EmitsStateBaselineAckWhenBaselineCompletes()
    {
        using var pair = new LoopbackTransportPair();
        pair.Client.Start();
        pair.Server.Start();

        var observerState = new ClientObserverStateBridge(new ConnectionId(1));
        var host = new ClientMovementSessionHost(
            pair.Client,
            new ConnectionId(1),
            default,
            observerState: observerState,
            maxPollBatch: 8);

        pair.Server.EnqueueOutbound(new OutboundDatagram(
            new ConnectionId(1),
            StateBaselineBeginPacket.Plane,
            StateBaselineBeginPacket.PacketClass,
            StateBaselineBeginPacket.Channel,
            PacketCodec.Encode(new StateBaselineBeginPacket(new StateBaselineDescriptor(9, 50, 42, 1)))));
        pair.Server.EnqueueOutbound(new OutboundDatagram(
            new ConnectionId(1),
            StateBaselineFramePacket.Plane,
            StateBaselineFramePacket.PacketClass,
            StateBaselineFramePacket.Channel,
            PacketCodec.Encode(new StateBaselineFramePacket(9, 1001, 50, ImmutablePayloadBuffer.CopyFrom([2])))));
        pair.Server.EnqueueOutbound(new OutboundDatagram(
            new ConnectionId(1),
            StateBaselineCompletePacket.Plane,
            StateBaselineCompletePacket.PacketClass,
            StateBaselineCompletePacket.Channel,
            PacketCodec.Encode(new StateBaselineCompletePacket(9))));
        pair.Server.FlushOutbound();

        Assert.Equal(3, host.Pump(1000));

        var ackInbound = new InboundDatagram[4];
        int ackCount = pair.Server.PollInbound(ackInbound);
        Assert.Equal(1, ackCount);
        Assert.True(PacketCodec.TryDecodeEnvelope(ackInbound[0].Payload.Span, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> payload));
        Assert.Equal(StateBaselineAckPacket.PacketId, header.PacketId);
        var reader = new BitReader(payload);
        StateBaselineAckPacket ack = StateBaselineAckPacket.Deserialize(ref reader);
        Assert.Equal((uint)9, ack.Descriptor.BaselineId);
        Assert.Equal((ulong)50, ack.Descriptor.HighestLatestStateSequence);
        Assert.Equal((ulong)42, ack.Descriptor.LastOrderedEventSequence);
    }

    private static void ClientMovementSessionHost_BaselineCompletion_ResetsObserverEventSequenceFloor()
    {
        using var pair = new LoopbackTransportPair();
        pair.Client.Start();
        pair.Server.Start();

        var appliedEvents = new List<ulong>();
        var observerState = new ClientObserverStateBridge(new ConnectionId(1));
        var observerEvents = new ClientObserverEventBridge(
            new ConnectionId(1),
            (sequence, _) => appliedEvents.Add(sequence));
        var host = new ClientMovementSessionHost(
            pair.Client,
            new ConnectionId(1),
            default,
            observerState: observerState,
            observerEvents: observerEvents,
            maxPollBatch: 8);

        pair.Server.EnqueueOutbound(new OutboundDatagram(
            new ConnectionId(1),
            ReliableEventFramePacket.Plane,
            ReliableEventFramePacket.PacketClass,
            ReliableEventFramePacket.Channel,
            PacketCodec.Encode(new ReliableEventFramePacket(10, ImmutablePayloadBuffer.CopyFrom([1])))));
        pair.Server.FlushOutbound();
        Assert.Equal(1, host.Pump(1000));
        Assert.Equal((ulong)10, observerEvents.LastAppliedSequence);

        pair.Server.EnqueueOutbound(new OutboundDatagram(
            new ConnectionId(1),
            StateBaselineBeginPacket.Plane,
            StateBaselineBeginPacket.PacketClass,
            StateBaselineBeginPacket.Channel,
            PacketCodec.Encode(new StateBaselineBeginPacket(new StateBaselineDescriptor(9, 50, 42, 1)))));
        pair.Server.EnqueueOutbound(new OutboundDatagram(
            new ConnectionId(1),
            StateBaselineFramePacket.Plane,
            StateBaselineFramePacket.PacketClass,
            StateBaselineFramePacket.Channel,
            PacketCodec.Encode(new StateBaselineFramePacket(9, 1001, 50, ImmutablePayloadBuffer.CopyFrom([2])))));
        pair.Server.EnqueueOutbound(new OutboundDatagram(
            new ConnectionId(1),
            StateBaselineCompletePacket.Plane,
            StateBaselineCompletePacket.PacketClass,
            StateBaselineCompletePacket.Channel,
            PacketCodec.Encode(new StateBaselineCompletePacket(9))));
        pair.Server.EnqueueOutbound(new OutboundDatagram(
            new ConnectionId(1),
            ReliableEventFramePacket.Plane,
            ReliableEventFramePacket.PacketClass,
            ReliableEventFramePacket.Channel,
            PacketCodec.Encode(new ReliableEventFramePacket(43, ImmutablePayloadBuffer.CopyFrom([3])))));
        pair.Server.FlushOutbound();

        Assert.Equal(4, host.Pump(1002));
        Assert.Equal((ulong)43, observerEvents.LastAppliedSequence);
        Assert.SequenceEqual([10UL, 43UL], appliedEvents);
    }

    private static void ClientMovementNetworkBridge_LoopbackRoundTripsMovementInputAndAuthoritativeState()
    {
        using var pair = new LoopbackTransportPair();
        pair.Client.Start();
        pair.Server.Start();

        var bridge = new ClientMovementNetworkBridge(pair.Client, new ConnectionId(1));
        Assert.True(bridge.TrySendInput(
            new ClientMovementInput(300, -200, 3),
            clientTick: 100,
            predictedState: new ClientMovementState(1, 2, 3, 4),
            out ClientMovementInputSendResult sendResult));
        Assert.Equal((ushort)1, sendResult.Sequence);
        pair.Client.FlushOutbound();

        var inbound = new InboundDatagram[4];
        int received = pair.Server.PollInbound(inbound);
        Assert.Equal(1, received);
        Assert.True(PacketCodec.TryDecodeEnvelope(inbound[0].Payload.Span, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> payload));
        Assert.Equal(MovementInputPacket.PacketId, header.PacketId);
        var reader = new BitReader(payload);
        MovementInputPacket input = MovementInputPacket.Deserialize(ref reader);
        Assert.Equal((ushort)1, input.InputSequence);
        Assert.Equal((uint)100, input.ClientTick);
        Assert.Equal((short)300, input.MoveX);
        Assert.Equal((short)-200, input.MoveY);

        ImmutablePayloadBuffer statePayload = PacketCodec.Encode(new MovementStatePacket(
            NetworkEntityId: 42,
            ServerTick: 200,
            LastProcessedInputSequence: input.InputSequence,
            PositionX: 10f,
            PositionY: 20f,
            VelocityX: 1.5f,
            VelocityY: 2.5f));
        pair.Server.EnqueueOutbound(new OutboundDatagram(
            new ConnectionId(1),
            MovementStatePacket.Plane,
            MovementStatePacket.PacketClass,
            MovementStatePacket.Channel,
            statePayload));
        pair.Server.FlushOutbound();

        int inboundCount = bridge.PollInbound();
        Assert.Equal(1, inboundCount);
        Assert.Equal((ushort)1, bridge.LastReceiveResult.LastProcessedInputSequence);
        Assert.Equal(new ClientMovementState(10f, 20f, 1.5f, 2.5f), bridge.LastReceiveResult.AuthoritativeState);
        Assert.True(bridge.LastReceiveResult.HadPredictedState);
        Assert.Equal(1, bridge.LastReceiveResult.RemovedPendingInputs);
        Assert.Equal(1, bridge.LastReceiveResult.RemovedPredictions);
        Assert.Equal(0, bridge.Sequencer.PendingCount);
        Assert.Equal(0, bridge.PredictionStore.Count);
        Assert.True(bridge.LastReceiveResult.PositionErrorMagnitude > 0f);
        Assert.True(bridge.CorrectionStats.Snapshot().SampleCount > 0);

        bridge.Interpolation.AddSample(210, new ClientMovementState(20f, 30f, 3f, 4f));
        Assert.True(bridge.TrySampleInterpolatedState(205, static (in ClientMovementState a, in ClientMovementState b, float alpha) =>
        {
            return new ClientMovementState(
                a.PositionX + (b.PositionX - a.PositionX) * alpha,
                a.PositionY + (b.PositionY - a.PositionY) * alpha,
                a.VelocityX + (b.VelocityX - a.VelocityX) * alpha,
                a.VelocityY + (b.VelocityY - a.VelocityY) * alpha);
        }, out ClientMovementState interpolated));

        Assert.Equal(new ClientMovementState(15f, 25f, 2.25f, 3.25f), interpolated);
    }

    private static void ClientMovementSessionHost_ControlPlaneHandshakeAndPing_RoundTrip()
    {
        using var pair = new LoopbackTransportPair();
        var hello = new HandshakeHelloPacket(
            protocolVersion: 3,
            schemaHash: 4,
            contentHash: 5,
            features: FeatureFlags.SharedPayloadFanout | FeatureFlags.Compression);
        var policy = new HandshakePolicy(
            ProtocolVersion: 3,
            SchemaHash: 4,
            ContentHash: 5,
            RequiredFeatures: FeatureFlags.None);
        var server = new NetworkControlPlaneHost(
            NetworkControlPlaneRole.Server,
            pair.Server,
            handshakePolicy: policy);
        var client = new ClientMovementSessionHost(
            pair.Client,
            new ConnectionId(1),
            hello,
            maxPollBatch: 8);

        server.Start();
        client.Start();
        SessionId sessionId = client.BeginHandshake(1000);
        Assert.True(sessionId.IsValid);

        Assert.Equal(1, server.Pump(1001));
        Assert.Equal(1, client.Pump(1002));
        Assert.True(client.IsReady);

        Assert.True(client.Control.SendPingProbe(new ConnectionId(1), sequence: 77, clientTick: 12345));
        Assert.Equal(1, server.Pump(1003));
        Assert.Equal(1, client.Pump(1004));

        Span<PongProbePacket> pongs = stackalloc PongProbePacket[2];
        int pongCount = client.Control.DrainReceivedPongs(pongs);
        Assert.Equal(1, pongCount);
        Assert.Equal((ushort)77, pongs[0].Sequence);
        Assert.Equal((uint)12345, pongs[0].ClientTick);

        ControlPlaneMetricsSnapshot serverMetrics = server.GetMetricsSnapshot();
        ControlPlaneMetricsSnapshot clientMetrics = client.Control.GetMetricsSnapshot();
        Assert.Equal(1, serverMetrics.PingProbesReceived);
        Assert.Equal(1, serverMetrics.PongProbesSent);
        Assert.Equal(1, clientMetrics.PingProbesSent);
        Assert.Equal(1, clientMetrics.PongProbesReceived);
    }

    private static void ClientMovementSessionHost_Pump_AggregatesObserverCountsAcrossPollBatches()
    {
        using var pair = new LoopbackTransportPair();
        pair.Client.Start();
        pair.Server.Start();

        int latestApplyCount = 0;
        int eventApplyCount = 0;
        var observerState = new ClientObserverStateBridge(
            new ConnectionId(1),
            (_, _, _) => latestApplyCount++);
        var observerEvents = new ClientObserverEventBridge(
            new ConnectionId(1),
            (_, _) => eventApplyCount++);
        var host = new ClientMovementSessionHost(
            pair.Client,
            new ConnectionId(1),
            default,
            observerState: observerState,
            observerEvents: observerEvents,
            maxPollBatch: 2);

        pair.Server.EnqueueOutbound(new OutboundDatagram(
            new ConnectionId(1),
            MovementStatePacket.Plane,
            MovementStatePacket.PacketClass,
            MovementStatePacket.Channel,
            PacketCodec.Encode(new MovementStatePacket(42, 100, 1, 1f, 2f, 3f, 4f))));
        pair.Server.EnqueueOutbound(new OutboundDatagram(
            new ConnectionId(1),
            RealtimeStateFramePacket.Plane,
            RealtimeStateFramePacket.PacketClass,
            RealtimeStateFramePacket.Channel,
            PacketCodec.Encode(new RealtimeStateFramePacket(10, 1001, ImmutablePayloadBuffer.CopyFrom([1, 2])))));
        pair.Server.EnqueueOutbound(new OutboundDatagram(
            new ConnectionId(1),
            RealtimeStateFramePacket.Plane,
            RealtimeStateFramePacket.PacketClass,
            RealtimeStateFramePacket.Channel,
            PacketCodec.Encode(new RealtimeStateFramePacket(11, 1002, ImmutablePayloadBuffer.CopyFrom([3, 4])))));
        pair.Server.EnqueueOutbound(new OutboundDatagram(
            new ConnectionId(1),
            ReliableEventFramePacket.Plane,
            ReliableEventFramePacket.PacketClass,
            ReliableEventFramePacket.Channel,
            PacketCodec.Encode(new ReliableEventFramePacket(20, ImmutablePayloadBuffer.CopyFrom([5, 6])))));
        pair.Server.FlushOutbound();

        int inboundCount = host.Pump(1000);

        Assert.Equal(4, inboundCount);
        Assert.Equal(2, observerState.LastReceivedFrameCount);
        Assert.Equal(2, observerState.LastAppliedFrameCount);
        Assert.Equal(2, latestApplyCount);
        Assert.Equal(1, observerEvents.LastReceivedEventCount);
        Assert.Equal(1, observerEvents.LastAppliedEventCount);
        Assert.Equal(1, eventApplyCount);
        Assert.Equal((ushort)1, host.Movement.LastReceiveResult.LastProcessedInputSequence);
    }

    private static UiSchemaDocument CreateClientUiSchema(UiCatalogIdentity? catalogIdentity = null)
    {
        Assert.True(UiSchemaIdentity.TryCreate(
            "server/main",
            catalogIdentity is null ? "hud.status" : "craft.blacksmith",
            1,
            new string('c', 64),
            1,
            out UiSchemaIdentity identity,
            out string error), error);

        string panelType = catalogIdentity is null ? "hud.status" : "craft.blacksmith";
        var metadata = new UiPanelMetadata(
            panelType,
            catalogIdentity is null ? UiSurfaceTarget.Hud : UiSurfaceTarget.Menu,
            catalogIdentity is null ? UiFocusMode.Passive : UiFocusMode.Normal,
            ExclusiveGroup: null,
            ZOrderClass: 1,
            UiOpenPolicy.SingletonByKey,
            UiBackPolicy.None,
            UiTooltipPolicy.Allow,
            BlocksGameplayInput: false,
            PersistenceKey: null);

        return new UiSchemaDocument(
            identity,
            metadata,
            RootNodeId: 1,
            Nodes:
            [
                new UiSchemaNodeDescriptor(1, UiNodeKind.Panel, null, null, [2]),
                new UiSchemaNodeDescriptor(2, UiNodeKind.Row, null, null, [3, 4]),
                new UiSchemaNodeDescriptor(3, UiNodeKind.DynamicText, "titleText", "Title", []),
                new UiSchemaNodeDescriptor(4, UiNodeKind.DynamicText, "levelValue", "Level", []),
            ],
            PayloadSlots:
            [
                new UiPayloadSlotDescriptor("titleText", UiModelValueKind.String, Required: true, MaxStringLength: 64),
                new UiPayloadSlotDescriptor("levelValue", UiModelValueKind.Integer, Required: false, MinNumber: 1, MaxNumber: 100),
            ],
            Actions:
            [
                new UiActionDescriptor(
                    "hud.open_character",
                    "Open Character",
                    IconId: null,
                    UiActionRole.Normal,
                    UiActionContextKind.Panel,
                    RequiresConfirm: false,
                    ChildMenuKey: null),
            ],
            StateBindings: [],
            CatalogIdentity: catalogIdentity);
    }

    private static UiModelDocument CreateClientUiModel(UiSchemaDocument schema)
    {
        return new UiModelDocument(
            schema.Identity,
            Values:
            [
                UiModelSlotValue.String("titleText", "Status"),
                UiModelSlotValue.Integer("levelValue", 12),
            ]);
    }

    private static UiSchemaDocument CreateClientSchemaRevision(UiSchemaDocument schema, int revision, char hashChar)
    {
        Assert.True(UiSchemaIdentity.TryCreate(
            schema.Identity.ServerIdentity,
            schema.Identity.PanelType,
            revision,
            new string(hashChar, 64),
            schema.Identity.ProtocolVersion,
            out UiSchemaIdentity identity,
            out string error), error);
        return schema with { Identity = identity };
    }

    private static UiCatalogIdentity CreateClientCatalogIdentity(int revision, char hashChar)
    {
        Assert.True(UiCatalogIdentity.TryCreate(
            "server/main",
            "craft.blacksmith",
            revision,
            new string(hashChar, 64),
            1,
            "en-US",
            out UiCatalogIdentity identity,
            out string error), error);
        return identity;
    }

    private static UiRendererCapabilities CreateClientRendererCapabilities()
    {
        return new UiRendererCapabilities(
            ProtocolVersion: 1,
            CapabilityFlags: UiCapabilityFlags.ContextMenusV1 | UiCapabilityFlags.RuntimeStateBindingsV1,
            MaxSchemaBytes: 64 * 1024,
            MaxModelBytes: 16 * 1024,
            MaxNodeCount: 512,
            MaxTreeDepth: 32,
            MaxRepeaterRows: 256,
            MaxTableColumns: 24);
    }

    private static UiModelSlotValue GetModelValue(UiModelDocument model, string slotId)
    {
        if (TryGetModelValue(model, slotId, out UiModelSlotValue value))
        {
            return value;
        }

        throw new InvalidOperationException($"Missing model slot '{slotId}'.");
    }

    private static bool TryGetModelValue(UiModelDocument model, string slotId, out UiModelSlotValue value)
    {
        for (int i = 0; i < model.Values.Count; i++)
        {
            if (string.Equals(model.Values[i].SlotId, slotId, StringComparison.Ordinal))
            {
                value = model.Values[i];
                return true;
            }
        }

        value = UiModelSlotValue.Null(slotId);
        return false;
    }

    private static UiModelSlotValue GetRowValue(ClientUiRepeaterRowState row, string slotId)
    {
        for (int i = 0; i < row.Values.Count; i++)
        {
            if (string.Equals(row.Values[i].SlotId, slotId, StringComparison.Ordinal))
            {
                return row.Values[i];
            }
        }

        throw new InvalidOperationException($"Missing row slot '{slotId}'.");
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL {name}: {ex}");
            Environment.Exit(1);
        }
    }
}

internal static class Assert
{
    public static void True(bool condition, string? message = null)
    {
        if (!condition)
            throw new InvalidOperationException(message ?? "Expected condition to be true.");
    }

    public static void False(bool condition, string? message = null)
    {
        if (condition)
            throw new InvalidOperationException(message ?? "Expected condition to be false.");
    }

    public static void Equal<T>(T expected, T actual, string? message = null)
        where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(message ?? $"Expected '{expected}' but got '{actual}'.");
    }

    public static void SequenceEqual<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual, string? message = null)
    {
        if (expected.Count != actual.Count)
            throw new InvalidOperationException(message ?? $"Expected sequence count {expected.Count}, got {actual.Count}.");

        for (int i = 0; i < expected.Count; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(expected[i], actual[i]))
                throw new InvalidOperationException(message ?? $"Sequence mismatch at {i}: expected '{expected[i]}', got '{actual[i]}'.");
        }
    }
}

internal sealed class FakeStateBindingResolver : IClientUiStateBindingResolver
{
    private readonly UiModelSlotValue _value;

    public FakeStateBindingResolver(string sourceId, UiModelSlotValue value)
    {
        SourceId = sourceId;
        _value = value;
    }

    public string SourceId { get; }

    public bool TryResolve(
        UiStateBindingDescriptor binding,
        out UiModelSlotValue value,
        out string error)
    {
        value = _value with { SlotId = binding.SlotId };
        error = string.Empty;
        return true;
    }
}

internal sealed class ThrowingDiscoveryQuery : IServerDiscoveryQuery
{
    private readonly string _message;

    public ThrowingDiscoveryQuery(string message)
    {
        _message = message;
    }

    public ValueTask<IReadOnlyList<ServerDiscoveryRow>> QueryAsync(CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException(_message);
    }
}

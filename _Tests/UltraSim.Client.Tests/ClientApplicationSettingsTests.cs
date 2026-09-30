using UltraSim.Client.Core;
using UltraSim.Game.Application;

static class ClientApplicationSettingsTests
{
    internal static void Run()
    {
        ResolvedClientSettings defaults = ClientSettingsResolver.Resolve(ClientSettingsDocument.Empty);
        Equal(ClientCameraProfile.Orbit, defaults.CameraProfile);
        Equal(ClientSettingsResolver.DefaultOrbitCameraDistance, defaults.CameraDistance);

        True(ClientSettingsScopeKey.TryCreate("fixture.realm-17", out ClientSettingsScopeKey scope));
        ClientPhysicalBinding globalInteract = Binding(
            ClientSemanticAction.Interact, ClientPhysicalInputKind.Keyboard, "Q");
        ClientPhysicalBinding scopedInteract = Binding(
            ClientSemanticAction.Interact, ClientPhysicalInputKind.Keyboard, "R");
        var document = new ClientSettingsDocument(new ClientSettingsLayer(
            uiScale: 1.25f,
            masterVolume: 0.55f,
            pointerSensitivity: 1.4f,
            invertY: true,
            controlGrammar: ClientControlGrammar.OrbitCommand,
            bindings: [globalInteract],
            renderDistanceChunks: 2));
        document = document.WithScope(scope, new ClientSettingsLayer(
            cameraProfile: ClientCameraProfile.IndependentMoveLook,
            cameraFovDegrees: 82f,
            bindings: [scopedInteract]));

        var recommendation = new ClientProfileRecommendation(
            ClientControlGrammar.ThirdPersonDirect,
            ClientCameraProfile.ThirdPersonFollow,
            12f,
            76f,
            [
                Binding(ClientSemanticAction.Interact, ClientPhysicalInputKind.Keyboard, "F"),
                Binding(ClientSemanticAction.OpenSettings, ClientPhysicalInputKind.Keyboard, "P"),
                Binding(ClientSemanticAction.CycleDebug, ClientPhysicalInputKind.Keyboard, "F12"),
                Binding(ClientSemanticAction.ContextMenu, ClientPhysicalInputKind.Keyboard, "ESCAPE"),
                Binding(ClientSemanticAction.Select, ClientPhysicalInputKind.MouseButton, "MIDDLE"),
                Binding(ClientSemanticAction.Primary, ClientPhysicalInputKind.MouseButton, "LEFT"),
            ]);

        ResolvedClientSettings accepted = ClientSettingsResolver.Resolve(
            document, scope, recommendation, ClientProfileApplicationMode.UseServerDefaults);
        Equal(1.25f, accepted.UiScale);
        Equal(0.55f, accepted.MasterVolume);
        Equal(1.4f, accepted.PointerSensitivity);
        True(accepted.InvertY);
        Equal(ClientControlGrammar.ThirdPersonDirect, accepted.ControlGrammar);
        Equal(ClientCameraProfile.IndependentMoveLook, accepted.CameraProfile);
        Equal(82f, accepted.CameraFovDegrees);
        Equal(2, accepted.RenderDistanceChunks);
        Equal("R", RequireBinding(accepted, ClientSemanticAction.Interact).Code);
        Equal("F10", RequireBinding(accepted, ClientSemanticAction.OpenSettings).Code);
        Equal("RIGHT", RequireBinding(accepted, ClientSemanticAction.ContextMenu).Code);
        Equal("MIDDLE", RequireBinding(accepted, ClientSemanticAction.Select).Code);
        Equal("LEFT", RequireBinding(accepted, ClientSemanticAction.Primary).Code);
        Equal("SPACE", RequireBinding(accepted, ClientSemanticAction.Jump).Code);
        Equal("F11", RequireBinding(accepted, ClientSemanticAction.CycleDebug).Code);
        ClientPhysicalInput acceptedInteract = RequireBinding(
            accepted, ClientSemanticAction.Interact);
        True(accepted.TryResolveInput(in acceptedInteract, out ClientSemanticAction resolvedInteract));
        Equal(ClientSemanticAction.Interact, resolvedInteract);
        True(ClientPhysicalInput.TryCreate(
            ClientPhysicalInputKind.Keyboard,
            "PF1_UNMAPPED",
            ClientInputModifiers.None,
            out ClientPhysicalInput unmapped));
        True(!accepted.TryResolveInput(in unmapped, out ClientSemanticAction unmappedAction));
        Equal(default(ClientSemanticAction), unmappedAction);
        ClientPhysicalInput invalid = default;
        True(!accepted.TryResolveInput(in invalid, out _));

        ResolvedClientSettings keepMine = ClientSettingsResolver.Resolve(
            document, default, recommendation, ClientProfileApplicationMode.KeepMyControls);
        Equal(ClientControlGrammar.ThirdPersonDirect, keepMine.ControlGrammar);
        Equal("Q", RequireBinding(keepMine, ClientSemanticAction.Interact).Code);
        Equal("F10", RequireBinding(keepMine, ClientSemanticAction.OpenSettings).Code);
        Equal("LEFT", RequireBinding(keepMine, ClientSemanticAction.Select).Code);
        Equal("MIDDLE", RequireBinding(keepMine, ClientSemanticAction.Primary).Code);

        var ambiguousRecommendation = new ClientProfileRecommendation(
            ClientControlGrammar.ThirdPersonDirect,
            ClientCameraProfile.ThirdPersonFollow,
            12f,
            76f,
            [
                Binding(ClientSemanticAction.Interact, ClientPhysicalInputKind.Keyboard, "Q"),
                Binding(ClientSemanticAction.Jump, ClientPhysicalInputKind.Keyboard, "Q"),
            ]);
        ResolvedClientSettings ambiguous = ClientSettingsResolver.Resolve(
            ClientSettingsDocument.Empty, default, ambiguousRecommendation,
            ClientProfileApplicationMode.UseServerDefaults);
        Equal("E", RequireBinding(ambiguous, ClientSemanticAction.Interact).Code);
        Equal("SPACE", RequireBinding(ambiguous, ClientSemanticAction.Jump).Code);

        // Device-only prototype preference survives every document update and realm resolution.
        var opted = document.WithExperimentalVegetation(true);
        True(!ClientSettingsDocument.Empty.ExperimentalVegetation);
        True(opted.WithGlobal(document.Global).WithDisplay(opted.Display).WithScope(scope, ClientSettingsLayer.Empty)
            .ExperimentalVegetation);
        True(ClientSettingsResolver.Resolve(opted, scope, recommendation,
            ClientProfileApplicationMode.UseServerDefaults).ExperimentalVegetation);
        string vegetationJson = ClientSettingsJsonCodec.Write(opted);
        True(ClientSettingsJsonCodec.TryRead(vegetationJson, out var reopenedVegetation, out _));
        True(reopenedVegetation.ExperimentalVegetation);
        string v5 = vegetationJson.Replace("\"version\": 7", "\"version\": 5", StringComparison.Ordinal)
            .Replace("  \"experimentalVegetation\": true,\n", "", StringComparison.Ordinal)
            .Replace("  \"inventoryViewer\": \"D2Grid\",\n", "", StringComparison.Ordinal);
        True(ClientSettingsJsonCodec.TryRead(v5, out var migratedVegetation, out _));
        True(!migratedVegetation.ExperimentalVegetation);
        True(!ClientSettingsJsonCodec.TryRead(vegetationJson.Replace(
            "  \"experimentalVegetation\": true,\n", "", StringComparison.Ordinal), out _, out _));

        var list = opted.WithInventoryViewer(ClientInventoryViewerProfile.List);
        Equal(ClientInventoryViewerProfile.List, ClientSettingsResolver.Resolve(list, scope).InventoryViewer);
        Equal(ClientInventoryViewerProfile.List, list.WithGlobal(document.Global).WithDisplay(list.Display)
            .WithExperimentalVegetation(false).InventoryViewer);
        string listJson = ClientSettingsJsonCodec.Write(list);
        True(ClientSettingsJsonCodec.TryRead(listJson, out var reopenedList, out _));
        Equal(ClientInventoryViewerProfile.List, reopenedList.InventoryViewer);
        string v6 = listJson.Replace("\"version\": 7", "\"version\": 6", StringComparison.Ordinal)
            .Replace("  \"inventoryViewer\": \"List\",\n", "", StringComparison.Ordinal);
        True(ClientSettingsJsonCodec.TryRead(v6, out var migratedViewer, out _));
        Equal(ClientInventoryViewerProfile.D2Grid, migratedViewer.InventoryViewer);
        True(!ClientSettingsJsonCodec.TryRead(listJson.Replace(
            "  \"inventoryViewer\": \"List\",\n", "", StringComparison.Ordinal), out _, out _));
        Throws<ArgumentOutOfRangeException>(() => new ClientSettingsDocument(
            inventoryViewer: (ClientInventoryViewerProfile)255));

        string json = ClientSettingsJsonCodec.Write(document);
        True(json.Contains("\"uiScale\": 1.25", StringComparison.Ordinal));
        True(json.Contains("\"controlGrammar\": \"OrbitCommand\"", StringComparison.Ordinal));
        True(json.Contains("\"renderDistanceChunks\": 2", StringComparison.Ordinal));
        True(json.Contains("fixture.realm-17", StringComparison.Ordinal));
        True(!json.Contains("ThirdPersonDirect", StringComparison.Ordinal),
            "A transient server recommendation leaked into user settings persistence.");
        True(ClientSettingsJsonCodec.TryRead(json, out ClientSettingsDocument restored, out string? error), error);
        ResolvedClientSettings roundTrip = ClientSettingsResolver.Resolve(restored, scope);
        Equal(accepted.UiScale, roundTrip.UiScale);
        Equal(2, roundTrip.RenderDistanceChunks);
        Equal("R", RequireBinding(roundTrip, ClientSemanticAction.Interact).Code);
        True(!ClientSettingsJsonCodec.TryRead(
            json.Replace(
                $"\"version\": {ClientSettingsDocument.CurrentVersion}",
                "\"version\": 99",
                StringComparison.Ordinal),
            out _, out _));
        True(!ClientSettingsJsonCodec.TryRead(
            $"{{ \"version\": {ClientSettingsDocument.CurrentVersion}, \"mystery\": true }}",
            out _, out _));
        True(ClientSettingsJsonCodec.TryRead(
            "{ \"version\": 2, \"global\": { \"cameraProfile\": \"Orbit\", \"cameraDistance\": 28 } }",
            out ClientSettingsDocument versionTwo, out _));
        Equal(ClientSettingsResolver.DefaultOrbitCameraDistance,
            ClientSettingsResolver.Resolve(versionTwo).CameraDistance);
        True(ClientSettingsJsonCodec.TryRead(
            "{ \"version\": 4, \"global\": " +
            "{ \"cameraProfile\": \"Orbit\", \"cameraDistance\": 28 } }",
            out ClientSettingsDocument currentExplicitDistance, out _));
        Equal(28f, ClientSettingsResolver.Resolve(currentExplicitDistance).CameraDistance);
        True(ClientSettingsJsonCodec.TryRead(
            "{ \"version\": 1, \"global\": { \"cameraDistance\": 18 } }",
            out ClientSettingsDocument versionOne, out _));
        Equal(1, ClientSettingsResolver.Resolve(versionOne).RenderDistanceChunks);

        Throws<InvalidOperationException>(() => ClientSettingsResolver.Resolve(
            new ClientSettingsDocument(new ClientSettingsLayer(bindings:
            [
                Binding(ClientSemanticAction.Interact, ClientPhysicalInputKind.Keyboard, "ESCAPE"),
            ]))));
        Throws<ArgumentOutOfRangeException>(() =>
            new ClientSettingsLayer(renderDistanceChunks: 4));
        True(!ClientSettingsScopeKey.TryCreate("host:2593/path", out _),
            "Endpoint syntax must not masquerade as a settings scope identity.");

        ProfileDecisionsAreRealmAndExactIdentityScoped();
    }

    private static void ProfileDecisionsAreRealmAndExactIdentityScoped()
    {
        string root = Path.Combine(Path.GetTempPath(),
            $"ultrasim-profile-decisions-{Environment.ProcessId}-{Guid.NewGuid():N}");
        string path = Path.Combine(root, "settings", "server-profiles.json");
        try
        {
            var realm = new ApplicationRealmId(0xF03C, 1);
            var hashA = new ClientProfileHash(1, 2, 3, 4);
            var hashB = new ClientProfileHash(5, 6, 7, 8);
            var decision = new ClientProfileDecision(
                realm, 7, hashA, ClientProfileApplicationMode.KeepMyControls);
            ClientProfileDecisionDocument document =
                ClientProfileDecisionDocument.Empty.WithDecision(in decision);
            var store = new ClientProfileDecisionFileStore(path);
            store.Save(document);
            Equal(ClientProfileDecisionLoadStatus.Loaded, store.Load().Status);
            ClientProfileDecisionDocument restored = store.Load().Document;
            True(restored.TryFindExact(realm, 7, hashA, out ClientProfileDecision exact));
            Equal(ClientProfileApplicationMode.KeepMyControls, exact.Mode);
            True(!restored.TryFindExact(realm, 8, hashA, out _),
                "A changed profile revision reused an old consent decision.");
            True(!restored.TryFindExact(realm, 7, hashB, out _),
                "A changed profile hash reused an old consent decision.");
            True(!restored.TryFindExact(new ApplicationRealmId(0xF03C, 2), 7, hashA, out _),
                "A different verified realm reused another realm's profile consent.");
            string persisted = File.ReadAllText(path);
            True(!persisted.Contains("recommendation", StringComparison.OrdinalIgnoreCase),
                "Transient server recommendation data leaked into durable consent persistence.");
            File.WriteAllText(path, persisted.Replace(
                realm.ToString(), realm.ToString().ToUpperInvariant(), StringComparison.Ordinal));
            Equal(ClientProfileDecisionLoadStatus.InvalidFallback, store.Load().Status);
            Equal(0, store.Load().Document.Decisions.Count);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static ClientPhysicalBinding Binding(
        ClientSemanticAction action,
        ClientPhysicalInputKind kind,
        string code,
        ClientInputModifiers modifiers = ClientInputModifiers.None)
    {
        True(ClientPhysicalInput.TryCreate(kind, code, modifiers, out ClientPhysicalInput input));
        return new ClientPhysicalBinding(action, input);
    }

    private static ClientPhysicalInput RequireBinding(
        ResolvedClientSettings settings,
        ClientSemanticAction action)
    {
        True(settings.TryGetBinding(action, out ClientPhysicalInput input));
        return input;
    }

    private static void True(bool value, string? message = null)
    {
        if (!value) throw new InvalidOperationException(message ?? "Expected true.");
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    private static void Throws<T>(Action body) where T : Exception
    {
        try { body(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}

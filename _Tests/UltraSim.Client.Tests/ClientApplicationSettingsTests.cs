using UltraSim.Client.Core;

static class ClientApplicationSettingsTests
{
    internal static void Run()
    {
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
            bindings: [globalInteract]));
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
                Binding(ClientSemanticAction.ContextMenu, ClientPhysicalInputKind.Keyboard, "ESCAPE"),
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
        Equal("R", RequireBinding(accepted, ClientSemanticAction.Interact).Code);
        Equal("F10", RequireBinding(accepted, ClientSemanticAction.OpenSettings).Code);
        Equal("RIGHT", RequireBinding(accepted, ClientSemanticAction.ContextMenu).Code);

        ResolvedClientSettings keepMine = ClientSettingsResolver.Resolve(
            document, default, recommendation, ClientProfileApplicationMode.KeepMyControls);
        Equal(ClientControlGrammar.ThirdPersonDirect, keepMine.ControlGrammar);
        Equal("Q", RequireBinding(keepMine, ClientSemanticAction.Interact).Code);
        Equal("F10", RequireBinding(keepMine, ClientSemanticAction.OpenSettings).Code);

        string json = ClientSettingsJsonCodec.Write(document);
        True(json.Contains("\"uiScale\": 1.25", StringComparison.Ordinal));
        True(json.Contains("\"controlGrammar\": \"OrbitCommand\"", StringComparison.Ordinal));
        True(json.Contains("fixture.realm-17", StringComparison.Ordinal));
        True(!json.Contains("ThirdPersonDirect", StringComparison.Ordinal),
            "A transient server recommendation leaked into user settings persistence.");
        True(ClientSettingsJsonCodec.TryRead(json, out ClientSettingsDocument restored, out string? error), error);
        ResolvedClientSettings roundTrip = ClientSettingsResolver.Resolve(restored, scope);
        Equal(accepted.UiScale, roundTrip.UiScale);
        Equal("R", RequireBinding(roundTrip, ClientSemanticAction.Interact).Code);
        True(!ClientSettingsJsonCodec.TryRead(
            json.Replace("\"version\": 1", "\"version\": 99", StringComparison.Ordinal),
            out _, out _));
        True(!ClientSettingsJsonCodec.TryRead("{ \"version\": 1, \"mystery\": true }", out _, out _));

        Throws<InvalidOperationException>(() => ClientSettingsResolver.Resolve(
            new ClientSettingsDocument(new ClientSettingsLayer(bindings:
            [
                Binding(ClientSemanticAction.Interact, ClientPhysicalInputKind.Keyboard, "ESCAPE"),
            ]))));
        True(!ClientSettingsScopeKey.TryCreate("host:2593/path", out _),
            "Endpoint syntax must not masquerade as a settings scope identity.");
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

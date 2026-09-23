using UltraSim.Client.Core;

internal static class ClientCameraRigTests
{
    internal static void Run()
    {
        ClientCameraRig orbit = ClientStockCameraRigs.Orbit;
        ClientCameraRig third = ClientStockCameraRigs.ThirdPerson;
        ClientCameraRig independent = ClientStockCameraRigs.Independent;
        Assert.True(orbit.IsValid && third.IsValid && independent.IsValid);
        Assert.True(orbit != third && third != independent);
        Assert.Equal(ClientCameraZoom.Disabled, independent.Zoom);
        Assert.Equal(ClientCameraAxisMode.Locked, independent.YawMode);

        ClientCanonicalViewProjection full = Project(orbit, 1920, 1080, 1920, 1080);
        AssertNear(full.CanonicalVerticalExtent, full.TargetVerticalExtent);
        AssertNear(full.CanonicalHorizontalExtent, full.TargetHorizontalExtent);
        foreach ((int width, int height) in new[]
                 { (1280, 720), (640, 360), (3840, 2160) })
        {
            ClientCanonicalViewProjection same = Project(orbit, width, height, width, height);
            AssertNear(full.TargetVerticalExtent, same.TargetVerticalExtent);
            AssertNear(full.TargetHorizontalExtent, same.TargetHorizontalExtent);
        }

        ClientCanonicalViewProjection fourThree = Project(orbit, 1920, 1080, 800, 600);
        AssertNear(full.TargetVerticalExtent, fourThree.TargetVerticalExtent);
        Assert.True(fourThree.TargetHorizontalExtent < full.TargetHorizontalExtent);
        ClientCanonicalViewProjection fourThreeLarge = Project(orbit, 1600, 1200, 1600, 1200);
        AssertNear(fourThree.TargetVerticalExtent, fourThreeLarge.TargetVerticalExtent);
        AssertNear(fourThree.TargetHorizontalExtent, fourThreeLarge.TargetHorizontalExtent);
        ClientCanonicalViewProjection portrait = Project(orbit, 1920, 1080, 600, 800);
        AssertNear(full.TargetVerticalExtent, portrait.TargetVerticalExtent);
        Assert.True(portrait.TargetHorizontalExtent < fourThree.TargetHorizontalExtent);

        ClientCanonicalViewProjection wide = Project(orbit, 3440, 1440, 2560, 1080);
        AssertNear(full.TargetHorizontalExtent, wide.TargetHorizontalExtent);
        Assert.True(wide.TargetVerticalExtent < full.TargetVerticalExtent);

        foreach ((int width, int height) in new[]
                 { (1920, 1080), (2560, 1440), (3440, 1440) })
        {
            GameplayFrameLayout layout = Layout(width, height, 1920, 1080);
            ClientCanonicalViewProjection current =
                ClientCanonicalViewProjection.ForFrame(orbit, layout);
            AssertNear(full.TargetVerticalExtent, current.TargetVerticalExtent);
            AssertNear(full.TargetHorizontalExtent, current.TargetHorizontalExtent);
            AssertNear(current.TargetVerticalExtent,
                current.MainViewportVerticalExtent(layout) * layout.Frame.Height /
                layout.Application.Height);
        }

        ClientCameraRig orthographic = orbit with
        {
            Projection = ClientCameraProjection.Orthographic,
            CanonicalOrthographicSizeMillimeters = 20_000,
        };
        Assert.True(orthographic.IsValid);
        ClientCanonicalViewProjection orthoFull = Project(orthographic, 1920, 1080, 1920, 1080);
        ClientCanonicalViewProjection orthoWide = Project(orthographic, 1920, 1080, 2560, 1080);
        AssertNear(20d, orthoFull.TargetVerticalExtent);
        AssertNear(orthoFull.TargetHorizontalExtent, orthoWide.TargetHorizontalExtent);
        Assert.True(orthoWide.TargetVerticalExtent < orthoFull.TargetVerticalExtent);

        Assert.False((orbit with { Projection = (ClientCameraProjection)99 }).IsValid);
        Assert.False((orbit with { Target = (ClientCameraTarget)99 }).IsValid);
        Assert.False((orbit with { YawMode = (ClientCameraAxisMode)99 }).IsValid);
        Assert.False((orbit with { DefaultDistanceMillimeters = 90_000 }).IsValid);
        Assert.False((orbit with { MinimumPitchTenths = 600 }).IsValid);
        Assert.False((orbit with { CanonicalFovTenths = 0 }).IsValid);
        Assert.False((orbit with { CanonicalOrthographicSizeMillimeters = 0 }).IsValid);
        Assert.False((orbit with { ZoomStepMillimeters = 0 }).IsValid);

        ResolvedClientSettings defaults = ClientSettingsResolver.Resolve(ClientSettingsDocument.Empty);
        var custom = new ClientNamedControlProfile("My Orbit", defaults.ControlGrammar,
            defaults.CameraRig, defaults.Bindings);
        ClientSettingsDocument document = ClientSettingsDocument.Empty.WithNamedProfile(custom);
        ResolvedClientSettings resolved = ClientSettingsResolver.Resolve(document,
            namedProfile: "My Orbit");
        Assert.Equal(defaults.CameraRig, resolved.CameraRig);
        Assert.Equal(defaults.ControlGrammar, resolved.ControlGrammar);
        Assert.True(defaults.Bindings.SequenceEqual(resolved.Bindings));
        string saved = ClientSettingsJsonCodec.Write(document);
        if (!ClientSettingsJsonCodec.TryRead(saved, out ClientSettingsDocument reopened,
                out string? error))
            throw new InvalidOperationException($"Named profile reopen failed: {error}.");
        Assert.Equal(defaults.CameraRig,
            ClientSettingsResolver.Resolve(reopened, namedProfile: "My Orbit").CameraRig);
        ClientSettingsDocument renamed = ClientSettingsDocument.Empty.WithNamedProfile(
            new ClientNamedControlProfile("Same Rig, New Name", defaults.ControlGrammar,
                defaults.CameraRig, defaults.Bindings));
        ResolvedClientSettings renamedResolved = ClientSettingsResolver.Resolve(renamed,
            namedProfile: "Same Rig, New Name");
        Assert.Equal(resolved.CameraRig, renamedResolved.CameraRig);
        Assert.True(resolved.Bindings.SequenceEqual(renamedResolved.Bindings));

        // V3 persisted layers and per-realm overrides must migrate without losing lens,
        // grammar, or physical input meaning. The old enum only seeds the new rig.
        const string legacy = """
            {"version":3,"global":{"controlGrammar":"ThirdPersonDirect",
            "cameraProfile":"ThirdPersonFollow","cameraDistance":11.5,
            "cameraFovDegrees":83,"bindings":[{"action":"Interact",
            "kind":"Keyboard","code":"R","modifiers":"None"}]},
            "scopes":[{"scope":"realm.f18","settings":{"cameraDistance":17}}]}
            """;
        Assert.True(ClientSettingsJsonCodec.TryRead(legacy,
            out ClientSettingsDocument migrated, out _));
        ResolvedClientSettings migratedGlobal = ClientSettingsResolver.Resolve(migrated);
        Assert.Equal(ClientControlGrammar.ThirdPersonDirect, migratedGlobal.ControlGrammar);
        Assert.Equal(11_500, migratedGlobal.CameraRig.DefaultDistanceMillimeters);
        Assert.Equal(830, migratedGlobal.CameraRig.CanonicalFovTenths);
        Assert.True(migratedGlobal.TryGetBinding(ClientSemanticAction.Interact,
            out ClientPhysicalInput migratedInteract) && migratedInteract.Code == "R");
        Assert.True(ClientSettingsScopeKey.TryCreate("realm.f18", out ClientSettingsScopeKey scope));
        ResolvedClientSettings migratedScoped = ClientSettingsResolver.Resolve(migrated, scope);
        Assert.Equal(17_000, migratedScoped.CameraRig.DefaultDistanceMillimeters);
        Assert.Equal(830, migratedScoped.CameraRig.CanonicalFovTenths);
        Assert.True(ClientSettingsJsonCodec.Write(migrated).Contains("\"version\": 4",
            StringComparison.Ordinal));
    }

    private static GameplayFrameLayout Layout(int appWidth, int appHeight,
        int outputWidth, int outputHeight) => GameplayFrameLayout.MaxFit(
            ClientPixelSize.Create(appWidth, appHeight),
            ClientPixelSize.Create(outputWidth, outputHeight));

    private static ClientCanonicalViewProjection Project(ClientCameraRig rig,
        int appWidth, int appHeight, int outputWidth, int outputHeight) =>
        ClientCanonicalViewProjection.ForFrame(rig,
            Layout(appWidth, appHeight, outputWidth, outputHeight));

    private static void AssertNear(double expected, double actual)
    {
        if (Math.Abs(expected - actual) > 0.000_001d)
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}

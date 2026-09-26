using UltraSim.Client.Core;

internal static class ClientDisplayConfigurationTests
{
    internal static void Run()
    {
        var expected = new Dictionary<ClientDisplayAspectPreset, ClientPixelSize[]>
        {
            [ClientDisplayAspectPreset.SixteenNine] =
            [Size(1280, 720), Size(1600, 900), Size(1920, 1080), Size(2560, 1440), Size(3840, 2160)],
            [ClientDisplayAspectPreset.SixteenTen] =
            [Size(1280, 800), Size(1440, 900), Size(1680, 1050), Size(1920, 1200), Size(2560, 1600)],
            [ClientDisplayAspectPreset.FourThree] =
            [Size(1024, 768), Size(1280, 960), Size(1600, 1200), Size(1920, 1440)],
            [ClientDisplayAspectPreset.FiveFour] = [Size(1280, 1024)],
        };
        foreach ((ClientDisplayAspectPreset aspect, ClientPixelSize[] sizes) in expected)
            Assert.True(ClientDisplayPresetCatalog.ForAspect(aspect).SequenceEqual(sizes));
        Assert.False(ClientDisplayPresetCatalog.Contains(ClientDisplayAspectPreset.FourThree,
            Size(1920, 1080)));

        ResolvedClientDisplayConfiguration simple = ClientDisplayResolver.Resolve(ClientDisplaySettings.Default);
        Assert.Equal(ClientDisplaySettingsMode.Simple, simple.SettingsMode);
        Assert.Equal(Size(1920, 1080), simple.ApplicationResolution);
        Assert.Equal(new ClientPixelRect(0, 0, 1920, 1080), simple.GameplayFrame.Frame);
        Assert.Equal(Size(1920, 1080), simple.GameplayOutputResolution);
        Assert.Equal(Size(1920, 1080), simple.InternalRenderResolution);
        Assert.Equal(100, simple.InternalRenderScalePercent);
        foreach ((ClientDisplayAspectPreset aspect, ClientPixelSize[] sizes) in expected)
            foreach (ClientPixelSize size in sizes)
            {
                ResolvedClientDisplayConfiguration resolved = ClientDisplayResolver.Resolve(
                    ClientDisplaySettings.Default with { SimpleAspect = aspect, SimpleResolution = size });
                Assert.Equal(size, resolved.ApplicationResolution);
                Assert.Equal(size, resolved.GameplayOutputResolution);
                Assert.Equal(size, resolved.InternalRenderResolution);
            }

        var advanced = ClientDisplaySettings.Default.WithAdvancedFromSimple() with
        {
            AdvancedApplication = Size(2560, 1440),
            AdvancedFrameMode = ClientGameplayFrameMode.Custom,
            AdvancedFrameSize = Size(1920, 1080),
            AdvancedOutput = Size(1280, 720),
            AdvancedInternalScalePercent = 50,
        };
        ResolvedClientDisplayConfiguration composite = ClientDisplayResolver.Resolve(advanced);
        Assert.Equal(new ClientPixelRect(320, 180, 1920, 1080), composite.GameplayFrame.Frame);
        Assert.Equal(Size(1280, 720), composite.GameplayOutputResolution);
        Assert.Equal(Size(640, 360), composite.InternalRenderResolution);
        Assert.True(composite.GameplayFrame.TryToOutput(new ClientPixelPoint(1280, 720),
            out ClientPixelPoint center));
        Assert.Equal(new ClientPixelPoint(640, 360), center);
        Assert.False(composite.GameplayFrame.TryToOutput(new ClientPixelPoint(0, 0), out _));

        var custom = ClientDisplaySettings.Default.WithAdvancedFromSimple() with
        {
            AdvancedApplication = Size(1500, 920), AdvancedOutput = Size(1500, 920),
            AdvancedInternalScalePercent = 75,
        };
        ResolvedClientDisplayConfiguration weird = ClientDisplayResolver.Resolve(custom);
        Assert.Equal(Size(1125, 690), weird.InternalRenderResolution);
        Assert.Equal(new ClientPixelRect(0, 0, 1500, 920), weird.GameplayFrame.Frame);
        Assert.True(weird.GameplayFrame.TryToOutput(new ClientPixelPoint(750, 460),
            out ClientPixelPoint weirdCenter));
        Assert.Equal(new ClientPixelPoint(750, 460), weirdCenter);
        ResolvedClientDisplayConfiguration exactFit = ClientDisplayResolver.Resolve(custom with
        {
            AdvancedFrameMode = ClientGameplayFrameMode.MaxFit,
            AdvancedOutput = Size(1280, 720),
        });
        Assert.Equal(new ClientPixelRect(6, 41, 1488, 837), exactFit.GameplayFrame.Frame);
        Assert.Equal((long)exactFit.GameplayFrame.Frame.Width * 720,
            (long)exactFit.GameplayFrame.Frame.Height * 1280);
        ClientCanonicalViewProjection customProjection = ClientCanonicalViewProjection.ForFrame(
            ClientStockCameraRigs.Orbit, weird.GameplayFrame);
        ClientCanonicalViewProjection doubledProjection = ClientCanonicalViewProjection.ForFrame(
            ClientStockCameraRigs.Orbit,
            GameplayFrameLayout.Fill(Size(3000, 1840), Size(3000, 1840)));
        Assert.Equal(customProjection.TargetHorizontalExtent, doubledProjection.TargetHorizontalExtent);
        Assert.Equal(customProjection.TargetVerticalExtent, doubledProjection.TargetVerticalExtent);
        Assert.True(customProjection.TargetHorizontalExtent < customProjection.CanonicalHorizontalExtent);
        Assert.Equal(customProjection.CanonicalVerticalExtent, customProjection.TargetVerticalExtent);
        ClientCanonicalViewProjection smallerPixels = ClientCanonicalViewProjection.ForFrame(
            ClientStockCameraRigs.Orbit,
            GameplayFrameLayout.Fill(Size(960, 540), Size(960, 540)));
        ClientCanonicalViewProjection fullPixels = ClientCanonicalViewProjection.ForFrame(
            ClientStockCameraRigs.Orbit, simple.GameplayFrame);
        Assert.Equal(fullPixels.TargetHorizontalExtent, smallerPixels.TargetHorizontalExtent);
        Assert.Equal(fullPixels.TargetVerticalExtent, smallerPixels.TargetVerticalExtent);
        Assert.Equal(fullPixels.TargetVerticalExtent,
            ClientCanonicalViewProjection.ForFrame(ClientStockCameraRigs.Orbit,
                composite.GameplayFrame).TargetVerticalExtent);
        Assert.Throws<ArgumentException>(() => ClientDisplayResolver.Resolve(custom with
        { AdvancedOutput = Size(1280, 720) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => ClientDisplayResolver.Resolve(custom with
        { AdvancedApplication = Size(4096, 4096) }));

        ResolvedClientDisplayConfiguration relocked = ClientDisplayResolver.Resolve(advanced with
        { SettingsMode = ClientDisplaySettingsMode.Simple });
        Assert.Equal(simple.GameplayFrame, relocked.GameplayFrame);
        Assert.Equal(simple.InternalRenderResolution, relocked.InternalRenderResolution);

        Assert.True(ClientSettingsJsonCodec.TryRead(
            "{\"version\":4,\"global\":{\"uiScale\":1.25},\"scopes\":[]}",
            out ClientSettingsDocument migrated, out string? error));
        Assert.Equal(ClientDisplaySettings.Default, migrated.Display);
        Assert.Equal(1.25f, ClientSettingsResolver.Resolve(migrated).UiScale);
        var document = migrated.WithDisplay(advanced);
        Assert.True(ClientSettingsJsonCodec.TryRead(ClientSettingsJsonCodec.Write(document),
            out ClientSettingsDocument reopened, out error));
        Assert.Equal(composite, ClientDisplayResolver.Resolve(reopened.Display));
        Assert.Equal(advanced, reopened.Display);
        Assert.Equal(advanced, document.WithGlobal(ClientSettingsLayer.Empty).Display);
        Assert.Equal(advanced, document.WithScope(CreateScope(), ClientSettingsLayer.Empty).Display);
        Assert.True(!ClientSettingsJsonCodec.TryRead(
            ClientSettingsJsonCodec.Write(document).Replace("\"applicationWidth\": 2560",
                "\"applicationWidth\": 32768", StringComparison.Ordinal), out _, out _));
        // Physical fullscreen presentation does not mutate the logical selected resolution.
        Assert.Equal(simple.ApplicationResolution, ClientDisplayResolver.Resolve(
            ClientDisplaySettings.Default with { DisplayMode = ClientDisplayMode.Fullscreen })
            .ApplicationResolution);
    }

    private static ClientSettingsScopeKey CreateScope()
    {
        Assert.True(ClientSettingsScopeKey.TryCreate("display.test", out ClientSettingsScopeKey scope));
        return scope;
    }

    private static ClientPixelSize Size(int width, int height) => ClientPixelSize.Create(width, height);
}

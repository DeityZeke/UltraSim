namespace UltraSim.Client.Core;

public enum ClientDisplaySettingsMode : byte { Simple = 1, Advanced = 2 }
public enum ClientDisplayMode : byte { Windowed = 1, BorderlessFullscreen = 2, Fullscreen = 3 }
public enum ClientDisplayAspectPreset : byte { SixteenNine = 1, SixteenTen = 2, FourThree = 3, FiveFour = 4 }
public enum ClientGameplayFrameMode : byte { Fill = 1, MaxFit = 2, Custom = 3 }

/// <summary>Exact, finite user-facing Simple presets. The aspect family is never inferred from a label.</summary>
public static class ClientDisplayPresetCatalog
{
    private static readonly ClientPixelSize[] SixteenNine =
    [Create(1280, 720), Create(1600, 900), Create(1920, 1080), Create(2560, 1440), Create(3840, 2160)];
    private static readonly ClientPixelSize[] SixteenTen =
    [Create(1280, 800), Create(1440, 900), Create(1680, 1050), Create(1920, 1200), Create(2560, 1600)];
    private static readonly ClientPixelSize[] FourThree =
    [Create(1024, 768), Create(1280, 960), Create(1600, 1200), Create(1920, 1440)];
    private static readonly ClientPixelSize[] FiveFour = [Create(1280, 1024)];

    public static IReadOnlyList<ClientPixelSize> ForAspect(ClientDisplayAspectPreset aspect) => aspect switch
    {
        ClientDisplayAspectPreset.SixteenNine => Array.AsReadOnly(SixteenNine),
        ClientDisplayAspectPreset.SixteenTen => Array.AsReadOnly(SixteenTen),
        ClientDisplayAspectPreset.FourThree => Array.AsReadOnly(FourThree),
        ClientDisplayAspectPreset.FiveFour => Array.AsReadOnly(FiveFour),
        _ => throw new ArgumentOutOfRangeException(nameof(aspect)),
    };

    public static bool Contains(ClientDisplayAspectPreset aspect, ClientPixelSize size) =>
        ForAspect(aspect).Contains(size);

    private static ClientPixelSize Create(int width, int height) => ClientPixelSize.Create(width, height);
}

/// <summary>
/// User-global authoring choices. Advanced values are retained when Simple is active, but can
/// never influence the effective Simple configuration. No server profile owns this value.
/// </summary>
public sealed record ClientDisplaySettings(
    ClientDisplaySettingsMode SettingsMode,
    ClientDisplayMode DisplayMode,
    ClientDisplayAspectPreset SimpleAspect,
    ClientPixelSize SimpleResolution,
    ClientPixelSize AdvancedApplication,
    ClientGameplayFrameMode AdvancedFrameMode,
    ClientPixelSize AdvancedFrameSize,
    ClientPixelSize AdvancedOutput,
    int AdvancedInternalScalePercent)
{
    public static ClientDisplaySettings Default { get; } = new(
        ClientDisplaySettingsMode.Simple, ClientDisplayMode.Windowed,
        ClientDisplayAspectPreset.SixteenNine, ClientPixelSize.Create(1920, 1080),
        ClientPixelSize.Create(1920, 1080), ClientGameplayFrameMode.Fill,
        ClientPixelSize.Create(1920, 1080), ClientPixelSize.Create(1920, 1080), 100);

    public ClientDisplaySettings WithAdvancedFromSimple() => this with
    {
        SettingsMode = ClientDisplaySettingsMode.Advanced,
        AdvancedApplication = SimpleResolution,
        AdvancedFrameMode = ClientGameplayFrameMode.Fill,
        AdvancedFrameSize = SimpleResolution,
        AdvancedOutput = SimpleResolution,
        AdvancedInternalScalePercent = 100,
    };
}

/// <summary>One validated, renderer-neutral display result for all editions and sessions.</summary>
public readonly record struct ResolvedClientDisplayConfiguration(
    ClientDisplaySettingsMode SettingsMode,
    ClientDisplayMode DisplayMode,
    ClientPixelSize ApplicationResolution,
    GameplayFrameLayout GameplayFrame,
    ClientPixelSize GameplayOutputResolution,
    int InternalRenderScalePercent,
    ClientPixelSize InternalRenderResolution);

public static class ClientDisplayResolver
{
    public const int MaximumDimension = 4096;
    public const long MaximumPixels = 8_294_400; // 3840 × 2160
    public const int MinimumInternalScalePercent = 50;
    public const int MaximumInternalScalePercent = 100;

    public static ResolvedClientDisplayConfiguration Resolve(ClientDisplaySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.SettingsMode is not (ClientDisplaySettingsMode.Simple or ClientDisplaySettingsMode.Advanced) ||
            settings.DisplayMode is not (ClientDisplayMode.Windowed or ClientDisplayMode.BorderlessFullscreen or
                ClientDisplayMode.Fullscreen) ||
            !ClientDisplayPresetCatalog.Contains(settings.SimpleAspect, settings.SimpleResolution))
            throw new ArgumentException("Invalid display mode or Simple preset.", nameof(settings));
        // Hidden Advanced choices remain inert in Simple mode, but persistent input must still be
        // finite and safe to expose later. Relationship validation occurs when Advanced is active.
        ValidateNativeSize(settings.AdvancedApplication);
        ValidateNativeSize(settings.AdvancedFrameSize);
        ValidateNativeSize(settings.AdvancedOutput);
        if (settings.AdvancedFrameMode is not (ClientGameplayFrameMode.Fill or
            ClientGameplayFrameMode.MaxFit or ClientGameplayFrameMode.Custom) ||
            settings.AdvancedInternalScalePercent is < MinimumInternalScalePercent or
                > MaximumInternalScalePercent)
            throw new ArgumentException("Invalid retained Advanced display choices.", nameof(settings));

        ClientPixelSize application;
        ClientPixelSize output;
        GameplayFrameLayout frame;
        int scale;
        if (settings.SettingsMode == ClientDisplaySettingsMode.Simple)
        {
            application = settings.SimpleResolution;
            output = application;
            frame = GameplayFrameLayout.Fill(application, output);
            scale = 100;
        }
        else
        {
            application = settings.AdvancedApplication;
            output = settings.AdvancedOutput;
            ValidateNativeSize(application);
            ValidateNativeSize(output);
            frame = settings.AdvancedFrameMode switch
            {
                ClientGameplayFrameMode.Fill => GameplayFrameLayout.Fill(application, output),
                ClientGameplayFrameMode.MaxFit => GameplayFrameLayout.MaxFitExact(application, output),
                ClientGameplayFrameMode.Custom => GameplayFrameLayout.Custom(
                    application, output, settings.AdvancedFrameSize),
                _ => throw new ArgumentException("Invalid Gameplay Frame mode.", nameof(settings)),
            };
            // No nonuniform World stretch: exact reduced ratios must agree.
            if ((long)frame.Frame.Width * output.Height != (long)frame.Frame.Height * output.Width)
                throw new ArgumentException("Gameplay Output aspect must match the Gameplay Frame.", nameof(settings));
            scale = settings.AdvancedInternalScalePercent;
        }

        ValidateNativeSize(application);
        ValidateNativeSize(output);
        if (scale is < MinimumInternalScalePercent or > MaximumInternalScalePercent)
            throw new ArgumentOutOfRangeException(nameof(settings), "Internal scale is outside native bounds.");
        ClientPixelSize internalSize = ClientPixelSize.Create(
            Math.Max(1, (int)(((long)output.Width * scale + 50) / 100)),
            Math.Max(1, (int)(((long)output.Height * scale + 50) / 100)));
        ValidateNativeSize(internalSize);
        return new ResolvedClientDisplayConfiguration(settings.SettingsMode, settings.DisplayMode,
            application, frame, output, scale, internalSize);
    }

    private static void ValidateNativeSize(ClientPixelSize size)
    {
        if (!size.IsValid || size.Width > MaximumDimension || size.Height > MaximumDimension ||
            (long)size.Width * size.Height > MaximumPixels)
            throw new ArgumentOutOfRangeException(nameof(size), "Display allocation exceeds bounded native limits.");
    }
}

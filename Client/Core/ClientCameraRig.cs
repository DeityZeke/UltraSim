namespace UltraSim.Client.Core;

public enum ClientCameraProjection : byte { Perspective = 1, Orthographic = 2 }
public enum ClientCameraTarget : byte { ControlledActor = 1 }
public enum ClientCameraFollow : byte { Immediate = 1 }
public enum ClientCameraAxisMode : byte { Locked = 1, Free = 2 }
public enum ClientCameraZoom : byte { Disabled = 1, Stepped = 2 }

/// <summary>
/// Finite, engine-neutral camera meaning. Distances and orthographic size are millimeters;
/// angles and FOV are tenths of a degree; look speed is millidegrees per pointer pixel.
/// The configured lens describes the maximum 16:9 stock-client envelope.
/// </summary>
public readonly record struct ClientCameraRig(
    ClientCameraProjection Projection,
    ClientCameraTarget Target,
    ClientCameraFollow Follow,
    ClientCameraAxisMode YawMode,
    ClientCameraAxisMode PitchMode,
    ClientCameraZoom Zoom,
    int PivotHeightMillimeters,
    int MinimumDistanceMillimeters,
    int DefaultDistanceMillimeters,
    int MaximumDistanceMillimeters,
    int DefaultYawTenths,
    int DefaultPitchTenths,
    int MinimumPitchTenths,
    int MaximumPitchTenths,
    int LookMillidegreesPerPixel,
    int ZoomStepMillimeters,
    int CanonicalFovTenths,
    int CanonicalOrthographicSizeMillimeters)
{
    public bool IsValid =>
        Projection is ClientCameraProjection.Perspective or ClientCameraProjection.Orthographic &&
        Target == ClientCameraTarget.ControlledActor && Follow == ClientCameraFollow.Immediate &&
        YawMode is ClientCameraAxisMode.Locked or ClientCameraAxisMode.Free &&
        PitchMode is ClientCameraAxisMode.Locked or ClientCameraAxisMode.Free &&
        Zoom is ClientCameraZoom.Disabled or ClientCameraZoom.Stepped &&
        PivotHeightMillimeters is >= 0 and <= 20_000 &&
        MinimumDistanceMillimeters is >= 2_000 and <= 80_000 &&
        DefaultDistanceMillimeters >= MinimumDistanceMillimeters &&
        MaximumDistanceMillimeters >= DefaultDistanceMillimeters &&
        MaximumDistanceMillimeters <= 80_000 &&
        DefaultYawTenths is >= -3_600 and <= 3_600 &&
        MinimumPitchTenths is >= 0 and < 900 &&
        DefaultPitchTenths >= MinimumPitchTenths &&
        MaximumPitchTenths >= DefaultPitchTenths && MaximumPitchTenths < 900 &&
        LookMillidegreesPerPixel is >= 1 and <= 2_000 &&
        (Zoom == ClientCameraZoom.Disabled && ZoomStepMillimeters == 0 ||
         Zoom == ClientCameraZoom.Stepped && ZoomStepMillimeters is >= 100 and <= 10_000) &&
        CanonicalFovTenths is >= 400 and <= 1_200 &&
        CanonicalOrthographicSizeMillimeters is >= 2_000 and <= 80_000;

    public ClientCameraRig WithLegacyLens(float distanceMeters, float fovDegrees)
    {
        if (!float.IsFinite(distanceMeters) || !float.IsFinite(fovDegrees))
            throw new ArgumentOutOfRangeException(nameof(distanceMeters));
        var value = this with
        {
            MinimumDistanceMillimeters = Math.Min(MinimumDistanceMillimeters,
                checked((int)MathF.Round(distanceMeters * 1_000f))),
            DefaultDistanceMillimeters = checked((int)MathF.Round(distanceMeters * 1_000f)),
            MaximumDistanceMillimeters = Math.Max(MaximumDistanceMillimeters,
                checked((int)MathF.Round(distanceMeters * 1_000f))),
            CanonicalFovTenths = checked((int)MathF.Round(fovDegrees * 10f)),
        };
        if (!value.IsValid) throw new ArgumentOutOfRangeException(nameof(distanceMeters));
        return value;
    }
}

/// <summary>Local preset labels are migration/UI metadata, not wire semantics.</summary>
public static class ClientStockCameraRigs
{
    public static ClientCameraRig Orbit { get; } = Stock(14_000, -401, 450, 700, true);
    public static ClientCameraRig ThirdPerson { get; } = Stock(8_500, -401, 195, 760, true);
    public static ClientCameraRig Independent { get; } = Stock(14_000, -450, 353, 700, false);

    public static ClientCameraRig FromLegacy(ClientCameraProfile profile) => profile switch
    {
        ClientCameraProfile.Orbit => Orbit,
        ClientCameraProfile.ThirdPersonFollow => ThirdPerson,
        ClientCameraProfile.IndependentMoveLook => Independent,
        _ => throw new ArgumentOutOfRangeException(nameof(profile)),
    };

    private static ClientCameraRig Stock(int distance, int yaw, int pitch, int fov, bool free) => new(
        ClientCameraProjection.Perspective, ClientCameraTarget.ControlledActor,
        ClientCameraFollow.Immediate,
        free ? ClientCameraAxisMode.Free : ClientCameraAxisMode.Locked,
        free ? ClientCameraAxisMode.Free : ClientCameraAxisMode.Locked,
        free ? ClientCameraZoom.Stepped : ClientCameraZoom.Disabled,
        1_600, 3_000, distance, 55_000, yaw, pitch, 115, 773, 344,
        free ? 2_000 : 0, fov, 20_000);
}

/// <summary>A local name does not contribute to effective camera or binding identity.</summary>
public sealed class ClientNamedControlProfile
{
    public const int MaximumProfiles = 16;
    public const int MaximumNameCharacters = 48;

    public ClientNamedControlProfile(string name, ClientControlGrammar grammar,
        ClientCameraRig cameraRig, IEnumerable<ClientPhysicalBinding> bindings)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaximumNameCharacters ||
            grammar is < ClientControlGrammar.OrbitCommand or > ClientControlGrammar.IndependentMoveLook ||
            !cameraRig.IsValid)
            throw new ArgumentException("A finite named profile is required.", nameof(name));
        Name = name.Trim();
        ControlGrammar = grammar;
        CameraRig = cameraRig;
        ClientPhysicalBinding[] copy = bindings.ToArray();
        _ = new ClientSettingsLayer(bindings: copy);
        Bindings = Array.AsReadOnly(copy);
    }

    public string Name { get; }
    public ClientControlGrammar ControlGrammar { get; }
    public ClientCameraRig CameraRig { get; }
    public IReadOnlyList<ClientPhysicalBinding> Bindings { get; }

    public ClientSettingsLayer AsLayer() => new(
        controlGrammar: ControlGrammar, cameraRig: CameraRig, bindings: Bindings);
}

/// <summary>Projection bounds in camera space. Display aspect can only crop the 16:9 envelope.</summary>
public readonly record struct ClientCanonicalViewProjection(
    ClientCameraProjection Projection,
    double CanonicalVerticalExtent,
    double CanonicalHorizontalExtent,
    double TargetVerticalExtent,
    double TargetHorizontalExtent,
    double TargetFrameAspect)
{
    public const double CanonicalAspect = 16d / 9d;

    public static ClientCanonicalViewProjection ForFrame(
        in ClientCameraRig rig, in GameplayFrameLayout layout)
    {
        if (!rig.IsValid || !layout.Application.IsValid || !layout.SelectedOutput.IsValid ||
            !layout.Frame.IsValid)
            throw new ArgumentException("A valid rig and realized Gameplay Frame are required.");
        double aspect = (double)layout.SelectedOutput.Width / layout.SelectedOutput.Height;
        double vertical = rig.Projection == ClientCameraProjection.Perspective
            ? Math.Tan(rig.CanonicalFovTenths * Math.PI / 3_600d)
            : rig.CanonicalOrthographicSizeMillimeters / 1_000d;
        double horizontal = vertical * CanonicalAspect;
        double targetVertical = aspect <= CanonicalAspect ? vertical : horizontal / aspect;
        double targetHorizontal = aspect <= CanonicalAspect ? vertical * aspect : horizontal;
        if (!double.IsFinite(targetVertical) || targetVertical <= 0 ||
            !double.IsFinite(targetHorizontal) || targetHorizontal <= 0)
            throw new ArgumentOutOfRangeException(nameof(layout));
        return new ClientCanonicalViewProjection(rig.Projection, vertical, horizontal,
            targetVertical, targetHorizontal, aspect);
    }

    public double TargetVerticalFovDegrees => Projection == ClientCameraProjection.Perspective
        ? 2d * Math.Atan(TargetVerticalExtent) * 180d / Math.PI : 0d;

    /// <summary>Temporary F1.8 compensation while Camera3D still owns the application viewport.</summary>
    public double MainViewportVerticalExtent(in GameplayFrameLayout layout)
    {
        if (!layout.Frame.IsValid || !layout.Application.IsValid)
            throw new ArgumentException("A valid realized Gameplay Frame is required.", nameof(layout));
        return TargetVerticalExtent * layout.Application.Height / layout.Frame.Height;
    }

    public double MainViewportVerticalFovDegrees(in GameplayFrameLayout layout) =>
        Projection == ClientCameraProjection.Perspective
            ? 2d * Math.Atan(MainViewportVerticalExtent(layout)) * 180d / Math.PI
            : 0d;
}

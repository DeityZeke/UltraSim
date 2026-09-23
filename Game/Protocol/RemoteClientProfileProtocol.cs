using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using UltraSim.Network;

namespace UltraSim.Game.Protocol;

public enum RemoteClientControlGrammar : byte
{
    OrbitCommand = 1,
    ThirdPersonDirect = 2,
    IndependentMoveLook = 3,
}

public enum RemoteClientCameraProfile : byte
{
    Orbit = 1,
    ThirdPersonFollow = 2,
    IndependentMoveLook = 3,
}

public enum RemoteClientCameraProjection : byte { Perspective = 1, Orthographic = 2 }
public enum RemoteClientCameraTarget : byte { ControlledActor = 1 }
public enum RemoteClientCameraFollow : byte { Immediate = 1 }
public enum RemoteClientCameraAxisMode : byte { Locked = 1, Free = 2 }
public enum RemoteClientCameraZoom : byte { Disabled = 1, Stepped = 2 }

/// <summary>Canonical fixed-unit camera recommendation. No engine name or executable behavior.</summary>
public readonly record struct RemoteClientCameraRig(
    RemoteClientCameraProjection Projection,
    RemoteClientCameraTarget Target,
    RemoteClientCameraFollow Follow,
    RemoteClientCameraAxisMode YawMode,
    RemoteClientCameraAxisMode PitchMode,
    RemoteClientCameraZoom Zoom,
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
    public const int EncodedSize = 6 + 12 * sizeof(int);

    public bool IsValid =>
        Projection is RemoteClientCameraProjection.Perspective or RemoteClientCameraProjection.Orthographic &&
        Target == RemoteClientCameraTarget.ControlledActor && Follow == RemoteClientCameraFollow.Immediate &&
        YawMode is RemoteClientCameraAxisMode.Locked or RemoteClientCameraAxisMode.Free &&
        PitchMode is RemoteClientCameraAxisMode.Locked or RemoteClientCameraAxisMode.Free &&
        Zoom is RemoteClientCameraZoom.Disabled or RemoteClientCameraZoom.Stepped &&
        PivotHeightMillimeters is >= 0 and <= 20_000 &&
        MinimumDistanceMillimeters is >= 2_000 and <= 80_000 &&
        DefaultDistanceMillimeters >= MinimumDistanceMillimeters &&
        MaximumDistanceMillimeters >= DefaultDistanceMillimeters && MaximumDistanceMillimeters <= 80_000 &&
        DefaultYawTenths is >= -3_600 and <= 3_600 &&
        MinimumPitchTenths is >= 0 and < 900 &&
        DefaultPitchTenths >= MinimumPitchTenths &&
        MaximumPitchTenths >= DefaultPitchTenths && MaximumPitchTenths < 900 &&
        LookMillidegreesPerPixel is >= 1 and <= 2_000 &&
        (Zoom == RemoteClientCameraZoom.Disabled && ZoomStepMillimeters == 0 ||
         Zoom == RemoteClientCameraZoom.Stepped && ZoomStepMillimeters is >= 100 and <= 10_000) &&
        CanonicalFovTenths is >= 400 and <= 1_200 &&
        CanonicalOrthographicSizeMillimeters is >= 2_000 and <= 80_000;

    internal void WriteCanonical(Span<byte> destination)
    {
        if (destination.Length < EncodedSize) throw new ArgumentException("Camera rig buffer is too short.");
        destination[0] = (byte)Projection;
        destination[1] = (byte)Target;
        destination[2] = (byte)Follow;
        destination[3] = (byte)YawMode;
        destination[4] = (byte)PitchMode;
        destination[5] = (byte)Zoom;
        Span<int> values = stackalloc int[]
        {
            PivotHeightMillimeters, MinimumDistanceMillimeters, DefaultDistanceMillimeters,
            MaximumDistanceMillimeters, DefaultYawTenths, DefaultPitchTenths,
            MinimumPitchTenths, MaximumPitchTenths, LookMillidegreesPerPixel,
            ZoomStepMillimeters, CanonicalFovTenths, CanonicalOrthographicSizeMillimeters,
        };
        for (int index = 0; index < values.Length; index++)
            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(6 + index * 4, 4), values[index]);
    }
}

public enum RemoteClientSemanticAction : byte
{
    ExitApplication = 1,
    OpenClientMenu = 2,
    Disconnect = 3,
    OpenSettings = 4,
    MoveLeft = 5,
    MoveRight = 6,
    MoveForward = 7,
    MoveBackward = 8,
    Select = 9,
    Destination = 10,
    ContextMenu = 11,
    Primary = 12,
    Interact = 13,
    OpenInventory = 14,
    Chat = 15,
    Jump = 16,
    CameraLook = 19,
    CameraZoomIn = 20,
    CameraZoomOut = 21,
}

public enum RemoteClientPhysicalInputKind : byte
{
    Keyboard = 1,
    MouseButton = 2,
    GamepadButton = 3,
}

[Flags]
public enum RemoteClientInputModifiers : byte
{
    None = 0,
    Shift = 1 << 0,
    Control = 1 << 1,
    Alt = 1 << 2,
    Meta = 1 << 3,
}

public enum RemoteClientProfileApplyStatus : byte
{
    Applied = 1,
    Rejected = 2,
}

public readonly record struct RemoteClientProfileHash(
    ulong Part0, ulong Part1, ulong Part2, ulong Part3)
{
    public bool IsValid => Part0 != 0 || Part1 != 0 || Part2 != 0 || Part3 != 0;

    internal static RemoteClientProfileHash FromBytes(ReadOnlySpan<byte> bytes) => new(
        BinaryPrimitives.ReadUInt64LittleEndian(bytes),
        BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]),
        BinaryPrimitives.ReadUInt64LittleEndian(bytes[16..]),
        BinaryPrimitives.ReadUInt64LittleEndian(bytes[24..]));
}

public readonly record struct RemoteClientProfileBinding(
    RemoteClientSemanticAction Action,
    RemoteClientPhysicalInputKind Kind,
    RemoteClientInputModifiers Modifiers,
    string Code);

/// <summary>
/// One bounded declarative stock-client recommendation. Its identity excludes transport/session
/// location and includes only canonical profile meaning plus its explicit revision.
/// </summary>
public sealed class RemoteClientProfileAdvertisement
{
    public const int MaximumBindings = 32;
    public const int MaximumInputCodeUtf8Bytes = 32;
    private const RemoteClientInputModifiers KnownModifiers = RemoteClientInputModifiers.Shift |
        RemoteClientInputModifiers.Control | RemoteClientInputModifiers.Alt | RemoteClientInputModifiers.Meta;
    private readonly ReadOnlyCollection<RemoteClientProfileBinding> _bindings;

    private RemoteClientProfileAdvertisement(
        ulong revision,
        RemoteClientControlGrammar grammar,
        RemoteClientCameraRig cameraRig,
        RemoteClientProfileBinding[] bindings)
    {
        Revision = revision;
        ControlGrammar = grammar;
        CameraRig = cameraRig;
        _bindings = Array.AsReadOnly(bindings);
        Hash = ComputeHash(this);
    }

    public ulong Revision { get; }
    public RemoteClientProfileHash Hash { get; }
    public RemoteClientControlGrammar ControlGrammar { get; }
    public RemoteClientCameraRig CameraRig { get; }
    public ReadOnlyCollection<RemoteClientProfileBinding> Bindings => _bindings;

    public static bool TryCreate(
        ulong revision,
        RemoteClientControlGrammar grammar,
        RemoteClientCameraRig cameraRig,
        IEnumerable<RemoteClientProfileBinding>? bindings,
        out RemoteClientProfileAdvertisement? profile)
    {
        profile = null;
        RemoteClientProfileBinding[] copy = (bindings ?? [])
            .OrderBy(static value => value.Action)
            .ToArray();
        if (revision == 0 ||
            grammar is < RemoteClientControlGrammar.OrbitCommand or
                > RemoteClientControlGrammar.IndependentMoveLook ||
            !cameraRig.IsValid || copy.Length > MaximumBindings)
            return false;
        for (int index = 0; index < copy.Length; index++)
        {
            ref readonly RemoteClientProfileBinding binding = ref copy[index];
            if (binding.Action is < RemoteClientSemanticAction.ExitApplication or
                    > RemoteClientSemanticAction.CameraZoomOut or
                    (RemoteClientSemanticAction)17 or (RemoteClientSemanticAction)18 ||
                binding.Kind is < RemoteClientPhysicalInputKind.Keyboard or
                    > RemoteClientPhysicalInputKind.GamepadButton ||
                (binding.Modifiers & ~KnownModifiers) != 0 ||
                binding.Kind != RemoteClientPhysicalInputKind.Keyboard &&
                    binding.Modifiers != RemoteClientInputModifiers.None ||
                !IsCanonicalCode(binding.Code) ||
                index != 0 && copy[index - 1].Action == binding.Action)
                return false;
        }
        if (copy.Select(static value => (value.Kind, value.Modifiers, value.Code))
            .Distinct().Count() != copy.Length)
            return false;
        profile = new RemoteClientProfileAdvertisement(
            revision, grammar, cameraRig, copy);
        return true;
    }

    /// <summary>Legacy construction helper; the v2 wire always carries the resulting rig.</summary>
    public static bool TryCreate(ulong revision, RemoteClientControlGrammar grammar,
        RemoteClientCameraProfile camera, uint cameraDistanceMillimeters, ushort cameraFovTenths,
        IEnumerable<RemoteClientProfileBinding>? bindings, out RemoteClientProfileAdvertisement? profile)
    {
        RemoteClientCameraRig stock = camera switch
        {
            RemoteClientCameraProfile.Orbit => Stock(14_000, -401, 450, 700, true),
            RemoteClientCameraProfile.ThirdPersonFollow => Stock(8_500, -401, 195, 760, true),
            RemoteClientCameraProfile.IndependentMoveLook => Stock(14_000, -450, 353, 700, false),
            _ => default,
        };
        return TryCreate(revision, grammar, stock with
        {
            MinimumDistanceMillimeters = Math.Min(stock.MinimumDistanceMillimeters,
                checked((int)cameraDistanceMillimeters)),
            DefaultDistanceMillimeters = checked((int)cameraDistanceMillimeters),
            MaximumDistanceMillimeters = Math.Max(stock.MaximumDistanceMillimeters,
                checked((int)cameraDistanceMillimeters)),
            CanonicalFovTenths = cameraFovTenths,
        }, bindings, out profile);
    }

    private static RemoteClientCameraRig Stock(int distance, int yaw, int pitch, int fov, bool free) => new(
        RemoteClientCameraProjection.Perspective, RemoteClientCameraTarget.ControlledActor,
        RemoteClientCameraFollow.Immediate,
        free ? RemoteClientCameraAxisMode.Free : RemoteClientCameraAxisMode.Locked,
        free ? RemoteClientCameraAxisMode.Free : RemoteClientCameraAxisMode.Locked,
        free ? RemoteClientCameraZoom.Stepped : RemoteClientCameraZoom.Disabled,
        1_600, 3_000, distance, 55_000, yaw, pitch, 115, 773, 344,
        free ? 2_000 : 0, fov, 20_000);

    private static bool IsCanonicalCode(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaximumInputCodeUtf8Bytes) return false;
        foreach (char c in value)
            if (!(c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-')) return false;
        return Encoding.UTF8.GetByteCount(value) <= MaximumInputCodeUtf8Bytes;
    }

    private static RemoteClientProfileHash ComputeHash(RemoteClientProfileAdvertisement value)
    {
        Span<byte> fixedBytes = stackalloc byte[8 + 1 + RemoteClientCameraRig.EncodedSize + 1];
        BinaryPrimitives.WriteUInt64LittleEndian(fixedBytes, value.Revision);
        fixedBytes[8] = (byte)value.ControlGrammar;
        value.CameraRig.WriteCanonical(fixedBytes[9..]);
        fixedBytes[^1] = checked((byte)value.Bindings.Count);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(fixedBytes);
        Span<byte> code = stackalloc byte[MaximumInputCodeUtf8Bytes];
        Span<byte> prefix = stackalloc byte[4];
        foreach (RemoteClientProfileBinding binding in value.Bindings)
        {
            int count = Encoding.UTF8.GetBytes(binding.Code, code);
            prefix[0] = (byte)binding.Action;
            prefix[1] = (byte)binding.Kind;
            prefix[2] = (byte)binding.Modifiers;
            prefix[3] = checked((byte)count);
            hash.AppendData(prefix);
            hash.AppendData(code[..count]);
        }
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        if (!hash.TryGetHashAndReset(digest, out int written) || written != digest.Length)
            throw new InvalidOperationException("The client profile hash could not be computed.");
        return RemoteClientProfileHash.FromBytes(digest);
    }
}

[NetworkMessage(142, "ultrasim.game.client-profile", 2, NetworkLane.Control)]
public partial struct RemoteClientProfileHeader
{
    public ulong ApplicationSessionEpoch;
    public ulong ProfileRevision;
    public ulong ProfileHash0;
    public ulong ProfileHash1;
    public ulong ProfileHash2;
    public ulong ProfileHash3;
    public byte ControlGrammar;
    public byte CameraProjection;
    public byte CameraTarget;
    public byte CameraFollow;
    public byte CameraYawMode;
    public byte CameraPitchMode;
    public byte CameraZoom;
    public int PivotHeightMillimeters;
    public int MinimumDistanceMillimeters;
    public int DefaultDistanceMillimeters;
    public int MaximumDistanceMillimeters;
    public int DefaultYawTenths;
    public int DefaultPitchTenths;
    public int MinimumPitchTenths;
    public int MaximumPitchTenths;
    public int LookMillidegreesPerPixel;
    public int ZoomStepMillimeters;
    public int CanonicalFovTenths;
    public int CanonicalOrthographicSizeMillimeters;
    public byte BindingCount;
}

[NetworkMessage(143, "ultrasim.game.client-profile-applied", 1, NetworkLane.Control)]
public partial struct RemoteClientProfileAppliedMessage
{
    public ulong ApplicationSessionEpoch;
    public ulong ProfileRevision;
    public ulong ProfileHash0;
    public ulong ProfileHash1;
    public ulong ProfileHash2;
    public ulong ProfileHash3;
    public RemoteClientProfileApplyStatus Status;
}

public static partial class UltraSimGameProtocol
{
    public const int MaximumClientProfileMessageBytes = 2_048;

    public static bool TryWriteClientProfile(
        Span<byte> destination,
        ulong applicationSessionEpoch,
        RemoteClientProfileAdvertisement profile,
        out int written)
    {
        written = 0;
        ArgumentNullException.ThrowIfNull(profile);
        if (applicationSessionEpoch == 0) return false;
        var header = new RemoteClientProfileHeader
        {
            ApplicationSessionEpoch = applicationSessionEpoch,
            ProfileRevision = profile.Revision,
            ProfileHash0 = profile.Hash.Part0,
            ProfileHash1 = profile.Hash.Part1,
            ProfileHash2 = profile.Hash.Part2,
            ProfileHash3 = profile.Hash.Part3,
            ControlGrammar = (byte)profile.ControlGrammar,
            CameraProjection = (byte)profile.CameraRig.Projection,
            CameraTarget = (byte)profile.CameraRig.Target,
            CameraFollow = (byte)profile.CameraRig.Follow,
            CameraYawMode = (byte)profile.CameraRig.YawMode,
            CameraPitchMode = (byte)profile.CameraRig.PitchMode,
            CameraZoom = (byte)profile.CameraRig.Zoom,
            PivotHeightMillimeters = profile.CameraRig.PivotHeightMillimeters,
            MinimumDistanceMillimeters = profile.CameraRig.MinimumDistanceMillimeters,
            DefaultDistanceMillimeters = profile.CameraRig.DefaultDistanceMillimeters,
            MaximumDistanceMillimeters = profile.CameraRig.MaximumDistanceMillimeters,
            DefaultYawTenths = profile.CameraRig.DefaultYawTenths,
            DefaultPitchTenths = profile.CameraRig.DefaultPitchTenths,
            MinimumPitchTenths = profile.CameraRig.MinimumPitchTenths,
            MaximumPitchTenths = profile.CameraRig.MaximumPitchTenths,
            LookMillidegreesPerPixel = profile.CameraRig.LookMillidegreesPerPixel,
            ZoomStepMillimeters = profile.CameraRig.ZoomStepMillimeters,
            CanonicalFovTenths = profile.CameraRig.CanonicalFovTenths,
            CanonicalOrthographicSizeMillimeters = profile.CameraRig.CanonicalOrthographicSizeMillimeters,
            BindingCount = checked((byte)profile.Bindings.Count),
        };
        var writer = new NetworkPacketWriter(destination);
        if (!RemoteClientProfileHeaderCodec.TryWrite(ref writer, in header)) return false;
        foreach (RemoteClientProfileBinding binding in profile.Bindings)
        {
            if (!writer.TryWriteByte((byte)binding.Action) ||
                !writer.TryWriteByte((byte)binding.Kind) ||
                !writer.TryWriteByte((byte)binding.Modifiers) ||
                !writer.TryWriteBoundedUtf8(binding.Code,
                    RemoteClientProfileAdvertisement.MaximumInputCodeUtf8Bytes))
                return false;
        }
        written = writer.WrittenCount;
        return written <= MaximumClientProfileMessageBytes;
    }

    public static bool TryReadClientProfile(
        ReadOnlySpan<byte> source,
        out ulong applicationSessionEpoch,
        out RemoteClientProfileAdvertisement? profile)
    {
        applicationSessionEpoch = 0;
        profile = null;
        if (source.Length > MaximumClientProfileMessageBytes) return false;
        var reader = new NetworkPacketReader(source);
        if (!RemoteClientProfileHeaderCodec.TryRead(ref reader, out RemoteClientProfileHeader header) ||
            header.ApplicationSessionEpoch == 0 || header.ProfileRevision == 0 ||
            header.BindingCount > RemoteClientProfileAdvertisement.MaximumBindings)
            return false;
        var bindings = new RemoteClientProfileBinding[header.BindingCount];
        for (int index = 0; index < bindings.Length; index++)
        {
            if (!reader.TryReadByte(out byte action) || !reader.TryReadByte(out byte kind) ||
                !reader.TryReadByte(out byte modifiers) ||
                !reader.TryReadBoundedUtf8Bytes(
                    RemoteClientProfileAdvertisement.MaximumInputCodeUtf8Bytes,
                    out ReadOnlySpan<byte> code))
                return false;
            if (index != 0 && (byte)bindings[index - 1].Action >= action)
                return false;
            bindings[index] = new RemoteClientProfileBinding(
                (RemoteClientSemanticAction)action,
                (RemoteClientPhysicalInputKind)kind,
                (RemoteClientInputModifiers)modifiers,
                Encoding.UTF8.GetString(code));
        }
        var rig = new RemoteClientCameraRig(
            (RemoteClientCameraProjection)header.CameraProjection,
            (RemoteClientCameraTarget)header.CameraTarget,
            (RemoteClientCameraFollow)header.CameraFollow,
            (RemoteClientCameraAxisMode)header.CameraYawMode,
            (RemoteClientCameraAxisMode)header.CameraPitchMode,
            (RemoteClientCameraZoom)header.CameraZoom,
            header.PivotHeightMillimeters, header.MinimumDistanceMillimeters,
            header.DefaultDistanceMillimeters, header.MaximumDistanceMillimeters,
            header.DefaultYawTenths, header.DefaultPitchTenths,
            header.MinimumPitchTenths, header.MaximumPitchTenths,
            header.LookMillidegreesPerPixel, header.ZoomStepMillimeters,
            header.CanonicalFovTenths, header.CanonicalOrthographicSizeMillimeters);
        if (!reader.IsComplete || !RemoteClientProfileAdvertisement.TryCreate(
                header.ProfileRevision,
                (RemoteClientControlGrammar)header.ControlGrammar,
                rig,
                bindings,
                out RemoteClientProfileAdvertisement? candidate) ||
            candidate!.Hash != new RemoteClientProfileHash(
                header.ProfileHash0, header.ProfileHash1,
                header.ProfileHash2, header.ProfileHash3))
            return false;
        applicationSessionEpoch = header.ApplicationSessionEpoch;
        profile = candidate;
        return true;
    }

    public static RemoteClientProfileAppliedMessage CreateClientProfileApplied(
        ulong applicationSessionEpoch,
        RemoteClientProfileAdvertisement profile,
        RemoteClientProfileApplyStatus status) => new()
    {
        ApplicationSessionEpoch = applicationSessionEpoch,
        ProfileRevision = profile.Revision,
        ProfileHash0 = profile.Hash.Part0,
        ProfileHash1 = profile.Hash.Part1,
        ProfileHash2 = profile.Hash.Part2,
        ProfileHash3 = profile.Hash.Part3,
        Status = status,
    };

    public static bool IsValidClientProfileApplied(in RemoteClientProfileAppliedMessage value) =>
        value.ApplicationSessionEpoch != 0 && value.ProfileRevision != 0 &&
        (value.ProfileHash0 != 0 || value.ProfileHash1 != 0 ||
         value.ProfileHash2 != 0 || value.ProfileHash3 != 0) &&
        value.Status is RemoteClientProfileApplyStatus.Applied or RemoteClientProfileApplyStatus.Rejected;
}

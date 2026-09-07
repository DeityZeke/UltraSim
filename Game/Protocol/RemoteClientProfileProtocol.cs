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
        RemoteClientCameraProfile camera,
        uint cameraDistanceMillimeters,
        ushort cameraFovTenths,
        RemoteClientProfileBinding[] bindings)
    {
        Revision = revision;
        ControlGrammar = grammar;
        CameraProfile = camera;
        CameraDistanceMillimeters = cameraDistanceMillimeters;
        CameraFovTenths = cameraFovTenths;
        _bindings = Array.AsReadOnly(bindings);
        Hash = ComputeHash(this);
    }

    public ulong Revision { get; }
    public RemoteClientProfileHash Hash { get; }
    public RemoteClientControlGrammar ControlGrammar { get; }
    public RemoteClientCameraProfile CameraProfile { get; }
    public uint CameraDistanceMillimeters { get; }
    public ushort CameraFovTenths { get; }
    public ReadOnlyCollection<RemoteClientProfileBinding> Bindings => _bindings;

    public static bool TryCreate(
        ulong revision,
        RemoteClientControlGrammar grammar,
        RemoteClientCameraProfile camera,
        uint cameraDistanceMillimeters,
        ushort cameraFovTenths,
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
            camera is < RemoteClientCameraProfile.Orbit or
                > RemoteClientCameraProfile.IndependentMoveLook ||
            cameraDistanceMillimeters is < 2_000 or > 80_000 ||
            cameraFovTenths is < 400 or > 1_200 || copy.Length > MaximumBindings)
            return false;
        for (int index = 0; index < copy.Length; index++)
        {
            ref readonly RemoteClientProfileBinding binding = ref copy[index];
            if (binding.Action is < RemoteClientSemanticAction.ExitApplication or
                    > RemoteClientSemanticAction.Jump ||
                binding.Kind is < RemoteClientPhysicalInputKind.Keyboard or
                    > RemoteClientPhysicalInputKind.GamepadButton ||
                (binding.Modifiers & ~KnownModifiers) != 0 ||
                binding.Kind != RemoteClientPhysicalInputKind.Keyboard &&
                    binding.Modifiers != RemoteClientInputModifiers.None ||
                !IsCanonicalCode(binding.Code) ||
                index != 0 && copy[index - 1].Action == binding.Action)
                return false;
        }
        profile = new RemoteClientProfileAdvertisement(
            revision, grammar, camera, cameraDistanceMillimeters, cameraFovTenths, copy);
        return true;
    }

    private static bool IsCanonicalCode(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaximumInputCodeUtf8Bytes) return false;
        foreach (char c in value)
            if (!(c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-')) return false;
        return Encoding.UTF8.GetByteCount(value) <= MaximumInputCodeUtf8Bytes;
    }

    private static RemoteClientProfileHash ComputeHash(RemoteClientProfileAdvertisement value)
    {
        Span<byte> fixedBytes = stackalloc byte[8 + 1 + 1 + 4 + 2 + 1];
        BinaryPrimitives.WriteUInt64LittleEndian(fixedBytes, value.Revision);
        fixedBytes[8] = (byte)value.ControlGrammar;
        fixedBytes[9] = (byte)value.CameraProfile;
        BinaryPrimitives.WriteUInt32LittleEndian(fixedBytes[10..], value.CameraDistanceMillimeters);
        BinaryPrimitives.WriteUInt16LittleEndian(fixedBytes[14..], value.CameraFovTenths);
        fixedBytes[16] = checked((byte)value.Bindings.Count);
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

[NetworkMessage(142, "ultrasim.game.client-profile", 1, NetworkLane.Control)]
public partial struct RemoteClientProfileHeader
{
    public ulong ApplicationSessionEpoch;
    public ulong ProfileRevision;
    public ulong ProfileHash0;
    public ulong ProfileHash1;
    public ulong ProfileHash2;
    public ulong ProfileHash3;
    public byte ControlGrammar;
    public byte CameraProfile;
    public uint CameraDistanceMillimeters;
    public ushort CameraFovTenths;
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
            CameraProfile = (byte)profile.CameraProfile,
            CameraDistanceMillimeters = profile.CameraDistanceMillimeters,
            CameraFovTenths = profile.CameraFovTenths,
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
        if (!reader.IsComplete || !RemoteClientProfileAdvertisement.TryCreate(
                header.ProfileRevision,
                (RemoteClientControlGrammar)header.ControlGrammar,
                (RemoteClientCameraProfile)header.CameraProfile,
                header.CameraDistanceMillimeters,
                header.CameraFovTenths,
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

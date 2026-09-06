using System.Collections.ObjectModel;
using System.Text;
using UltraSim.Network;

namespace UltraSim.Game.Protocol;

public enum RemoteContentRequirementPolicy : byte
{
    Required = 1,
    Optional = 2,
}

public enum RemoteContentSourceKind : byte
{
    Local = 1,
    ServerHostedStatic = 2,
    ExternalHttps = 3,
}

public enum RemoteContentApplyStatus : byte
{
    None = 0,
    Applied = 1,
    Rejected = 2,
}

/// <summary>Wire-stable SHA-256 projection without a dependency on client content storage.</summary>
public readonly record struct RemoteContentHash(ulong Part0, ulong Part1, ulong Part2, ulong Part3)
{
    public bool IsValid => Part0 != 0 || Part1 != 0 || Part2 != 0 || Part3 != 0;
}

public readonly record struct RemoteContentSource(RemoteContentSourceKind Kind, string Locator)
{
    public bool IsValid
    {
        get
        {
            if (Kind is < RemoteContentSourceKind.Local or > RemoteContentSourceKind.ExternalHttps ||
                string.IsNullOrWhiteSpace(Locator) || Locator != Locator.Trim() ||
                Encoding.UTF8.GetByteCount(Locator) > UltraSimGameProtocol.MaximumContentSourceLocatorUtf8Bytes)
                return false;
            if (Kind == RemoteContentSourceKind.Local)
                return Locator[0] != '/' && Locator[^1] != '/' &&
                    !Locator.Contains('\\') && !Locator.Contains(':') &&
                    Locator.Split('/').All(static segment => segment.Length > 0 &&
                        segment is not "." and not "..");
            if (!Uri.TryCreate(Locator, UriKind.Absolute, out Uri? uri) ||
                !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
                return false;
            return Kind == RemoteContentSourceKind.ServerHostedStatic
                ? uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps
                : uri.Scheme == Uri.UriSchemeHttps;
        }
    }
}

public sealed class RemoteContentPackageRequirement
{
    private readonly ReadOnlyCollection<RemoteContentSource> _sources;

    public RemoteContentPackageRequirement(
        string packageId,
        RemoteContentHash manifestHash,
        RemoteContentRequirementPolicy policy,
        IEnumerable<RemoteContentSource> sources)
    {
        ArgumentNullException.ThrowIfNull(packageId);
        ArgumentNullException.ThrowIfNull(sources);
        RemoteContentSource[] copy = sources.Take(
            UltraSimGameProtocol.MaximumContentSourcesPerPackage + 1).ToArray();
        if (string.IsNullOrWhiteSpace(packageId) || packageId != packageId.Trim() ||
            Encoding.UTF8.GetByteCount(packageId) > UltraSimGameProtocol.MaximumContentPackageIdUtf8Bytes ||
            !manifestHash.IsValid ||
            policy is not (RemoteContentRequirementPolicy.Required or RemoteContentRequirementPolicy.Optional) ||
            copy.Length is 0 || copy.Length > UltraSimGameProtocol.MaximumContentSourcesPerPackage ||
            copy.Any(static value => !value.IsValid) || copy.Distinct().Count() != copy.Length)
            throw new ArgumentException("The remote content package requirement is invalid.");
        PackageId = packageId;
        ManifestHash = manifestHash;
        Policy = policy;
        _sources = Array.AsReadOnly(copy);
    }

    public string PackageId { get; }
    public RemoteContentHash ManifestHash { get; }
    public RemoteContentRequirementPolicy Policy { get; }
    public ReadOnlyCollection<RemoteContentSource> Sources => _sources;
}

/// <summary>
/// One immutable authenticated content authorization. The expected resolved fingerprint remains
/// authority-side validation data and is deliberately not sent as part of the requirement.
/// </summary>
public sealed class RemoteContentAuthorization
{
    private readonly ReadOnlyCollection<RemoteContentPackageRequirement> _packages;

    public RemoteContentAuthorization(
        ulong realmHigh,
        ulong realmLow,
        ulong activationHigh,
        ulong activationLow,
        RemoteContentHash requiredBaseCatalogFingerprint,
        RemoteContentHash planHash,
        RemoteContentHash expectedResolvedCatalogFingerprint,
        IEnumerable<RemoteContentPackageRequirement> packages)
    {
        ArgumentNullException.ThrowIfNull(packages);
        RemoteContentPackageRequirement[] copy = packages.Take(
            UltraSimGameProtocol.MaximumContentPackages + 1).ToArray();
        if ((realmHigh == 0 && realmLow == 0) || (activationHigh == 0 && activationLow == 0) ||
            !requiredBaseCatalogFingerprint.IsValid || !planHash.IsValid ||
            copy.Length > UltraSimGameProtocol.MaximumContentPackages ||
            copy.Any(static value => value is null) ||
            copy.Select(static value => value.PackageId).Distinct(StringComparer.Ordinal).Count() != copy.Length)
            throw new ArgumentException("The remote content authorization is invalid.");
        RealmHigh = realmHigh;
        RealmLow = realmLow;
        ActivationHigh = activationHigh;
        ActivationLow = activationLow;
        RequiredBaseCatalogFingerprint = requiredBaseCatalogFingerprint;
        PlanHash = planHash;
        ExpectedResolvedCatalogFingerprint = expectedResolvedCatalogFingerprint;
        _packages = Array.AsReadOnly(copy);
    }

    public ulong RealmHigh { get; }
    public ulong RealmLow { get; }
    public ulong ActivationHigh { get; }
    public ulong ActivationLow { get; }
    public RemoteContentHash RequiredBaseCatalogFingerprint { get; }
    public RemoteContentHash PlanHash { get; }
    public RemoteContentHash ExpectedResolvedCatalogFingerprint { get; }
    public ReadOnlyCollection<RemoteContentPackageRequirement> Packages => _packages;
    public bool IsAuthorityValid => ExpectedResolvedCatalogFingerprint.IsValid;
}

public readonly record struct RemoteContentRequirement(
    ulong ApplicationSessionEpoch,
    ulong RequirementSequence,
    RemoteContentAuthorization Authorization)
{
    public bool IsValid => ApplicationSessionEpoch != 0 && RequirementSequence != 0 && Authorization is not null;
}

/// <summary>Header followed by ordered package requirements and their ordered approved sources.</summary>
[NetworkMessage(140, "ultrasim.game.content-requirement", 1, NetworkLane.Event)]
public partial struct RemoteContentRequirementHeader
{
    public ulong ApplicationSessionEpoch;
    public ulong RequirementSequence;
    public ulong ApplicationRealmHigh;
    public ulong ApplicationRealmLow;
    public ulong ActivationHigh;
    public ulong ActivationLow;
    public ulong BaseHash0;
    public ulong BaseHash1;
    public ulong BaseHash2;
    public ulong BaseHash3;
    public ulong PlanHash0;
    public ulong PlanHash1;
    public ulong PlanHash2;
    public ulong PlanHash3;
    public byte PackageCount;
}

/// <summary>Explicit semantic application result for one authenticated content requirement.</summary>
[NetworkMessage(141, "ultrasim.game.content-set-applied", 1, NetworkLane.Control)]
public partial struct RemoteContentSetAppliedMessage
{
    public ulong ApplicationSessionEpoch;
    public ulong RequirementSequence;
    public ulong ApplicationRealmHigh;
    public ulong ApplicationRealmLow;
    public ulong ActivationHigh;
    public ulong ActivationLow;
    public ulong PlanHash0;
    public ulong PlanHash1;
    public ulong PlanHash2;
    public ulong PlanHash3;
    public ulong ResolvedHash0;
    public ulong ResolvedHash1;
    public ulong ResolvedHash2;
    public ulong ResolvedHash3;
    public RemoteContentApplyStatus Status;
}

public static partial class UltraSimGameProtocol
{
    public const int MaximumContentPackages = 64;
    public const int MaximumContentSourcesPerPackage = 8;
    public const int MaximumContentPackageIdUtf8Bytes = 96;
    public const int MaximumContentSourceLocatorUtf8Bytes = 2_048;
    public const int MaximumContentRequirementMessageBytes = MaximumMessageBytes;

    public static bool TryWriteContentRequirement(
        Span<byte> destination,
        in RemoteContentRequirement requirement,
        out int written)
    {
        written = 0;
        if (!requirement.IsValid)
            return false;
        RemoteContentAuthorization authorization = requirement.Authorization;
        var header = new RemoteContentRequirementHeader
        {
            ApplicationSessionEpoch = requirement.ApplicationSessionEpoch,
            RequirementSequence = requirement.RequirementSequence,
            ApplicationRealmHigh = authorization.RealmHigh,
            ApplicationRealmLow = authorization.RealmLow,
            ActivationHigh = authorization.ActivationHigh,
            ActivationLow = authorization.ActivationLow,
            BaseHash0 = authorization.RequiredBaseCatalogFingerprint.Part0,
            BaseHash1 = authorization.RequiredBaseCatalogFingerprint.Part1,
            BaseHash2 = authorization.RequiredBaseCatalogFingerprint.Part2,
            BaseHash3 = authorization.RequiredBaseCatalogFingerprint.Part3,
            PlanHash0 = authorization.PlanHash.Part0,
            PlanHash1 = authorization.PlanHash.Part1,
            PlanHash2 = authorization.PlanHash.Part2,
            PlanHash3 = authorization.PlanHash.Part3,
            PackageCount = checked((byte)authorization.Packages.Count),
        };
        var writer = new NetworkPacketWriter(destination);
        if (!RemoteContentRequirementHeaderCodec.TryWrite(ref writer, in header)) return false;
        foreach (RemoteContentPackageRequirement package in authorization.Packages)
        {
            if (!writer.TryWriteBoundedUtf8(package.PackageId, MaximumContentPackageIdUtf8Bytes) ||
                !WriteHash(ref writer, package.ManifestHash) ||
                !writer.TryWriteByte((byte)package.Policy) ||
                !writer.TryWriteByte(checked((byte)package.Sources.Count)))
                return false;
            foreach (RemoteContentSource source in package.Sources)
                if (!writer.TryWriteByte((byte)source.Kind) ||
                    !writer.TryWriteBoundedUtf8(source.Locator, MaximumContentSourceLocatorUtf8Bytes))
                    return false;
        }
        written = writer.WrittenCount;
        return written <= MaximumContentRequirementMessageBytes;
    }

    public static bool TryReadContentRequirement(
        ReadOnlySpan<byte> source,
        out RemoteContentRequirement requirement)
    {
        requirement = default;
        if (source.Length > MaximumContentRequirementMessageBytes) return false;
        var reader = new NetworkPacketReader(source);
        if (!RemoteContentRequirementHeaderCodec.TryRead(
                ref reader, out RemoteContentRequirementHeader header) ||
            header.ApplicationSessionEpoch == 0 || header.RequirementSequence == 0 ||
            (header.ApplicationRealmHigh == 0 && header.ApplicationRealmLow == 0) ||
            (header.ActivationHigh == 0 && header.ActivationLow == 0) ||
            header.PackageCount > MaximumContentPackages)
            return false;
        var packages = new RemoteContentPackageRequirement[header.PackageCount];
        try
        {
            for (int index = 0; index < packages.Length; index++)
            {
                if (!reader.TryReadBoundedUtf8Bytes(
                        MaximumContentPackageIdUtf8Bytes, out ReadOnlySpan<byte> packageBytes) ||
                    packageBytes.IsEmpty || !ReadHash(ref reader, out RemoteContentHash manifestHash) ||
                    !reader.TryReadByte(out byte policy) ||
                    !reader.TryReadByte(out byte sourceCount) ||
                    sourceCount is 0 or > MaximumContentSourcesPerPackage)
                    return false;
                var sources = new RemoteContentSource[sourceCount];
                for (int sourceIndex = 0; sourceIndex < sources.Length; sourceIndex++)
                {
                    if (!reader.TryReadByte(out byte kind) ||
                        !reader.TryReadBoundedUtf8Bytes(
                            MaximumContentSourceLocatorUtf8Bytes, out ReadOnlySpan<byte> locatorBytes) ||
                        locatorBytes.IsEmpty)
                        return false;
                    sources[sourceIndex] = new RemoteContentSource(
                        (RemoteContentSourceKind)kind, Encoding.UTF8.GetString(locatorBytes));
                }
                packages[index] = new RemoteContentPackageRequirement(
                    Encoding.UTF8.GetString(packageBytes), manifestHash,
                    (RemoteContentRequirementPolicy)policy, sources);
            }
            if (!reader.IsComplete) return false;
            var authorization = new RemoteContentAuthorization(
                header.ApplicationRealmHigh, header.ApplicationRealmLow,
                header.ActivationHigh, header.ActivationLow,
                new RemoteContentHash(header.BaseHash0, header.BaseHash1, header.BaseHash2, header.BaseHash3),
                new RemoteContentHash(header.PlanHash0, header.PlanHash1, header.PlanHash2, header.PlanHash3),
                default, packages);
            requirement = new RemoteContentRequirement(
                header.ApplicationSessionEpoch, header.RequirementSequence, authorization);
            return true;
        }
        catch (ArgumentException)
        {
            requirement = default;
            return false;
        }
    }

    public static bool IsValidContentApplied(in RemoteContentSetAppliedMessage message) =>
        message.ApplicationSessionEpoch != 0 && message.RequirementSequence != 0 &&
        (message.ApplicationRealmHigh != 0 || message.ApplicationRealmLow != 0) &&
        (message.ActivationHigh != 0 || message.ActivationLow != 0) &&
        new RemoteContentHash(
            message.PlanHash0, message.PlanHash1, message.PlanHash2, message.PlanHash3).IsValid &&
        message.Status is RemoteContentApplyStatus.Applied or RemoteContentApplyStatus.Rejected &&
        (message.Status == RemoteContentApplyStatus.Applied
            ? new RemoteContentHash(
                message.ResolvedHash0, message.ResolvedHash1,
                message.ResolvedHash2, message.ResolvedHash3).IsValid
            : message.ResolvedHash0 == 0 && message.ResolvedHash1 == 0 &&
              message.ResolvedHash2 == 0 && message.ResolvedHash3 == 0);

    private static bool WriteHash(ref NetworkPacketWriter writer, RemoteContentHash hash) =>
        hash.IsValid && writer.TryWriteUInt64(hash.Part0) && writer.TryWriteUInt64(hash.Part1) &&
        writer.TryWriteUInt64(hash.Part2) && writer.TryWriteUInt64(hash.Part3);

    private static bool ReadHash(ref NetworkPacketReader reader, out RemoteContentHash hash)
    {
        hash = default;
        if (!reader.TryReadUInt64(out ulong part0) || !reader.TryReadUInt64(out ulong part1) ||
            !reader.TryReadUInt64(out ulong part2) || !reader.TryReadUInt64(out ulong part3))
            return false;
        hash = new RemoteContentHash(part0, part1, part2, part3);
        return hash.IsValid;
    }
}

using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using UltraSim.Game.Application;

namespace UltraSim.Client.Content;

/// <summary>Stable authored identity of one scope-owned content activation.</summary>
[StructLayout(LayoutKind.Sequential, Size = 16)]
public readonly struct ContentActivationId :
    IEquatable<ContentActivationId>, IComparable<ContentActivationId>
{
    public ContentActivationId(ulong high, ulong low)
    {
        if (high == 0 && low == 0) throw new ArgumentOutOfRangeException(nameof(high));
        High = high;
        Low = low;
    }

    public ulong High { get; }
    public ulong Low { get; }
    public bool IsValid => High != 0 || Low != 0;

    public int CompareTo(ContentActivationId other)
    {
        int high = High.CompareTo(other.High);
        return high != 0 ? high : Low.CompareTo(other.Low);
    }

    public bool Equals(ContentActivationId other) => High == other.High && Low == other.Low;
    public override bool Equals(object? obj) => obj is ContentActivationId other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(High, Low);
    public override string ToString() => IsValid ? $"{High:x16}{Low:x16}" : "invalid";
    public static bool operator ==(ContentActivationId left, ContentActivationId right) => left.Equals(right);
    public static bool operator !=(ContentActivationId left, ContentActivationId right) => !left.Equals(right);
}

/// <summary>One exact package requirement in authenticated activation order.</summary>
public readonly record struct ContentActivationPackage(
    ContentPackageId PackageId,
    ContentHash ManifestHash,
    ContentRequirementPolicy Requirement)
{
    public bool IsValid => PackageId.IsValid && ManifestHash.IsValid &&
        Requirement is ContentRequirementPolicy.Required or ContentRequirementPolicy.Optional;
}

/// <summary>
/// Immutable Game-authorized package order. Physical byte sources are intentionally absent from
/// this semantic identity.
/// </summary>
public sealed class ContentActivationPlan
{
    private readonly ReadOnlyCollection<ContentActivationPackage> _packages;

    private ContentActivationPlan(
        ContentScopeKey scope,
        ContentActivationId activation,
        ContentHash requiredBaseCatalogFingerprint,
        ContentActivationPackage[] packages)
    {
        Scope = scope;
        Activation = activation;
        RequiredBaseCatalogFingerprint = requiredBaseCatalogFingerprint;
        _packages = Array.AsReadOnly(packages);
        PlanHash = ContentCanonicalHash.ComputePlan(this);
    }

    public ContentScopeKey Scope { get; }
    /// <summary>Compatibility projection for remote protocol adapters. Invalid for Local.</summary>
    public ApplicationRealmId Realm => Scope.Realm;
    public ContentActivationId Activation { get; }
    public ContentScopeActivationId ScopeActivation => new(Scope, Activation);
    public ContentRealmActivationId RealmActivation => new(Realm, Activation);
    public ContentHash RequiredBaseCatalogFingerprint { get; }
    public ReadOnlyCollection<ContentActivationPackage> Packages => _packages;
    public ContentHash PlanHash { get; }

    public static bool TryCreate(
        ContentScopeKey scope,
        ContentActivationId activation,
        ContentHash requiredBaseCatalogFingerprint,
        IEnumerable<ContentActivationPackage>? packages,
        out ContentActivationPlan? plan,
        out string? error,
        ContentLimits limits = default)
    {
        plan = null;
        error = null;
        limits = ContentLimits.Resolve(limits);
        if (!scope.IsValid || !activation.IsValid || !requiredBaseCatalogFingerprint.IsValid)
            return Fail("The activation lineage or required base catalog is invalid.", out error);
        if (!ContentBoundedCopy.TryCopy(packages, limits.MaximumPackages,
                out ContentActivationPackage[] copy))
            return Fail("The activation exceeds its package bound.", out error);
        var identities = new HashSet<ContentPackageId>();
        foreach (ContentActivationPackage package in copy)
        {
            if (!package.IsValid)
                return Fail("The activation contains an invalid package requirement.", out error);
            if (!identities.Add(package.PackageId))
                return Fail("One package identity may occur only once in an activation.", out error);
        }
        plan = new ContentActivationPlan(scope, activation, requiredBaseCatalogFingerprint, copy);
        return true;
    }

    /// <summary>Authenticated-remote convenience adapter over the scope-aware implementation.</summary>
    public static bool TryCreate(
        ApplicationRealmId realm,
        ContentActivationId activation,
        ContentHash requiredBaseCatalogFingerprint,
        IEnumerable<ContentActivationPackage>? packages,
        out ContentActivationPlan? plan,
        out string? error,
        ContentLimits limits = default)
    {
        if (!realm.IsValid)
        {
            plan = null;
            return Fail("The activation lineage or required base catalog is invalid.", out error);
        }
        return TryCreate(ContentScopeKey.ForRealm(realm), activation, requiredBaseCatalogFingerprint,
            packages, out plan, out error, limits);
    }

    private static bool Fail(string message, out string? error)
    {
        error = message;
        return false;
    }
}

/// <summary>Stable scope-owned activation identity used by the generic content store.</summary>
public readonly record struct ContentScopeActivationId(
    ContentScopeKey Scope,
    ContentActivationId Activation)
{
    public bool IsValid => Scope.IsValid && Activation.IsValid;
}

/// <summary>Exact authorization key for one immutable plan in one content scope.</summary>
public readonly record struct ContentScopeActivationKey(
    ContentScopeActivationId Identity,
    ContentHash PlanHash)
{
    public bool IsValid => Identity.IsValid && PlanHash.IsValid;
}

/// <summary>
/// Stable realm-scoped authored identity. Stores key by this value so reuse with a different plan
/// hash is detectable rather than becoming a second unrelated record.
/// </summary>
public readonly record struct ContentRealmActivationId(
    ApplicationRealmId Realm,
    ContentActivationId Activation)
{
    public bool IsValid => Realm.IsValid && Activation.IsValid;
}

/// <summary>Exact authorization key for one immutable plan bound to its stable realm activation.</summary>
public readonly record struct ContentRealmActivationKey(
    ContentRealmActivationId Identity,
    ContentHash PlanHash)
{
    public bool IsValid => Identity.IsValid && PlanHash.IsValid;
}

/// <summary>Process-local handle for one immutable engine-neutral catalog publication.</summary>
[StructLayout(LayoutKind.Sequential, Size = 8)]
public readonly struct ContentCatalogGenerationId : IEquatable<ContentCatalogGenerationId>
{
    private ContentCatalogGenerationId(ulong value) => Value = value;
    public ulong Value { get; }
    public bool IsValid => Value != 0;
    internal static ContentCatalogGenerationId Next()
    {
        long value = Interlocked.Increment(ref Sequence.Value);
        if (value <= 0) throw new InvalidOperationException("Content catalog generation identity exhausted.");
        return new ContentCatalogGenerationId((ulong)value);
    }
    public bool Equals(ContentCatalogGenerationId other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is ContentCatalogGenerationId other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => IsValid ? Value.ToString() : "invalid";
    public static bool operator ==(ContentCatalogGenerationId left, ContentCatalogGenerationId right) =>
        left.Equals(right);
    public static bool operator !=(ContentCatalogGenerationId left, ContentCatalogGenerationId right) =>
        !left.Equals(right);

    private static class Sequence
    {
        internal static long Value;
    }
}

/// <summary>
/// Exact semantic application proof. It becomes transport data only in a later remote checkpoint.
/// </summary>
public readonly record struct ContentSetAppliedProof(
    ulong ApplicationSessionEpoch,
    ApplicationRealmId Realm,
    ContentActivationId Activation,
    ContentHash PlanHash,
    ContentHash ResolvedCatalogFingerprint)
{
    public bool IsValid => ApplicationSessionEpoch != 0 && Realm.IsValid && Activation.IsValid &&
        PlanHash.IsValid && ResolvedCatalogFingerprint.IsValid;
}

/// <summary>
/// Exact semantic authorization applied for one client application lifetime. Presentation
/// generations, renderer handles, and optional actor art are deliberately absent.
/// </summary>
public readonly record struct AuthorizedSemanticActivation(
    ContentScopeKey Scope,
    ContentActivationId Activation,
    ContentHash PlanHash,
    ContentHash ResolvedFingerprint,
    ulong ApplicationSessionEpoch)
{
    public bool IsValid => Scope.IsValid && Activation.IsValid && PlanHash.IsValid &&
        ResolvedFingerprint.IsValid && ApplicationSessionEpoch != 0;
}

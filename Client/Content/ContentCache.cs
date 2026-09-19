using System.Buffers.Binary;
using UltraSim.Game.Application;

namespace UltraSim.Client.Content;

/// <summary>
/// Source-independent persistent store for verified passive content. Physical blob identity is
/// global; manifest and activation authorization remain scoped to Local or an authenticated realm.
/// </summary>
public sealed class ContentCache : IDisposable
{
    private const string BlobExtension = ".blob";
    private const string ManifestExtension = ".manifest";
    private const string ActivationExtension = ".activation";
    private const string AccessExtension = ".access";

    private readonly object _sync = new();
    private readonly string _root;
    private readonly string _blobsRoot;
    private readonly string _scopesRoot;
    private readonly string _localScopeRoot;
    private readonly string _realmsRoot;
    private readonly string _accessRoot;
    private readonly string _stagingRoot;
    private readonly FileStream _writerLock;
    private readonly ContentCacheLimits _cacheLimits;
    private readonly ContentLimits _contentLimits;
    private readonly Dictionary<ContentHash, int> _liveBlobPins = [];
    private readonly Dictionary<ContentCacheManifestKey, int> _liveManifestPins = [];
    private int _liveCatalogLeases;
    private bool _disposed;
    private long _lastAccessTicks;
    private ulong _installs;
    private ulong _reusedBlobs;
    private ulong _corruptionsDetected;
    private ulong _blobsRepaired;
    private ulong _manifestsRepaired;
    private ulong _blobsEvicted;
    private long _bytesEvicted;

    private ContentCache(
        string root,
        FileStream writerLock,
        ContentCacheLimits cacheLimits,
        ContentLimits contentLimits)
    {
        _root = root;
        _writerLock = writerLock;
        _cacheLimits = cacheLimits;
        _contentLimits = contentLimits;
        _blobsRoot = Path.Combine(root, "blobs", "sha256");
        _scopesRoot = Path.Combine(root, "scopes");
        _localScopeRoot = Path.Combine(_scopesRoot, "local");
        _realmsRoot = Path.Combine(root, "realms");
        _accessRoot = Path.Combine(root, "metadata", "access");
        _stagingRoot = Path.Combine(root, "staging");
    }

    public string RootPath => _root;

    public static bool TryOpen(
        string rootPath,
        out ContentCache? cache,
        out ContentCacheError error,
        ContentCacheLimits cacheLimits = default,
        ContentLimits contentLimits = default)
    {
        cache = null;
        error = ContentCacheError.None;
        cacheLimits = ContentCacheLimits.Resolve(cacheLimits);
        contentLimits = ContentLimits.Resolve(contentLimits);
        if (string.IsNullOrWhiteSpace(rootPath))
            return Fail(ContentCacheError.InvalidRequest, out error);

        FileStream? writerLock = null;
        try
        {
            string root = Path.GetFullPath(rootPath);
            Directory.CreateDirectory(root);
            if (IsReparsePoint(root))
                return Fail(ContentCacheError.UnsafePath, out error);

            string blobs = Path.Combine(root, "blobs", "sha256");
            string scopes = Path.Combine(root, "scopes");
            string local = Path.Combine(scopes, "local");
            string realms = Path.Combine(root, "realms");
            string access = Path.Combine(root, "metadata", "access");
            string staging = Path.Combine(root, "staging");
            foreach (string directory in new[] { blobs, scopes, local, realms, access, staging })
            {
                Directory.CreateDirectory(directory);
                if (IsReparsePoint(directory))
                    return Fail(ContentCacheError.UnsafePath, out error);
            }

            string writerLockPath = Path.Combine(root, "writer.lock");
            if (File.Exists(writerLockPath) && IsReparsePoint(writerLockPath))
                return Fail(ContentCacheError.UnsafePath, out error);

            try
            {
                writerLock = new FileStream(writerLockPath, FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None, 1, FileOptions.WriteThrough);
            }
            catch (IOException)
            {
                return Fail(ContentCacheError.CacheBusy, out error);
            }

            foreach (string path in Directory.EnumerateFiles(staging))
            {
                if (IsReparsePoint(path))
                    return Fail(ContentCacheError.UnsafePath, out error);
                File.Delete(path);
            }

            cache = new ContentCache(root, writerLock, cacheLimits, contentLimits);
            writerLock = null;
            if (!cache.TryValidateStoreBounds(out error))
            {
                cache.Dispose();
                cache = null;
                return false;
            }
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return Fail(ContentCacheError.UnsafePath, out error);
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or NotSupportedException)
        {
            return Fail(ContentCacheError.IoFailure, out error);
        }
        finally
        {
            writerLock?.Dispose();
        }
    }

    public bool TryInstallPackage(
        ApplicationRealmId realm,
        ContentPackageManifest manifest,
        IEnumerable<ContentFilePayload>? payloads,
        out InstalledContentPackage installed,
        out ContentCacheError error)
    {
        if (!realm.IsValid)
        {
            installed = default;
            return Fail(ContentCacheError.InvalidRequest, out error);
        }
        return TryInstallPackage(ContentScopeKey.ForRealm(realm), manifest, payloads, out installed, out error);
    }

    public bool TryInstallPackage(
        ContentScopeKey scope,
        ContentPackageManifest manifest,
        IEnumerable<ContentFilePayload>? payloads,
        out InstalledContentPackage installed,
        out ContentCacheError error)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        installed = default;
        error = ContentCacheError.None;
        if (!TryCopyPayloads(payloads, manifest.Files.Count, out ContentFilePayload[] copy) ||
            !ContentPackageValidator.TryValidatePayloads(manifest, copy, out _, out _))
            return Fail(ContentCacheError.PayloadValidationFailed, out error);
        lock (_sync)
            return TryInstallPackageCore(scope, manifest, copy, out installed, out error);
    }

    /// <summary>
    /// Installs a complete package from only the payloads not already valid in the global CAS.
    /// Publication remains transactional: every descriptor is valid before scope metadata moves.
    /// </summary>
    public bool TryInstallPackageDelta(
        ApplicationRealmId realm,
        ContentPackageManifest manifest,
        IEnumerable<ContentFilePayload>? acquiredPayloads,
        out InstalledContentPackage installed,
        out ContentCacheError error)
    {
        if (!realm.IsValid)
        {
            installed = default;
            return Fail(ContentCacheError.InvalidRequest, out error);
        }
        return TryInstallPackageDelta(ContentScopeKey.ForRealm(realm), manifest, acquiredPayloads,
            out installed, out error);
    }

    public bool TryInstallPackageDelta(
        ContentScopeKey scope,
        ContentPackageManifest manifest,
        IEnumerable<ContentFilePayload>? acquiredPayloads,
        out InstalledContentPackage installed,
        out ContentCacheError error)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        installed = default;
        error = ContentCacheError.None;
        if (!TryCopyPayloads(acquiredPayloads, manifest.Files.Count, out ContentFilePayload[] copy))
            return Fail(ContentCacheError.PayloadValidationFailed, out error);
        lock (_sync)
            return TryInstallPackageCore(scope, manifest, copy, out installed, out error);
    }

    /// <summary>Plans exact missing/corrupt blob work without publishing or mutating the cache.</summary>
    public bool TryPlanPackageInstall(
        ApplicationRealmId realm,
        ContentPackageManifest manifest,
        out ContentPackageInstallPlan? plan,
        out ContentCacheError error)
    {
        if (!realm.IsValid)
        {
            plan = null;
            return Fail(ContentCacheError.InvalidRequest, out error);
        }
        return TryPlanPackageInstall(ContentScopeKey.ForRealm(realm), manifest, out plan, out error);
    }

    public bool TryPlanPackageInstall(
        ContentScopeKey scope,
        ContentPackageManifest manifest,
        out ContentPackageInstallPlan? plan,
        out ContentCacheError error)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        lock (_sync)
        {
            plan = null;
            if (_disposed || !scope.IsValid)
                return Fail(ContentCacheError.InvalidRequest, out error);
            if (manifest.TrustClass != ContentTrustClass.ScopeAuthorizedPassive)
                return Fail(ContentCacheError.UnsupportedTrustClass, out error);
            try
            {
                string manifestPath = GetManifestPath(scope, manifest.ManifestHash);
                bool manifestNeedsRepair = false;
                if (File.Exists(manifestPath) &&
                    (!TryReadCanonicalManifest(manifestPath, manifest.ManifestHash,
                        out ContentPackageManifest? existing) || existing!.PackageId != manifest.PackageId))
                {
                    manifestNeedsRepair = true;
                    SaturatingIncrement(ref _corruptionsDetected);
                }
                var required = new List<ContentFileDescriptor>();
                var visited = new HashSet<ContentHash>();
                int reused = 0, corrupt = 0;
                foreach (ContentFileDescriptor descriptor in manifest.Files)
                {
                    if (!visited.Add(descriptor.Sha256)) continue;
                    string blobPath = GetBlobPath(descriptor.Sha256);
                    if (!File.Exists(blobPath))
                    {
                        required.Add(descriptor);
                    }
                    else if (TryValidateBlob(blobPath, descriptor))
                    {
                        reused++;
                    }
                    else
                    {
                        corrupt++;
                        SaturatingIncrement(ref _corruptionsDetected);
                        required.Add(descriptor);
                    }
                }
                plan = new ContentPackageInstallPlan(required.ToArray(), reused, corrupt, manifestNeedsRepair);
                error = ContentCacheError.None;
                return true;
            }
            catch (Exception exception) when (IsStorageException(exception))
            {
                return Fail(ContentCacheError.IoFailure, out error);
            }
        }
    }

    /// <summary>Reads authorized realm manifest metadata without treating blob absence as success.</summary>
    public bool TryOpenPackageManifest(
        ApplicationRealmId realm,
        ContentPackageId packageId,
        ContentHash manifestHash,
        out ContentPackageManifest? manifest,
        out ContentCacheError error)
    {
        if (!realm.IsValid)
        {
            manifest = null;
            return Fail(ContentCacheError.InvalidRequest, out error);
        }
        return TryOpenPackageManifest(ContentScopeKey.ForRealm(realm), packageId, manifestHash,
            out manifest, out error);
    }

    /// <summary>Reads scope-authorized manifest metadata without treating blob absence as success.</summary>
    public bool TryOpenPackageManifest(
        ContentScopeKey scope,
        ContentPackageId packageId,
        ContentHash manifestHash,
        out ContentPackageManifest? manifest,
        out ContentCacheError error)
    {
        lock (_sync)
        {
            manifest = null;
            if (_disposed || !scope.IsValid || !packageId.IsValid || !manifestHash.IsValid)
                return Fail(ContentCacheError.InvalidRequest, out error);
            try
            {
                string path = GetManifestPath(scope, manifestHash);
                if (!File.Exists(path)) return Fail(ContentCacheError.MissingManifest, out error);
                if (!TryReadCanonicalManifest(path, manifestHash, out manifest) ||
                    manifest!.PackageId != packageId)
                {
                    manifest = null;
                    return Corrupt(ContentCacheError.CorruptManifest, out error);
                }
                error = ContentCacheError.None;
                return true;
            }
            catch (Exception exception) when (IsStorageException(exception))
            {
                manifest = null;
                return Fail(ContentCacheError.IoFailure, out error);
            }
        }
    }

    public bool TryOpenPackage(
        ApplicationRealmId realm,
        ContentPackageId packageId,
        ContentHash manifestHash,
        out VerifiedContentPackage? package,
        out ContentCacheError error)
    {
        if (!realm.IsValid)
        {
            package = null;
            return Fail(ContentCacheError.InvalidRequest, out error);
        }
        return TryOpenPackage(ContentScopeKey.ForRealm(realm), packageId, manifestHash, out package, out error);
    }

    public bool TryOpenPackage(
        ContentScopeKey scope,
        ContentPackageId packageId,
        ContentHash manifestHash,
        out VerifiedContentPackage? package,
        out ContentCacheError error)
    {
        lock (_sync)
            return TryOpenPackageCore(scope, packageId, manifestHash, out package, out error);
    }

    private bool TryInstallPackageCore(
        ContentScopeKey scope,
        ContentPackageManifest manifest,
        ContentFilePayload[] payloads,
        out InstalledContentPackage installed,
        out ContentCacheError error)
    {
        installed = default;
        error = ContentCacheError.None;
        if (_disposed || !scope.IsValid)
            return Fail(ContentCacheError.InvalidRequest, out error);
        if (manifest.TrustClass != ContentTrustClass.ScopeAuthorizedPassive)
            return Fail(ContentCacheError.UnsupportedTrustClass, out error);

        var descriptorsByPath = manifest.Files.ToDictionary(static value => value.Path);
        var payloadsByHash = new Dictionary<ContentHash, ReadOnlyMemory<byte>>();
        var payloadPaths = new HashSet<ContentFilePath>();
        foreach (ContentFilePayload payload in payloads)
        {
            if (!payloadPaths.Add(payload.Path) ||
                !descriptorsByPath.TryGetValue(payload.Path, out ContentFileDescriptor descriptor) ||
                !ContentPackageValidator.TryValidatePayload(descriptor, payload.Bytes.Span))
                return Fail(ContentCacheError.PayloadValidationFailed, out error);
            if (payloadsByHash.TryGetValue(descriptor.Sha256, out ReadOnlyMemory<byte> duplicate))
            {
                if (!duplicate.Span.SequenceEqual(payload.Bytes.Span))
                    return Fail(ContentCacheError.PayloadValidationFailed, out error);
            }
            else
            {
                payloadsByHash.Add(descriptor.Sha256, payload.Bytes);
            }
        }

        try
        {
            if (!TryEnsureScopeDirectories(scope, out error)) return false;
            string manifestPath = GetManifestPath(scope, manifest.ManifestHash);
            byte[] metadata = ContentCacheMetadata.EncodeManifest(manifest);
            if (metadata.Length > _cacheLimits.MaximumMetadataFileBytes)
                return Fail(ContentCacheError.CapacityExceeded, out error);

            bool manifestCorrupt = false;
            if (File.Exists(manifestPath) &&
                (!TryReadCanonicalManifest(manifestPath, manifest.ManifestHash,
                    out ContentPackageManifest? existing) || existing!.PackageId != manifest.PackageId))
            {
                var manifestKey = new ContentCacheManifestKey(scope, manifest.ManifestHash);
                if (_liveManifestPins.ContainsKey(manifestKey))
                    return Fail(ContentCacheError.CacheBusy, out error);
                manifestCorrupt = true;
                SaturatingIncrement(ref _corruptionsDetected);
            }

            var writes = new Dictionary<ContentHash, BlobWrite>();
            var visited = new HashSet<ContentHash>();
            long packageBytes = 0;
            int reused = 0, newBlobCount = 0;
            long incomingGrowth = 0;
            foreach (ContentFileDescriptor descriptor in manifest.Files)
            {
                packageBytes = checked(packageBytes + descriptor.SizeBytes);
                if (!visited.Add(descriptor.Sha256)) continue;
                string blobPath = GetBlobPath(descriptor.Sha256);
                bool exists = File.Exists(blobPath);
                if (exists && TryValidateBlob(blobPath, descriptor))
                {
                    reused++;
                    continue;
                }
                if (!payloadsByHash.TryGetValue(descriptor.Sha256, out ReadOnlyMemory<byte> bytes))
                    return Fail(ContentCacheError.PayloadValidationFailed, out error);
                if (exists && _liveBlobPins.ContainsKey(descriptor.Sha256))
                    return Fail(ContentCacheError.CacheBusy, out error);
                long previousBytes = exists ? new FileInfo(blobPath).Length : 0;
                if (!exists) newBlobCount++;
                incomingGrowth = checked(incomingGrowth + Math.Max(0, descriptor.SizeBytes - previousBytes));
                writes.Add(descriptor.Sha256, new BlobWrite(bytes, exists));
            }

            ContentHash[] installPins = visited.ToArray();
            foreach (ContentHash hash in installPins) AddPin(_liveBlobPins, hash);
            try
            {
                StoreCensus census = ScanStore();
                if (census.BlobCount + newBlobCount > _cacheLimits.MaximumBlobs ||
                    census.ManifestCount + (File.Exists(manifestPath) ? 0 : 1) >
                    _cacheLimits.MaximumManifests)
                    return Fail(ContentCacheError.CapacityExceeded, out error);
                if (!TryMakeRoom(census.BlobBytes, incomingGrowth, out _, out error)) return false;

                foreach ((ContentHash hash, BlobWrite write) in writes)
                {
                    string blobPath = GetBlobPath(hash);
                    Directory.CreateDirectory(Path.GetDirectoryName(blobPath)!);
                    if (IsReparsePoint(Path.GetDirectoryName(blobPath)!))
                        return Fail(ContentCacheError.UnsafePath, out error);
                    if (!TryWriteAtomic(blobPath, write.Bytes.Span, overwrite: write.Repair, out error))
                        return false;
                    if (write.Repair) SaturatingIncrement(ref _blobsRepaired);
                    Touch(hash);
                }

                foreach (ContentFileDescriptor descriptor in manifest.Files)
                    if (!TryValidateBlob(GetBlobPath(descriptor.Sha256), descriptor))
                        return Corrupt(ContentCacheError.CorruptBlob, out error);

                if ((!File.Exists(manifestPath) || manifestCorrupt) &&
                    !TryWriteAtomic(manifestPath, metadata, overwrite: manifestCorrupt, out error))
                    return false;
                if (manifestCorrupt) SaturatingIncrement(ref _manifestsRepaired);
                foreach (ContentFileDescriptor descriptor in manifest.Files) Touch(descriptor.Sha256);
            }
            finally
            {
                foreach (ContentHash hash in installPins) RemovePin(_liveBlobPins, hash);
            }
            _reusedBlobs = checked(_reusedBlobs + (ulong)reused);
            SaturatingIncrement(ref _installs);
            installed = new InstalledContentPackage(
                scope, manifest.PackageId, manifest.ManifestHash, visited.Count, packageBytes);
            return true;
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            return Fail(ContentCacheError.IoFailure, out error);
        }
    }

    public bool TryPublishCatalog(
        ContentBaseCatalog baseCatalog,
        ContentActivationPlan plan,
        bool persistentlyPinned,
        out ContentCatalogLease? lease,
        out ContentCacheError error)
    {
        ArgumentNullException.ThrowIfNull(baseCatalog);
        ArgumentNullException.ThrowIfNull(plan);
        lock (_sync)
        {
            lease = null;
            error = ContentCacheError.None;
            if (_disposed || !plan.Scope.IsValid)
                return Fail(ContentCacheError.InvalidRequest, out error);
            try
            {
                string activationPath = GetActivationPath(plan.Scope, plan.Activation);
                if (File.Exists(activationPath))
                {
                    if (!TryReadActivation(activationPath, plan.ScopeActivation, default,
                            out ContentCacheActivationRecord existing))
                        return Corrupt(ContentCacheError.CorruptActivation, out error);
                    if (existing.Plan.PlanHash != plan.PlanHash)
                        return Fail(ContentCacheError.ActivationConflict, out error);
                }

                if (!TryResolveInstalled(baseCatalog, plan, out ResolvedContentCatalog? catalog,
                        out VerifiedContentPackage[] selected, out error))
                    return false;

                StoreCensus census = ScanStore();
                if (!File.Exists(activationPath) && census.ActivationCount >= _cacheLimits.MaximumActivations)
                    return Fail(ContentCacheError.CapacityExceeded, out error);
                PackageManifestIdentity[] selectedIdentities = selected.Select(static package =>
                    new PackageManifestIdentity(package.PackageId, package.ManifestHash)).ToArray();
                var record = new ContentCacheActivationRecord(
                    plan, catalog!.Fingerprint, persistentlyPinned, selectedIdentities);
                byte[] metadata = ContentCacheMetadata.EncodeActivation(record);
                if (metadata.Length > _cacheLimits.MaximumMetadataFileBytes)
                    return Fail(ContentCacheError.CapacityExceeded, out error);
                if (!TryEnsureScopeDirectories(plan.Scope, out error) ||
                    !TryWriteAtomic(activationPath, metadata, overwrite: true, out error))
                    return false;
                lease = CreateCatalogLease(catalog, selected);
                return true;
            }
            catch (Exception exception) when (IsStorageException(exception))
            {
                lease = null;
                return Fail(ContentCacheError.IoFailure, out error);
            }
        }
    }

    public bool TryOpenCatalog(
        ContentBaseCatalog baseCatalog,
        ContentRealmActivationKey key,
        out ContentCatalogLease? lease,
        out ContentCacheError error)
    {
        if (!key.IsValid)
        {
            lease = null;
            return Fail(ContentCacheError.InvalidRequest, out error);
        }
        return TryOpenCatalog(baseCatalog, new ContentScopeActivationKey(
            new ContentScopeActivationId(ContentScopeKey.ForRealm(key.Identity.Realm),
                key.Identity.Activation), key.PlanHash), out lease, out error);
    }

    public bool TryOpenCatalog(
        ContentBaseCatalog baseCatalog,
        ContentScopeActivationKey key,
        out ContentCatalogLease? lease,
        out ContentCacheError error)
    {
        ArgumentNullException.ThrowIfNull(baseCatalog);
        lock (_sync)
        {
            lease = null;
            error = ContentCacheError.None;
            if (_disposed || !key.IsValid)
                return Fail(ContentCacheError.InvalidRequest, out error);
            try
            {
                string path = GetActivationPath(key.Identity.Scope, key.Identity.Activation);
                if (!File.Exists(path)) return Fail(ContentCacheError.ActivationNotFound, out error);
                if (!TryReadActivation(path, key.Identity, key.PlanHash,
                        out ContentCacheActivationRecord record))
                    return Corrupt(ContentCacheError.CorruptActivation, out error);
                if (!TryResolveSelected(baseCatalog, record, out ResolvedContentCatalog? catalog,
                        out VerifiedContentPackage[] selected, out error))
                    return false;
                if (catalog!.Fingerprint != record.ResolvedCatalogFingerprint)
                    return Corrupt(ContentCacheError.CorruptActivation, out error);
                lease = CreateCatalogLease(catalog, selected);
                return true;
            }
            catch (Exception exception) when (IsStorageException(exception))
            {
                lease = null;
                return Fail(ContentCacheError.IoFailure, out error);
            }
        }
    }

    public bool TrySetActivationPinned(
        ContentRealmActivationKey key,
        bool pinned,
        out ContentCacheError error)
    {
        if (!key.IsValid) return Fail(ContentCacheError.InvalidRequest, out error);
        return TrySetActivationPinned(new ContentScopeActivationKey(
            new ContentScopeActivationId(ContentScopeKey.ForRealm(key.Identity.Realm),
                key.Identity.Activation), key.PlanHash), pinned, out error);
    }

    public bool TrySetActivationPinned(
        ContentScopeActivationKey key,
        bool pinned,
        out ContentCacheError error)
    {
        lock (_sync)
        {
            error = ContentCacheError.None;
            if (_disposed || !key.IsValid)
                return Fail(ContentCacheError.InvalidRequest, out error);
            try
            {
                string path = GetActivationPath(key.Identity.Scope, key.Identity.Activation);
                if (!File.Exists(path)) return Fail(ContentCacheError.ActivationNotFound, out error);
                if (!TryReadActivation(path, key.Identity, key.PlanHash,
                        out ContentCacheActivationRecord record))
                    return Corrupt(ContentCacheError.CorruptActivation, out error);
                byte[] bytes = ContentCacheMetadata.EncodeActivation(record with { PersistentlyPinned = pinned });
                return TryWriteAtomic(path, bytes, overwrite: true, out error);
            }
            catch (Exception exception) when (IsStorageException(exception))
            {
                return Fail(ContentCacheError.IoFailure, out error);
            }
        }
    }

    /// <summary>
    /// Retires one scope's authorization metadata. Global blobs remain in the CAS and become
    /// ordinary eviction candidates only when no other persistent reachability or live lease
    /// protects them.
    /// </summary>
    public bool TryRetireScope(
        ContentScopeKey scope,
        out ContentScopeRetirementResult result,
        out ContentCacheError error)
    {
        lock (_sync)
        {
            result = default;
            error = ContentCacheError.None;
            if (_disposed || !scope.IsValid)
                return Fail(ContentCacheError.InvalidRequest, out error);
            if (_liveManifestPins.Keys.Any(key => key.Scope == scope))
                return Fail(ContentCacheError.CacheBusy, out error);
            try
            {
                if (!TryScanMetadata(out List<ManifestRecord> manifests,
                        out List<ActivationFileRecord> activations, out error)) return false;
                ManifestRecord[] ownedManifests = manifests.Where(value => value.Key.Scope == scope).ToArray();
                ActivationFileRecord[] ownedActivations = activations
                    .Where(value => value.Record.Plan.Scope == scope).ToArray();
                foreach (ActivationFileRecord activation in ownedActivations) File.Delete(activation.Path);
                foreach (ManifestRecord manifest in ownedManifests) File.Delete(manifest.Path);
                result = new ContentScopeRetirementResult(
                    ownedManifests.Length, ownedActivations.Length);
                return true;
            }
            catch (Exception exception) when (IsStorageException(exception))
            {
                return Fail(ContentCacheError.IoFailure, out error);
            }
        }
    }

    public bool TryOpenBlob(
        ContentFileDescriptor descriptor,
        out ContentBlobLease? lease,
        out ContentCacheError error)
    {
        lock (_sync)
        {
            lease = null;
            error = ContentCacheError.None;
            if (_disposed || !descriptor.IsValid)
                return Fail(ContentCacheError.InvalidRequest, out error);
            try
            {
                ContentHash hash = descriptor.Sha256;
                string path = GetBlobPath(hash);
                if (!File.Exists(path)) return Fail(ContentCacheError.MissingBlob, out error);
                if (!TryValidateBlob(path, descriptor))
                    return Corrupt(ContentCacheError.CorruptBlob, out error);
                AddPin(_liveBlobPins, hash);
                try
                {
                    var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                        64 * 1024, FileOptions.SequentialScan);
                    Touch(hash);
                    lease = new ContentBlobLease(this, hash, stream);
                    return true;
                }
                catch
                {
                    RemovePin(_liveBlobPins, hash);
                    throw;
                }
            }
            catch (Exception exception) when (IsStorageException(exception))
            {
                return Fail(ContentCacheError.IoFailure, out error);
            }
        }
    }

    public bool TryEnforceQuota(
        out ContentCacheEvictionResult result,
        out ContentCacheError error)
    {
        lock (_sync)
        {
            result = default;
            error = ContentCacheError.None;
            if (_disposed) return Fail(ContentCacheError.InvalidRequest, out error);
            try
            {
                StoreCensus census = ScanStore();
                if (census.BlobBytes <= _cacheLimits.HighWaterBytes)
                {
                    result = new ContentCacheEvictionResult(0, 0, 0, 0, census.BlobBytes);
                    return true;
                }
                return TryEvictTo(census.BlobBytes, _cacheLimits.HighWaterBytes, out result, out error);
            }
            catch (Exception exception) when (IsStorageException(exception))
            {
                return Fail(ContentCacheError.IoFailure, out error);
            }
        }
    }

    public ContentCacheSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            StoreCensus census = ScanStore();
            return new ContentCacheSnapshot(
                census.BlobCount, census.BlobBytes, census.ScopeCount,
                census.LocalManifestCount, census.RealmManifestCount,
                census.LocalActivationCount, census.RealmActivationCount,
                _liveBlobPins.Values.Sum(), _liveCatalogLeases,
                _cacheLimits.QuotaBytes, _cacheLimits.HighWaterBytes,
                _installs, _reusedBlobs, _corruptionsDetected, _blobsRepaired,
                _manifestsRepaired, _blobsEvicted, _bytesEvicted);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _liveBlobPins.Clear();
            _liveManifestPins.Clear();
            _liveCatalogLeases = 0;
            _writerLock.Dispose();
        }
    }

    internal void ReleaseBlob(ContentHash hash)
    {
        lock (_sync)
            if (!_disposed) RemovePin(_liveBlobPins, hash);
    }

    internal void ReleaseCatalog(ContentCacheManifestKey[] manifests, ContentHash[] blobs)
    {
        lock (_sync)
        {
            if (_disposed) return;
            foreach (ContentCacheManifestKey manifest in manifests) RemovePin(_liveManifestPins, manifest);
            foreach (ContentHash blob in blobs) RemovePin(_liveBlobPins, blob);
            if (_liveCatalogLeases > 0) _liveCatalogLeases--;
        }
    }

    private bool TryOpenPackageCore(
        ContentScopeKey scope,
        ContentPackageId packageId,
        ContentHash manifestHash,
        out VerifiedContentPackage? package,
        out ContentCacheError error)
    {
        package = null;
        error = ContentCacheError.None;
        if (_disposed || !scope.IsValid || !packageId.IsValid || !manifestHash.IsValid)
            return Fail(ContentCacheError.InvalidRequest, out error);
        try
        {
            string path = GetManifestPath(scope, manifestHash);
            if (!File.Exists(path)) return Fail(ContentCacheError.MissingManifest, out error);
            if (!TryReadCanonicalManifest(path, manifestHash, out ContentPackageManifest? manifest) ||
                manifest!.PackageId != packageId)
                return Corrupt(ContentCacheError.CorruptManifest, out error);
            foreach (ContentFileDescriptor descriptor in manifest.Files)
            {
                string blobPath = GetBlobPath(descriptor.Sha256);
                if (!File.Exists(blobPath)) return Fail(ContentCacheError.MissingBlob, out error);
                if (!TryValidateBlob(blobPath, descriptor))
                    return Corrupt(ContentCacheError.CorruptBlob, out error);
                Touch(descriptor.Sha256);
            }
            package = new VerifiedContentPackage(manifest);
            return true;
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            package = null;
            return Fail(ContentCacheError.IoFailure, out error);
        }
    }

    private bool TryResolveInstalled(
        ContentBaseCatalog baseCatalog,
        ContentActivationPlan plan,
        out ResolvedContentCatalog? catalog,
        out VerifiedContentPackage[] selected,
        out ContentCacheError error)
    {
        catalog = null;
        selected = [];
        error = ContentCacheError.None;
        var packages = new List<VerifiedContentPackage>(plan.Packages.Count);
        foreach (ContentActivationPackage required in plan.Packages)
        {
            if (TryOpenPackageCore(plan.Scope, required.PackageId, required.ManifestHash,
                    out VerifiedContentPackage? package, out ContentCacheError packageError))
            {
                packages.Add(package!);
                continue;
            }
            if (required.Requirement == ContentRequirementPolicy.Optional &&
                packageError == ContentCacheError.MissingManifest)
                continue;
            error = packageError;
            return false;
        }
        if (!ContentCatalogResolver.TryResolve(baseCatalog, plan, packages,
                out catalog, out _))
            return Fail(ContentCacheError.CatalogResolutionFailed, out error);
        selected = packages.ToArray();
        return true;
    }

    private bool TryResolveSelected(
        ContentBaseCatalog baseCatalog,
        in ContentCacheActivationRecord record,
        out ResolvedContentCatalog? catalog,
        out VerifiedContentPackage[] selected,
        out ContentCacheError error)
    {
        catalog = null;
        selected = [];
        error = ContentCacheError.None;
        var packages = new VerifiedContentPackage[record.SelectedPackages.Length];
        for (int index = 0; index < packages.Length; index++)
        {
            PackageManifestIdentity identity = record.SelectedPackages[index];
            if (!TryOpenPackageCore(record.Plan.Scope, identity.PackageId, identity.ManifestHash,
                    out VerifiedContentPackage? package, out error))
                return false;
            packages[index] = package!;
        }
        if (!ContentCatalogResolver.TryResolve(baseCatalog, record.Plan, packages,
                out catalog, out _))
            return Fail(ContentCacheError.CatalogResolutionFailed, out error);
        selected = packages;
        return true;
    }

    private ContentCatalogLease CreateCatalogLease(
        ResolvedContentCatalog catalog,
        VerifiedContentPackage[] selected)
    {
        var manifests = new ContentCacheManifestKey[selected.Length];
        var blobs = selected.SelectMany(static package => package.Manifest.Files)
            .Select(static file => file.Sha256).Distinct().ToArray();
        for (int index = 0; index < selected.Length; index++)
        {
            manifests[index] = new ContentCacheManifestKey(catalog.Scope, selected[index].ManifestHash);
            AddPin(_liveManifestPins, manifests[index]);
        }
        foreach (ContentHash blob in blobs) AddPin(_liveBlobPins, blob);
        _liveCatalogLeases++;
        PackageManifestIdentity[] selectedPackages = selected.Select(static package =>
            new PackageManifestIdentity(package.PackageId, package.ManifestHash)).ToArray();
        return new ContentCatalogLease(this, catalog, selectedPackages, manifests, blobs);
    }

    private bool TryMakeRoom(
        long currentBytes,
        long incomingBytes,
        out ContentCacheEvictionResult result,
        out ContentCacheError error)
    {
        result = default;
        if (incomingBytes > _cacheLimits.QuotaBytes)
            return Fail(ContentCacheError.QuotaExceeded, out error);
        long projected = checked(currentBytes + incomingBytes);
        if (projected <= _cacheLimits.QuotaBytes)
        {
            error = ContentCacheError.None;
            return true;
        }
        long targetCurrent = Math.Min(
            _cacheLimits.HighWaterBytes,
            _cacheLimits.QuotaBytes - incomingBytes);
        return TryEvictTo(currentBytes, targetCurrent, out result, out error);
    }

    private bool TryEvictTo(
        long currentBytes,
        long targetBytes,
        out ContentCacheEvictionResult result,
        out ContentCacheError error)
    {
        result = default;
        error = ContentCacheError.None;
        if (currentBytes <= targetBytes)
        {
            result = new ContentCacheEvictionResult(0, 0, 0, 0, currentBytes);
            return true;
        }

        if (!TryScanMetadata(out List<ManifestRecord> manifests,
                out List<ActivationFileRecord> activations, out error))
            return false;
        var protectedManifests = new HashSet<ContentCacheManifestKey>(_liveManifestPins.Keys);
        foreach (ActivationFileRecord activation in activations)
            if (activation.Record.PersistentlyPinned)
                foreach (PackageManifestIdentity package in activation.Record.SelectedPackages)
                    protectedManifests.Add(new ContentCacheManifestKey(
                        activation.Record.Plan.Scope, package.ManifestHash));
        var protectedBlobs = new HashSet<ContentHash>(_liveBlobPins.Keys);
        foreach (ManifestRecord manifest in manifests)
            if (protectedManifests.Contains(manifest.Key))
                foreach (ContentFileDescriptor file in manifest.Manifest.Files)
                    protectedBlobs.Add(file.Sha256);

        List<BlobRecord> blobs = ScanBlobs();
        BlobRecord[] candidates = blobs.Where(value => !protectedBlobs.Contains(value.Hash))
            .OrderBy(static value => value.AccessTicks)
            .ThenBy(static value => value.Hash)
            .ToArray();
        long required = currentBytes - targetBytes;
        long selectedBytes = 0;
        int selectedCount = 0;
        while (selectedCount < candidates.Length && selectedBytes < required)
            selectedBytes = checked(selectedBytes + candidates[selectedCount++].Bytes);
        if (selectedBytes < required)
            return Fail(ContentCacheError.QuotaExceeded, out error);

        var selectedHashes = candidates.Take(selectedCount).Select(static value => value.Hash).ToHashSet();
        var removedManifestKeys = manifests
            .Where(value => !protectedManifests.Contains(value.Key) &&
                value.Manifest.Files.Any(file => selectedHashes.Contains(file.Sha256)))
            .Select(static value => value.Key).ToHashSet();
        int activationsRemoved = 0;
        foreach (ActivationFileRecord activation in activations)
        {
            if (activation.Record.PersistentlyPinned) continue;
            bool remove = activation.Record.SelectedPackages.Any(package =>
                removedManifestKeys.Contains(new ContentCacheManifestKey(
                    activation.Record.Plan.Scope, package.ManifestHash)));
            if (remove)
            {
                File.Delete(activation.Path);
                activationsRemoved++;
            }
        }
        int manifestsRemoved = 0;
        foreach (ManifestRecord manifest in manifests)
        {
            if (!removedManifestKeys.Contains(manifest.Key)) continue;
            File.Delete(manifest.Path);
            manifestsRemoved++;
        }
        for (int index = 0; index < selectedCount; index++)
        {
            BlobRecord blob = candidates[index];
            File.Delete(blob.Path);
            string access = GetAccessPath(blob.Hash);
            if (File.Exists(access)) File.Delete(access);
        }
        _blobsEvicted += (ulong)selectedCount;
        _bytesEvicted = checked(_bytesEvicted + selectedBytes);
        result = new ContentCacheEvictionResult(
            selectedCount, selectedBytes, manifestsRemoved, activationsRemoved,
            currentBytes - selectedBytes);
        return true;
    }

    private bool TryScanMetadata(
        out List<ManifestRecord> manifests,
        out List<ActivationFileRecord> activations,
        out ContentCacheError error)
    {
        manifests = [];
        activations = [];
        error = ContentCacheError.None;
        if (!TryScanScopeMetadata(ContentScopeKey.Local, _localScopeRoot, manifests, activations, out error))
            return false;
        foreach (string realmDirectory in Directory.EnumerateDirectories(_realmsRoot))
        {
            if (IsReparsePoint(realmDirectory) ||
                !ApplicationRealmId.TryParse(Path.GetFileName(realmDirectory), out ApplicationRealmId realm))
                return Fail(ContentCacheError.UnsafePath, out error);
            if (!TryScanScopeMetadata(ContentScopeKey.ForRealm(realm), realmDirectory,
                    manifests, activations, out error)) return false;
        }
        return true;
    }

    private bool TryScanScopeMetadata(
        ContentScopeKey scope,
        string scopeDirectory,
        List<ManifestRecord> manifests,
        List<ActivationFileRecord> activations,
        out ContentCacheError error)
    {
        error = ContentCacheError.None;
        if (IsReparsePoint(scopeDirectory)) return Fail(ContentCacheError.UnsafePath, out error);
        string manifestDirectory = Path.Combine(scopeDirectory, "manifests");
        if (Directory.Exists(manifestDirectory))
        {
            if (IsReparsePoint(manifestDirectory)) return Fail(ContentCacheError.UnsafePath, out error);
            foreach (string path in Directory.EnumerateFiles(manifestDirectory))
            {
                if (IsReparsePoint(path) || !TryHashFileName(path, ManifestExtension, out ContentHash hash) ||
                    !TryReadCanonicalManifest(path, hash, out ContentPackageManifest? manifest))
                    return Corrupt(ContentCacheError.CorruptManifest, out error);
                manifests.Add(new ManifestRecord(new ContentCacheManifestKey(scope, hash), manifest!, path));
                if (manifests.Count > _cacheLimits.MaximumManifests)
                    return Fail(ContentCacheError.CapacityExceeded, out error);
            }
        }
        string activationDirectory = Path.Combine(scopeDirectory, "activations");
        if (Directory.Exists(activationDirectory))
        {
            if (IsReparsePoint(activationDirectory)) return Fail(ContentCacheError.UnsafePath, out error);
            foreach (string path in Directory.EnumerateFiles(activationDirectory))
            {
                if (IsReparsePoint(path) ||
                    !TryActivationFileName(path, out ContentActivationId activation) ||
                    !TryReadActivation(path, new ContentScopeActivationId(scope, activation), default,
                        out ContentCacheActivationRecord record))
                    return Corrupt(ContentCacheError.CorruptActivation, out error);
                activations.Add(new ActivationFileRecord(record, path));
                if (activations.Count > _cacheLimits.MaximumActivations)
                    return Fail(ContentCacheError.CapacityExceeded, out error);
            }
        }
        return true;
    }

    private StoreCensus ScanStore()
    {
        List<BlobRecord> blobs = ScanBlobs();
        (int localManifests, int localActivations) = CountScopeMetadata(_localScopeRoot);
        int realmManifests = 0, realmActivations = 0, realmScopes = 0;
        foreach (string realm in Directory.EnumerateDirectories(_realmsRoot))
        {
            (int manifests, int activations) = CountScopeMetadata(realm);
            realmManifests += manifests;
            realmActivations += activations;
            if (manifests != 0 || activations != 0) realmScopes++;
        }
        int scopeCount = realmScopes + (localManifests != 0 || localActivations != 0 ? 1 : 0);
        return new StoreCensus(blobs.Count, blobs.Sum(static value => value.Bytes), scopeCount,
            localManifests, realmManifests, localActivations, realmActivations);
    }

    private static (int Manifests, int Activations) CountScopeMetadata(string scopeRoot)
    {
        string manifests = Path.Combine(scopeRoot, "manifests");
        string activations = Path.Combine(scopeRoot, "activations");
        return (
            Directory.Exists(manifests) ? Directory.EnumerateFiles(manifests).Count() : 0,
            Directory.Exists(activations) ? Directory.EnumerateFiles(activations).Count() : 0);
    }

    private List<BlobRecord> ScanBlobs()
    {
        var result = new List<BlobRecord>();
        foreach (string prefix in Directory.EnumerateDirectories(_blobsRoot))
        {
            if (IsReparsePoint(prefix) || Path.GetFileName(prefix).Length != 2) continue;
            foreach (string path in Directory.EnumerateFiles(prefix))
            {
                if (IsReparsePoint(path) || !TryHashFileName(path, BlobExtension, out ContentHash hash)) continue;
                result.Add(new BlobRecord(hash, path, new FileInfo(path).Length, ReadAccessTicks(hash)));
            }
        }
        return result;
    }

    private bool TryValidateStoreBounds(out ContentCacheError error)
    {
        try
        {
            foreach (string prefix in Directory.EnumerateFileSystemEntries(_blobsRoot))
            {
                string prefixName = Path.GetFileName(prefix);
                if (!Directory.Exists(prefix) || IsReparsePoint(prefix) || prefixName.Length != 2 ||
                    prefixName.Any(static value => value is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
                    return Fail(ContentCacheError.UnsafePath, out error);
                foreach (string path in Directory.EnumerateFiles(prefix))
                    if (IsReparsePoint(path) || !TryHashFileName(path, BlobExtension, out ContentHash hash) ||
                        !hash.ToString().StartsWith(prefixName, StringComparison.Ordinal))
                        return Fail(ContentCacheError.UnsafePath, out error);
            }
            foreach (string path in Directory.EnumerateFiles(_accessRoot))
                if (IsReparsePoint(path) || !TryHashFileName(path, AccessExtension, out _))
                    return Fail(ContentCacheError.UnsafePath, out error);
            foreach (string path in Directory.EnumerateDirectories(_stagingRoot))
                return Fail(ContentCacheError.UnsafePath, out error);
            foreach (string path in Directory.EnumerateFileSystemEntries(_scopesRoot))
                if (!Directory.Exists(path) || IsReparsePoint(path) ||
                    !string.Equals(Path.GetFileName(path), "local", StringComparison.Ordinal))
                    return Fail(ContentCacheError.UnsafePath, out error);
            if (!TryScanMetadata(out _, out _, out error)) return false;
            StoreCensus census = ScanStore();
            if (census.BlobCount > _cacheLimits.MaximumBlobs ||
                census.ManifestCount > _cacheLimits.MaximumManifests ||
                census.ActivationCount > _cacheLimits.MaximumActivations)
                return Fail(ContentCacheError.CapacityExceeded, out error);
            error = ContentCacheError.None;
            return true;
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            return Fail(ContentCacheError.IoFailure, out error);
        }
    }

    private bool TryReadCanonicalManifest(
        string path,
        ContentHash expected,
        out ContentPackageManifest? manifest)
    {
        manifest = null;
        FileInfo info = new(path);
        if (info.Length < 1 || info.Length > _cacheLimits.MaximumMetadataFileBytes) return false;
        byte[] bytes = File.ReadAllBytes(path);
        return ContentCacheMetadata.TryDecodeManifest(bytes, expected, _contentLimits, out manifest);
    }

    private bool TryReadActivation(
        string path,
        ContentScopeActivationId expected,
        ContentHash expectedPlanHash,
        out ContentCacheActivationRecord record)
    {
        record = default;
        FileInfo info = new(path);
        if (info.Length < 1 || info.Length > _cacheLimits.MaximumMetadataFileBytes) return false;
        byte[] bytes = File.ReadAllBytes(path);
        return ContentCacheMetadata.TryDecodeActivation(
            bytes, expected, expectedPlanHash, _contentLimits, out record);
    }

    private bool TryValidateBlob(string path, in ContentFileDescriptor descriptor)
    {
        FileInfo info = new(path);
        if (info.Length != descriptor.SizeBytes || info.Length > _contentLimits.MaximumFileBytes) return false;
        byte[] bytes = File.ReadAllBytes(path);
        return ContentPackageValidator.TryValidatePayload(descriptor, bytes);
    }

    private bool TryEnsureScopeDirectories(ContentScopeKey scope, out ContentCacheError error)
    {
        if (!scope.IsValid) return Fail(ContentCacheError.InvalidRequest, out error);
        string scopePath = GetScopeRoot(scope);
        string manifests = Path.Combine(scopePath, "manifests");
        string activations = Path.Combine(scopePath, "activations");
        Directory.CreateDirectory(manifests);
        Directory.CreateDirectory(activations);
        if (IsReparsePoint(scopePath) || IsReparsePoint(manifests) || IsReparsePoint(activations))
            return Fail(ContentCacheError.UnsafePath, out error);
        error = ContentCacheError.None;
        return true;
    }

    private bool TryWriteAtomic(
        string destination,
        ReadOnlySpan<byte> bytes,
        bool overwrite,
        out ContentCacheError error)
    {
        string temporary = Path.Combine(_stagingRoot, $"{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                       FileShare.None, 64 * 1024, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, destination, overwrite);
            error = ContentCacheError.None;
            return true;
        }
        catch (IOException) when (!overwrite && File.Exists(destination))
        {
            error = ContentCacheError.None;
            return true;
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            return Fail(ContentCacheError.IoFailure, out error);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private void Touch(ContentHash hash)
    {
        long ticks = DateTime.UtcNow.Ticks;
        if (ticks <= _lastAccessTicks) ticks = _lastAccessTicks + 1;
        _lastAccessTicks = ticks;
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, ticks);
        _ = TryWriteAtomic(GetAccessPath(hash), bytes, overwrite: true, out _);
    }

    private long ReadAccessTicks(ContentHash hash)
    {
        string path = GetAccessPath(hash);
        if (!File.Exists(path)) return 0;
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            return bytes.Length == sizeof(long) ? BinaryPrimitives.ReadInt64LittleEndian(bytes) : 0;
        }
        catch (IOException) { return 0; }
    }

    private string GetBlobPath(ContentHash hash)
    {
        string text = hash.ToString();
        return Path.Combine(_blobsRoot, text[..2], text + BlobExtension);
    }

    private string GetScopeRoot(ContentScopeKey scope) => scope.Kind switch
    {
        ContentScopeKind.Local => _localScopeRoot,
        ContentScopeKind.Realm when scope.Realm.IsValid => Path.Combine(_realmsRoot, scope.Realm.ToString()),
        _ => throw new ArgumentException("A valid content scope is required.", nameof(scope)),
    };

    private string GetManifestPath(ContentScopeKey scope, ContentHash hash) =>
        Path.Combine(GetScopeRoot(scope), "manifests", hash + ManifestExtension);

    private string GetActivationPath(ContentScopeKey scope, ContentActivationId activation) =>
        Path.Combine(GetScopeRoot(scope), "activations", activation + ActivationExtension);

    private string GetAccessPath(ContentHash hash) => Path.Combine(_accessRoot, hash + AccessExtension);

    private static bool TryCopyPayloads(
        IEnumerable<ContentFilePayload>? source,
        int maximum,
        out ContentFilePayload[] copy)
    {
        copy = [];
        if (source is null) return false;
        var list = new List<ContentFilePayload>(Math.Min(maximum, 256));
        foreach (ContentFilePayload value in source)
        {
            if (list.Count == maximum) return false;
            list.Add(value);
        }
        copy = list.ToArray();
        return true;
    }

    private static bool TryHashFileName(string path, string extension, out ContentHash hash)
    {
        hash = default;
        string name = Path.GetFileName(path);
        return name.EndsWith(extension, StringComparison.Ordinal) &&
            ContentHash.TryParse(name[..^extension.Length], out hash);
    }

    private static bool TryActivationFileName(string path, out ContentActivationId activation)
    {
        activation = default;
        string name = Path.GetFileName(path);
        if (!name.EndsWith(ActivationExtension, StringComparison.Ordinal)) return false;
        string value = name[..^ActivationExtension.Length];
        if (value.Length != 32 ||
            !ulong.TryParse(value.AsSpan(0, 16), System.Globalization.NumberStyles.AllowHexSpecifier,
                System.Globalization.CultureInfo.InvariantCulture, out ulong high) ||
            !ulong.TryParse(value.AsSpan(16), System.Globalization.NumberStyles.AllowHexSpecifier,
                System.Globalization.CultureInfo.InvariantCulture, out ulong low) ||
            (high == 0 && low == 0)) return false;
        activation = new ContentActivationId(high, low);
        return activation.ToString() == value;
    }

    private static bool IsReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return true;
        FileSystemInfo info = Directory.Exists(path)
            ? new DirectoryInfo(path)
            : new FileInfo(path);
        return info.LinkTarget is not null;
    }

    private static bool IsStorageException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or
            NotSupportedException or OverflowException;

    private bool Corrupt(ContentCacheError value, out ContentCacheError error)
    {
        _corruptionsDetected++;
        return Fail(value, out error);
    }

    private static void AddPin<TKey>(Dictionary<TKey, int> pins, TKey key) where TKey : notnull =>
        pins[key] = pins.GetValueOrDefault(key) + 1;

    private static void RemovePin<TKey>(Dictionary<TKey, int> pins, TKey key) where TKey : notnull
    {
        if (!pins.TryGetValue(key, out int count)) return;
        if (count == 1) pins.Remove(key);
        else pins[key] = count - 1;
    }

    private static bool Fail(ContentCacheError value, out ContentCacheError error)
    {
        error = value;
        return false;
    }

    private static void SaturatingIncrement(ref ulong value)
    {
        if (value != ulong.MaxValue) value++;
    }

    private readonly record struct StoreCensus(
        int BlobCount,
        long BlobBytes,
        int ScopeCount,
        int LocalManifestCount,
        int RealmManifestCount,
        int LocalActivationCount,
        int RealmActivationCount)
    {
        internal int ManifestCount => LocalManifestCount + RealmManifestCount;
        internal int ActivationCount => LocalActivationCount + RealmActivationCount;
    }

    private readonly record struct BlobRecord(
        ContentHash Hash,
        string Path,
        long Bytes,
        long AccessTicks);

    private readonly record struct BlobWrite(ReadOnlyMemory<byte> Bytes, bool Repair);

    private readonly record struct ManifestRecord(
        ContentCacheManifestKey Key,
        ContentPackageManifest Manifest,
        string Path);

    private readonly record struct ActivationFileRecord(
        ContentCacheActivationRecord Record,
        string Path);
}

/// <summary>Process-local pin on an immutable catalog and all selected cached package bytes.</summary>
public sealed class ContentCatalogLease : IDisposable
{
    private ContentCache? _owner;
    private readonly PackageManifestIdentity[] _selectedPackages;
    private readonly ContentCacheManifestKey[] _manifests;
    private readonly ContentHash[] _blobs;

    internal ContentCatalogLease(
        ContentCache owner,
        ResolvedContentCatalog catalog,
        PackageManifestIdentity[] selectedPackages,
        ContentCacheManifestKey[] manifests,
        ContentHash[] blobs)
    {
        _owner = owner;
        Catalog = catalog;
        _selectedPackages = selectedPackages;
        _manifests = manifests;
        _blobs = blobs;
    }

    public ResolvedContentCatalog Catalog { get; }

    internal ContentCache? Owner => Volatile.Read(ref _owner);
    internal ReadOnlySpan<PackageManifestIdentity> SelectedPackages => _selectedPackages;

    public void Dispose() =>
        Interlocked.Exchange(ref _owner, null)?.ReleaseCatalog(_manifests, _blobs);
}

/// <summary>Process-local native/resource pin on one verified immutable blob.</summary>
public sealed class ContentBlobLease : IDisposable
{
    private ContentCache? _owner;
    private readonly ContentHash _hash;

    internal ContentBlobLease(ContentCache owner, ContentHash hash, FileStream stream)
    {
        _owner = owner;
        _hash = hash;
        Stream = stream;
    }

    public Stream Stream { get; }

    public void Dispose()
    {
        ContentCache? owner = Interlocked.Exchange(ref _owner, null);
        Stream.Dispose();
        owner?.ReleaseBlob(_hash);
    }
}

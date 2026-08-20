using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace UltraSim.ECS
{
    public delegate void PrefabApplyResolved(Archetype archetype, int slot, ReadOnlySpan<int> columns);

    public readonly struct PrefabEntry
    {
        public readonly ArchetypeSignature Signature;
        public readonly PrefabApply Apply;
        public readonly PrefabApply? ApplyPerInstance;
        public readonly PrefabApplyResolved? ApplyResolved;
        public readonly ComponentId[]? ResolvedComponentIds;
        public readonly bool EnableTemplateCopy;

        public bool HasResolvedColumns
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ApplyResolved != null && ResolvedComponentIds != null && ResolvedComponentIds.Length > 0;
        }

        public bool HasPerInstanceApply
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ApplyPerInstance != null;
        }

        public PrefabEntry(ArchetypeSignature signature, PrefabApply apply)
        {
            Signature = signature ?? throw new ArgumentNullException(nameof(signature));
            Apply = apply ?? throw new ArgumentNullException(nameof(apply));
            ApplyPerInstance = null;
            ApplyResolved = null;
            ResolvedComponentIds = null;
            EnableTemplateCopy = false;
        }

        public PrefabEntry(ArchetypeSignature signature, PrefabApply applyTemplate, PrefabApply? applyPerInstance, bool enableTemplateCopy)
        {
            Signature = signature ?? throw new ArgumentNullException(nameof(signature));
            Apply = applyTemplate ?? throw new ArgumentNullException(nameof(applyTemplate));
            ApplyPerInstance = applyPerInstance;
            ApplyResolved = null;
            ResolvedComponentIds = null;
            EnableTemplateCopy = enableTemplateCopy;
        }

        public PrefabEntry(ArchetypeSignature signature, ComponentId[] resolvedComponentIds, PrefabApplyResolved applyResolved)
        {
            Signature = signature ?? throw new ArgumentNullException(nameof(signature));
            Apply = static (_, _) => { };
            ApplyPerInstance = null;
            ApplyResolved = applyResolved ?? throw new ArgumentNullException(nameof(applyResolved));
            ResolvedComponentIds = resolvedComponentIds ?? throw new ArgumentNullException(nameof(resolvedComponentIds));
            EnableTemplateCopy = false;
        }
    }

    public static class PrefabRegistry
    {
        private static PrefabEntry[] _entries = new PrefabEntry[64];
        private static int _count;
        private static readonly object _lock = new();
        private static volatile bool _sealed;
        private static volatile PrefabEntry[]? _sealedEntries;
        private static readonly Dictionary<string, PrefabId> _keyToId = new(StringComparer.Ordinal);
        private static volatile Dictionary<string, PrefabId>? _sealedKeyToId;
        private static ResolvedColumnCache?[] _resolvedCaches = new ResolvedColumnCache?[64];
        private static volatile ResolvedColumnCache?[]? _sealedResolvedCaches;

        public static int Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Volatile.Read(ref _count);
        }

        public static bool IsSealed
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _sealed;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PrefabId Register<T>() where T : unmanaged, IPrefab<T>
        {
            var prefab = default(T);
            return Register(prefab.Signature, static (arch, slot) => default(T).ApplyTo(arch, slot));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PrefabId RegisterTemplate<T>() where T : unmanaged, ITemplatePrefab<T>
        {
            var prefab = default(T);
            PrefabApply? applyPerInstance = prefab.HasPerInstanceData
                ? static (arch, slot) => default(T).ApplyInstanceTo(arch, slot)
                : null;

            return RegisterTemplate(
                prefab.Signature,
                static (arch, slot) => default(T).ApplyTemplateTo(arch, slot),
                applyPerInstance);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PrefabId RegisterResolved<T>() where T : unmanaged, IResolvedPrefab<T>
        {
            var prefab = default(T);
            return RegisterResolved(
                prefab.Signature,
                T.ResolvedComponentIds,
                static (arch, slot, cols) => default(T).ApplyResolvedTo(arch, slot, cols));
        }

        public static PrefabId Register(ArchetypeSignature signature, PrefabApply apply)
        {
            if (signature == null)
                throw new ArgumentNullException(nameof(signature));
            if (apply == null)
                throw new ArgumentNullException(nameof(apply));

            lock (_lock)
            {
                if (_sealed)
                    throw new InvalidOperationException("Prefab registry is sealed.");
                return RegisterUnkeyedUnsafe(new PrefabEntry(signature, apply), null);
            }
        }

        public static PrefabId RegisterTemplate(
            ArchetypeSignature signature,
            PrefabApply applyTemplate,
            PrefabApply? applyPerInstance = null)
        {
            if (signature == null)
                throw new ArgumentNullException(nameof(signature));
            if (applyTemplate == null)
                throw new ArgumentNullException(nameof(applyTemplate));

            lock (_lock)
            {
                if (_sealed)
                    throw new InvalidOperationException("Prefab registry is sealed.");

                return RegisterUnkeyedUnsafe(new PrefabEntry(signature, applyTemplate, applyPerInstance, enableTemplateCopy: true), null);
            }
        }

        public static PrefabId Register(
            string key,
            ArchetypeSignature signature,
            PrefabApply apply,
            bool replaceIfExists = true)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            lock (_lock)
            {
                if (_sealed)
                    throw new InvalidOperationException("Prefab registry is sealed.");

                if (_keyToId.TryGetValue(key, out var existingId))
                {
                    if (!replaceIfExists)
                        return existingId;

                    _entries[existingId.Value] = new PrefabEntry(signature, apply);
                    _resolvedCaches[existingId.Value] = null;
                    return existingId;
                }

                var id = RegisterUnkeyedUnsafe(new PrefabEntry(signature, apply), null);
                _keyToId[key] = id;
                return id;
            }
        }

        public static PrefabId RegisterTemplate(
            string key,
            ArchetypeSignature signature,
            PrefabApply applyTemplate,
            PrefabApply? applyPerInstance = null,
            bool replaceIfExists = true)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));
            if (signature == null)
                throw new ArgumentNullException(nameof(signature));
            if (applyTemplate == null)
                throw new ArgumentNullException(nameof(applyTemplate));

            lock (_lock)
            {
                if (_sealed)
                    throw new InvalidOperationException("Prefab registry is sealed.");

                if (_keyToId.TryGetValue(key, out var existingId))
                {
                    if (!replaceIfExists)
                        return existingId;

                    _entries[existingId.Value] = new PrefabEntry(signature, applyTemplate, applyPerInstance, enableTemplateCopy: true);
                    _resolvedCaches[existingId.Value] = null;
                    return existingId;
                }

                var id = RegisterUnkeyedUnsafe(new PrefabEntry(signature, applyTemplate, applyPerInstance, enableTemplateCopy: true), null);
                _keyToId[key] = id;
                return id;
            }
        }

        public static PrefabId RegisterResolved(
            ArchetypeSignature signature,
            ReadOnlySpan<ComponentId> resolvedComponentIds,
            PrefabApplyResolved applyResolved)
        {
            if (signature == null)
                throw new ArgumentNullException(nameof(signature));
            if (applyResolved == null)
                throw new ArgumentNullException(nameof(applyResolved));
            if (resolvedComponentIds.Length == 0)
                throw new ArgumentException("Resolved component list cannot be empty.", nameof(resolvedComponentIds));

            lock (_lock)
            {
                if (_sealed)
                    throw new InvalidOperationException("Prefab registry is sealed.");

                var copiedIds = resolvedComponentIds.ToArray();
                return RegisterUnkeyedUnsafe(new PrefabEntry(signature, copiedIds, applyResolved), new ResolvedColumnCache());
            }
        }

        public static PrefabId RegisterResolved(
            string key,
            ArchetypeSignature signature,
            ReadOnlySpan<ComponentId> resolvedComponentIds,
            PrefabApplyResolved applyResolved,
            bool replaceIfExists = true)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            lock (_lock)
            {
                if (_sealed)
                    throw new InvalidOperationException("Prefab registry is sealed.");

                if (_keyToId.TryGetValue(key, out var existingId))
                {
                    if (!replaceIfExists)
                        return existingId;

                    var copiedIds = resolvedComponentIds.ToArray();
                    _entries[existingId.Value] = new PrefabEntry(signature, copiedIds, applyResolved);
                    _resolvedCaches[existingId.Value] = new ResolvedColumnCache();
                    return existingId;
                }

                var copiedIdsNew = resolvedComponentIds.ToArray();
                var id = RegisterUnkeyedUnsafe(new PrefabEntry(signature, copiedIdsNew, applyResolved), new ResolvedColumnCache());
                _keyToId[key] = id;
                return id;
            }
        }

        public static PrefabId Reload(string key, ArchetypeSignature signature, PrefabApply apply)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));
            if (signature == null)
                throw new ArgumentNullException(nameof(signature));
            if (apply == null)
                throw new ArgumentNullException(nameof(apply));

            lock (_lock)
            {
                if (_keyToId.TryGetValue(key, out var existingId))
                {
                    _entries[existingId.Value] = new PrefabEntry(signature, apply);
                    _resolvedCaches[existingId.Value] = null;
                    PublishSealedSnapshotsUnsafe();
                    return existingId;
                }

                var id = RegisterUnkeyedUnsafe(new PrefabEntry(signature, apply), null);
                _keyToId[key] = id;
                PublishSealedSnapshotsUnsafe();
                return id;
            }
        }

        public static PrefabId ReloadTemplate(
            string key,
            ArchetypeSignature signature,
            PrefabApply applyTemplate,
            PrefabApply? applyPerInstance = null)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));
            if (signature == null)
                throw new ArgumentNullException(nameof(signature));
            if (applyTemplate == null)
                throw new ArgumentNullException(nameof(applyTemplate));

            lock (_lock)
            {
                var entry = new PrefabEntry(signature, applyTemplate, applyPerInstance, enableTemplateCopy: true);
                if (_keyToId.TryGetValue(key, out var existingId))
                {
                    _entries[existingId.Value] = entry;
                    _resolvedCaches[existingId.Value] = null;
                    PublishSealedSnapshotsUnsafe();
                    return existingId;
                }

                var id = RegisterUnkeyedUnsafe(entry, null);
                _keyToId[key] = id;
                PublishSealedSnapshotsUnsafe();
                return id;
            }
        }

        public static PrefabId ReloadResolved(
            string key,
            ArchetypeSignature signature,
            ReadOnlySpan<ComponentId> resolvedComponentIds,
            PrefabApplyResolved applyResolved)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));
            if (signature == null)
                throw new ArgumentNullException(nameof(signature));
            if (applyResolved == null)
                throw new ArgumentNullException(nameof(applyResolved));
            if (resolvedComponentIds.Length == 0)
                throw new ArgumentException("Resolved component list cannot be empty.", nameof(resolvedComponentIds));

            lock (_lock)
            {
                var copiedIds = resolvedComponentIds.ToArray();
                var cache = new ResolvedColumnCache();

                if (_keyToId.TryGetValue(key, out var existingId))
                {
                    _entries[existingId.Value] = new PrefabEntry(signature, copiedIds, applyResolved);
                    _resolvedCaches[existingId.Value] = cache;
                    PublishSealedSnapshotsUnsafe();
                    return existingId;
                }

                var id = RegisterUnkeyedUnsafe(new PrefabEntry(signature, copiedIds, applyResolved), cache);
                _keyToId[key] = id;
                PublishSealedSnapshotsUnsafe();
                return id;
            }
        }

        public static bool UnregisterKey(string key)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            lock (_lock)
            {
                if (!_keyToId.Remove(key))
                    return false;

                PublishSealedSnapshotsUnsafe();
                return true;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PrefabEntry Get(PrefabId id)
        {
            int index = id.Value;
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(id), "PrefabId.Invalid is not allowed.");

            if (_sealed)
            {
                var entries = _sealedEntries!;
                if ((uint)index < (uint)entries.Length)
                    return entries[index];

                throw new ArgumentOutOfRangeException(nameof(id), $"Invalid prefab id: {id.Value}");
            }

            lock (_lock)
            {
                if ((uint)index < (uint)_count)
                    return _entries[index];
            }

            throw new ArgumentOutOfRangeException(nameof(id), $"Invalid prefab id: {id.Value}");
        }

        public static void Apply(PrefabId id, Archetype archetype, int slot)
        {
            var entry = Get(id);
            if (!entry.HasResolvedColumns)
            {
                entry.Apply(archetype, slot);
                entry.ApplyPerInstance?.Invoke(archetype, slot);
                return;
            }

            var columns = GetResolvedColumns(id, archetype, entry);
            entry.ApplyResolved!(archetype, slot, columns);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGet(PrefabId id, out PrefabEntry entry)
        {
            int index = id.Value;
            if (index < 0)
            {
                entry = default;
                return false;
            }

            if (_sealed)
            {
                var entries = _sealedEntries!;
                if ((uint)index < (uint)entries.Length)
                {
                    entry = entries[index];
                    return true;
                }

                entry = default;
                return false;
            }

            lock (_lock)
            {
                if ((uint)index < (uint)_count)
                {
                    entry = _entries[index];
                    return true;
                }
            }

            entry = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PrefabId GetId(string key)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            if (_sealed)
            {
                var map = _sealedKeyToId!;
                if (map.TryGetValue(key, out var id))
                    return id;

                throw new KeyNotFoundException($"Prefab key not found: {key}");
            }

            lock (_lock)
            {
                if (_keyToId.TryGetValue(key, out var id))
                    return id;
            }

            throw new KeyNotFoundException($"Prefab key not found: {key}");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetId(string key, out PrefabId id)
        {
            if (key == null)
            {
                id = PrefabId.Invalid;
                return false;
            }

            if (_sealed)
                return _sealedKeyToId!.TryGetValue(key, out id);

            lock (_lock)
                return _keyToId.TryGetValue(key, out id);
        }

        public static void Seal()
        {
            lock (_lock)
            {
                if (_sealed)
                    return;
                _sealed = true;
                PublishSealedSnapshotsUnsafe();
            }
        }

        internal static void Clear()
        {
            lock (_lock)
            {
                Array.Clear(_entries, 0, _count);
                Array.Clear(_resolvedCaches, 0, _count);
                _count = 0;
                _sealedEntries = null;
                _sealedResolvedCaches = null;
                _keyToId.Clear();
                _sealedKeyToId = null;
                _sealed = false;
            }
        }

        private static ReadOnlySpan<int> GetResolvedColumns(PrefabId id, Archetype archetype, in PrefabEntry entry)
        {
            int index = id.Value;
            ResolvedColumnCache? cache;

            if (_sealed)
                cache = (uint)index < (uint)_sealedResolvedCaches!.Length ? _sealedResolvedCaches[index] : null;
            else
                cache = (uint)index < (uint)_resolvedCaches.Length ? _resolvedCaches[index] : null;

            if (cache == null)
                throw new InvalidOperationException("Resolved prefab cache not found.");

            return cache.GetOrCreate(archetype, entry.ResolvedComponentIds!);
        }

        private static PrefabId RegisterUnkeyedUnsafe(in PrefabEntry entry, ResolvedColumnCache? resolvedCache)
        {
            int id = _count;
            EnsureCapacityUnsafe(id + 1);

            _entries[id] = entry;
            _resolvedCaches[id] = resolvedCache;
            _count = id + 1;
            return new PrefabId(id);
        }

        private static void EnsureCapacityUnsafe(int needed)
        {
            if (needed <= _entries.Length)
                return;

            int newSize = Math.Max(_entries.Length * 2, needed);
            Array.Resize(ref _entries, newSize);
            Array.Resize(ref _resolvedCaches, newSize);
        }

        private static void PublishSealedSnapshotsUnsafe()
        {
            if (!_sealed)
                return;

            var snapshot = new PrefabEntry[_count];
            Array.Copy(_entries, snapshot, _count);
            _sealedEntries = snapshot;

            var cacheSnapshot = new ResolvedColumnCache?[_count];
            Array.Copy(_resolvedCaches, cacheSnapshot, _count);
            _sealedResolvedCaches = cacheSnapshot;

            _sealedKeyToId = new Dictionary<string, PrefabId>(_keyToId, StringComparer.Ordinal);
        }

        private sealed class ResolvedColumnCache
        {
            private readonly object _lock = new();
            private readonly Dictionary<Archetype, int[]> _columnsByArchetype = new(8);

            public ReadOnlySpan<int> GetOrCreate(Archetype archetype, ComponentId[] ids)
            {
                lock (_lock)
                {
                    if (_columnsByArchetype.TryGetValue(archetype, out var existing))
                        return existing;

                    var cols = new int[ids.Length];
                    for (int i = 0; i < ids.Length; i++)
                    {
                        int columnIndex = archetype.GetColumnIndex(ids[i]);
                        if (columnIndex < 0)
                            throw new InvalidOperationException($"Resolved prefab component {ids[i]} not present in archetype.");
                        cols[i] = columnIndex;
                    }

                    _columnsByArchetype[archetype] = cols;
                    return cols;
                }
            }
        }
    }
}

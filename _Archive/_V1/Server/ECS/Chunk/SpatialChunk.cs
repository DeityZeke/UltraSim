#nullable enable

using System.Collections.Generic;
using System.Linq;

using UltraSim.ECS.Components;

namespace Server.ECS.Chunk
{
    /// <summary>
    /// Represents a spatial chunk that tracks entities within its bounds.
    /// Tracks packed IDs only (no component access) and exposes basic dirty/version info.
    /// </summary>
    public sealed class SpatialChunk
    {
        public ChunkLocation Location { get; }

        private readonly HashSet<ulong> _trackedEntities = new();
        private readonly object _lock = new object();
        private bool _dirty = true;
        private long _version = 0;

        public SpatialChunk(ChunkLocation location)
        {
            Location = location;
        }

        public int EntityCount
        {
            get
            {
                lock (_lock)
                {
                    return _trackedEntities.Count;
                }
            }
        }

        public bool IsActive => EntityCount > 0;

        public bool IsDirty
        {
            get
            {
                lock (_lock)
                {
                    return _dirty;
                }
            }
        }

        public long Version
        {
            get
            {
                lock (_lock)
                {
                    return _version;
                }
            }
        }

        public void TrackEntity(ulong entityPacked)
        {
            lock (_lock)
            {
                if (_trackedEntities.Add(entityPacked))
                    MarkDirty_NoLock();
            }
        }

        public void StopTracking(ulong entityPacked)
        {
            lock (_lock)
            {
                if (_trackedEntities.Remove(entityPacked))
                    MarkDirty_NoLock();
            }
        }

        public bool IsTracking(ulong entityPacked)
        {
            lock (_lock)
            {
                return _trackedEntities.Contains(entityPacked);
            }
        }

        public void CopyTrackedEntities(List<ulong> target)
        {
            lock (_lock)
            {
                target.AddRange(_trackedEntities);
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                if (_trackedEntities.Count == 0)
                    return;

                _trackedEntities.Clear();
                MarkDirty_NoLock();
            }
        }

        public int RemoveMatching(System.ReadOnlySpan<ulong> packedIds)
        {
            lock (_lock)
            {
                int removed = 0;
                for (int i = 0; i < packedIds.Length; i++)
                {
                    if (_trackedEntities.Remove(packedIds[i]))
                    {
                        removed++;
                    }
                }

                if (removed > 0)
                    MarkDirty_NoLock();

                return removed;
            }
        }

        public int RemoveIfInSet(HashSet<ulong> deadSet)
        {
            lock (_lock)
            {
                int removed = 0;
                foreach (var entityPacked in _trackedEntities.ToArray())
                {
                    if (deadSet.Contains(entityPacked))
                    {
                        _trackedEntities.Remove(entityPacked);
                        removed++;
                    }
                }

                if (removed > 0)
                    MarkDirty_NoLock();

                return removed;
            }
        }

        public void ClearDirty()
        {
            lock (_lock)
            {
                _dirty = false;
            }
        }

        private void MarkDirty_NoLock()
        {
            _dirty = true;
            unchecked { _version++; }
        }
    }
}

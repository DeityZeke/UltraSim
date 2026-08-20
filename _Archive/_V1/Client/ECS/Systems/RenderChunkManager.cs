#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using UltraSim;
using UltraSim.ECS;
using UltraSim.Server.ECS.Chunk;
using UltraSim.ECS.Components;
using UltraSim.ECS.Settings;
using UltraSim.ECS.Systems;
using UltraSim.Client.ECS;
using UltraSim.Client.ECS.Components;
using UltraSim.Client.ECS.Rendering;

namespace Client.ECS.Systems
{
    /// <summary>
    /// RenderChunkManager - SINGLE RESPONSIBILITY: Proactive window management + zone tagging.
    ///
    /// NEW ARCHITECTURE: This system creates render chunk entities proactively.
    ///
    /// This system:
    /// 1. Determines player chunk location
    /// 2. Calculates required render window (camera ± render distance)
    /// 3. Creates render chunk entities for all locations in window
    /// 4. Tags each chunk with zone components (NearZoneTag/MidZoneTag/FarZoneTag)
    /// 5. Pools chunks that exit the window (single-pass algorithm)
    ///
    /// SINGLE-PASS ALGORITHM:
    /// - Query all existing RenderChunk entities
    /// - Calculate new window bounds
    /// - For existing chunks:
    ///   - Inside window → Update zone tags
    ///   - Outside window → Remove zone tags + pool entity
    /// - Create missing chunks (window - existing) from pool or new
    ///
    /// ECS PATTERN: Uses tag components for zone assignment.
    /// - Near chunks: Have NearZoneTag → Query<NearZoneTag>() finds them
    /// - Mid chunks: Have MidZoneTag → Query<MidZoneTag>() finds them
    /// - Far chunks: Have FarZoneTag → Query<FarZoneTag>() finds them
    /// - Pooled chunks: Have RenderChunkPoolTag → Query<RenderChunkPoolTag>() finds them for reuse
    ///
    /// Benefits:
    /// - Zone systems query DIFFERENT archetypes (no read conflicts → parallelizable!)
    /// - Automatic filtering by ECS (no manual enum checks)
    /// - Complete render coverage (no gaps in frustum)
    /// - Predictable memory usage (fixed window size)
    /// - Entity pooling prevents microstutters (no allocations on window slide)
    /// </summary>
    public sealed class RenderChunkManager : BaseSystem
    {
        #region Settings

        public sealed class Settings : SystemSettings
        {
            public IntSetting DynamicRange { get; private set; }
            public IntSetting StaticRange { get; private set; }
            public IntSetting BillboardRange { get; private set; }
            public IntSetting ViewDistance { get; private set; }
            public FloatSetting RecenterDelaySeconds { get; private set; }
            public IntSetting PoolCapacity { get; private set; }
            public BoolSetting EnableDebugLogs { get; private set; }
            public BoolSetting ShowChunkOutlines { get; private set; }

            public Settings()
            {
                DynamicRange = RegisterInt("Dynamic Range", 1,
                    min: 1, max: 10, step: 1,
                    tooltip: "Near-zone radius (in chunks) used for dynamic entity rendering.");

                StaticRange = RegisterInt("Static Range", 3,
                    min: 1, max: 10, step: 1,
                    tooltip: "Mid-zone radius (in chunks) used for static MultiMesh rendering.");

                BillboardRange = RegisterInt("Billboard Range", 5,
                    min: 1, max: 10, step: 1,
                    tooltip: "Far-zone radius (in chunks) used for billboard rendering.");

                ViewDistance = RegisterInt("View Distance", 5,
                    min: 1, max: 10, step: 1,
                    tooltip: "Master clamp for render ranges. Lower values reduce all zones.");

                RecenterDelaySeconds = RegisterFloat("Recenter Delay", 0.5f,
                    min: 0.0f, max: 2.0f, step: 0.1f,
                    tooltip: "Delay before recentering window around new camera chunk (prevents thrashing)");

                PoolCapacity = RegisterInt("Pool Capacity", 512,
                    min: 64, max: 4096, step: 64,
                    tooltip: "Maximum pooled render chunk entities (prevents allocation overhead)");

                EnableDebugLogs = RegisterBool("Enable Debug Logs", false,
                    tooltip: "Log zone assignments and window updates");

                ShowChunkOutlines = RegisterBool("Show Chunk Outlines", false,
                    tooltip: "Toggle the chunk debug overlay if present in the scene.");

                HideInControlPanel = true;
            }
        }

        public Settings SystemSettings { get; } = new();
        public override SystemSettings? GetSettings() => SystemSettings;

        #endregion

        public override string Name => "Render Chunk Manager";
        public override int SystemId => typeof(RenderChunkManager).GetHashCode();
        public override TickRate Rate => TickRate.EveryFrame;

        // Read: RenderChunk for querying existing chunks, RenderChunkPoolTag for pool queries
        public override Type[] ReadSet { get; } = new[] { typeof(RenderChunk), typeof(RenderChunkPoolTag) };
        // Write: Zone tags + RenderChunkPoolTag + create/destroy entities
        // NOTE: RenderChunk entities do NOT have ChunkLocation component to avoid conflicts with ChunkSystem's spatial chunks
        public override Type[] WriteSet { get; } = new[] { typeof(NearZoneTag), typeof(MidZoneTag), typeof(FarZoneTag), typeof(RenderChunkPoolTag), typeof(RenderChunk) };

        private SimplifiedChunkManager? _chunkManager;
        private ChunkLocation _lastPlayerChunk = new(int.MaxValue, int.MaxValue, int.MaxValue);
        private double _timeSinceLastUpdate = 0;
        private int _frameCounter = 0;

        // Track previous zone assignments to know which tags to remove
        private enum ZoneType : byte { None, Near, Mid, Far }
        private readonly Dictionary<ChunkLocation, ZoneType> _previousZones = new();
        private static readonly int RenderChunkTypeId = ComponentManager.GetTypeId<RenderChunk>();
        private static readonly int RenderChunkPoolTagTypeId = ComponentManager.GetTypeId<RenderChunkPoolTag>();
        private static readonly int NearZoneTypeId = ComponentManager.GetTypeId<NearZoneTag>();
        private static readonly int MidZoneTypeId = ComponentManager.GetTypeId<MidZoneTag>();
        private static readonly int FarZoneTypeId = ComponentManager.GetTypeId<FarZoneTag>();

        public override void OnInitialize(World world)
        {
            Logging.Log($"[{Name}] Initialized - Dynamic radius: {SystemSettings.DynamicRange.Value}, Static: {SystemSettings.StaticRange.Value}, Billboard: {SystemSettings.BillboardRange.Value}, View: {SystemSettings.ViewDistance.Value}");
            Logging.Log($"[{Name}] Proactive render chunk creation enabled with tag-based pooling (max capacity: {SystemSettings.PoolCapacity.Value})");
        }

        public void SetChunkManager(SimplifiedChunkManager chunkManager)
        {
            _chunkManager = chunkManager;
            RenderOrigin.ApplyChunkManager(chunkManager);
            Logging.Log($"[{Name}] ChunkManager reference set");
        }

        public SimplifiedChunkManager? GetChunkManager() => _chunkManager;

        public void GetCurrentWindowChunks(List<ChunkLocation> target)
        {
            target.Clear();
            foreach (var kvp in _previousZones)
                target.Add(kvp.Key);
        }

        public override void Update(World world, double delta)
        {
            if (_chunkManager == null)
            {
                if (_frameCounter % 60 == 0 && SystemSettings.EnableDebugLogs.Value)
                {
                    Logging.Log($"[{Name}] ChunkManager is null, waiting for connection");
                }
                _frameCounter++;
                return;
            }

            _frameCounter++;

            // Use camera cache (updated on main thread by WorldECS)
            if (!CameraCache.IsValid)
            {
                if (_frameCounter % 60 == 0 && SystemSettings.EnableDebugLogs.Value)
                {
                    Logging.Log($"[{Name}] CameraCache not valid");
                }
                return;
            }

            var cameraPos = CameraCache.Position;
            var playerChunk = _chunkManager.WorldToChunk(cameraPos.X, cameraPos.Y, cameraPos.Z);
            RenderOrigin.UpdateOrigin(cameraPos, playerChunk);

            // Skip updates if player is stationary (and < recenter delay)
            bool playerMovedChunk = playerChunk != _lastPlayerChunk;
            _timeSinceLastUpdate += delta;

            if (!playerMovedChunk && _timeSinceLastUpdate < SystemSettings.RecenterDelaySeconds.Value)
            {
                // Skip - player hasn't moved to new chunk and recenter delay not reached
                return;
            }

            if (playerMovedChunk)
            {
                _lastPlayerChunk = playerChunk;
                _timeSinceLastUpdate = 0;

                if (SystemSettings.EnableDebugLogs.Value)
                {
                    Logging.Log($"[{Name}] Player moved to chunk {playerChunk}");
                }
            }

            // Single-pass algorithm: Query → Slide → Create/Destroy
            UpdateRenderWindow(world, playerChunk, delta);

            _timeSinceLastUpdate = 0;
        }

        /// <summary>
        /// Single-pass algorithm: Query existing chunks, slide window, create missing, destroy outside.
        /// </summary>
        private void UpdateRenderWindow(World world, ChunkLocation playerChunk, double delta)
        {
            if (_chunkManager == null)
                return;

            int dynamicRadius = Mathf.Clamp(SystemSettings.DynamicRange.Value, 1, 10);
            int staticRadius = Math.Max(dynamicRadius, Mathf.Clamp(SystemSettings.StaticRange.Value, 1, 10));
            int billboardRadius = Math.Max(staticRadius, Mathf.Clamp(SystemSettings.BillboardRange.Value, 1, 10));
            int viewRadius = Math.Max(billboardRadius, Mathf.Clamp(SystemSettings.ViewDistance.Value, 1, 10));

            int nearRadius = dynamicRadius;
            int midRadius = staticRadius;
            int farRadius = viewRadius;

            int yRadius = nearRadius;
            var sharedState = HybridRenderSharedState.Instance;
            sharedState.Configure(nearRadius, yRadius, midRadius, yRadius);
            sharedState.Update(playerChunk, delta, SystemSettings.RecenterDelaySeconds.Value);
            var windowLocations = new HashSet<ChunkLocation>();
            var windowZones = new Dictionary<ChunkLocation, ZoneType>();

            for (int dz = -farRadius; dz <= farRadius; dz++)
            {
                for (int dx = -farRadius; dx <= farRadius; dx++)
                {
                    for (int dy = -yRadius; dy <= yRadius; dy++)
                    {
                        var renderOffset = new ChunkLocation(dx, dz, dy);
                        var serverChunkLoc = new ChunkLocation(
                            playerChunk.X + dx,
                            playerChunk.Z + dz,
                            playerChunk.Y + dy);

                        int chebyshev = Math.Max(Math.Abs(dx), Math.Abs(dz));
                        ZoneType zone;
                        if (chebyshev <= nearRadius)
                            zone = ZoneType.Near;
                        else if (chebyshev <= midRadius)
                            zone = ZoneType.Mid;
                        else if (chebyshev <= farRadius)
                            zone = ZoneType.Far;
                        else
                            zone = ZoneType.None;

                        if (zone != ZoneType.None)
                        {
                            windowLocations.Add(serverChunkLoc);
                            windowZones[serverChunkLoc] = zone;
                        }
                    }
                }
            }

            var existingChunks = new Dictionary<ChunkLocation, Entity>();
            var zoneTagTypes = new[] { typeof(NearZoneTag), typeof(MidZoneTag), typeof(FarZoneTag) };
            foreach (var zoneTagType in zoneTagTypes)
            {
                using var archetypes = world.QueryArchetypes(zoneTagType);
                foreach (var archetype in archetypes)
                {
                    if (archetype.Count == 0)
                        continue;

                    var renderChunks = archetype.GetComponentSpan<RenderChunk>(RenderChunkTypeId);
                    var entities = archetype.GetEntitySpan();
                    for (int i = 0; i < renderChunks.Length; i++)
                    {
                        existingChunks[renderChunks[i].ServerChunkLocation] = entities[i];
                    }
                }
            }

            var pooledEntities = new List<Entity>();
            using var pooledArchetypes = world.QueryArchetypes(typeof(RenderChunkPoolTag));
            foreach (var archetype in pooledArchetypes)
            {
                if (archetype.Count == 0)
                    continue;

                var entities = archetype.GetEntitySpan();
                for (int i = 0; i < entities.Length; i++)
                    pooledEntities.Add(entities[i]);
            }

            int pooled = 0;
            int updated = 0;

            foreach (var kvp in existingChunks)
            {
                var location = kvp.Key;
                var entity = kvp.Value;

                if (windowLocations.Contains(location))
                {
                    var desiredZone = windowZones[location];
                    var previousZone = _previousZones.TryGetValue(location, out var prev) ? prev : ZoneType.None;

                    if (previousZone != desiredZone)
                    {
                        UpdateZoneTags(world, entity, previousZone, desiredZone);
                        updated++;
                    }
                }
                else
                {
                    var previousZone = _previousZones.TryGetValue(location, out var prev) ? prev : ZoneType.None;
                    RemoveAllZoneTags(world, entity, previousZone);

                    if (pooledEntities.Count + pooled < SystemSettings.PoolCapacity.Value)
                    {
                        world.EnqueueComponentAdd(entity, RenderChunkPoolTagTypeId, new RenderChunkPoolTag());
                        pooled++;
                    }
                    else
                    {
                        world.EnqueueDestroyEntity(entity);
                    }
                }
            }

            int created = 0;
            int reused = 0;
            int poolIndex = 0;

            foreach (var serverChunkLoc in windowLocations)
            {
                if (existingChunks.ContainsKey(serverChunkLoc))
                    continue;

                var renderOffset = new ChunkLocation(
                    serverChunkLoc.X - playerChunk.X,
                    serverChunkLoc.Z - playerChunk.Z,
                    serverChunkLoc.Y - playerChunk.Y);

                var bounds = _chunkManager.ChunkToWorldBounds(serverChunkLoc);
                var zone = windowZones[serverChunkLoc];

                if (poolIndex < pooledEntities.Count)
                {
                    var entity = pooledEntities[poolIndex++];
                    world.EnqueueComponentRemove(entity, RenderChunkPoolTagTypeId);
                    world.EnqueueComponentRemove(entity, RenderChunkTypeId);
                    world.EnqueueComponentAdd(entity, RenderChunkTypeId, new RenderChunk(renderOffset, serverChunkLoc, bounds, visible: true));
                    AddZoneTag(world, entity, zone);
                    reused++;
                }
                else
                {
                    var builder = world.CreateEntityBuilder();
                    builder.Add(new RenderChunk(renderOffset, serverChunkLoc, bounds, visible: true));
                    switch (zone)
                    {
                        case ZoneType.Near:
                            builder.Add(new NearZoneTag());
                            break;
                        case ZoneType.Mid:
                            builder.Add(new MidZoneTag());
                            break;
                        case ZoneType.Far:
                            builder.Add(new FarZoneTag());
                            break;
                    }
                    world.EnqueueCreateEntity(builder);
                    created++;
                }
            }

            _previousZones.Clear();
            foreach (var kvp in windowZones)
                _previousZones[kvp.Key] = kvp.Value;

            if (SystemSettings.EnableDebugLogs.Value)
            {
                int nearCount = windowZones.Values.Count(z => z == ZoneType.Near);
                int midCount = windowZones.Values.Count(z => z == ZoneType.Mid);
                int farCount = windowZones.Values.Count(z => z == ZoneType.Far);
                int poolSizeAfter = pooledEntities.Count - reused + pooled;

                Logging.Log($"[{Name}] Window: Near={nearCount}, Mid={midCount}, Far={farCount}, Created={created}, Reused={reused}, Pooled={pooled}, Updated={updated}, PoolSize={poolSizeAfter}");
            }
        }

        private void UpdateZoneTags(World world, Entity chunkEntity, ZoneType oldZone, ZoneType newZone)
        {
            RemoveZoneTag(world, chunkEntity, oldZone);
            AddZoneTag(world, chunkEntity, newZone);
        }

        private void RemoveAllZoneTags(World world, Entity chunkEntity, ZoneType previousZone)
        {
            RemoveZoneTag(world, chunkEntity, previousZone);
        }

        private void RemoveZoneTag(World world, Entity chunkEntity, ZoneType zone)
        {
            switch (zone)
            {
                case ZoneType.Near:
                    world.EnqueueComponentRemove(chunkEntity, NearZoneTypeId);
                    break;
                case ZoneType.Mid:
                    world.EnqueueComponentRemove(chunkEntity, MidZoneTypeId);
                    break;
                case ZoneType.Far:
                    world.EnqueueComponentRemove(chunkEntity, FarZoneTypeId);
                    break;
            }
        }

        private void AddZoneTag(World world, Entity chunkEntity, ZoneType zone)
        {
            switch (zone)
            {
                case ZoneType.Near:
                    world.EnqueueComponentAdd(chunkEntity, NearZoneTypeId, new NearZoneTag());
                    break;
                case ZoneType.Mid:
                    world.EnqueueComponentAdd(chunkEntity, MidZoneTypeId, new MidZoneTag());
                    break;
                case ZoneType.Far:
                    world.EnqueueComponentAdd(chunkEntity, FarZoneTypeId, new FarZoneTag());
                    break;
            }
        }

        public override void OnShutdown(World world)
        {
            _previousZones.Clear();

            if (SystemSettings.EnableDebugLogs.Value)
            {
                Logging.Log($"[{Name}] Shutting down");
            }
        }
    }
}


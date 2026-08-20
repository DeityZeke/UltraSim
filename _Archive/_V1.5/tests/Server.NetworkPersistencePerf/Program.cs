using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using UltraSim.ECS;
using UltraSim.ECS.Persistence;
using UltraSim.Network;
using UltraSim.Network.Protocol;
using UltraSim.Server;
using UltraSim.Server.Engines.Accounting;
using UltraSim.Server.Engines.Networking;
using UltraSim.Server.Gameplay;
using UltraSim.Shared.Engines.Networking;
using UltraSim.Shared.Gameplay;

namespace Server.NetworkPersistencePerf
{
    // ── Serializable item components (same as perf harness, re-declared here) ───

    [PersistenceId(49001u)]
    internal struct PerfItemStateComponent : ISerializableComponent<PerfItemStateComponent>
    {
        public int TemplateId;
        public int StackCount;
        public int ValueCents;

        public static void Serialize(ref UltraSim.ECS.BitWriter writer, in PerfItemStateComponent value)
        {
            writer.WriteInt32(value.TemplateId);
            writer.WriteInt32(value.StackCount);
            writer.WriteInt32(value.ValueCents);
        }

        public static PerfItemStateComponent Deserialize(ref UltraSim.ECS.BitReader reader, ushort version)
            => new PerfItemStateComponent
            {
                TemplateId = reader.ReadInt32(),
                StackCount = reader.ReadInt32(),
                ValueCents = reader.ReadInt32(),
            };
    }

    [PersistenceId(49002u)]
    internal struct PerfItemDurabilityComponent : ISerializableComponent<PerfItemDurabilityComponent>
    {
        public float Durability;
        public ushort Flags;

        public static void Serialize(ref UltraSim.ECS.BitWriter writer, in PerfItemDurabilityComponent value)
        {
            writer.WriteSingle(value.Durability);
            writer.WriteUInt16(value.Flags);
        }

        public static PerfItemDurabilityComponent Deserialize(ref UltraSim.ECS.BitReader reader, ushort version)
            => new PerfItemDurabilityComponent
            {
                Durability = reader.ReadSingle(),
                Flags = reader.ReadUInt16(),
            };
    }

    internal sealed class TestAuthProvider : IAccountAuthenticationProvider
    {
        public bool TryAuthenticateExistingAccount(
            AccountModel account,
            ReadOnlySpan<byte> passwordHash32,
            ReadOnlySpan<byte> challengeResponse32,
            ReadOnlySpan<byte> challenge16,
            out string errorMessage)
        {
            errorMessage = string.Empty;
            return true;
        }

        public bool TryPrepareNewAccount(
            string username,
            ReadOnlySpan<byte> passwordHash32,
            ReadOnlySpan<byte> challengeResponse32,
            ReadOnlySpan<byte> challenge16,
            out string passwordHash,
            out string passwordSalt,
            out string errorMessage)
        {
            passwordHash = "TEST_HASH";
            passwordSalt = "TEST_SALT";
            errorMessage = string.Empty;
            return true;
        }
    }

    // ── Options ───────────────────────────────────────────────────────────────

    internal enum SpawnTopology
    {
        /// <summary>
        /// Default: SpawnSpacingGrid=4, no wrap. Linear spread ~2m apart; with AOI radius=1
        /// each bot sees ~3 chunks = up to ~24 visible peers at typical densities.
        /// </summary>
        Default,

        /// <summary>
        /// Dense hotspot: SpawnSpacingGrid=1, WrapCount=10. All bots within a 5m×5m area,
        /// all in the same chunk. Each bot sees every other bot. Worst-case O(N²) fan-out.
        /// </summary>
        Dense,

        /// <summary>
        /// Clustered village: SpawnSpacingGrid=4, WrapCount=8. 8-wide rows at 2m spacing.
        /// Bots spread across multiple chunks; AOI radius=1 gives ~24 visible peers per bot.
        /// </summary>
        Clustered,

        /// <summary>
        /// Sparse world: SpawnSpacingGrid=32, no wrap. Bots placed one per chunk (16m apart).
        /// AOI radius=1 → each bot sees at most 3 bots (self + 2 immediate neighbors).
        /// </summary>
        Sparse,

        /// <summary>
        /// Realistic open world: 256×256 chunk map (chunk=16m → 4096m×4096m).
        /// A 9×9-chunk town sits at the center (chunk 128,128). Inside the town are 3 dense
        /// hotspots (bank, crafter, market) each with ~25 bots packed in a single chunk.
        /// Another ~150 bots fill the wider town area. The remaining ~700+ bots are scattered
        /// randomly across the wilderness, one per chunk, far enough apart to be sparse.
        /// </summary>
        World,
    }

    /// <summary>
    /// Pre-computed spawn position table for the World topology.
    /// Positions are world-units (metres). Chunk = 16m × 16m, grid unit = 0.5m.
    /// </summary>
    internal sealed class WorldSpawnLayout
    {
        private const float ChunkMeters   = 16f;   // 32 grid units × 0.5m
        private const int   WorldChunks   = 256;
        private const int   TownCenterChunk = 128;  // centre of map

        private readonly (float X, float Z)[] _positions;

        public WorldSpawnLayout(int totalBots)
        {
            _positions = BuildPositions(totalBots);
        }

        public PlayerSpawnSnapshot GetSpawnSnapshot(int botIndex)
        {
            (float x, float z) = _positions[botIndex % _positions.Length];
            return new PlayerSpawnSnapshot(x, 0f, z, 0f);
        }

        private static (float X, float Z)[] BuildPositions(int total)
        {
            var rng = new Random(unchecked((int)0xDEADBEEF));
            var positions = new List<(float, float)>(total);

            // ── 3 town hotspots: bank, crafter, market ───────────────────────
            // Each hotspot is anchored at a specific chunk inside the town 9×9.
            // Bots are packed tightly within that chunk (0.5m apart, ring-0).
            (int cx, int cz)[] hotspotChunks =
            {
                (TownCenterChunk - 1, TownCenterChunk - 1), // bank: NW of centre
                (TownCenterChunk + 1, TownCenterChunk),     // crafter: E of centre
                (TownCenterChunk,     TownCenterChunk + 2), // market: S of centre
            };
            int botsPerHotspot = 25;
            foreach (var (hcx, hcz) in hotspotChunks)
            {
                float originX = hcx * ChunkMeters;
                float originZ = hcz * ChunkMeters;
                for (int i = 0; i < botsPerHotspot; i++)
                {
                    // Pack within the chunk: 5×5 grid at 0.5m spacing (well inside 16m chunk)
                    float x = originX + (i % 5) * 0.5f + 0.5f;
                    float z = originZ + (i / 5) * 0.5f + 0.5f;
                    positions.Add((x, z));
                }
            }
            int hotspotTotal = botsPerHotspot * hotspotChunks.Length; // 75

            // ── Town area: scattered across the 9×9 chunk town ───────────────
            int townBots = Math.Min(150, Math.Max(0, total - hotspotTotal - 600));
            int townMin = TownCenterChunk - 4;
            int townMax = TownCenterChunk + 4;
            for (int i = 0; i < townBots; i++)
            {
                int chunkX = rng.Next(townMin, townMax + 1);
                int chunkZ = rng.Next(townMin, townMax + 1);
                float x = chunkX * ChunkMeters + (float)(rng.NextDouble() * ChunkMeters * 0.8f);
                float z = chunkZ * ChunkMeters + (float)(rng.NextDouble() * ChunkMeters * 0.8f);
                positions.Add((x, z));
            }

            // ── Wilderness: one bot per chunk, randomly across 256×256 map ──
            // Avoid the 9×9 town area in the centre.
            int wildernessTarget = total - positions.Count;
            var usedChunks = new HashSet<(int, int)>();
            // Reserve town chunks
            for (int cx = townMin; cx <= townMax; cx++)
                for (int cz = townMin; cz <= townMax; cz++)
                    usedChunks.Add((cx, cz));

            int attempts = 0;
            while (positions.Count < total && attempts < wildernessTarget * 10)
            {
                attempts++;
                int chunkX = rng.Next(0, WorldChunks);
                int chunkZ = rng.Next(0, WorldChunks);
                if (!usedChunks.Add((chunkX, chunkZ))) continue;
                float x = chunkX * ChunkMeters + (float)(rng.NextDouble() * ChunkMeters * 0.8f);
                float z = chunkZ * ChunkMeters + (float)(rng.NextDouble() * ChunkMeters * 0.8f);
                positions.Add((x, z));
            }

            // Shuffle so bots don't connect in zone-order (bank first, then crafter, etc.)
            for (int i = positions.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (positions[i], positions[j]) = (positions[j], positions[i]);
            }

            return positions.ToArray();
        }
    }

    internal sealed class HarnessOptions
    {
        public int Players        = 100;
        public int Items          = 1000;
        public int Port           = 42042;
        public WorldSaveParallelMode ParallelMode = WorldSaveParallelMode.Serial;
        public SpawnTopology Topology = SpawnTopology.Default;
        public double SettleSeconds = 3.0;
        public int WindowTicks    = 100;
        public int TickHz         = 62;
        public string? BotHarnessExe = null;
        public int Bots           = 0;
        public string? OutJson    = null;
        public double SloTickAvgMs  = 16.0;
        public double SloTickP95Ms  = 32.0;
        public double SloSaveMs     = 50.0;
        public int ActivityPct       = 100;  // % of bots that send movement+chat each tick (0–100)
        public int SpawnWrapOverride    = 0;   // 0 = use topology default
        public int SpawnSpacingOverride = 0;   // 0 = use topology default
        public bool NoSave          = false; // skip save trigger + AFTER window; network metrics only
        public int AoiRadius        = 1;    // PlayerChunkAoiRadius passed to ServerRuntimeOptions

        public (int SpawnSpacingGrid, int SpawnWrapCount) ResolveSpawnParams()
        {
            (int spacing, int wrap) = Topology switch
            {
                SpawnTopology.Dense     => (1, 10),
                SpawnTopology.Clustered => (4, 8),
                SpawnTopology.Sparse    => (32, 0),
                SpawnTopology.World     => (1, 0),  // ignored — WorldSpawnLayout takes over
                _                      => (4, 0),  // Default
            };
            if (SpawnWrapOverride > 0)
                wrap = SpawnWrapOverride;
            if (SpawnSpacingOverride > 0)
                spacing = SpawnSpacingOverride;
            return (spacing, wrap);
        }

        public WorldSpawnLayout? BuildWorldLayout()
            => Topology == SpawnTopology.World ? new WorldSpawnLayout(Math.Max(1, Bots + Players)) : null;

        public static HarnessOptions Parse(string[] args)
        {
            var o = new HarnessOptions();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--players":         o.Players        = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                    case "--items":           o.Items          = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                    case "--port":            o.Port           = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                    case "--settle-s":        o.SettleSeconds  = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
                    case "--window-ticks":    o.WindowTicks    = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                    case "--tick-hz":         o.TickHz         = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                    case "--bots":            o.Bots           = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                    case "--bot-harness-exe": o.BotHarnessExe  = args[++i]; break;
                    case "--out-json":        o.OutJson        = args[++i]; break;
                    case "--slo-tick-avg-ms": o.SloTickAvgMs   = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
                    case "--slo-tick-p95-ms": o.SloTickP95Ms   = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
                    case "--slo-save-ms":     o.SloSaveMs      = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
                    case "--activity-pct":    o.ActivityPct    = Math.Clamp(int.Parse(args[++i], CultureInfo.InvariantCulture), 0, 100); break;
                    case "--spawn-wrap":      o.SpawnWrapOverride    = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                    case "--spawn-spacing":   o.SpawnSpacingOverride = Math.Max(1, int.Parse(args[++i], CultureInfo.InvariantCulture)); break;
                    case "--no-save":         o.NoSave = true; break;
                    case "--aoi-radius":      o.AoiRadius = Math.Max(0, int.Parse(args[++i], CultureInfo.InvariantCulture)); break;
                    case "--spawn-topology":
                        o.Topology = args[++i].ToLowerInvariant() switch
                        {
                            "default"   => SpawnTopology.Default,
                            "dense"     => SpawnTopology.Dense,
                            "clustered" => SpawnTopology.Clustered,
                            "sparse"    => SpawnTopology.Sparse,
                            "world"     => SpawnTopology.World,
                            var v       => throw new ArgumentException($"Unknown spawn-topology '{v}'. Use default|dense|clustered|sparse|world."),
                        };
                        break;
                    case "--parallel-mode":
                        o.ParallelMode = args[++i].ToLowerInvariant() switch
                        {
                            "serial"   => WorldSaveParallelMode.Serial,
                            "parallel" => WorldSaveParallelMode.SystemsParallel,
                            var v      => throw new ArgumentException($"Unknown parallel-mode '{v}'. Use 'serial' or 'parallel'."),
                        };
                        break;
                    default:
                        throw new ArgumentException($"Unknown argument '{args[i]}'.");
                }
            }
            if (o.TickHz <= 0)  throw new ArgumentOutOfRangeException(nameof(o.TickHz));
            if (o.WindowTicks <= 0) throw new ArgumentOutOfRangeException(nameof(o.WindowTicks));
            return o;
        }
    }

    // ── Window stats ──────────────────────────────────────────────────────────

    internal readonly struct WindowStats
    {
        public readonly double AvgMs;
        public readonly double P95Ms;
        public readonly double MaxMs;
        public readonly int Count;
        public readonly int Gc0;
        public readonly int Gc1;
        public readonly int Gc2;
        public readonly long AllocBytes;

        public WindowStats(List<double> ticks, int gc0, int gc1, int gc2, long allocBytes)
        {
            Count = ticks.Count;
            Gc0 = gc0;
            Gc1 = gc1;
            Gc2 = gc2;
            AllocBytes = allocBytes;
            if (Count == 0) { AvgMs = 0; P95Ms = 0; MaxMs = 0; return; }
            ticks.Sort();
            double sum = 0;
            for (int i = 0; i < Count; i++) sum += ticks[i];
            AvgMs = sum / Count;
            P95Ms = ticks[(int)Math.Min(Count - 1, Math.Ceiling((Count - 1) * 0.95))];
            MaxMs = ticks[Count - 1];
        }
    }

    internal readonly struct SaveStats
    {
        public readonly double BarrierMs;
        public readonly double CompletionMs;
        public readonly double BackgroundWriteMs;
        public readonly double WorldSaveMs;
        public readonly double EntitiesMs;
        public readonly double AccountMs;
        public readonly int Gc0;
        public readonly int Gc1;
        public readonly int Gc2;
        public readonly long AllocBytes;

        public SaveStats(
            double barrierMs, double completionMs, double backgroundWriteMs, double worldSaveMs, double entitiesMs, double accountMs,
            int gc0, int gc1, int gc2, long allocBytes)
        {
            BarrierMs = barrierMs;
            CompletionMs = completionMs;
            BackgroundWriteMs = backgroundWriteMs;
            WorldSaveMs = worldSaveMs;
            EntitiesMs = entitiesMs;
            AccountMs = accountMs;
            Gc0 = gc0;
            Gc1 = gc1;
            Gc2 = gc2;
            AllocBytes = allocBytes;
        }
    }

    // ── Bandwidth stats ───────────────────────────────────────────────────────

    internal readonly struct BandwidthSnapshot
    {
        public readonly long TimestampTicks;
        public readonly int Sessions;
        public readonly int TrackedConnections;
        // Transport tx
        public readonly long TcpTxFramedBytes;
        public readonly long UdpTxFramedBytes;
        // Transport rx
        public readonly long UdpRxBytes;
        public readonly long UdpRxDatagrams;
        // Channel breakdown (framed bytes)
        public readonly long TcpReliableOrderedBytes;    // control, chat, UI
        public readonly long UdpUnreliableSequencedBytes; // corrections, replication
        // Correction counters (per entity-connection correction sent)
        public readonly long OwnerCorrectionsSent;
        public readonly long OwnerCorrectionPayloadBytes;
        public readonly long ObserverCorrectionsSent;
        public readonly long ObserverCorrectionPayloadBytes;
        // LOD gate telemetry (cumulative)
        public readonly long LodChecks;
        public readonly long LodSent;
        public readonly long LodGated;
        public readonly long LodNearSent;
        public readonly long LodRing1Sent;
        public readonly long LodRing2Sent;
        // Raw ring classification counts (all checks, gate-independent)
        public readonly long LodNearChecks;
        public readonly long LodRing1Checks;
        public readonly long LodRing2Checks;
        // AOI-gated visibility: point-in-time stats from _visibleByConnection (non-cumulative)
        public readonly double AvgVisiblePeers;
        public readonly int MinVisiblePeers;
        public readonly int MaxVisiblePeers;
        // Diagnostic: AOI config
        public readonly bool EnableChunkAoi;
        public readonly int ChunkAoiRadius;
        public readonly int ChunkSizeGridUnits;

        public BandwidthSnapshot(
            long timestampTicks, int sessions, int trackedConnections,
            long tcpTxFramedBytes, long udpTxFramedBytes,
            long udpRxBytes, long udpRxDatagrams,
            long tcpReliableOrderedBytes, long udpUnreliableSequencedBytes,
            long ownerCorrectionsSent, long ownerCorrectionPayloadBytes,
            long observerCorrectionsSent, long observerCorrectionPayloadBytes,
            long lodChecks, long lodSent, long lodGated,
            long lodNearSent, long lodRing1Sent, long lodRing2Sent,
            long lodNearChecks, long lodRing1Checks, long lodRing2Checks,
            double avgVisiblePeers, int minVisiblePeers, int maxVisiblePeers,
            bool enableChunkAoi, int chunkAoiRadius, int chunkSizeGridUnits)
        {
            TimestampTicks = timestampTicks;
            Sessions = sessions;
            TrackedConnections = trackedConnections;
            TcpTxFramedBytes = tcpTxFramedBytes;
            UdpTxFramedBytes = udpTxFramedBytes;
            UdpRxBytes = udpRxBytes;
            UdpRxDatagrams = udpRxDatagrams;
            TcpReliableOrderedBytes = tcpReliableOrderedBytes;
            UdpUnreliableSequencedBytes = udpUnreliableSequencedBytes;
            OwnerCorrectionsSent = ownerCorrectionsSent;
            OwnerCorrectionPayloadBytes = ownerCorrectionPayloadBytes;
            ObserverCorrectionsSent = observerCorrectionsSent;
            ObserverCorrectionPayloadBytes = observerCorrectionPayloadBytes;
            LodChecks = lodChecks;
            LodSent = lodSent;
            LodGated = lodGated;
            LodNearSent = lodNearSent;
            LodRing1Sent = lodRing1Sent;
            LodRing2Sent = lodRing2Sent;
            LodNearChecks = lodNearChecks;
            LodRing1Checks = lodRing1Checks;
            LodRing2Checks = lodRing2Checks;
            AvgVisiblePeers = avgVisiblePeers;
            MinVisiblePeers = minVisiblePeers;
            MaxVisiblePeers = maxVisiblePeers;
            EnableChunkAoi = enableChunkAoi;
            ChunkAoiRadius = chunkAoiRadius;
            ChunkSizeGridUnits = chunkSizeGridUnits;
        }

        public static BandwidthSnapshot Capture(
            TcpFramedNetworkTransport transport,
            ServerRuntime runtime)
        {
            TcpFramedNetworkTransport.TcpUdpStatsSnapshot tx = transport.SnapshotTcpUdpStats();
            ServerPlayerReplicationService.ReplicationTimingSnapshot repl = runtime.PlayerReplication.GetTimingSnapshot();
            (double avgVis, int minVis, int maxVis) = runtime.PlayerReplication.GetVisiblePeerStats();
            return new BandwidthSnapshot(
                Stopwatch.GetTimestamp(),
                runtime.Sessions.Count,
                runtime.PlayerReplication.TrackedConnectionCount,
                tx.TcpTxFramedBytes,
                tx.UdpTxFramedBytes,
                tx.UdpRxBytes,
                tx.UdpRxDatagrams,
                tx.TcpChannelFramedBytes.ReliableOrdered,
                tx.UdpChannelFramedBytes.UnreliableSequenced,
                repl.OwnerCorrectionsSent,
                repl.OwnerCorrectionPayloadBytes,
                repl.ObserverCorrectionsSent,
                repl.ObserverCorrectionPayloadBytes,
                repl.LodChecks, repl.LodSent, repl.LodGated,
                repl.LodNearSent, repl.LodRing1Sent, repl.LodRing2Sent,
                repl.LodNearChecks, repl.LodRing1Checks, repl.LodRing2Checks,
                avgVis, minVis, maxVis,
                runtime.PlayerReplication.EnableChunkAoi,
                runtime.PlayerReplication.ChunkAoiRadius,
                runtime.PlayerReplication.ChunkSizeGridUnits);
        }
    }

    internal readonly struct BandwidthStats
    {
        public readonly double ElapsedSeconds;
        public readonly int Sessions;
        public readonly int TrackedConnections;
        // Totals per second
        public readonly double TotalBytesOutPerSec;
        public readonly double TotalBytesInPerSec;
        // Per-CCU per second
        public readonly double BytesOutPerCcuPerSec;
        public readonly double BytesInPerCcuPerSec;
        // Channel breakdown per second
        public readonly double ReliableBytesOutPerSec;      // TCP ReliableOrdered (chat, UI, control)
        public readonly double UnreliableBytesOutPerSec;    // UDP UnreliableSequenced (corrections, repl)
        // Average payload sizes
        public readonly double AvgCorrectionPayloadBytes;   // per owner correction packet
        public readonly double AvgObserverPayloadBytes;     // per observer update sent
        // LOD gate telemetry (delta over window)
        public readonly long LodChecks;
        public readonly long LodSent;
        public readonly long LodGated;
        public readonly long LodNearSent;
        public readonly long LodRing1Sent;
        public readonly long LodRing2Sent;
        // Avg entities visible per connection per tick, broken down by ring
        public readonly double AvgEntitiesPerConnPerTickRing0;
        public readonly double AvgEntitiesPerConnPerTickRing1;
        public readonly double AvgEntitiesPerConnPerTickRing2;
        // Visibility: point-in-time snapshot at end of window (from _visibleByConnection AOI state)
        public readonly double AvgVisiblePeersPerPlayer;
        public readonly int MinVisiblePeersPerPlayer;
        public readonly int MaxVisiblePeersPerPlayer;
        // AOI config (from end snapshot)
        public readonly bool EnableChunkAoi;
        public readonly int ChunkAoiRadius;
        public readonly int ChunkSizeGridUnits;

        public BandwidthStats(BandwidthSnapshot start, BandwidthSnapshot end, int windowTicks)
        {
            ElapsedSeconds = (end.TimestampTicks - start.TimestampTicks) / (double)Stopwatch.Frequency;
            Sessions = Math.Max(1, end.Sessions);
            TrackedConnections = end.TrackedConnections;
            double elapsed = Math.Max(0.001, ElapsedSeconds);

            long tcpTxDelta = end.TcpTxFramedBytes - start.TcpTxFramedBytes;
            long udpTxDelta = end.UdpTxFramedBytes - start.UdpTxFramedBytes;
            long udpRxDelta = end.UdpRxBytes - start.UdpRxBytes;
            long tcpRelDelta = end.TcpReliableOrderedBytes - start.TcpReliableOrderedBytes;
            long udpUnrelDelta = end.UdpUnreliableSequencedBytes - start.UdpUnreliableSequencedBytes;

            TotalBytesOutPerSec = (tcpTxDelta + udpTxDelta) / elapsed;
            TotalBytesInPerSec = udpRxDelta / elapsed;
            BytesOutPerCcuPerSec = TotalBytesOutPerSec / Sessions;
            BytesInPerCcuPerSec = TotalBytesInPerSec / Sessions;
            ReliableBytesOutPerSec = tcpRelDelta / elapsed;
            UnreliableBytesOutPerSec = udpUnrelDelta / elapsed;

            long corrSent = end.OwnerCorrectionsSent - start.OwnerCorrectionsSent;
            long corrBytes = end.OwnerCorrectionPayloadBytes - start.OwnerCorrectionPayloadBytes;
            AvgCorrectionPayloadBytes = corrSent > 0 ? (double)corrBytes / corrSent : 0;

            long obsSent = end.ObserverCorrectionsSent - start.ObserverCorrectionsSent;
            long obsBytes = end.ObserverCorrectionPayloadBytes - start.ObserverCorrectionPayloadBytes;
            AvgObserverPayloadBytes = obsSent > 0 ? (double)obsBytes / obsSent : 0;

            LodChecks = end.LodChecks - start.LodChecks;
            LodSent = end.LodSent - start.LodSent;
            LodGated = end.LodGated - start.LodGated;
            LodNearSent = end.LodNearSent - start.LodNearSent;
            LodRing1Sent = end.LodRing1Sent - start.LodRing1Sent;
            LodRing2Sent = end.LodRing2Sent - start.LodRing2Sent;

            // avg entities visible per connection per tick at each ring.
            // lod_Xchecks = sum over all ticks of (connections × entities_at_ring_X_per_connection).
            // Dividing by (sessions × windowTicks) gives avg entities per connection per tick.
            double connTicks = Math.Max(1, Sessions) * Math.Max(1, windowTicks);
            long nearChecks  = end.LodNearChecks  - start.LodNearChecks;
            long ring1Checks = end.LodRing1Checks - start.LodRing1Checks;
            long ring2Checks = end.LodRing2Checks - start.LodRing2Checks;
            AvgEntitiesPerConnPerTickRing0 = nearChecks  / connTicks;
            AvgEntitiesPerConnPerTickRing1 = ring1Checks / connTicks;
            AvgEntitiesPerConnPerTickRing2 = ring2Checks / connTicks;

            // Point-in-time snapshot at end of window; use end snapshot directly.
            AvgVisiblePeersPerPlayer = end.AvgVisiblePeers;
            MinVisiblePeersPerPlayer = end.MinVisiblePeers;
            MaxVisiblePeersPerPlayer = end.MaxVisiblePeers;
            EnableChunkAoi = end.EnableChunkAoi;
            ChunkAoiRadius = end.ChunkAoiRadius;
            ChunkSizeGridUnits = end.ChunkSizeGridUnits;
        }
    }

    internal readonly struct HotPathTimingSnapshot
    {
        public readonly long TimestampTicks;
        public readonly long AoiRefreshCalls;
        public readonly long AoiRefreshElapsedTicksTotal;
        public readonly long ObserverChunkBuildCalls;
        public readonly long ObserverChunkBuildElapsedTicksTotal;
        public readonly long ObserverChunkPublishCalls;
        public readonly long ObserverChunkPublishElapsedTicksTotal;
        public readonly long BridgeBuildCalls;
        public readonly long BridgeBuildElapsedTicksTotal;
        public readonly long PipelineReplayVisitCalls;
        public readonly long PipelineReplayVisitElapsedTicksTotal;
        public readonly long PipelineCoalesceCommitCalls;
        public readonly long PipelineCoalesceCommitElapsedTicksTotal;
        public readonly long PipelineDeferredFlushCalls;
        public readonly long PipelineDeferredFlushElapsedTicksTotal;
        public readonly long PipelineRecipientSelectionCalls;
        public readonly long PipelineRecipientSelectionElapsedTicksTotal;
        public readonly long PipelineSelectedDispatchCalls;
        public readonly long PipelineSelectedDispatchElapsedTicksTotal;
        public readonly long PipelineTrySendCalls;
        public readonly long PipelineTrySendElapsedTicksTotal;
        public readonly long ServerFlushCalls;
        public readonly long ServerFlushElapsedTicksTotal;
        public readonly long TcpBatchWriteCalls;
        public readonly long TcpBatchWriteElapsedTicksTotal;
        public readonly long TcpSingleWriteCalls;
        public readonly long TcpSingleWriteElapsedTicksTotal;

        public HotPathTimingSnapshot(
            long timestampTicks,
            long aoiRefreshCalls,
            long aoiRefreshElapsedTicksTotal,
            long observerChunkBuildCalls,
            long observerChunkBuildElapsedTicksTotal,
            long observerChunkPublishCalls,
            long observerChunkPublishElapsedTicksTotal,
            long bridgeBuildCalls,
            long bridgeBuildElapsedTicksTotal,
            long pipelineReplayVisitCalls,
            long pipelineReplayVisitElapsedTicksTotal,
            long pipelineCoalesceCommitCalls,
            long pipelineCoalesceCommitElapsedTicksTotal,
            long pipelineDeferredFlushCalls,
            long pipelineDeferredFlushElapsedTicksTotal,
            long pipelineRecipientSelectionCalls,
            long pipelineRecipientSelectionElapsedTicksTotal,
            long pipelineSelectedDispatchCalls,
            long pipelineSelectedDispatchElapsedTicksTotal,
            long pipelineTrySendCalls,
            long pipelineTrySendElapsedTicksTotal,
            long serverFlushCalls,
            long serverFlushElapsedTicksTotal,
            long tcpBatchWriteCalls,
            long tcpBatchWriteElapsedTicksTotal,
            long tcpSingleWriteCalls,
            long tcpSingleWriteElapsedTicksTotal)
        {
            TimestampTicks = timestampTicks;
            AoiRefreshCalls = aoiRefreshCalls;
            AoiRefreshElapsedTicksTotal = aoiRefreshElapsedTicksTotal;
            ObserverChunkBuildCalls = observerChunkBuildCalls;
            ObserverChunkBuildElapsedTicksTotal = observerChunkBuildElapsedTicksTotal;
            ObserverChunkPublishCalls = observerChunkPublishCalls;
            ObserverChunkPublishElapsedTicksTotal = observerChunkPublishElapsedTicksTotal;
            BridgeBuildCalls = bridgeBuildCalls;
            BridgeBuildElapsedTicksTotal = bridgeBuildElapsedTicksTotal;
            PipelineReplayVisitCalls = pipelineReplayVisitCalls;
            PipelineReplayVisitElapsedTicksTotal = pipelineReplayVisitElapsedTicksTotal;
            PipelineCoalesceCommitCalls = pipelineCoalesceCommitCalls;
            PipelineCoalesceCommitElapsedTicksTotal = pipelineCoalesceCommitElapsedTicksTotal;
            PipelineDeferredFlushCalls = pipelineDeferredFlushCalls;
            PipelineDeferredFlushElapsedTicksTotal = pipelineDeferredFlushElapsedTicksTotal;
            PipelineRecipientSelectionCalls = pipelineRecipientSelectionCalls;
            PipelineRecipientSelectionElapsedTicksTotal = pipelineRecipientSelectionElapsedTicksTotal;
            PipelineSelectedDispatchCalls = pipelineSelectedDispatchCalls;
            PipelineSelectedDispatchElapsedTicksTotal = pipelineSelectedDispatchElapsedTicksTotal;
            PipelineTrySendCalls = pipelineTrySendCalls;
            PipelineTrySendElapsedTicksTotal = pipelineTrySendElapsedTicksTotal;
            ServerFlushCalls = serverFlushCalls;
            ServerFlushElapsedTicksTotal = serverFlushElapsedTicksTotal;
            TcpBatchWriteCalls = tcpBatchWriteCalls;
            TcpBatchWriteElapsedTicksTotal = tcpBatchWriteElapsedTicksTotal;
            TcpSingleWriteCalls = tcpSingleWriteCalls;
            TcpSingleWriteElapsedTicksTotal = tcpSingleWriteElapsedTicksTotal;
        }

        public static HotPathTimingSnapshot Capture(TcpFramedNetworkTransport transport, ServerRuntime runtime)
        {
            ServerPlayerReplicationService.ReplicationTimingSnapshot repl = runtime.PlayerReplication.GetTimingSnapshot();
            ServerNetworkReplicationBridge.BridgeTimingSnapshot bridge = runtime.PlayerReplication.GetBridgeTimingSnapshot();
            NetworkReplicationPipeline.BuildTimingSnapshot pipeline = runtime.PlayerReplication.GetPipelineTimingSnapshot();
            TcpFramedNetworkTransport.TcpUdpStatsSnapshot tx = transport.SnapshotTcpUdpStats();

            return new HotPathTimingSnapshot(
                Stopwatch.GetTimestamp(),
                repl.AoiRefreshCalls,
                repl.AoiRefreshElapsedTicksTotal,
                repl.ObserverChunkBuildCalls,
                repl.ObserverChunkBuildElapsedTicksTotal,
                repl.ObserverChunkPublishCalls,
                repl.ObserverChunkPublishElapsedTicksTotal,
                bridge.BuildCalls,
                bridge.BuildElapsedTicksTotal,
                pipeline.ReplayVisitCalls,
                pipeline.ReplayVisitElapsedTicksTotal,
                pipeline.CoalesceCommitCalls,
                pipeline.CoalesceCommitElapsedTicksTotal,
                pipeline.DeferredFlushCalls,
                pipeline.DeferredFlushElapsedTicksTotal,
                pipeline.RecipientSelectionCalls,
                pipeline.RecipientSelectionElapsedTicksTotal,
                pipeline.SelectedDispatchCalls,
                pipeline.SelectedDispatchElapsedTicksTotal,
                pipeline.TrySendCalls,
                pipeline.TrySendElapsedTicksTotal,
                tx.ServerFlushCalls,
                tx.ServerFlushElapsedTicksTotal,
                tx.TcpBatchWriteCalls,
                tx.TcpBatchWriteElapsedTicksTotal,
                tx.TcpSingleWriteCalls,
                tx.TcpSingleWriteElapsedTicksTotal);
        }
    }

    internal readonly struct HotPathTimingStats
    {
        public readonly double ElapsedSeconds;
        public readonly int WindowTicks;
        public readonly double AoiMsPerTick;
        public readonly double ObserverChunkBuildMsPerTick;
        public readonly double ObserverChunkPublishMsPerTick;
        public readonly double BridgeBuildMsPerTick;
        public readonly double ReplayVisitMsPerTick;
        public readonly double CoalesceCommitMsPerTick;
        public readonly double DeferredFlushMsPerTick;
        public readonly double RecipientSelectionMsPerTick;
        public readonly double SelectedDispatchMsPerTick;
        public readonly double TrySendMsPerTick;
        public readonly double ServerFlushMsPerTick;
        public readonly double TcpBatchWriteMsPerTick;
        public readonly double TcpSingleWriteMsPerTick;
        public readonly double RecipientSelectionMsPerCall;
        public readonly double SelectedDispatchMsPerCall;
        public readonly double TrySendMsPerCall;
        public readonly string HottestStage;
        public readonly double HottestStageMsPerTick;

        public HotPathTimingStats(HotPathTimingSnapshot start, HotPathTimingSnapshot end, int windowTicks)
        {
            ElapsedSeconds = (end.TimestampTicks - start.TimestampTicks) / (double)Stopwatch.Frequency;
            WindowTicks = Math.Max(1, windowTicks);

            AoiMsPerTick = DeltaMsPerTick(end.AoiRefreshElapsedTicksTotal - start.AoiRefreshElapsedTicksTotal, WindowTicks);
            ObserverChunkBuildMsPerTick = DeltaMsPerTick(end.ObserverChunkBuildElapsedTicksTotal - start.ObserverChunkBuildElapsedTicksTotal, WindowTicks);
            ObserverChunkPublishMsPerTick = DeltaMsPerTick(end.ObserverChunkPublishElapsedTicksTotal - start.ObserverChunkPublishElapsedTicksTotal, WindowTicks);
            BridgeBuildMsPerTick = DeltaMsPerTick(end.BridgeBuildElapsedTicksTotal - start.BridgeBuildElapsedTicksTotal, WindowTicks);
            ReplayVisitMsPerTick = DeltaMsPerTick(end.PipelineReplayVisitElapsedTicksTotal - start.PipelineReplayVisitElapsedTicksTotal, WindowTicks);
            CoalesceCommitMsPerTick = DeltaMsPerTick(end.PipelineCoalesceCommitElapsedTicksTotal - start.PipelineCoalesceCommitElapsedTicksTotal, WindowTicks);
            DeferredFlushMsPerTick = DeltaMsPerTick(end.PipelineDeferredFlushElapsedTicksTotal - start.PipelineDeferredFlushElapsedTicksTotal, WindowTicks);
            RecipientSelectionMsPerTick = DeltaMsPerTick(end.PipelineRecipientSelectionElapsedTicksTotal - start.PipelineRecipientSelectionElapsedTicksTotal, WindowTicks);
            SelectedDispatchMsPerTick = DeltaMsPerTick(end.PipelineSelectedDispatchElapsedTicksTotal - start.PipelineSelectedDispatchElapsedTicksTotal, WindowTicks);
            TrySendMsPerTick = DeltaMsPerTick(end.PipelineTrySendElapsedTicksTotal - start.PipelineTrySendElapsedTicksTotal, WindowTicks);
            ServerFlushMsPerTick = DeltaMsPerTick(end.ServerFlushElapsedTicksTotal - start.ServerFlushElapsedTicksTotal, WindowTicks);
            TcpBatchWriteMsPerTick = DeltaMsPerTick(end.TcpBatchWriteElapsedTicksTotal - start.TcpBatchWriteElapsedTicksTotal, WindowTicks);
            TcpSingleWriteMsPerTick = DeltaMsPerTick(end.TcpSingleWriteElapsedTicksTotal - start.TcpSingleWriteElapsedTicksTotal, WindowTicks);

            RecipientSelectionMsPerCall = DeltaMsPerCall(
                end.PipelineRecipientSelectionElapsedTicksTotal - start.PipelineRecipientSelectionElapsedTicksTotal,
                end.PipelineRecipientSelectionCalls - start.PipelineRecipientSelectionCalls);
            SelectedDispatchMsPerCall = DeltaMsPerCall(
                end.PipelineSelectedDispatchElapsedTicksTotal - start.PipelineSelectedDispatchElapsedTicksTotal,
                end.PipelineSelectedDispatchCalls - start.PipelineSelectedDispatchCalls);
            TrySendMsPerCall = DeltaMsPerCall(
                end.PipelineTrySendElapsedTicksTotal - start.PipelineTrySendElapsedTicksTotal,
                end.PipelineTrySendCalls - start.PipelineTrySendCalls);

            (HottestStage, HottestStageMsPerTick) = FindHottestStage();
        }

        private static double DeltaMsPerTick(long elapsedTicks, int windowTicks) =>
            elapsedTicks <= 0 ? 0d : (elapsedTicks * 1000.0 / Stopwatch.Frequency) / Math.Max(1, windowTicks);

        private static double DeltaMsPerCall(long elapsedTicks, long calls) =>
            elapsedTicks <= 0 || calls <= 0 ? 0d : (elapsedTicks * 1000.0 / Stopwatch.Frequency) / calls;

        private (string Stage, double MsPerTick) FindHottestStage()
        {
            (string Stage, double MsPerTick) hottest = ("aoi", AoiMsPerTick);
            Consider(ref hottest, "observer_chunk_build", ObserverChunkBuildMsPerTick);
            Consider(ref hottest, "observer_chunk_publish", ObserverChunkPublishMsPerTick);
            Consider(ref hottest, "bridge_build", BridgeBuildMsPerTick);
            Consider(ref hottest, "pipeline_replay_visit", ReplayVisitMsPerTick);
            Consider(ref hottest, "pipeline_coalesce_commit", CoalesceCommitMsPerTick);
            Consider(ref hottest, "pipeline_deferred_flush", DeferredFlushMsPerTick);
            Consider(ref hottest, "pipeline_recipient_selection", RecipientSelectionMsPerTick);
            Consider(ref hottest, "pipeline_selected_dispatch", SelectedDispatchMsPerTick);
            Consider(ref hottest, "pipeline_try_send", TrySendMsPerTick);
            Consider(ref hottest, "transport_server_flush", ServerFlushMsPerTick);
            Consider(ref hottest, "transport_tcp_batch_write", TcpBatchWriteMsPerTick);
            Consider(ref hottest, "transport_tcp_single_write", TcpSingleWriteMsPerTick);
            return hottest;
        }

        private static void Consider(ref (string Stage, double MsPerTick) hottest, string stage, double value)
        {
            if (value > hottest.MsPerTick)
                hottest = (stage, value);
        }
    }

    internal readonly struct ActivitySnapshot
    {
        public readonly long BotInputsSent;
        public readonly long BotInboundDatagrams;
        public readonly int ConnectedBots;
        public readonly int PeakConnectedBots;
        public readonly int ServerReceivedInputs;
        public readonly int ServerAppliedInputs;

        public ActivitySnapshot(
            long botInputsSent,
            long botInboundDatagrams,
            int connectedBots,
            int peakConnectedBots,
            int serverReceivedInputs,
            int serverAppliedInputs)
        {
            BotInputsSent = botInputsSent;
            BotInboundDatagrams = botInboundDatagrams;
            ConnectedBots = connectedBots;
            PeakConnectedBots = peakConnectedBots;
            ServerReceivedInputs = serverReceivedInputs;
            ServerAppliedInputs = serverAppliedInputs;
        }

        public static ActivitySnapshot Capture(ServerRuntime runtime, AsyncBotSwarm? botSwarm)
        {
            BotSwarmSnapshot botSnapshot = botSwarm?.CaptureSnapshot() ?? default;
            return new ActivitySnapshot(
                botSnapshot.InputsSent,
                botSnapshot.InboundDatagrams,
                botSnapshot.ConnectedBots,
                botSnapshot.PeakConnectedBots,
                runtime.PlayerWorld.ReceivedInputCount,
                runtime.PlayerWorld.AppliedInputCount);
        }
    }

    internal readonly struct ActivityDelta
    {
        public readonly long BotInputsSent;
        public readonly long BotInboundDatagrams;
        public readonly int ConnectedBots;
        public readonly int PeakConnectedBots;
        public readonly int ServerReceivedInputs;
        public readonly int ServerAppliedInputs;

        public ActivityDelta(ActivitySnapshot start, ActivitySnapshot end)
        {
            BotInputsSent = end.BotInputsSent - start.BotInputsSent;
            BotInboundDatagrams = end.BotInboundDatagrams - start.BotInboundDatagrams;
            ConnectedBots = end.ConnectedBots;
            PeakConnectedBots = end.PeakConnectedBots;
            ServerReceivedInputs = end.ServerReceivedInputs - start.ServerReceivedInputs;
            ServerAppliedInputs = end.ServerAppliedInputs - start.ServerAppliedInputs;
        }
    }

    // ── Tick loop controller ─────────────────────────────────────────────────

    internal enum TickPhase { Warming, Before, Saving, After, Done }

    internal sealed class TickLoopController
    {
        private volatile TickPhase _phase = TickPhase.Warming;
        private readonly List<double> _beforeTicks = new();
        private readonly List<double> _duringTicks = new();
        private readonly List<double> _afterTicks = new();
        private readonly object _lock = new();
        private double? _barrierTickMs;

        public TickPhase Phase => _phase;

        public void SetPhase(TickPhase phase)
        {
            lock (_lock)
                _phase = phase;
        }

        public void RecordTick(double ms, TickPhase phase)
        {
            lock (_lock)
            {
                if (phase == TickPhase.Before)
                    _beforeTicks.Add(ms);
                else if (phase == TickPhase.Saving)
                    _duringTicks.Add(ms);
                else if (phase == TickPhase.After)
                    _afterTicks.Add(ms);

                Monitor.PulseAll(_lock);
            }
        }

        public void RecordBarrierTick(double ms)
        {
            lock (_lock)
            {
                _barrierTickMs = ms;
                Monitor.PulseAll(_lock);
            }
        }

        public double WaitForBarrierTick(TimeSpan timeout)
        {
            lock (_lock)
            {
                if (_barrierTickMs.HasValue)
                    return _barrierTickMs.Value;

                DateTime deadline = DateTime.UtcNow + timeout;
                while (!_barrierTickMs.HasValue)
                {
                    TimeSpan remaining = deadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero || !Monitor.Wait(_lock, remaining))
                        throw new TimeoutException("Timed out waiting for save barrier tick.");
                }

                return _barrierTickMs.Value;
            }
        }

        public void WaitForTickCount(TickPhase phase, int count, TimeSpan timeout)
        {
            lock (_lock)
            {
                DateTime deadline = DateTime.UtcNow + timeout;
                while (GetCount_NoLock(phase) < count)
                {
                    TimeSpan remaining = deadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero || !Monitor.Wait(_lock, remaining))
                        throw new TimeoutException($"Timed out waiting for {count} ticks in phase {phase}.");
                }
            }
        }

        private int GetCount_NoLock(TickPhase phase) => phase switch
        {
            TickPhase.Before => _beforeTicks.Count,
            TickPhase.Saving => _duringTicks.Count,
            TickPhase.After => _afterTicks.Count,
            _ => 0,
        };

        public List<double> GetBeforeTicks() { lock (_lock) return new List<double>(_beforeTicks); }
        public List<double> GetDuringTicks() { lock (_lock) return new List<double>(_duringTicks); }
        public List<double> GetAfterTicks()  { lock (_lock) return new List<double>(_afterTicks); }
    }

    internal sealed class SaveWriteTracker
    {
        private readonly object _lock = new();
        private int _completedWrites;
        private int _startedWrites;
        private double _lastBackgroundWriteMs;

        public void RecordWrite(Action writeAction)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            lock (_lock)
            {
                _startedWrites++;
                Monitor.PulseAll(_lock);
            }

            try
            {
                writeAction();
            }
            finally
            {
                stopwatch.Stop();
                lock (_lock)
                {
                    _lastBackgroundWriteMs = stopwatch.Elapsed.TotalMilliseconds;
                    _completedWrites++;
                    Monitor.PulseAll(_lock);
                }
            }
        }

        public void Reset()
        {
            lock (_lock)
            {
                _completedWrites = 0;
                _startedWrites = 0;
                _lastBackgroundWriteMs = 0d;
            }
        }

        public void WaitForWriteStart(TimeSpan timeout)
        {
            lock (_lock)
            {
                DateTime deadline = DateTime.UtcNow + timeout;
                while (_startedWrites == 0)
                {
                    TimeSpan remaining = deadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero || !Monitor.Wait(_lock, remaining))
                        throw new TimeoutException("Timed out waiting for async save write to start.");
                }
            }
        }

        public double WaitForWriteCompletion(TimeSpan timeout)
        {
            lock (_lock)
            {
                DateTime deadline = DateTime.UtcNow + timeout;
                while (_completedWrites == 0)
                {
                    TimeSpan remaining = deadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero || !Monitor.Wait(_lock, remaining))
                        throw new TimeoutException("Timed out waiting for async save write completion.");
                }

                return _lastBackgroundWriteMs;
            }
        }
    }

    internal readonly struct BotSwarmSnapshot
    {
        public readonly long InputsSent;
        public readonly long InboundDatagrams;
        public readonly int ConnectedBots;
        public readonly int PeakConnectedBots;

        public BotSwarmSnapshot(long inputsSent, long inboundDatagrams, int connectedBots, int peakConnectedBots)
        {
            InputsSent = inputsSent;
            InboundDatagrams = inboundDatagrams;
            ConnectedBots = connectedBots;
            PeakConnectedBots = peakConnectedBots;
        }
    }

    /// <summary>
    /// Async task-based bot swarm. Each bot uses one TcpClient with a single async
    /// receive Task (IOCP-backed) and is ticked from a shared scheduler Task every ~5ms.
    /// Scales to 2k+ CCU without thread explosion.
    /// </summary>
    internal sealed class AsyncBotSwarm : IDisposable
    {
        // ── Per-bot state ──────────────────────────────────────────────────────
        private sealed class AsyncBot
        {
            public readonly int BotId;
            public volatile bool Connected;
            public volatile bool Closed;

            private readonly NetworkStream _stream;
            private readonly bool _isActive;

            // Send state — written only from scheduler Task (no lock needed)
            private ushort _inputSeq;
            private int    _inputCount;
            private double _nextInputS;
            private double _nextChatS;

            // Cumulative counters — read from any thread via Interlocked
            public long InputsSent;
            public long FramesDrained;

            private readonly byte[] _readBuf = new byte[32768];
            private int _readBufLen;
            private readonly byte[] _sendBuf = new byte[256];
            private readonly CancellationTokenSource _cts = new();
            private Task? _recvTask;

            public AsyncBot(NetworkStream stream, int botId, double now, bool isActive)
            {
                BotId = botId;
                _stream = stream;
                Connected = true;
                _isActive = isActive;
                // Stagger first sends so all bots don't fire at t=0
                double jitter = (botId % 62) * (1.0 / 62.0 / 62.0);
                _nextInputS = now + jitter;
                _nextChatS  = now + 2.0 + (botId % 50) * 0.06;
            }

            public void Start()
            {
                _recvTask = Task.Run(() => ReceiveLoop(_cts.Token), _cts.Token);
            }

            public async Task StopAsync()
            {
                if (Closed) return;
                Closed = true; Connected = false;
                try { _cts.Cancel(); } catch { }
                try { _stream.Close(); } catch { }
                if (_recvTask is { } r) try { await r.ConfigureAwait(false); } catch { }
                _cts.Dispose();
            }

            public void Tick(double now)
            {
                if (Closed || !_isActive) return;

                const double InputIntervalS = 1.0 / 62.0;
                if (now >= _nextInputS)
                {
                    int skipped = (int)Math.Max(0, Math.Floor((now - _nextInputS) / InputIntervalS));
                    _nextInputS += (skipped + 1) * InputIntervalS;

                    int phase = (_inputCount / 45) % 4;
                    (short dx, short dz) = phase switch
                    {
                        0 => ((short) 32767, (short)     0),
                        1 => ((short)     0, (short) 32767),
                        2 => ((short)-32767, (short)     0),
                        _ => ((short)     0, (short)-32767),
                    };

                    int written = EncodeInput(_sendBuf, _inputSeq, dx, dz, (long)(now * 1000));
                    TryWrite(_sendBuf.AsSpan(0, written));
                    Interlocked.Increment(ref InputsSent);
                    _inputSeq = (ushort)((_inputSeq + 1) & 0xFFFF);
                    _inputCount++;
                }

                if (now >= _nextChatS)
                {
                    int written = EncodeChat(_sendBuf, "hi");
                    TryWrite(_sendBuf.AsSpan(0, written));
                    _nextChatS = now + 2.0 + (BotId % 50) * 0.06;
                }
            }

            private void TryWrite(ReadOnlySpan<byte> data)
            {
                if (Closed) return;
                try { _stream.Write(data); }
                catch { Closed = true; Connected = false; }
            }

            private async Task ReceiveLoop(CancellationToken ct)
            {
                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        int n = await _stream.ReadAsync(
                            _readBuf.AsMemory(_readBufLen, _readBuf.Length - _readBufLen), ct)
                            .ConfigureAwait(false);
                        if (n <= 0) break;
                        _readBufLen += n;
                        DrainFrames();
                    }
                }
                catch (OperationCanceledException) { /* clean shutdown */ }
                catch { /* socket reset */ }
                finally { Closed = true; Connected = false; }
            }

            private void DrainFrames()
            {
                int offset = 0, frames = 0;
                while (offset + 2 <= _readBufLen)
                {
                    int frameLen = BinaryPrimitives.ReadUInt16LittleEndian(_readBuf.AsSpan(offset, 2));
                    if (frameLen <= 0 || offset + 2 + frameLen > _readBufLen) break;
                    offset += 2 + frameLen;
                    frames++;
                }
                if (offset > 0)
                {
                    _readBufLen -= offset;
                    if (_readBufLen > 0)
                        Buffer.BlockCopy(_readBuf, offset, _readBuf, 0, _readBufLen);
                }
                if (frames > 0) Interlocked.Add(ref FramesDrained, frames);
            }

            // ── Inline frame encoders (no external codec dependency) ────────────

            // InputFloat3Codec wire: seq(2)+moveX(2)+moveZ(2)+flags(1)+clientTick(8) = 15 bytes
            // TCP frame:            len(2)+channel(1)+payload → 18 bytes total
            private static int EncodeInput(byte[] buf, ushort seq, short dx, short dz, long clientTickMs)
            {
                const int PayloadLen = 15;
                const byte Channel = 2; // ChannelType.UnreliableSequenced
                BinaryPrimitives.WriteUInt16LittleEndian(buf, (ushort)(1 + PayloadLen));
                buf[2] = Channel;
                BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(3), seq);
                BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(5), dx);
                BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(7), dz);
                buf[9] = 0; // flags
                BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(10), clientTickMs);
                return 2 + 1 + PayloadLen; // 18
            }

            // NetworkControlMessageCodec chat wire: type(0xD1)+utf8len(2)+utf8bytes
            private static int EncodeChat(byte[] buf, string text)
            {
                byte[] utf8 = Encoding.UTF8.GetBytes(text);
                const byte Channel = 0; // ChannelType.ReliableOrdered
                int payloadLen = 3 + utf8.Length; // type(1)+len(2)+utf8
                BinaryPrimitives.WriteUInt16LittleEndian(buf, (ushort)(1 + payloadLen));
                buf[2] = Channel;
                buf[3] = 0xD1; // MsgClientChat
                BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(4), (ushort)utf8.Length);
                utf8.AsSpan().CopyTo(buf.AsSpan(6));
                return 2 + 1 + payloadLen;
            }
        }

        // ── Swarm orchestration ────────────────────────────────────────────────

        private readonly string _host;
        private readonly int    _port;
        private readonly int    _botCount;
        private readonly double _spawnIntervalS;
        private readonly int    _activityPct;

        private readonly ConcurrentDictionary<int, AsyncBot> _bots = new();
        private int _nextBotId;

        private long _inputsSent;
        private long _framesDrained;
        private int  _connectedBots;
        private int  _peakConnectedBots;

        private readonly CancellationTokenSource _cts = new();
        private Task? _runTask;

        public AsyncBotSwarm(string host, int port, int botCount, double spawnIntervalS, int activityPct)
        {
            _host           = host;
            _port           = port;
            _botCount       = botCount;
            _spawnIntervalS = Math.Max(0.001, spawnIntervalS);
            _activityPct    = Math.Clamp(activityPct, 0, 100);
        }

        public void Start()
        {
            // Pre-provision ThreadPool so IOCP completions don't stall under burst.
            // Each bot needs ~2 concurrent tasks (recv + potential send overlap).
            int minWorkers    = _botCount * 2 + 128;
            int minCompletion = _botCount + 64;
            ThreadPool.GetMinThreads(out int curW, out int curC);
            ThreadPool.SetMinThreads(Math.Max(minWorkers, curW), Math.Max(minCompletion, curC));

            _runTask = Task.Run(RunAsync);
        }

        private async Task RunAsync()
        {
            var ct = _cts.Token;
            // Spawn and scheduler run concurrently; scheduler runs for the lifetime.
            await Task.WhenAll(SpawnAllAsync(ct), SchedulerLoopAsync(ct)).ConfigureAwait(false);
        }

        private async Task SpawnAllAsync(CancellationToken ct)
        {
            int intervalMs = Math.Max(1, (int)(_spawnIntervalS * 1000));
            for (int i = 0; i < _botCount && !ct.IsCancellationRequested; i++)
            {
                int botId = Interlocked.Increment(ref _nextBotId);
                _ = SpawnOneAsync(botId); // fire-and-forget; connect concurrently
                try { await Task.Delay(intervalMs, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }

        private async Task SpawnOneAsync(int botId)
        {
            var tcp = new TcpClient { NoDelay = true };
            try
            {
                var connectTask = tcp.ConnectAsync(_host, _port);
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                timeoutCts.CancelAfter(5000);
                try { await connectTask.WaitAsync(timeoutCts.Token).ConfigureAwait(false); }
                catch { tcp.Dispose(); return; }

                bool isActive = (botId % 100) < _activityPct;
                double now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
                var bot = new AsyncBot(tcp.GetStream(), botId, now, isActive);
                _bots[botId] = bot;
                bot.Start();
            }
            catch { tcp.Dispose(); }
        }

        private async Task SchedulerLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                double now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
                long inputs = 0, frames = 0;
                int connected = 0;

                foreach (var (id, bot) in _bots)
                {
                    if (bot.Closed) { _bots.TryRemove(id, out _); continue; }
                    bot.Tick(now);
                    inputs += Volatile.Read(ref bot.InputsSent);
                    frames += Volatile.Read(ref bot.FramesDrained);
                    if (bot.Connected) connected++;
                }

                Volatile.Write(ref _inputsSent,   inputs);
                Volatile.Write(ref _framesDrained, frames);
                Volatile.Write(ref _connectedBots, connected);
                if (connected > _peakConnectedBots)
                    Volatile.Write(ref _peakConnectedBots, connected);

                try { await Task.Delay(5, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }

        public BotSwarmSnapshot CaptureSnapshot() => new(
            Volatile.Read(ref _inputsSent),
            Volatile.Read(ref _framesDrained),
            Volatile.Read(ref _connectedBots),
            Volatile.Read(ref _peakConnectedBots));

        public void Dispose()
        {
            _cts.Cancel();
            foreach (var bot in _bots.Values) bot.Closed = true;
            try { _runTask?.Wait(3000); } catch { }
            _cts.Dispose();
        }
    }


    // ── Program ───────────────────────────────────────────────────────────────

    internal static class Program
    {
        private static double F(double v) => Math.Round(v, 3);

        private static string Slo(double actual, double threshold, bool lowerIsBetter = true)
            => lowerIsBetter
                ? (actual <= threshold ? "PASS" : "FAIL")
                : (actual >= threshold ? "PASS" : "FAIL");

        static int Main(string[] args)
        {
            try
            {
                HarnessOptions opts = HarnessOptions.Parse(args);
                Run(opts);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        private static void Run(HarnessOptions opts)
        {
            Console.OutputEncoding = Encoding.UTF8;
            string root = Path.Combine(Path.GetTempPath(), $"net_persist_perf_{Guid.NewGuid():N}");
            string saveRoot = Path.Combine(root, "Saves");

            Console.WriteLine("=== Server.NetworkPersistencePerf ===");
            Console.WriteLine($"Players: {opts.Players}  Items: {opts.Items}  Bots: {opts.Bots}  Topology: {opts.Topology}  Activity: {opts.ActivityPct}%");
            Console.WriteLine($"Port: {opts.Port}  TickHz: {opts.TickHz}  WindowTicks: {opts.WindowTicks}  SettleS: {opts.SettleSeconds}  SpawnWrap: {opts.ResolveSpawnParams().SpawnWrapCount}");
            Console.WriteLine($"SLO tick_avg: <{opts.SloTickAvgMs}ms  tick_p95: <{opts.SloTickP95Ms}ms  barrier: <{opts.SloSaveMs}ms  parallel-mode: {opts.ParallelMode}");
            Console.WriteLine();

            ServerPersistenceRegistration.EnsureComponentsRegistered();
            EnsureComponentRegistered<PerfItemStateComponent>();
            EnsureComponentRegistered<PerfItemDurabilityComponent>();

            var accountCoordinator = new ServerAccountPersistenceCoordinator(
                options: new ServerAccountOptions(AccountsPerIp: 0),
                serverDataRoot: root);
            var worldCoordinator = new WorldPersistenceCoordinator(
                new WorldPersistenceOptions(
                    SaveDirectory: saveRoot,
                    RecoveryMode: WorldPersistenceRecoveryMode.Abort,
                    ParallelMode: opts.ParallelMode));
            var persistenceCoordinator = new ServerPersistenceCoordinator(accountCoordinator, worldCoordinator);
            var saveWriteTracker = new SaveWriteTracker();

            ServerPersistenceRegistration.RegisterPersistentTypes(worldCoordinator);
            worldCoordinator.RegisterComponent<PerfItemStateComponent>();
            worldCoordinator.RegisterComponent<PerfItemDurabilityComponent>();

            var transport = new TcpFramedNetworkTransport(
                TcpFramedNetworkTransport.TransportRole.Server,
                host: "0.0.0.0",
                port: opts.Port);

            (int spawnSpacing, int spawnWrap) = opts.ResolveSpawnParams();

            var runtime = new ServerRuntime(
                transport,
                new ServerRuntimeOptions(
                    "net_persist_perf",
                    StartTransportOnStart: true,
                    StopTransportOnStop: true,
                    SaveOnStop: false,
                    SaveDirectory: saveRoot,
                    AccountAuthenticationProvider: new TestAuthProvider(),
                    PlayerSpawnSpacingGrid: spawnSpacing,
                    PlayerSpawnWrapCount: spawnWrap,
                    PlayerChunkAoiRadius: opts.AoiRadius),
                hooks: new ServerRuntimeHooks
                {
                    Load = rt => persistenceCoordinator.Load(rt),
                    CaptureSaveSnapshot = rt => persistenceCoordinator.CaptureSnapshot(rt),
                    WriteSaveSnapshot = snapshot => saveWriteTracker.RecordWrite(
                        () => persistenceCoordinator.WriteSnapshot((ServerPersistenceSaveSnapshot)snapshot)),
                });

            runtime.Start();
            Console.WriteLine($"Server listening on TCP port {opts.Port}.");

            WorldSpawnLayout? worldLayout = opts.BuildWorldLayout();
            if (worldLayout is not null)
                Console.WriteLine($"World topology: 256×256 chunks, 3 hotspots (25 bots each), town area + wilderness.");

            if (opts.Players > 0)
            {
                Console.WriteLine($"Creating {opts.Players} player entities...");
                CreatePlayers(runtime, opts.Players, worldLayout);
            }

            if (opts.Items > 0)
            {
                Console.WriteLine($"Creating {opts.Items} item entities...");
                CreateItems(runtime, opts.Items);
            }

            Console.WriteLine();

            AsyncBotSwarm? internalBots = null;
            if (opts.Bots > 0 && opts.BotHarnessExe is null)
            {
                internalBots = new AsyncBotSwarm("127.0.0.1", opts.Port, opts.Bots,
                    spawnIntervalS: 0.01, opts.ActivityPct);
                internalBots.Start();
                Console.WriteLine($"Async bot swarm started, targeting {opts.Bots} bots (activity={opts.ActivityPct}%).");
            }

            float deltaSeconds = 1.0f / opts.TickHz;
            long tickInterval = Stopwatch.Frequency / opts.TickHz;
            var controller = new TickLoopController();
            var tickLoopDone = new ManualResetEventSlim(false);
            bool stopLoop = false;
            int nextAutoBotIndex = 1;

            var tickThread = new Thread(() =>
            {
                var connectionEvents = new TcpFramedNetworkTransport.ConnectionEvent[128];
                bool barrierRecorded = false;
                long nextTick = Stopwatch.GetTimestamp();

                while (!stopLoop)
                {
                    long now = Stopwatch.GetTimestamp();
                    if (now < nextTick)
                    {
                        int waitMs = (int)((nextTick - now) * 1000 / Stopwatch.Frequency) - 1;
                        if (waitMs > 0)
                            Thread.Sleep(waitMs);

                        while (Stopwatch.GetTimestamp() < nextTick)
                            Thread.SpinWait(10);
                    }

                    TickPhase phase = controller.Phase;
                    if (phase != TickPhase.Saving)
                        barrierRecorded = false;

                    long tickStart = Stopwatch.GetTimestamp();
                    long tickNow = Stopwatch.GetTimestamp();
                    ProcessConnectionEvents(runtime, transport, connectionEvents, tickNow, ref nextAutoBotIndex, worldLayout);
                    runtime.ProcessInboundTransportDatagrams(tickNow, maxDatagrams: 4096);
                    runtime.Tick(deltaSeconds);
                    runtime.Transport.FlushOutbound();
                    double tickMs = (Stopwatch.GetTimestamp() - tickStart) * 1000.0 / Stopwatch.Frequency;

                    controller.RecordTick(tickMs, phase);
                    if (phase == TickPhase.Saving && !barrierRecorded)
                    {
                        controller.RecordBarrierTick(tickMs);
                        barrierRecorded = true;
                    }

                    nextTick += tickInterval;
                    if (Stopwatch.GetTimestamp() - nextTick > tickInterval * 2)
                        nextTick = Stopwatch.GetTimestamp() + tickInterval;
                }

                tickLoopDone.Set();
            })
            {
                IsBackground = true,
                Name = "TickLoop",
            };
            tickThread.Start();
            Console.WriteLine("Tick loop started.");

            Process? botProcess = null;
            string? botJsonPath = null;
            // When --no-save, only need settle + one BEFORE window; otherwise settle + BEFORE + save + AFTER.
            double totalDurationSeconds = opts.NoSave
                ? opts.SettleSeconds + (opts.WindowTicks / (double)opts.TickHz) + 5.0
                : opts.SettleSeconds + (opts.WindowTicks * 2.0 / opts.TickHz) + 5.0 + 3.0;

            if (opts.Bots > 0 && opts.BotHarnessExe is not null)
            {
                string botReportDir = Path.Combine(root, "bot_reports");
                Directory.CreateDirectory(botReportDir);

                var psi = new ProcessStartInfo
                {
                    FileName = opts.BotHarnessExe,
                    UseShellExecute = false,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false,
                };
                psi.ArgumentList.Add("--host"); psi.ArgumentList.Add("127.0.0.1");
                psi.ArgumentList.Add("--port"); psi.ArgumentList.Add(opts.Port.ToString(CultureInfo.InvariantCulture));
                psi.ArgumentList.Add("--stages"); psi.ArgumentList.Add(opts.Bots.ToString(CultureInfo.InvariantCulture));
                psi.ArgumentList.Add("--stage-mode"); psi.ArgumentList.Add("independent");
                // Use max(30, windowTicks*2) as the window floor so bots stay alive even under GC pressure.
                int botStageDuration = Math.Max(30, opts.WindowTicks * 2) + (int)opts.SettleSeconds + 30;
                psi.ArgumentList.Add("--stage-duration"); psi.ArgumentList.Add(botStageDuration.ToString(CultureInfo.InvariantCulture));
                psi.ArgumentList.Add("--settle-seconds"); psi.ArgumentList.Add("1");
                psi.ArgumentList.Add("--spawn-interval"); psi.ArgumentList.Add("0.01");
                psi.ArgumentList.Add("--report-dir"); psi.ArgumentList.Add(botReportDir);
                if (opts.ActivityPct < 100)
                {
                    psi.ArgumentList.Add("--active-ratio");
                    psi.ArgumentList.Add((opts.ActivityPct / 100.0).ToString("F2", CultureInfo.InvariantCulture));
                    psi.ArgumentList.Add("--chat-active-ratio");
                    psi.ArgumentList.Add((opts.ActivityPct / 100.0).ToString("F2", CultureInfo.InvariantCulture));
                }
                if (opts.ActivityPct > 0)
                    psi.ArgumentList.Add("--chat");

                botProcess = Process.Start(psi);
                Console.WriteLine($"External bot harness launched (PID {botProcess?.Id}), targeting {opts.Bots} bots.");
            }

            Console.Write($"Settling for {opts.SettleSeconds}s...");
            Thread.Sleep((int)(opts.SettleSeconds * 1000));
            Console.WriteLine($" done. Active sessions: {runtime.Sessions.Count}");

            // Allow up to 2 seconds per tick for heavily loaded scenarios.
            TimeSpan windowTimeout = TimeSpan.FromSeconds(Math.Max(30, opts.WindowTicks * 2));

            Console.WriteLine($"Recording BEFORE window ({opts.WindowTicks} ticks)...");
            controller.SetPhase(TickPhase.Before);
            ActivitySnapshot beforeStartActivity = ActivitySnapshot.Capture(runtime, internalBots);
            BandwidthSnapshot bandwidthStart = BandwidthSnapshot.Capture(transport, runtime);
            HotPathTimingSnapshot hotPathStart = HotPathTimingSnapshot.Capture(transport, runtime);
            int gc0Start = GC.CollectionCount(0);
            int gc1Start = GC.CollectionCount(1);
            int gc2Start = GC.CollectionCount(2);
            long allocStart = GC.GetTotalAllocatedBytes(true);
            controller.WaitForTickCount(TickPhase.Before, opts.WindowTicks, windowTimeout);
            int gc0BeforeEnd = GC.CollectionCount(0);
            int gc1BeforeEnd = GC.CollectionCount(1);
            int gc2BeforeEnd = GC.CollectionCount(2);
            long allocBeforeEnd = GC.GetTotalAllocatedBytes(false);
            ActivitySnapshot beforeEndActivity = ActivitySnapshot.Capture(runtime, internalBots);
            BandwidthSnapshot bandwidthEnd = BandwidthSnapshot.Capture(transport, runtime);
            HotPathTimingSnapshot hotPathEnd = HotPathTimingSnapshot.Capture(transport, runtime);

            var beforeWindow = new WindowStats(
                controller.GetBeforeTicks(),
                gc0BeforeEnd - gc0Start,
                gc1BeforeEnd - gc1Start,
                gc2BeforeEnd - gc2Start,
                allocBeforeEnd - allocStart);
            ActivityDelta beforeActivity = new(beforeStartActivity, beforeEndActivity);
            var bandwidthStats = new BandwidthStats(bandwidthStart, bandwidthEnd, opts.WindowTicks);
            var hotPathStats = new HotPathTimingStats(hotPathStart, hotPathEnd, opts.WindowTicks);

            if (opts.NoSave)
            {
                controller.SetPhase(TickPhase.Done);
                stopLoop = true;
                tickLoopDone.Wait(TimeSpan.FromSeconds(5));
                runtime.Stop();
                internalBots?.Dispose();
                if (botProcess is { HasExited: false })
                {
                    Console.Write("Stopping bot harness...");
                    try { botProcess.Kill(); botProcess.WaitForExit(3000); } catch { }
                    Console.WriteLine(" done.");
                }
                PrintNetworkOnlyResults(opts, beforeWindow, beforeActivity, bandwidthStats, hotPathStats);
                try { Directory.Delete(root, recursive: true); } catch { }
                return;
            }

            Console.Write("Triggering save request...");
            controller.SetPhase(TickPhase.Saving);
            saveWriteTracker.Reset();
            int sessionsAtSave = runtime.Sessions.Count;
            ActivitySnapshot saveStartActivity = ActivitySnapshot.Capture(runtime, internalBots);
            int gc0SaveStart = GC.CollectionCount(0);
            int gc1SaveStart = GC.CollectionCount(1);
            int gc2SaveStart = GC.CollectionCount(2);
            long allocSaveStart = GC.GetTotalAllocatedBytes(true);
            var completionStopwatch = Stopwatch.StartNew();
            runtime.RequestSave();

            double barrierMs = controller.WaitForBarrierTick(windowTimeout);
            saveWriteTracker.WaitForWriteStart(windowTimeout);
            double backgroundWriteMs = saveWriteTracker.WaitForWriteCompletion(windowTimeout);
            completionStopwatch.Stop();
            ActivitySnapshot saveEndActivity = ActivitySnapshot.Capture(runtime, internalBots);
            ActivityDelta duringActivity = new(saveStartActivity, saveEndActivity);

            var saveStats = new SaveStats(
                barrierMs,
                completionStopwatch.Elapsed.TotalMilliseconds,
                backgroundWriteMs,
                worldCoordinator.LastSaveTiming.TotalDuration.TotalMilliseconds,
                worldCoordinator.LastSaveTiming.EntitiesWriteDuration.TotalMilliseconds,
                accountCoordinator.LastSaveTiming.TotalDuration.TotalMilliseconds,
                GC.CollectionCount(0) - gc0SaveStart,
                GC.CollectionCount(1) - gc1SaveStart,
                GC.CollectionCount(2) - gc2SaveStart,
                GC.GetTotalAllocatedBytes(false) - allocSaveStart);
            var duringWindow = new WindowStats(
                controller.GetDuringTicks(),
                saveStats.Gc0,
                saveStats.Gc1,
                saveStats.Gc2,
                saveStats.AllocBytes);
            Console.WriteLine($" done. barrier={F(saveStats.BarrierMs)}ms completion={F(saveStats.CompletionMs)}ms background={F(saveStats.BackgroundWriteMs)}ms (sessions={sessionsAtSave})");

            Console.WriteLine($"Recording AFTER window ({opts.WindowTicks} ticks)...");
            controller.SetPhase(TickPhase.After);
            ActivitySnapshot afterStartActivity = ActivitySnapshot.Capture(runtime, internalBots);
            int gc0AfterStart = GC.CollectionCount(0);
            int gc1AfterStart = GC.CollectionCount(1);
            int gc2AfterStart = GC.CollectionCount(2);
            long allocAfterStart = GC.GetTotalAllocatedBytes(true);
            controller.WaitForTickCount(TickPhase.After, opts.WindowTicks, windowTimeout);
            int gc0AfterEnd = GC.CollectionCount(0);
            int gc1AfterEnd = GC.CollectionCount(1);
            int gc2AfterEnd = GC.CollectionCount(2);
            long allocAfterEnd = GC.GetTotalAllocatedBytes(false);
            ActivitySnapshot afterEndActivity = ActivitySnapshot.Capture(runtime, internalBots);

            var afterWindow = new WindowStats(
                controller.GetAfterTicks(),
                gc0AfterEnd - gc0AfterStart,
                gc1AfterEnd - gc1AfterStart,
                gc2AfterEnd - gc2AfterStart,
                allocAfterEnd - allocAfterStart);
            ActivityDelta afterActivity = new(afterStartActivity, afterEndActivity);

            controller.SetPhase(TickPhase.Done);
            stopLoop = true;
            tickLoopDone.Wait(TimeSpan.FromSeconds(5));

            runtime.Stop();
            internalBots?.Dispose();

            if (botProcess is { HasExited: false })
            {
                Console.Write("Stopping bot harness...");
                try { botProcess.Kill(); botProcess.WaitForExit(3000); } catch { }
                Console.WriteLine(" done.");
            }

            if (opts.Bots > 0 && opts.BotHarnessExe is not null)
            {
                string botReportDir = Path.Combine(root, "bot_reports");
                if (Directory.Exists(botReportDir))
                {
                    string[] jsonFiles = Directory.GetFiles(botReportDir, "*.json");
                    if (jsonFiles.Length > 0)
                        botJsonPath = jsonFiles[^1];
                }
            }

            PrintResults(opts, beforeWindow, beforeActivity, bandwidthStats, hotPathStats, duringWindow, duringActivity, saveStats, afterWindow, afterActivity, sessionsAtSave);

            if (opts.OutJson is not null)
            {
                WriteJson(
                    opts.OutJson,
                    opts,
                    beforeWindow,
                    beforeActivity,
                    hotPathStats,
                    duringWindow,
                    duringActivity,
                    saveStats,
                    afterWindow,
                    afterActivity,
                    sessionsAtSave,
                    botJsonPath);
            }

            try { Directory.Delete(root, recursive: true); } catch { }
        }

        private static void PrintNetworkOnlyResults(
            HarnessOptions opts,
            WindowStats before,
            ActivityDelta beforeActivity,
            BandwidthStats bandwidth,
            HotPathTimingStats hotPath)
        {
            Console.WriteLine();
            Console.WriteLine("=== RESULTS (network-only, --no-save) ===");
            Console.WriteLine($"NET_PERSIST|players={opts.Players}|items={opts.Items}|bots={opts.Bots}|mode=network_only");
            Console.WriteLine($"BEFORE_WINDOW|ticks={before.Count}|avg_ms={F(before.AvgMs)}|p95_ms={F(before.P95Ms)}|max_ms={F(before.MaxMs)}|gc0={before.Gc0}|gc1={before.Gc1}|gc2={before.Gc2}|alloc_bytes={before.AllocBytes}");
            Console.WriteLine($"ACTIVITY_BEFORE|bot_inputs_sent={beforeActivity.BotInputsSent}|server_inputs_received={beforeActivity.ServerReceivedInputs}|server_inputs_applied={beforeActivity.ServerAppliedInputs}|connected_bots={beforeActivity.ConnectedBots}|peak_connected_bots={beforeActivity.PeakConnectedBots}");
            Console.WriteLine($"BANDWIDTH   |elapsed_s={F(bandwidth.ElapsedSeconds)}|ccu={bandwidth.Sessions}|tracked={bandwidth.TrackedConnections}" +
                $"|aoi={bandwidth.EnableChunkAoi}|aoi_r={bandwidth.ChunkAoiRadius}|chunk_sz={bandwidth.ChunkSizeGridUnits}" +
                $"|out_total_KBps={Math.Round(bandwidth.TotalBytesOutPerSec / 1024, 1)}" +
                $"|out_per_ccu_KBps={Math.Round(bandwidth.BytesOutPerCcuPerSec / 1024, 2)}" +
                $"|reliable_KBps={Math.Round(bandwidth.ReliableBytesOutPerSec / 1024, 2)}" +
                $"|unreliable_KBps={Math.Round(bandwidth.UnreliableBytesOutPerSec / 1024, 1)}" +
                $"|avg_visible_peers={Math.Round(bandwidth.AvgVisiblePeersPerPlayer, 1)}" +
                $"|min_visible_peers={bandwidth.MinVisiblePeersPerPlayer}" +
                $"|max_visible_peers={bandwidth.MaxVisiblePeersPerPlayer}" +
                $"|avg_correction_B={F(bandwidth.AvgCorrectionPayloadBytes):F1}" +
                $"|avg_observer_B={F(bandwidth.AvgObserverPayloadBytes):F1}" +
                $"|lod_checks={bandwidth.LodChecks}|lod_sent={bandwidth.LodSent}|lod_gated={bandwidth.LodGated}" +
                $"|lod_near={bandwidth.LodNearSent}|lod_ring1={bandwidth.LodRing1Sent}|lod_ring2={bandwidth.LodRing2Sent}" +
                $"|ring0_avg={Math.Round(bandwidth.AvgEntitiesPerConnPerTickRing0, 1)}" +
                $"|ring1_avg={Math.Round(bandwidth.AvgEntitiesPerConnPerTickRing1, 1)}" +
                $"|ring2_avg={Math.Round(bandwidth.AvgEntitiesPerConnPerTickRing2, 1)}");
            Console.WriteLine($"HOTPATH     |elapsed_s={F(hotPath.ElapsedSeconds)}|ticks={hotPath.WindowTicks}|hot={hotPath.HottestStage}|hot_ms_per_tick={F(hotPath.HottestStageMsPerTick)}" +
                $"|aoi_mspt={F(hotPath.AoiMsPerTick)}|obs_chunk_build_mspt={F(hotPath.ObserverChunkBuildMsPerTick)}|obs_chunk_publish_mspt={F(hotPath.ObserverChunkPublishMsPerTick)}" +
                $"|bridge_mspt={F(hotPath.BridgeBuildMsPerTick)}|replay_mspt={F(hotPath.ReplayVisitMsPerTick)}|coalesce_mspt={F(hotPath.CoalesceCommitMsPerTick)}" +
                $"|deferred_mspt={F(hotPath.DeferredFlushMsPerTick)}|select_mspt={F(hotPath.RecipientSelectionMsPerTick)}|dispatch_mspt={F(hotPath.SelectedDispatchMsPerTick)}" +
                $"|trysend_mspt={F(hotPath.TrySendMsPerTick)}|flush_mspt={F(hotPath.ServerFlushMsPerTick)}|tcp_batch_mspt={F(hotPath.TcpBatchWriteMsPerTick)}|tcp_single_mspt={F(hotPath.TcpSingleWriteMsPerTick)}" +
                $"|select_mspc={F(hotPath.RecipientSelectionMsPerCall)}|dispatch_mspc={F(hotPath.SelectedDispatchMsPerCall)}|trysend_mspc={F(hotPath.TrySendMsPerCall)}");

            Console.WriteLine();
            Console.WriteLine("=== SLO CHECK ===");
            Console.WriteLine($"  before_tick_avg   {F(before.AvgMs):F3}ms < {opts.SloTickAvgMs}ms  [{Slo(before.AvgMs, opts.SloTickAvgMs)}]");
            Console.WriteLine($"  before_tick_p95   {F(before.P95Ms):F3}ms < {opts.SloTickP95Ms}ms  [{Slo(before.P95Ms, opts.SloTickP95Ms)}]");

            bool anyFail = before.AvgMs > opts.SloTickAvgMs || before.P95Ms > opts.SloTickP95Ms;
            Console.WriteLine();
            Console.WriteLine(anyFail ? "OVERALL: FAIL" : "OVERALL: PASS");
        }

        private static void PrintResults(
            HarnessOptions opts,
            WindowStats before,
            ActivityDelta beforeActivity,
            BandwidthStats bandwidth,
            HotPathTimingStats hotPath,
            WindowStats during,
            ActivityDelta duringActivity,
            SaveStats save,
            WindowStats after,
            ActivityDelta afterActivity,
            int sessionsAtSave)
        {
            double tickDeltaAvg = after.AvgMs - before.AvgMs;
            double tickDeltaP95 = after.P95Ms - before.P95Ms;

            Console.WriteLine();
            Console.WriteLine("=== RESULTS ===");
            Console.WriteLine($"NET_PERSIST|players={opts.Players}|items={opts.Items}|bots={opts.Bots}|sessions_at_save={sessionsAtSave}");
            Console.WriteLine($"BEFORE_WINDOW|ticks={before.Count}|avg_ms={F(before.AvgMs)}|p95_ms={F(before.P95Ms)}|max_ms={F(before.MaxMs)}|gc0={before.Gc0}|gc1={before.Gc1}|gc2={before.Gc2}|alloc_bytes={before.AllocBytes}");
            Console.WriteLine($"DURING_SAVE |ticks={during.Count}|avg_ms={F(during.AvgMs)}|p95_ms={F(during.P95Ms)}|max_ms={F(during.MaxMs)}|gc0={during.Gc0}|gc1={during.Gc1}|gc2={during.Gc2}|alloc_bytes={during.AllocBytes}");
            Console.WriteLine($"SAVE        |barrier_ms={F(save.BarrierMs)}|completion_ms={F(save.CompletionMs)}|background_ms={F(save.BackgroundWriteMs)}|world_save_ms={F(save.WorldSaveMs)}|entities_ms={F(save.EntitiesMs)}|account_ms={F(save.AccountMs)}");
            Console.WriteLine($"AFTER_WINDOW|ticks={after.Count}|avg_ms={F(after.AvgMs)}|p95_ms={F(after.P95Ms)}|max_ms={F(after.MaxMs)}|gc0={after.Gc0}|gc1={after.Gc1}|gc2={after.Gc2}|alloc_bytes={after.AllocBytes}");
            Console.WriteLine($"ACTIVITY_BEFORE|bot_inputs_sent={beforeActivity.BotInputsSent}|server_inputs_received={beforeActivity.ServerReceivedInputs}|server_inputs_applied={beforeActivity.ServerAppliedInputs}|connected_bots={beforeActivity.ConnectedBots}|peak_connected_bots={beforeActivity.PeakConnectedBots}");
            Console.WriteLine($"ACTIVITY_SAVE  |bot_inputs_sent={duringActivity.BotInputsSent}|server_inputs_received={duringActivity.ServerReceivedInputs}|server_inputs_applied={duringActivity.ServerAppliedInputs}|connected_bots={duringActivity.ConnectedBots}|peak_connected_bots={duringActivity.PeakConnectedBots}");
            Console.WriteLine($"ACTIVITY_AFTER |bot_inputs_sent={afterActivity.BotInputsSent}|server_inputs_received={afterActivity.ServerReceivedInputs}|server_inputs_applied={afterActivity.ServerAppliedInputs}|connected_bots={afterActivity.ConnectedBots}|peak_connected_bots={afterActivity.PeakConnectedBots}");
            Console.WriteLine($"TICK_DELTA  |avg_delta_ms={F(tickDeltaAvg):+0.000;-0.000;0.000}|p95_delta_ms={F(tickDeltaP95):+0.000;-0.000;0.000}");
            Console.WriteLine($"BANDWIDTH   |elapsed_s={F(bandwidth.ElapsedSeconds)}|ccu={bandwidth.Sessions}|tracked={bandwidth.TrackedConnections}" +
                $"|aoi={bandwidth.EnableChunkAoi}|aoi_r={bandwidth.ChunkAoiRadius}|chunk_sz={bandwidth.ChunkSizeGridUnits}" +
                $"|out_total_KBps={Math.Round(bandwidth.TotalBytesOutPerSec / 1024, 1)}" +
                $"|out_per_ccu_KBps={Math.Round(bandwidth.BytesOutPerCcuPerSec / 1024, 2)}" +
                $"|reliable_KBps={Math.Round(bandwidth.ReliableBytesOutPerSec / 1024, 2)}" +
                $"|unreliable_KBps={Math.Round(bandwidth.UnreliableBytesOutPerSec / 1024, 1)}" +
                $"|avg_visible_peers={Math.Round(bandwidth.AvgVisiblePeersPerPlayer, 1)}" +
                $"|min_visible_peers={bandwidth.MinVisiblePeersPerPlayer}" +
                $"|max_visible_peers={bandwidth.MaxVisiblePeersPerPlayer}" +
                $"|avg_correction_B={F(bandwidth.AvgCorrectionPayloadBytes):F1}" +
                $"|avg_observer_B={F(bandwidth.AvgObserverPayloadBytes):F1}" +
                $"|lod_checks={bandwidth.LodChecks}|lod_sent={bandwidth.LodSent}|lod_gated={bandwidth.LodGated}" +
                $"|lod_near={bandwidth.LodNearSent}|lod_ring1={bandwidth.LodRing1Sent}|lod_ring2={bandwidth.LodRing2Sent}" +
                $"|ring0_avg={Math.Round(bandwidth.AvgEntitiesPerConnPerTickRing0, 1)}" +
                $"|ring1_avg={Math.Round(bandwidth.AvgEntitiesPerConnPerTickRing1, 1)}" +
                $"|ring2_avg={Math.Round(bandwidth.AvgEntitiesPerConnPerTickRing2, 1)}");
            Console.WriteLine($"HOTPATH     |elapsed_s={F(hotPath.ElapsedSeconds)}|ticks={hotPath.WindowTicks}|hot={hotPath.HottestStage}|hot_ms_per_tick={F(hotPath.HottestStageMsPerTick)}" +
                $"|aoi_mspt={F(hotPath.AoiMsPerTick)}|obs_chunk_build_mspt={F(hotPath.ObserverChunkBuildMsPerTick)}|obs_chunk_publish_mspt={F(hotPath.ObserverChunkPublishMsPerTick)}" +
                $"|bridge_mspt={F(hotPath.BridgeBuildMsPerTick)}|replay_mspt={F(hotPath.ReplayVisitMsPerTick)}|coalesce_mspt={F(hotPath.CoalesceCommitMsPerTick)}" +
                $"|deferred_mspt={F(hotPath.DeferredFlushMsPerTick)}|select_mspt={F(hotPath.RecipientSelectionMsPerTick)}|dispatch_mspt={F(hotPath.SelectedDispatchMsPerTick)}" +
                $"|trysend_mspt={F(hotPath.TrySendMsPerTick)}|flush_mspt={F(hotPath.ServerFlushMsPerTick)}|tcp_batch_mspt={F(hotPath.TcpBatchWriteMsPerTick)}|tcp_single_mspt={F(hotPath.TcpSingleWriteMsPerTick)}" +
                $"|select_mspc={F(hotPath.RecipientSelectionMsPerCall)}|dispatch_mspc={F(hotPath.SelectedDispatchMsPerCall)}|trysend_mspc={F(hotPath.TrySendMsPerCall)}");

            Console.WriteLine();
            Console.WriteLine("=== SLO CHECK ===");
            Console.WriteLine($"  before_tick_avg   {F(before.AvgMs):F3}ms < {opts.SloTickAvgMs}ms  [{Slo(before.AvgMs, opts.SloTickAvgMs)}]");
            Console.WriteLine($"  before_tick_p95   {F(before.P95Ms):F3}ms < {opts.SloTickP95Ms}ms  [{Slo(before.P95Ms, opts.SloTickP95Ms)}]");
            Console.WriteLine($"  during_tick_avg   {F(during.AvgMs):F3}ms < {opts.SloTickAvgMs}ms  [{Slo(during.AvgMs, opts.SloTickAvgMs)}]");
            Console.WriteLine($"  during_tick_p95   {F(during.P95Ms):F3}ms < {opts.SloTickP95Ms}ms  [{Slo(during.P95Ms, opts.SloTickP95Ms)}]");
            Console.WriteLine($"  after_tick_avg    {F(after.AvgMs):F3}ms < {opts.SloTickAvgMs}ms  [{Slo(after.AvgMs, opts.SloTickAvgMs)}]");
            Console.WriteLine($"  after_tick_p95    {F(after.P95Ms):F3}ms < {opts.SloTickP95Ms}ms  [{Slo(after.P95Ms, opts.SloTickP95Ms)}]");
            Console.WriteLine($"  save_barrier_ms   {F(save.BarrierMs):F3}ms < {opts.SloSaveMs}ms  [{Slo(save.BarrierMs, opts.SloSaveMs)}]");

            bool anyFail =
                save.BarrierMs > opts.SloSaveMs ||
                before.AvgMs > opts.SloTickAvgMs || before.P95Ms > opts.SloTickP95Ms ||
                during.AvgMs > opts.SloTickAvgMs || during.P95Ms > opts.SloTickP95Ms ||
                after.AvgMs > opts.SloTickAvgMs || after.P95Ms > opts.SloTickP95Ms;

            Console.WriteLine();
            Console.WriteLine(anyFail ? "OVERALL: FAIL" : "OVERALL: PASS");
        }

        private static void WriteJson(
            string path,
            HarnessOptions opts,
            WindowStats before,
            ActivityDelta beforeActivity,
            HotPathTimingStats hotPath,
            WindowStats during,
            ActivityDelta duringActivity,
            SaveStats save,
            WindowStats after,
            ActivityDelta afterActivity,
            int sessionsAtSave,
            string? botJsonPath)
        {
            string dir = Path.GetDirectoryName(path) ?? ".";
            Directory.CreateDirectory(dir);

            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            using var jw = new Utf8JsonWriter(fs, new JsonWriterOptions { Indented = true });

            jw.WriteStartObject();
            jw.WriteString("run_at", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            jw.WriteString("harness", "Server.NetworkPersistencePerf");
            jw.WriteString("host", Environment.MachineName);
            jw.WriteNumber("cpu_count", Environment.ProcessorCount);

            jw.WriteStartObject("config");
            jw.WriteNumber("players", opts.Players);
            jw.WriteNumber("items", opts.Items);
            jw.WriteNumber("bots", opts.Bots);
            jw.WriteNumber("port", opts.Port);
            jw.WriteNumber("tick_hz", opts.TickHz);
            jw.WriteNumber("window_ticks", opts.WindowTicks);
            jw.WriteNumber("settle_s", opts.SettleSeconds);
            jw.WriteString("parallel_mode", opts.ParallelMode.ToString());
            jw.WriteEndObject();

            jw.WriteNumber("sessions_at_save", sessionsAtSave);
            WriteWindowObject(jw, "before_window", before);
            WriteActivityObject(jw, "before_activity", beforeActivity);
            WriteHotPathObject(jw, "before_hotpath", hotPath);
            WriteWindowObject(jw, "during_save_window", during);
            WriteActivityObject(jw, "during_save_activity", duringActivity);

            jw.WriteStartObject("save");
            jw.WriteNumber("barrier_ms", Math.Round(save.BarrierMs, 3));
            jw.WriteNumber("completion_ms", Math.Round(save.CompletionMs, 3));
            jw.WriteNumber("background_write_ms", Math.Round(save.BackgroundWriteMs, 3));
            jw.WriteNumber("world_save_ms", Math.Round(save.WorldSaveMs, 3));
            jw.WriteNumber("entities_ms", Math.Round(save.EntitiesMs, 3));
            jw.WriteNumber("account_ms", Math.Round(save.AccountMs, 3));
            jw.WriteNumber("gc0", save.Gc0);
            jw.WriteNumber("gc1", save.Gc1);
            jw.WriteNumber("gc2", save.Gc2);
            jw.WriteNumber("alloc_bytes", save.AllocBytes);
            jw.WriteEndObject();

            WriteWindowObject(jw, "after_window", after);
            WriteActivityObject(jw, "after_activity", afterActivity);

            jw.WriteStartObject("tick_delta");
            jw.WriteNumber("avg_ms", Math.Round(after.AvgMs - before.AvgMs, 3));
            jw.WriteNumber("p95_ms", Math.Round(after.P95Ms - before.P95Ms, 3));
            jw.WriteEndObject();

            jw.WriteStartObject("slo");
            jw.WriteBoolean("before_tick_avg_pass", before.AvgMs <= opts.SloTickAvgMs);
            jw.WriteBoolean("before_tick_p95_pass", before.P95Ms <= opts.SloTickP95Ms);
            jw.WriteBoolean("during_tick_avg_pass", during.AvgMs <= opts.SloTickAvgMs);
            jw.WriteBoolean("during_tick_p95_pass", during.P95Ms <= opts.SloTickP95Ms);
            jw.WriteBoolean("after_tick_avg_pass", after.AvgMs <= opts.SloTickAvgMs);
            jw.WriteBoolean("after_tick_p95_pass", after.P95Ms <= opts.SloTickP95Ms);
            jw.WriteBoolean("save_barrier_ms_pass", save.BarrierMs <= opts.SloSaveMs);
            jw.WriteEndObject();

            if (botJsonPath is not null)
                jw.WriteString("bot_harness_report", botJsonPath);

            jw.WriteEndObject();
            Console.WriteLine($"JSON: {path}");
        }

        private static void WriteWindowObject(Utf8JsonWriter jw, string name, WindowStats w)
        {
            jw.WriteStartObject(name);
            jw.WriteNumber("ticks", w.Count);
            jw.WriteNumber("avg_ms", Math.Round(w.AvgMs, 3));
            jw.WriteNumber("p95_ms", Math.Round(w.P95Ms, 3));
            jw.WriteNumber("max_ms", Math.Round(w.MaxMs, 3));
            jw.WriteNumber("gc0", w.Gc0);
            jw.WriteNumber("gc1", w.Gc1);
            jw.WriteNumber("gc2", w.Gc2);
            jw.WriteNumber("alloc_bytes", w.AllocBytes);
            jw.WriteEndObject();
        }

        private static void WriteActivityObject(Utf8JsonWriter jw, string name, ActivityDelta activity)
        {
            jw.WriteStartObject(name);
            jw.WriteNumber("bot_inputs_sent", activity.BotInputsSent);
            jw.WriteNumber("bot_inbound_datagrams", activity.BotInboundDatagrams);
            jw.WriteNumber("server_inputs_received", activity.ServerReceivedInputs);
            jw.WriteNumber("server_inputs_applied", activity.ServerAppliedInputs);
            jw.WriteNumber("connected_bots", activity.ConnectedBots);
            jw.WriteNumber("peak_connected_bots", activity.PeakConnectedBots);
            jw.WriteEndObject();
        }

        private static void WriteHotPathObject(Utf8JsonWriter jw, string name, HotPathTimingStats hotPath)
        {
            jw.WriteStartObject(name);
            jw.WriteNumber("elapsed_s", Math.Round(hotPath.ElapsedSeconds, 3));
            jw.WriteNumber("ticks", hotPath.WindowTicks);
            jw.WriteString("hot_stage", hotPath.HottestStage);
            jw.WriteNumber("hot_stage_ms_per_tick", Math.Round(hotPath.HottestStageMsPerTick, 4));
            jw.WriteNumber("aoi_ms_per_tick", Math.Round(hotPath.AoiMsPerTick, 4));
            jw.WriteNumber("observer_chunk_build_ms_per_tick", Math.Round(hotPath.ObserverChunkBuildMsPerTick, 4));
            jw.WriteNumber("observer_chunk_publish_ms_per_tick", Math.Round(hotPath.ObserverChunkPublishMsPerTick, 4));
            jw.WriteNumber("bridge_build_ms_per_tick", Math.Round(hotPath.BridgeBuildMsPerTick, 4));
            jw.WriteNumber("pipeline_replay_visit_ms_per_tick", Math.Round(hotPath.ReplayVisitMsPerTick, 4));
            jw.WriteNumber("pipeline_coalesce_commit_ms_per_tick", Math.Round(hotPath.CoalesceCommitMsPerTick, 4));
            jw.WriteNumber("pipeline_deferred_flush_ms_per_tick", Math.Round(hotPath.DeferredFlushMsPerTick, 4));
            jw.WriteNumber("pipeline_recipient_selection_ms_per_tick", Math.Round(hotPath.RecipientSelectionMsPerTick, 4));
            jw.WriteNumber("pipeline_selected_dispatch_ms_per_tick", Math.Round(hotPath.SelectedDispatchMsPerTick, 4));
            jw.WriteNumber("pipeline_try_send_ms_per_tick", Math.Round(hotPath.TrySendMsPerTick, 4));
            jw.WriteNumber("transport_server_flush_ms_per_tick", Math.Round(hotPath.ServerFlushMsPerTick, 4));
            jw.WriteNumber("transport_tcp_batch_write_ms_per_tick", Math.Round(hotPath.TcpBatchWriteMsPerTick, 4));
            jw.WriteNumber("transport_tcp_single_write_ms_per_tick", Math.Round(hotPath.TcpSingleWriteMsPerTick, 4));
            jw.WriteNumber("pipeline_recipient_selection_ms_per_call", Math.Round(hotPath.RecipientSelectionMsPerCall, 6));
            jw.WriteNumber("pipeline_selected_dispatch_ms_per_call", Math.Round(hotPath.SelectedDispatchMsPerCall, 6));
            jw.WriteNumber("pipeline_try_send_ms_per_call", Math.Round(hotPath.TrySendMsPerCall, 6));
            jw.WriteEndObject();
        }

        private static void ProcessConnectionEvents(
            ServerRuntime runtime,
            TcpFramedNetworkTransport transport,
            TcpFramedNetworkTransport.ConnectionEvent[] scratch,
            long nowTicks,
            ref int nextAutoBotIndex,
            WorldSpawnLayout? worldLayout)
        {
            while (true)
            {
                int count = transport.DrainConnectionEvents(scratch);
                if (count <= 0)
                    return;

                for (int i = 0; i < count; i++)
                {
                    TcpFramedNetworkTransport.ConnectionEvent evt = scratch[i];
                    switch (evt.Kind)
                    {
                        case TcpFramedNetworkTransport.ConnectionEventKind.Connected:
                        {
                            long sessionId = runtime.Sessions.CreateSession(evt.ConnectionId, nowTicks);
                            runtime.Sessions.TryMarkHandshaking(sessionId, nowTicks);
                            runtime.Sessions.TryMarkAuthenticated(sessionId, nowTicks);
                            ushort prefabId = (nextAutoBotIndex & 1) == 0 ? PlayerArchetypeIds.DefaultFemale : PlayerArchetypeIds.Default;
                            runtime.SetPendingSessionSpawnPrefabId(sessionId, prefabId);
                            runtime.SetPendingSessionSpawnDisplayName(sessionId, $"Bot{nextAutoBotIndex}");
                            if (worldLayout is not null)
                                runtime.SetPendingSessionSpawnSnapshot(sessionId, worldLayout.GetSpawnSnapshot(nextAutoBotIndex));
                            runtime.Sessions.TryMarkReady(sessionId, nowTicks);
                            nextAutoBotIndex++;
                            break;
                        }
                        case TcpFramedNetworkTransport.ConnectionEventKind.Disconnected:
                            runtime.Sessions.TryDisconnectConnection(evt.ConnectionId, SessionDisconnectReason.Unknown, nowTicks, removeDisconnected: true);
                            break;
                        case TcpFramedNetworkTransport.ConnectionEventKind.ConnectFailed:
                        case TcpFramedNetworkTransport.ConnectionEventKind.ListenFailed:
                        case TcpFramedNetworkTransport.ConnectionEventKind.ListenerRuntimeError:
                            break;
                    }
                }
            }
        }

        // World population ─────────────────────────────────────────────────

        private static void CreatePlayers(ServerRuntime runtime, int count, WorldSpawnLayout? worldLayout = null)
        {
            byte[] payload   = new byte[32];
            byte[] challenge = new byte[16];
            const int parkedSessionBase = 1_000_000;

            for (int i = 0; i < count; i++)
            {
                string username      = $"user{i + 1}";
                string characterName = $"Char{i + 1}";
                ushort prefabId      = (i & 1) == 0 ? PlayerArchetypeIds.Default : PlayerArchetypeIds.DefaultFemale;
                string remoteIp      = $"10.0.{(i / 250) % 250}.{(i % 250) + 1}";

                if (!runtime.Accounts.TryLogin(username, payload, payload, challenge, remoteIp,
                        out AccountModel? account, out _, out string loginError))
                    throw new InvalidOperationException($"TryLogin failed for {username}: {loginError}");

                if (!runtime.Accounts.TryCreateCharacter(account!, characterName, prefabId,
                        out _, out CharacterModel? character, out string createError))
                    throw new InvalidOperationException($"TryCreateCharacter failed for {username}: {createError}");

                PlayerSpawnSnapshot spawnSnap = worldLayout is not null
                    ? worldLayout.GetSpawnSnapshot(i)
                    : new PlayerSpawnSnapshot(i * 1.25f, (i % 7) * 0.5f, -i * 0.75f, (i % 360) * 0.017453292f);

                long parkedSessionId = parkedSessionBase + i;
                Entity entity = runtime.PlayerWorld.BindSession(
                    sessionId:         parkedSessionId,
                    connectionId:      parkedSessionBase + i,
                    requestedPrefabId: character!.ArchetypeId,
                    playerDisplayName: character.Name,
                    characterIdentity: new PlayerCharacterComponent
                    {
                        CharacterId = character.CharacterId,
                        SlotIndex   = character.SlotIndex,
                    },
                    spawnSnapshot: spawnSnap);

                if (!runtime.PlayerWorld.World.TryGetComponent(entity, out PlayerState state))
                    throw new InvalidOperationException("Expected PlayerState on created player.");
                state.X          = spawnSnap.X;
                state.Y          = spawnSnap.Y;
                state.Z          = spawnSnap.Z;
                state.YawRadians = spawnSnap.YawRadians;
                if (!runtime.PlayerWorld.World.TrySetComponent(entity, state))
                    throw new InvalidOperationException("Failed to write PlayerState.");

                if (!runtime.PlayerWorld.UnbindSession(parkedSessionId))
                    throw new InvalidOperationException("Failed to park persistent player entity.");
            }
        }

        private static void CreateItems(ServerRuntime runtime, int count)
        {
            ComponentId persistentTagId = ComponentManager.GetId<PersistentTag>();
            ComponentId prefabCid       = ComponentManager.GetId<PrefabIdComponent>();
            ComponentId itemStateId     = ComponentManager.GetId<PerfItemStateComponent>();
            ComponentId durabilityId    = ComponentManager.GetId<PerfItemDurabilityComponent>();
            var sig = new ArchetypeSignature(new[] { persistentTagId, prefabCid, itemStateId, durabilityId });

            for (int i = 0; i < count; i++)
                runtime.PlayerWorld.World.EnqueueCreateDefault(sig);

            runtime.PlayerWorld.World.ProcessStructuralQueues(StructuralBarrierPhase.Manual);

            int idx = 0;
            runtime.PlayerWorld.World.Query<PerfItemStateComponent, PerfItemDurabilityComponent, PrefabIdComponent>(
                (Entity _, ref PerfItemStateComponent s, ref PerfItemDurabilityComponent d, ref PrefabIdComponent p) =>
                {
                    p.PrefabId     = (ushort)(5000 + (idx % 64));
                    s.TemplateId   = 100000 + idx;
                    s.StackCount   = 1 + (idx % 20);
                    s.ValueCents   = 25 + (idx % 5000);
                    d.Durability   = 0.25f + (idx % 100) / 100f;
                    d.Flags        = (ushort)(idx & 0xF);
                    idx++;
                });

            if (idx < count)
                throw new InvalidOperationException(
                    $"Expected at least {count} item entities, found {idx}.");
        }

        private static void EnsureComponentRegistered<T>() where T : unmanaged, IComponent<T>
        {
            if (ComponentManager.TryGetId(typeof(T), out _))
                return;

            if (ComponentManager.IsSealed)
                throw new InvalidOperationException(
                    $"Component {typeof(T).Name} is not registered and ComponentManager is already sealed.");

            ComponentManager.Register<T>();
        }
    }
}

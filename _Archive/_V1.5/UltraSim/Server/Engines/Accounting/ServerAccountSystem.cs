using System;
using System.IO;
using UltraSim.ECS;

namespace UltraSim.Server.Engines.Accounting
{
    /// <summary>
    /// ECS system wrapper for the account and character subsystem.
    ///
    /// Responsibilities:
    ///   - Drives periodic dirty-flush to the account store (every FlushPeriodTicks ticks).
    ///   - Hooks into the world save/load lifecycle via Serialize/Deserialize.
    ///   - Creates and owns the ServerAccountRegistry and IAccountStore instances.
    ///
    /// Component access: none. Empty read/write masks — never conflicts with other systems.
    /// Stage: Cleanup. Runs after all game logic to catch mutations made during the tick.
    /// </summary>
    public sealed class ServerAccountSystem : SystemBase
    {
        /// <summary>Flush cadence: every 30 ticks (~480 ms at 60 Hz).</summary>
        private const int FlushPeriodTicks = 30;

        private static readonly SystemDescriptor _descriptor = new SystemDescriptor(
            name: "ServerAccountSystem",
            stage: SystemStage.Cleanup,
            defaultPeriodTicks: FlushPeriodTicks,
            defaultStartOffset: 0,
            readMask: default,
            writeMask: default);

        public override SystemDescriptor Descriptor => _descriptor;

        // -------------------------------------------------------------------------
        // Public surface
        // -------------------------------------------------------------------------

        public ServerAccountRegistry Registry { get; }

        private readonly ServerAccountOptions _options;
        private IAccountStore _store;

        public ServerAccountSystem(ServerAccountOptions? options = null, string? serverDataRoot = null)
        {
            _options = options ?? ServerAccountOptions.Default;
            string dataPath = Path.Combine(
                serverDataRoot ?? AppContext.BaseDirectory,
                _options.AccountDataPath);

            _store = AccountStoreRegistry.Create(_options.StorageProfile, dataPath, _options.BackupCount);
            Registry = new ServerAccountRegistry();
        }

        // -------------------------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------------------------

        public override void OnInitialize(World world)
        {
            Registry.LoadFrom(_store);
        }

        public override void Update(in SystemContext ctx)
        {
            Registry.FlushDirtyAsync(_store);
        }

        public override void OnShutdown(World world)
        {
            Registry.SaveAllAsync(_store);
            Registry.WaitForPendingSave();
        }

        // -------------------------------------------------------------------------
        // World save / load manifest
        // -------------------------------------------------------------------------

        /// <summary>
        /// Writes a lightweight manifest entry for cross-check during world load.
        /// Does NOT write account data (that lives in accounts.dat / accounts.json).
        /// Also triggers an unconditional background save so accounts are in sync.
        /// </summary>
        public override void Serialize(IDataWriter writer)
        {
            // Trigger a save so the account files are up-to-date with this world snapshot.
            Registry.SaveAllAsync(_store);

            // Write manifest: counts for integrity cross-check.
            writer.WriteInt32(Registry.AccountCount);
            writer.WriteInt32(Registry.CharacterCount);
            writer.WriteInt32(Registry.IpBanCount);
        }

        /// <summary>
        /// Reads the manifest entry. Validates counts against what was loaded from disk.
        /// Logs a warning if the counts don't match (non-fatal; accounts.dat is authoritative).
        /// </summary>
        public override void Deserialize(IDataReader reader)
        {
            int manifestAccountCount = reader.ReadInt32();
            int manifestCharacterCount = reader.ReadInt32();
            int manifestIpBanCount = reader.ReadInt32();

            int diskAccountCount = Registry.AccountCount;
            int diskCharacterCount = Registry.CharacterCount;
            int diskIpBanCount = Registry.IpBanCount;

            if (manifestAccountCount != diskAccountCount
                || manifestCharacterCount != diskCharacterCount
                || manifestIpBanCount != diskIpBanCount)
            {
                // Non-fatal: log the mismatch but trust the account store.
                // This can happen if accounts.dat was restored from a newer backup
                // than the world save, or vice versa.
                Console.Error.WriteLine(
                    $"[ServerAccountSystem] Manifest mismatch — " +
                    $"manifest: {manifestAccountCount} accounts / {manifestCharacterCount} chars / {manifestIpBanCount} ip bans, " +
                    $"disk: {diskAccountCount} accounts / {diskCharacterCount} chars / {diskIpBanCount} ip bans. " +
                    $"accounts.dat is authoritative.");
            }
        }

        // -------------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------------

        /// <summary>Exposes the server-owner options for downstream use (e.g. wire protocol).</summary>
        public ServerAccountOptions Options => _options;
    }
}

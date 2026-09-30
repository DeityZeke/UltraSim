using UltraSim.ECS;
using UltraSim.Framework.ControlPlane;
using UltraSim.Framework.Persistence;
using UltraSim.Game.Admission;
using UltraSim.Game.Composition;
using UltraSim.Game.Persistence;
using UltraSim.Game.Replication;
using UltraSim.Network;
using UltraSim.Server.Runtime;

namespace UltraSim.Server.Operator;

public enum OperatorExitDisposition : byte { None, Shutdown }

/// <summary>Cold server-owned policy, deliberately not saved as Game world state.</summary>
public readonly record struct ServerOperatorPolicy(bool AutosaveEnabled, int AutosaveSeconds,
    int BackupRetention, bool SaveOnShutdown)
{
    public static ServerOperatorPolicy Default => new(true, 300, 8, true);
    public void Validate()
    {
        if (AutosaveSeconds is < 10 or > 86_400 || BackupRetention is < 1 or > 64)
            throw new ArgumentOutOfRangeException(nameof(ServerOperatorPolicy));
    }
}

public readonly record struct ServerOperatorSnapshot(
    ulong AuthorityGeneration, ulong SimulationTick, int Participants, int Sessions,
    bool SaveInProgress, bool BackupInProgress, ulong LastSavedSequence,
    DateTimeOffset? LastSavedAt, long LastSaveMilliseconds, ulong LastBackupSequence,
    ulong AutosaveSkipped, string LastFailure, OperatorExitDisposition ExitDisposition);

/// <summary>
/// Direct Connect owner-thread operator semantics. Control Plane is the admission/audit surface;
/// the frontends never touch persistence, authority or Network directly.
/// </summary>
public sealed class ServerOperatorService : IDisposable
{
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly SharedPlayableAuthority _authority;
    private readonly HostedUdpAdmissionHost _host;
    private readonly SaveSetCoordinator _coordinator;
    private readonly GameRuntimePersistence _persistence;
    private readonly OperatorBackupStore _backups;
    private readonly ServerOperatorPolicy _policy;
    private readonly TimeProvider _time;
    private long _nextAutosaveTimestamp;
    private Task<GameSaveResult>? _save;
    private Task<OperatorBackupResult>? _backup;
    private long _saveStartedTimestamp;
    private bool _shutdownAfterSave;
    private bool _disposed;
    private ulong _lastSavedSequence;
    private DateTimeOffset? _lastSavedAt;
    private long _lastSaveMilliseconds;
    private ulong _lastBackupSequence;
    private ulong _autosaveSkipped;
    private string _lastFailure = "";
    private OperatorExitDisposition _exit;
    private ulong _revision = 1;

    public ServerOperatorService(SharedPlayableAuthority authority, HostedUdpAdmissionHost host,
        ServerGameFacet game, UltraSimGameComposition composition, string saveRoot,
        string backupRoot, ServerOperatorPolicy policy, TimeProvider? time = null)
    {
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        policy.Validate();
        _policy = policy;
        _time = time ?? TimeProvider.System;
        _coordinator = new SaveSetCoordinator(saveRoot);
        _persistence = new GameRuntimePersistence(game, composition,
            new PersistenceRestoreOptions(PersistenceLimits.ServerDefault, ChunkLayoutOptions.KiB16),
            _coordinator);
        _backups = new OperatorBackupStore(saveRoot, backupRoot, policy.BackupRetention);
        _nextAutosaveTimestamp = checked(_time.GetTimestamp() + CadenceTicks());
    }

    public ulong Revision { get { EnsureOwner(); return _revision; } }
    public ServerOperatorPolicy Policy => _policy;
    public OperatorBackupStore Backups => _backups;
    internal SharedPlayableAuthoritySnapshot CaptureAuthority()
    {
        EnsureOwner();
        return _authority.GetSnapshot();
    }

    public ServerOperatorSnapshot Capture()
    {
        EnsureOwner();
        SharedPlayableAuthoritySnapshot authority = _authority.GetSnapshot();
        return new(authority.GenerationId, authority.SimulationTick,
            authority.ParticipantCount, _host.SessionCount,
            _save is not null, _backup is not null, _lastSavedSequence,
            _lastSavedAt, _lastSaveMilliseconds, _lastBackupSequence,
            _autosaveSkipped, _lastFailure, _exit);
    }

    /// <summary>Service at the host owner boundary after Network and authority progress.</summary>
    public void Pump()
    {
        EnsureOwner();
        if (_save is { IsCompleted: true } saved)
        {
            _save = null;
            GameSaveResult result = saved.GetAwaiter().GetResult();
            _lastSaveMilliseconds = checked((long)(_time.GetElapsedTime(_saveStartedTimestamp).TotalMilliseconds));
            if (result.Succeeded && result.Manifest is not null)
            {
                _lastSavedSequence = result.Manifest.Sequence;
                _lastSavedAt = _time.GetUtcNow();
                _lastFailure = "";
                if (_shutdownAfterSave) PublishExit(OperatorExitDisposition.Shutdown);
            }
            else
            {
                _lastFailure = Bounded(result.Failure?.Message ?? "Save did not publish.");
                _shutdownAfterSave = false;
            }
            _revision++;
        }
        if (_backup is { IsCompleted: true } backup)
        {
            _backup = null;
            OperatorBackupResult result = backup.GetAwaiter().GetResult();
            if (result.Succeeded) { _lastBackupSequence = result.Sequence; _lastFailure = ""; }
            else _lastFailure = Bounded(result.Diagnostic);
            _revision++;
        }
        if (!_policy.AutosaveEnabled || _exit != OperatorExitDisposition.None ||
            _shutdownAfterSave) return;
        long now = _time.GetTimestamp();
        if (now < _nextAutosaveTimestamp) return;
        _nextAutosaveTimestamp = checked(now + CadenceTicks());
        if (_save is not null || _backup is not null)
        {
            if (_autosaveSkipped != ulong.MaxValue) _autosaveSkipped++;
            _revision++;
            return;
        }
        _ = TryStartSave();
    }

    public ControlOwnerResult TryStartSave()
    {
        EnsureOwner();
        if (_exit != OperatorExitDisposition.None || _save is not null || _backup is not null)
            return ControlOwnerResult.Reject(ControlResultCode.Disabled, "A save, backup or exit owns the operation slot.");
        if (!_coordinator.TryGetNextSequence(out ulong sequence))
            return ControlOwnerResult.Reject(ControlResultCode.OwnerRejected, "Save sequence exhausted.");
        _saveStartedTimestamp = _time.GetTimestamp();
        _save = _persistence.TrySaveAsync(
            new SaveSetContext(sequence, _time.GetUtcNow().ToUnixTimeMilliseconds()),
            static capture => capture());
        _revision++;
        return ControlOwnerResult.Accepted;
    }

    public ControlOwnerResult TryStartBackup()
    {
        EnsureOwner();
        if (_exit != OperatorExitDisposition.None || _save is not null || _backup is not null)
            return ControlOwnerResult.Reject(ControlResultCode.Disabled, "A save, backup or exit owns the operation slot.");
        _backup = Task.Run(_backups.TryBackupCurrent);
        _revision++;
        return ControlOwnerResult.Accepted;
    }

    public ControlOwnerResult RequestShutdown()
    {
        EnsureOwner();
        if (_exit != OperatorExitDisposition.None || _shutdownAfterSave)
            return ControlOwnerResult.Reject(ControlResultCode.Disabled, "Exit is already requested.");
        if (_policy.SaveOnShutdown)
        {
            if (_save is not null) _shutdownAfterSave = true;
            else
            {
                ControlOwnerResult saved = TryStartSave();
                if (saved.Status != ControlResultStatus.Accepted) return saved;
                _shutdownAfterSave = true;
            }
        }
        else PublishExit(OperatorExitDisposition.Shutdown);
        _revision++;
        return ControlOwnerResult.Accepted;
    }

    public ControlOwnerResult RequestRestartWithSave()
    {
        EnsureOwner();
        return ControlOwnerResult.Reject(ControlResultCode.Disabled,
            "Restart requires a verified supervisor and durable authority/session rehydration; use Save Now and a managed deployment restart.");
    }

    public ControlOwnerResult TryKick(ulong participantId)
    {
        EnsureOwner();
        if (participantId == 0 || _exit != OperatorExitDisposition.None)
            return ControlOwnerResult.Reject(ControlResultCode.OwnerRejected, "Participant is not currently entered.");
        SharedPlayableParticipant[] entered = new SharedPlayableParticipant[64];
        int count = _authority.CopyParticipants(entered);
        if (count < 0 || !OperatorParticipantSpan.ContainsAny(entered.AsSpan(0, count), participantId))
            return ControlOwnerResult.Reject(ControlResultCode.OwnerRejected, "Participant is not currently entered.");
        for (int ordinal = 0; _host.TryGetSession(ordinal, out HostedUdpProtocolSession? session); ordinal++)
            if (session!.OperatorParticipantId == participantId &&
                session.State == UdpProtocolSessionState.Established)
            {
                session.BeginDrain();
                _revision++;
                return ControlOwnerResult.Accepted;
            }
        return ControlOwnerResult.Reject(ControlResultCode.OwnerRejected, "Participant has no current remote session.");
    }

    public int CopyParticipants(Span<SharedPlayableParticipant> destination)
    {
        EnsureOwner();
        return _authority.CopyParticipants(destination);
    }

    public void Dispose()
    {
        if (_disposed) return;
        EnsureOwner();
        _exit = OperatorExitDisposition.Shutdown;
        try
        {
            if (_save is { } save)
            {
                save.GetAwaiter().GetResult();
                Pump();
            }
            if (_backup is { } backup)
            {
                backup.GetAwaiter().GetResult();
                Pump();
            }
        }
        finally { _disposed = true; }
    }

    private long CadenceTicks() => checked((long)(_policy.AutosaveSeconds * _time.TimestampFrequency));
    private void PublishExit(OperatorExitDisposition disposition)
    {
        _host.BeginShutdown();
        _exit = disposition;
    }
    private void EnsureOwner()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Environment.CurrentManagedThreadId != _owner)
            throw new InvalidOperationException("Server operator owner thread required.");
    }
    private static string Bounded(string value) => value.Length <= 256 ? value : value[..256];
}

internal static class OperatorParticipantSpan
{
    internal static bool ContainsAny(this ReadOnlySpan<SharedPlayableParticipant> participants, ulong id)
    {
        foreach (SharedPlayableParticipant participant in participants)
            if (participant.Participant.Value == id) return true;
        return false;
    }
}

using UltraSim.ECS;
using UltraSim.Framework.ControlPlane;
using UltraSim.Framework.Persistence;
using UltraSim.Game.Admission;
using UltraSim.Game.Composition;
using UltraSim.Game.Persistence;
using UltraSim.Game.Replication;
using UltraSim.Game.UI;
using UltraSim.Network;
using UltraSim.Server.Runtime;

namespace UltraSim.Server.Operator;

public enum OperatorExitDisposition : byte { None, Shutdown, RestartRequested }

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
    ulong AutosaveSkipped, string LastFailure, OperatorExitDisposition ExitDisposition)
{
    public ulong RestoredSaveSequence { get; init; }
    public int RecoveredGuestCount { get; init; }
}

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
    private OperatorExitDisposition _exitAfterSave;
    private bool _finalSaveStarted;
    private bool _disposed;
    private ulong _lastSavedSequence;
    private DateTimeOffset? _lastSavedAt;
    private long _lastSaveMilliseconds;
    private ulong _lastBackupSequence;
    private ulong _autosaveSkipped;
    private string _lastFailure = "";
    private OperatorExitDisposition _exit;
    private ulong _revision = 1;

    public OperatorLogRing Logs { get; } = new();

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
    public int PlaybackRate
    {
        get { EnsureOwner(); return _authority.CaptureRuntimeControlSnapshot().PlaybackRate; }
    }
    internal SharedPlayableAuthoritySnapshot CaptureAuthority()
    {
        EnsureOwner();
        return _authority.GetSnapshot();
    }

    public ServerOperatorSnapshot Capture()
    {
        EnsureOwner();
        SharedPlayableAuthoritySnapshot authority = _authority.GetSnapshot();
        return new ServerOperatorSnapshot(authority.GenerationId, authority.SimulationTick,
            authority.ParticipantCount, _host.SessionCount,
            _save is not null, _backup is not null, _lastSavedSequence,
            _lastSavedAt, _lastSaveMilliseconds, _lastBackupSequence,
            _autosaveSkipped, _lastFailure, _exit)
        {
            RestoredSaveSequence = _authority.RestoredSaveSequence,
            RecoveredGuestCount = _authority.RecoveredGuestCount,
        };
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
                Log(OperatorLogSeverity.Information, OperatorLogCategory.Persistence,
                    "Complete save generation published.");
                if (_exitAfterSave != OperatorExitDisposition.None && _finalSaveStarted)
                {
                    OperatorExitDisposition requested = _exitAfterSave;
                    _exitAfterSave = OperatorExitDisposition.None;
                    _finalSaveStarted = false;
                    PublishExit(requested);
                }
            }
            else
            {
                _lastFailure = Bounded(result.Failure?.Message ?? "Save did not publish.");
                Log(OperatorLogSeverity.Error, OperatorLogCategory.Persistence,
                    "Save capture or durable publication failed; prior CURRENT remains authoritative.");
                _exitAfterSave = OperatorExitDisposition.None;
                _finalSaveStarted = false;
            }
            _revision++;
        }
        if (_backup is { IsCompleted: true } backup)
        {
            _backup = null;
            OperatorBackupResult result = backup.GetAwaiter().GetResult();
            if (result.Succeeded)
            {
                _lastBackupSequence = result.Sequence; _lastFailure = "";
                Log(OperatorLogSeverity.Information, OperatorLogCategory.Backup,
                    "Complete backup snapshot published or verified reusable.");
            }
            else
            {
                _lastFailure = Bounded(result.Diagnostic);
                Log(OperatorLogSeverity.Error, OperatorLogCategory.Backup,
                    "Backup validation or publication failed; no incomplete snapshot was admitted.");
            }
            _revision++;
        }
        if (_exit == OperatorExitDisposition.None && _exitAfterSave != OperatorExitDisposition.None &&
            !_finalSaveStarted &&
            _save is null && _backup is null)
        {
            ControlOwnerResult started = TryStartFinalSave();
            if (started.Status != ControlResultStatus.Accepted)
            {
                _exitAfterSave = OperatorExitDisposition.None;
                _lastFailure = Bounded(started.Diagnostic);
                _revision++;
            }
        }
        if (!_policy.AutosaveEnabled || _exit != OperatorExitDisposition.None ||
            _exitAfterSave != OperatorExitDisposition.None) return;
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
        Log(OperatorLogSeverity.Information, OperatorLogCategory.Persistence,
            "Coherent save capture admitted for durable publication.");
        return ControlOwnerResult.Accepted;
    }

    public ControlOwnerResult TryStartBackup()
    {
        EnsureOwner();
        if (_exit != OperatorExitDisposition.None || _save is not null || _backup is not null)
            return ControlOwnerResult.Reject(ControlResultCode.Disabled, "A save, backup or exit owns the operation slot.");
        _backup = Task.Run(_backups.TryBackupCurrent);
        _revision++;
        Log(OperatorLogSeverity.Information, OperatorLogCategory.Backup,
            "Backup of current complete generation requested.");
        return ControlOwnerResult.Accepted;
    }

    public ControlOwnerResult RequestShutdown()
    {
        EnsureOwner();
        if (_exit != OperatorExitDisposition.None || _exitAfterSave != OperatorExitDisposition.None)
            return ControlOwnerResult.Reject(ControlResultCode.Disabled, "Exit is already requested.");
        if (_policy.SaveOnShutdown)
        {
            _exitAfterSave = OperatorExitDisposition.Shutdown;
            if (_save is null && _backup is null)
            {
                ControlOwnerResult start = TryStartFinalSave();
                if (start.Status != ControlResultStatus.Accepted)
                {
                    _exitAfterSave = OperatorExitDisposition.None;
                    return start;
                }
            }
        }
        else PublishExit(OperatorExitDisposition.Shutdown);
        _revision++;
        Log(OperatorLogSeverity.Information, OperatorLogCategory.Lifecycle,
            "Graceful shutdown requested.");
        return ControlOwnerResult.Accepted;
    }

    public ControlOwnerResult RequestRestartWithSave()
    {
        EnsureOwner();
        if (_exit != OperatorExitDisposition.None || _exitAfterSave != OperatorExitDisposition.None)
            return ControlOwnerResult.Reject(ControlResultCode.Disabled, "Exit is already requested.");
        _exitAfterSave = OperatorExitDisposition.RestartRequested;
        if (_save is null && _backup is null)
        {
            ControlOwnerResult start = TryStartFinalSave();
            if (start.Status != ControlResultStatus.Accepted)
            {
                _exitAfterSave = OperatorExitDisposition.None;
                return start;
            }
        }
        _revision++;
        Log(OperatorLogSeverity.Information, OperatorLogCategory.Lifecycle,
            "Supervised restart requested after a fresh durable save.");
        return ControlOwnerResult.Accepted;
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
                Log(OperatorLogSeverity.Information, OperatorLogCategory.Sessions,
                    "One exact entered participant session began draining by operator request.");
                return ControlOwnerResult.Accepted;
            }
        return ControlOwnerResult.Reject(ControlResultCode.OwnerRejected, "Participant has no current remote session.");
    }

    public int CopyParticipants(Span<SharedPlayableParticipant> destination)
    {
        EnsureOwner();
        return _authority.CopyParticipants(destination);
    }

    public ServerMonitorSnapshot CaptureParticipantMonitor()
    {
        EnsureOwner();
        return _authority.CaptureServerMonitor();
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
    private void Log(OperatorLogSeverity severity, OperatorLogCategory category, string message) =>
        Logs.Write(severity, category, message, _time.GetUtcNow());
    private ControlOwnerResult TryStartFinalSave()
    {
        ControlOwnerResult result = TryStartSave();
        if (result.Status == ControlResultStatus.Accepted)
        {
            if (_save is { IsCompleted: true } completed)
            {
                GameSaveResult immediate = completed.GetAwaiter().GetResult();
                if (!immediate.Succeeded)
                {
                    _lastFailure = Bounded(immediate.Failure?.Message ??
                        "Final authority capture did not complete.");
                    _save = null;
                    return ControlOwnerResult.Reject(ControlResultCode.OwnerRejected, _lastFailure);
                }
            }
            // TrySaveAsync captured coherently before returning. No Network pump runs between
            // that capture and closing admission, so no later accepted client action can claim
            // to be part of this final durable boundary.
            _finalSaveStarted = true;
            _host.BeginShutdown();
        }
        return result;
    }

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

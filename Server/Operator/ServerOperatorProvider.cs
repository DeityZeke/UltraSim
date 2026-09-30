using UltraSim.Framework.ControlPlane;
using UltraSim.Game.Replication;
using UltraSim.Game.UI;

namespace UltraSim.Server.Operator;

/// <summary>One explicitly published server-owner Control Plane surface, not a second action registry.</summary>
public sealed class ServerOperatorProvider : IControlProvider
{
    public static readonly ControlProviderId ProviderId = new("server.operator");
    public static readonly ControlCapabilityId Read = new("server.read");
    public static readonly ControlCapabilityId Save = new("server.save");
    public static readonly ControlCapabilityId Backup = new("server.backup");
    public static readonly ControlCapabilityId Lifecycle = new("server.lifecycle");
    public static readonly ControlCapabilityId Sessions = new("server.sessions");
    public static readonly ControlActionId SaveNow = new("save-now");
    public static readonly ControlActionId BackupNow = new("backup-now");
    public static readonly ControlActionId Shutdown = new("shutdown");
    public static readonly ControlActionId RestartWithSave = new("restart-with-save");
    public static readonly ControlActionId Kick = new("kick");
    private const ControlAudience OperatorAudience = ControlAudience.Operator | ControlAudience.Test;
    private readonly ServerOperatorService _service;
    private readonly OperatorAuditRing _audit;
    private ulong _revision = 1;
    private ulong _observedServiceRevision;
    private ulong _observedJoins;
    private ulong _observedLeaves;
    private ulong _observedAuditSequence;
    private ulong _observedLogSequence;

    public ServerOperatorProvider(ServerOperatorService service, OperatorAuditRing audit)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        Definition = new(ProviderId,
            controls: [
                new(new("autosave-enabled"), new("Server/Persistence"), "Autosave enabled",
                    "Cold server policy; changing it requires a server restart.",
                    new(ControlValueKind.Bool), ControlValue.Bool(service.Policy.AutosaveEnabled),
                    Read, Lifecycle, ControlApplyMode.ReadOnly,
                    ControlPersistenceClass.ServerColdConfiguration, OperatorAudience),
                new(new("autosave-seconds"), new("Server/Persistence"), "Autosave cadence (seconds)",
                    "Cold server policy; changing it requires a server restart.",
                    new(ControlValueKind.Int64, ControlValue.Int64(10), ControlValue.Int64(86_400)),
                    ControlValue.Int64(service.Policy.AutosaveSeconds), Read, Lifecycle,
                    ControlApplyMode.ReadOnly, ControlPersistenceClass.ServerColdConfiguration, OperatorAudience),
                new(new("playback-rate"), new("Server/Runtime"), "Playback rate",
                    "The fixed-rate Concept Demo composition does not admit variable playback or Pause.",
                    new(ControlValueKind.Int64, ControlValue.Int64(1), ControlValue.Int64(int.MaxValue)),
                    ControlValue.Int64(1), Read, Lifecycle,
                    ControlApplyMode.ReadOnly, ControlPersistenceClass.None, OperatorAudience)],
            diagnostics: new[] { "generation", "tick", "participant-count", "sessions", "save-in-progress",
                "backup-in-progress", "last-save", "last-backup", "autosave-skipped", "restored-save",
                "recovered-guests", "last-failure", "exit" }
                .Select(name => new DiagnosticDescriptor(new(name), new("Server/Status"), name,
                    "Owner-captured operator status.", new(name switch
                    {
                        "save-in-progress" or "backup-in-progress" => ControlValueKind.Bool,
                        "last-failure" or "exit" => ControlValueKind.Text,
                        _ => ControlValueKind.UInt64
                    }), Read, OperatorAudience)),
            actions: [
                new(SaveNow, new("Server/Persistence"), "Save now", "Capture and publish one complete save generation.",
                    [], Read, Save, OperatorAudience),
                new(BackupNow, new("Server/Persistence"), "Backup now", "Copy the current complete save generation to the separate backup root.",
                    [], Read, Backup, OperatorAudience),
                new(Shutdown, new("Server/Lifecycle"), "Shutdown", "Request graceful host retirement.",
                    [], Read, Lifecycle, OperatorAudience, "Stop this server?"),
                new(RestartWithSave, new("Server/Lifecycle"), "Restart with save",
                    "Save, retire this process, then request a supervised cold reopen of the same deployment.",
                    [], Read, Lifecycle, OperatorAudience, "Save and restart this server?"),
                new(Kick, new("Server/Sessions"), "Kick participant",
                    "Drain one exact currently entered remote participant session.",
                    [new(ControlValueKind.UInt64, ControlValue.UInt64(1), ControlValue.UInt64(ulong.MaxValue))],
                    Read, Sessions, OperatorAudience, "Disconnect this participant?")],
            collections: [
                new(new("participants"), new("Server/Sessions"), "Entered participants",
                    "Current guest identities only; not durable accounts or characters.", 64,
                    [new(new("participant"), "Participant", new(ControlValueKind.UInt64)),
                     new(new("display"), "Display", new(ControlValueKind.Text, maximumText: 96)),
                     new(new("connection"), "Connection", new(ControlValueKind.Text, maximumText: 16)),
                     new(new("entered"), "Entered", new(ControlValueKind.Bool)),
                     new(new("control-session"), "Control session", new(ControlValueKind.UInt64)),
                     new(new("public-actor"), "Public actor", new(ControlValueKind.Text, maximumText: 32)),
                     new(new("rtt-ms"), "RTT ms", new(ControlValueKind.Text, maximumText: 16)),
                     new(new("chunk"), "Public chunk", new(ControlValueKind.Text, maximumText: 32))],
                    Read, OperatorAudience),
                new(new("audit"), new("Server/Audit"), "Recent operator actions",
                    "Bounded audit; text arguments are redacted.", OperatorAuditRing.Capacity,
                    [new(new("sequence"), "Sequence", new(ControlValueKind.UInt64)),
                     new(new("time"), "UTC time", new(ControlValueKind.Text, maximumText: 48)),
                     new(new("principal"), "Principal", new(ControlValueKind.Text, maximumText: 96)),
                     new(new("context"), "Trusted context", new(ControlValueKind.Text, maximumText: 128)),
                     new(new("action"), "Action", new(ControlValueKind.Text, maximumText: 256)),
                     new(new("arguments"), "Arguments", new(ControlValueKind.Text, maximumText: 256)),
                     new(new("outcome"), "Outcome", new(ControlValueKind.Text, maximumText: 64))],
                    Read, OperatorAudience),
                new(new("log"), new("Server/Log"), "Recent server events",
                    "Bounded owner-published operational events; not authoritative world history.",
                    OperatorLogRing.Capacity,
                    [new(new("sequence"), "Sequence", new(ControlValueKind.UInt64)),
                     new(new("time"), "UTC time", new(ControlValueKind.Text, maximumText: 48)),
                     new(new("severity"), "Severity", new(ControlValueKind.Text, maximumText: 16)),
                     new(new("category"), "Category", new(ControlValueKind.Text, maximumText: 24)),
                     new(new("message"), "Message", new(ControlValueKind.Text, maximumText: 256))],
                    Read, OperatorAudience)]);
    }

    public ControlProviderDefinition Definition { get; }

    public ulong Revision
    {
        get
        {
            SharedPlayableAuthoritySnapshot authority = _serviceAuthority();
            if (_observedServiceRevision != _service.Revision ||
                _observedAuditSequence != _audit.Sequence ||
                _observedLogSequence != _service.Logs.Sequence ||
                _observedJoins != authority.Joins || _observedLeaves != authority.Leaves)
            {
                _revision = checked(_revision + 1);
                _observedServiceRevision = _service.Revision;
                _observedAuditSequence = _audit.Sequence;
                _observedLogSequence = _service.Logs.Sequence;
                _observedJoins = authority.Joins;
                _observedLeaves = authority.Leaves;
            }
            return _revision;
        }
    }

    private SharedPlayableAuthoritySnapshot _serviceAuthority() => _service.CaptureAuthority();

    public ControlState Capture(ControlId id)
    {
        _ = Revision;
        ControlValue value = id.Value switch
        {
            "autosave-enabled" => ControlValue.Bool(_service.Policy.AutosaveEnabled),
            "autosave-seconds" => ControlValue.Int64(_service.Policy.AutosaveSeconds),
            "playback-rate" => ControlValue.Int64(_service.PlaybackRate),
            _ => throw new ArgumentException("Unknown server operator control.")
        };
        bool playback = id.Value == "playback-rate";
        var provenance = new ControlProvenance(playback ?
            ControlProvenanceKind.RuntimeConstraint : ControlProvenanceKind.ServerColdConfig);
        return new(value, value, null, provenance, provenance, null, false,
            playback ? ControlResultCode.Disabled : ControlResultCode.NotWritable,
            playback ? "Variable playback and Pause are disabled by this composition." :
                "Cold server configuration requires restart.");
    }

    public ControlValue Capture(DiagnosticId id)
    {
        ServerOperatorSnapshot s = _service.Capture();
        return id.Value switch
        {
            "generation" => ControlValue.UInt64(s.AuthorityGeneration),
            "tick" => ControlValue.UInt64(s.SimulationTick),
            "participant-count" => ControlValue.UInt64(checked((ulong)s.Participants)),
            "sessions" => ControlValue.UInt64(checked((ulong)s.Sessions)),
            "save-in-progress" => ControlValue.Bool(s.SaveInProgress),
            "backup-in-progress" => ControlValue.Bool(s.BackupInProgress),
            "last-save" => ControlValue.UInt64(s.LastSavedSequence),
            "last-backup" => ControlValue.UInt64(s.LastBackupSequence),
            "autosave-skipped" => ControlValue.UInt64(s.AutosaveSkipped),
            "restored-save" => ControlValue.UInt64(s.RestoredSaveSequence),
            "recovered-guests" => ControlValue.UInt64(checked((ulong)s.RecoveredGuestCount)),
            "last-failure" => ControlValue.Text(s.LastFailure),
            "exit" => ControlValue.Text(s.ExitDisposition.ToString()),
            _ => throw new ArgumentException("Unknown server operator diagnostic.")
        };
    }

    public IEnumerable<ControlCollectionRow> Capture(ControlCollectionId id)
    {
        if (id.Value == "participants")
        {
            var participants = new SharedPlayableParticipant[64];
            int count = _service.CopyParticipants(participants);
            if (count < 0) throw new InvalidOperationException("Operator participant census exceeded capacity.");
            ServerMonitorSnapshot monitor = _service.CaptureParticipantMonitor();
            SharedPlayableParticipant[] currentParticipants = participants.AsSpan(0, count).ToArray();
            foreach (ServerMonitorParticipant observed in monitor.Participants.ToArray())
            {
                SharedPlayableParticipant participant = Array.Find(currentParticipants,
                    current => current.Participant == observed.Participant);
                if (!participant.IsValid)
                    throw new InvalidOperationException("Operator and sanitized monitor participant identities diverged.");
                yield return new(new("p." + observed.Participant.Value),
                    [ControlValue.UInt64(observed.Participant.Value),
                     ControlValue.Text(observed.DisplayName),
                     ControlValue.Text(observed.Connection.ToString().ToLowerInvariant()),
                     ControlValue.Bool(true),
                     ControlValue.UInt64(participant.ControlSession.Value),
                     ControlValue.Text(observed.PublicActorIndex + ":" +
                        observed.PublicActorGeneration),
                     ControlValue.Text(observed.RoundTripMilliseconds < 0 ? "N/A" :
                         observed.RoundTripMilliseconds.ToString()),
                     ControlValue.Text(observed.ChunkX + "," + observed.ChunkZ)]);
            }
            yield break;
        }
        if (id.Value == "audit")
        {
            var rows = new OperatorAuditRow[OperatorAuditRing.Capacity];
            int count = _audit.CopyTo(rows);
            if (count < 0) throw new InvalidOperationException("Operator audit census exceeded capacity.");
            for (int index = 0; index < count; index++)
            {
                OperatorAuditRow row = rows[index];
                yield return new(new("a." + row.Sequence),
                    [ControlValue.UInt64(row.Sequence), ControlValue.Text(row.Time.ToString("O")),
                     ControlValue.Text(row.Principal), ControlValue.Text(row.Context),
                     ControlValue.Text(row.Provider + "/" + row.Action), ControlValue.Text(row.Arguments),
                     ControlValue.Text(row.Status + "/" + row.Code)]);
            }
            yield break;
        }
        if (id.Value == "log")
        {
            var rows = new OperatorLogEntry[OperatorLogRing.Capacity];
            int count = _service.Logs.CopyTo(rows);
            if (count < 0) throw new InvalidOperationException("Operator log census exceeded capacity.");
            for (int index = 0; index < count; index++)
            {
                OperatorLogEntry row = rows[index];
                yield return new(new("log." + row.Sequence),
                    [ControlValue.UInt64(row.Sequence), ControlValue.Text(row.Time.ToString("O")),
                     ControlValue.Text(row.Severity.ToString()), ControlValue.Text(row.Category.ToString()),
                     ControlValue.Text(row.Message)]);
            }
            yield break;
        }
        throw new ArgumentException("Unknown server operator collection.");
    }

    public ControlOwnerResult Set(ControlRequestContext context, IReadOnlyList<ControlMutation> mutations) =>
        ControlOwnerResult.Reject(ControlResultCode.NotWritable, "Server policy is cold configuration.");

    public ControlOwnerResult Invoke(ControlRequestContext context, ControlActionId id,
        IReadOnlyList<ControlValue> arguments) => id.Value switch
    {
        "save-now" => _service.TryStartSave(),
        "backup-now" => _service.TryStartBackup(),
        "shutdown" => _service.RequestShutdown(),
        "restart-with-save" => _service.RequestRestartWithSave(),
        "kick" => _service.TryKick(arguments[0].AsUInt64()),
        _ => ControlOwnerResult.Reject(ControlResultCode.UnknownPublication)
    };
}

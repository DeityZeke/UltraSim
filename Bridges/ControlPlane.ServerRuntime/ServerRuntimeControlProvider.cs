using UltraSim.Server.Runtime;
using UltraSim.Framework.ControlPlane;

namespace UltraSim.Bridges.ControlPlane.ServerRuntime;

/// <summary>Opt-in cold adapter. Invoke only at the runtime's idle owner boundary, not from an operator thread.</summary>
public sealed class ServerRuntimeControlProvider : IControlProvider
{
    public static readonly ControlCapabilityId Read = new("server.runtime.read"), Write = new("server.runtime.write");
    private readonly UltraSim.Server.Runtime.ServerRuntime _runtime;
    private ServerRuntimeControlSnapshot _previous;
    private ulong _revision;
    private readonly HashSet<ControlId> _overrides = new();
    public ServerRuntimeControlProvider(UltraSim.Server.Runtime.ServerRuntime runtime)
    {
        _runtime = runtime;
        var s = Snapshot();
        Definition = new(new("server.runtime"), [
            new(new("playback"), new("Server/Runtime"), "Playback rate", "Host reach time, not per-tick semantic time.",
                new(ControlValueKind.Int64, ControlValue.Int64(1), ControlValue.Int64(int.MaxValue)),
                ControlValue.Int64(s.PlaybackRate), Read, Write, s.AllowVariablePlayback ? ControlApplyMode.SafePoint : ControlApplyMode.ReadOnly),
            new(new("frequency"), new("Server/Runtime"), "Simulation frequency Hz", "Must exactly divide world-time quanta; fixed policies reject.",
                new(ControlValueKind.Int64, ControlValue.Int64(1), ControlValue.Int64(WorldClockOptions.MaximumSimulationFrequencyHz)),
                ControlValue.Int64(s.SimulationFrequencyHz), Read, Write, s.AllowVariableSimulationFrequency ? ControlApplyMode.SafePoint : ControlApplyMode.ReadOnly)],
            new[] { "state", "generation", "tick", "world-time" }.Select(n => new DiagnosticDescriptor(new(n), new("Server/Runtime"),
                n, "", new(n == "state" ? ControlValueKind.Text : n == "world-time" ? ControlValueKind.Int64 : ControlValueKind.UInt64), Read)));
    }
    public ControlProviderDefinition Definition { get; }
    private ServerRuntimeControlSnapshot Snapshot()
    {
        var s = _runtime.CaptureControlSnapshot();
        if (_revision == 0 || s != _previous) { _revision = checked(_revision + 1); _previous = s; }
        return s;
    }
    public ulong Revision { get { Snapshot(); return _revision; } }
    public ControlState Capture(ControlId id)
    {
        var s = Snapshot();
        bool playback = id.Value == "playback";
        if (!playback && id.Value != "frequency") throw new ArgumentException("Unknown control.");
        bool policy = playback ? s.AllowVariablePlayback : s.AllowVariableSimulationFrequency;
        bool writable = policy && s.Idle && s.State == ServerRuntimeState.Running;
        var value = ControlValue.Int64(playback ? s.PlaybackRate : s.SimulationFrequencyHz);
        var provenance = new ControlProvenance(_overrides.Contains(id) ? ControlProvenanceKind.AdminRuntimeOverride : ControlProvenanceKind.ServerColdConfig);
        return new(value, value, null, provenance, provenance, null, writable,
            writable ? ControlResultCode.Accepted : ControlResultCode.NotWritable,
            writable ? "" : !policy ? "Fixed runtime policy." : "Runtime requires a running idle owner boundary.");
    }
    public ControlValue Capture(DiagnosticId id)
    {
        var s = Snapshot();
        return id.Value switch
        {
            "state" => ControlValue.Text(s.State.ToString()), "generation" => ControlValue.UInt64(s.ActiveGeneration),
            "tick" => ControlValue.UInt64(s.SimulationTick), "world-time" => ControlValue.Int64(s.WorldTimeMicroseconds),
            _ => throw new ArgumentException("Unknown diagnostic.")
        };
    }
    public IEnumerable<ControlCollectionRow> Capture(ControlCollectionId id) => throw new ArgumentException("No runtime collection.");
    public ControlOwnerResult Set(ControlRequestContext context, IReadOnlyList<ControlMutation> mutations)
    {
        var s = Snapshot();
        if (s.State != ServerRuntimeState.Running || !s.Idle)
            return ControlOwnerResult.Reject(ControlResultCode.Disabled, "Runtime is not at an idle running boundary.");
        foreach (var m in mutations)
        {
            if (m.Id.Value == "playback" && !s.AllowVariablePlayback || m.Id.Value == "frequency" && !s.AllowVariableSimulationFrequency)
                return ControlOwnerResult.Reject(ControlResultCode.NotWritable, "Fixed runtime policy.");
            if (m.Id.Value == "frequency" && !WorldClockOptions.IsSupportedSimulationFrequency(checked((int)m.Value.AsInt64())))
                return ControlOwnerResult.Reject(ControlResultCode.OwnerRejected, "Unsupported world-time divisor.");
        }
        foreach (var m in mutations)
        {
            if (m.Id.Value == "playback") _runtime.SetPlaybackRate(checked((int)m.Value.AsInt64()));
            else _runtime.SetSimulationFrequency(checked((int)m.Value.AsInt64()));
            _overrides.Add(m.Id);
        }
        _revision = checked(_revision + 1); Snapshot(); return ControlOwnerResult.Accepted;
    }
    public ControlOwnerResult Invoke(ControlRequestContext context, ControlActionId id, IReadOnlyList<ControlValue> arguments) =>
        ControlOwnerResult.Reject(ControlResultCode.UnknownPublication);
}

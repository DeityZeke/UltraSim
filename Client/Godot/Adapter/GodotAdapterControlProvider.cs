using UltraSim.Framework.ControlPlane;

namespace UltraSim.Client.Godot;

/// <summary>Cold, engine-thread snapshot of the actual native adapter owner.</summary>
internal readonly record struct GodotAdapterStatus(
    string Edition, GodotApplicationPhase Phase, bool SessionActive,
    int WorldRootChildren, int UiLayerCount,
    int OpenGumps = 0, int InternalRenderWidth = 0, int InternalRenderHeight = 0,
    GodotClientAudioSnapshot Audio = default);

/// <summary>
/// Reports the fixed Godot implementation selection and its owned lifecycle. Selection is not
/// a live setting: ordinary client preferences remain in the Settings owner.
/// </summary>
internal sealed class GodotAdapterControlProvider : IControlProvider
{
    internal static readonly ControlProviderId Id = new("client.godot.adapters");
    internal static readonly ControlCapabilityId Read = new("client.adapter.read");
    internal static readonly ControlCapabilityId Select = new("client.adapter.select");
    private static readonly ControlOptionId Godot = new("godot");
    private static readonly ControlOptionId None = new("none");
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly Func<GodotAdapterStatus> _capture;
    private GodotAdapterStatus _previous;
    private bool _hasPrevious;
    private ulong _revision;

    internal GodotAdapterControlProvider(Func<GodotAdapterStatus> capture)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        var selection = new ControlValueSchema(ControlValueKind.Option, options: [Godot, None]);
        Definition = new(Id,
            controls:
            [
                Selection("presentation-implementation", "Presentation implementation", selection),
                Selection("input-implementation", "Input implementation", selection),
                Selection("runtime-implementation", "Runtime implementation", selection),
                Selection("audio-implementation", "Audio implementation", selection),
            ],
            diagnostics:
            [
                Diagnostic("edition", ControlValueKind.Text),
                Diagnostic("phase", ControlValueKind.Text),
                Diagnostic("session-active", ControlValueKind.Bool),
                Diagnostic("world-root-children", ControlValueKind.Int64),
                Diagnostic("ui-layer-count", ControlValueKind.Int64),
                Diagnostic("open-gumps", ControlValueKind.Int64),
                Diagnostic("internal-render-width", ControlValueKind.Int64),
                Diagnostic("internal-render-height", ControlValueKind.Int64),
                Diagnostic("audio-source-budget", ControlValueKind.Int64),
                Diagnostic("audio-active-sources", ControlValueKind.Int64),
                Diagnostic("audio-peak-sources", ControlValueKind.Int64),
                Diagnostic("audio-active-loops", ControlValueKind.Int64),
                Diagnostic("audio-dropped-requests", ControlValueKind.Int64),
                Diagnostic("audio-native-failures", ControlValueKind.Int64),
                Diagnostic("audio-loaded-cues", ControlValueKind.Int64),
                Diagnostic("audio-master-volume", ControlValueKind.Float64),
                Diagnostic("audio-sfx-volume", ControlValueKind.Float64),
                Diagnostic("audio-ambient-volume", ControlValueKind.Float64),
                Diagnostic("audio-ui-volume", ControlValueKind.Float64),
                Diagnostic("audio-muted", ControlValueKind.Bool),
            ]);
    }

    public ControlProviderDefinition Definition { get; }
    public ulong Revision { get { Refresh(); return _revision; } }

    private static ControlDescriptor Selection(string id, string label, ControlValueSchema schema) =>
        new(new(id), new("Client/Adapter"), label,
            "Physical adapter selection belongs to the compiled composition, not a live preference.",
            schema, ControlValue.Option(id == "audio-implementation" ? None : Godot),
            Read, Select, ControlApplyMode.ReadOnly, ControlPersistenceClass.None,
            ControlAudience.Operator | ControlAudience.Developer | ControlAudience.Test);

    private static DiagnosticDescriptor Diagnostic(string id, ControlValueKind kind) =>
        new(new(id), new("Client/Adapter"), id, "Current owned adapter state.",
            new(kind, maximumText: 128), Read,
            ControlAudience.Operator | ControlAudience.Developer | ControlAudience.Test);

    private GodotAdapterStatus Refresh()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("Godot adapter diagnostics require the engine owner thread.");
        GodotAdapterStatus state = _capture();
        if (string.IsNullOrWhiteSpace(state.Edition) || state.Edition.Length > 128 ||
            !Enum.IsDefined(state.Phase) || state.WorldRootChildren < 0 || state.UiLayerCount < 0 ||
            state.OpenGumps < 0 || state.InternalRenderWidth < 0 || state.InternalRenderHeight < 0 ||
            state.Audio.ActiveSources < 0 || state.Audio.ActiveSources > state.Audio.SourceBudget ||
            state.Audio.ActiveLoops < 0 || state.Audio.ActiveLoops > state.Audio.ActiveSources)
            throw new InvalidOperationException("Invalid Godot adapter status.");
        if (!_hasPrevious || state != _previous)
        {
            _revision = checked(_revision + 1);
            _previous = state;
            _hasPrevious = true;
        }
        return state;
    }

    public ControlState Capture(ControlId id)
    {
        GodotAdapterStatus state = Refresh();
        ControlOptionId selected = id.Value switch
        {
            "presentation-implementation" or "input-implementation" or "runtime-implementation" => Godot,
            "audio-implementation" => state.Audio.SourceBudget > 0 ? Godot : None,
            _ => throw new ArgumentException("Unknown adapter selection.", nameof(id)),
        };
        ControlValue value = ControlValue.Option(selected);
        var provenance = new ControlProvenance(ControlProvenanceKind.BuiltInDefault, "compiled client composition");
        return new(value, value, null, provenance, provenance, null, writable: false);
    }

    public ControlValue Capture(DiagnosticId id)
    {
        GodotAdapterStatus state = Refresh();
        return id.Value switch
        {
            "edition" => ControlValue.Text(state.Edition),
            "phase" => ControlValue.Text(state.Phase.ToString()),
            "session-active" => ControlValue.Bool(state.SessionActive),
            "world-root-children" => ControlValue.Int64(state.WorldRootChildren),
            "ui-layer-count" => ControlValue.Int64(state.UiLayerCount),
            "open-gumps" => ControlValue.Int64(state.OpenGumps),
            "internal-render-width" => ControlValue.Int64(state.InternalRenderWidth),
            "internal-render-height" => ControlValue.Int64(state.InternalRenderHeight),
            "audio-source-budget" => ControlValue.Int64(state.Audio.SourceBudget),
            "audio-active-sources" => ControlValue.Int64(state.Audio.ActiveSources),
            "audio-peak-sources" => ControlValue.Int64(state.Audio.PeakSources),
            "audio-active-loops" => ControlValue.Int64(state.Audio.ActiveLoops),
            "audio-dropped-requests" => ControlValue.Int64((long)Math.Min(state.Audio.DroppedRequests, long.MaxValue)),
            "audio-native-failures" => ControlValue.Int64((long)Math.Min(state.Audio.NativeFailures, long.MaxValue)),
            "audio-loaded-cues" => ControlValue.Int64(state.Audio.LoadedCues),
            "audio-master-volume" => ControlValue.Float64(state.Audio.MasterVolume),
            "audio-sfx-volume" => ControlValue.Float64(state.Audio.SfxVolume),
            "audio-ambient-volume" => ControlValue.Float64(state.Audio.AmbientVolume),
            "audio-ui-volume" => ControlValue.Float64(state.Audio.UiVolume),
            "audio-muted" => ControlValue.Bool(state.Audio.Muted),
            _ => throw new ArgumentException("Unknown adapter diagnostic.", nameof(id)),
        };
    }

    public IEnumerable<ControlCollectionRow> Capture(ControlCollectionId id) =>
        throw new ArgumentException("No adapter collection.", nameof(id));
    public ControlOwnerResult Set(ControlRequestContext context, IReadOnlyList<ControlMutation> mutations) =>
        ControlOwnerResult.Reject(ControlResultCode.NotWritable, "Physical adapter selection is not live.");
    public ControlOwnerResult Invoke(ControlRequestContext context, ControlActionId id, IReadOnlyList<ControlValue> arguments) =>
        ControlOwnerResult.Reject(ControlResultCode.UnknownPublication, "No adapter action.");
}

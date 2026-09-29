using UltraSim.Framework.ControlPlane;

namespace UltraSim.Client.Godot;

/// <summary>Cold, engine-thread snapshot of the actual native adapter owner.</summary>
internal readonly record struct GodotAdapterStatus(
    string Edition, GodotApplicationPhase Phase, bool SessionActive,
    int WorldRootChildren, int UiLayerCount);

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
            !Enum.IsDefined(state.Phase) || state.WorldRootChildren < 0 || state.UiLayerCount < 0)
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
        _ = Refresh();
        ControlOptionId selected = id.Value switch
        {
            "presentation-implementation" or "input-implementation" or "runtime-implementation" => Godot,
            "audio-implementation" => None,
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

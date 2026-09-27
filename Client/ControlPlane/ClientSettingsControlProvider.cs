using UltraSim.Client.Core;
using UltraSim.Framework.ControlPlane;

namespace UltraSim.Client.ControlPlane;

/// <summary>
/// Opt-in client-owner adapter, not a settings store. Composition supplies the existing document,
/// actual consumer-effective settings and a transactional owner commit (validate/save/publish).
/// No recommendation is granted a principal or write capability. Retire when its scope/session changes.
/// </summary>
public sealed class ClientSettingsControlProvider : IControlProvider
{
    public static readonly ControlCapabilityId Read = new("client.settings.read"), Write = new("client.settings.write");
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly Func<ClientSettingsDocument> _document;
    private readonly Func<ResolvedClientSettings> _effective;
    private readonly Action<ClientSettingsDocument> _commit;
    private readonly ClientSettingsScopeKey _scope;
    private readonly ClientProfileRecommendation? _recommendation;
    private readonly ClientProfileApplicationMode _consent;
    private ClientSettingsDocument? _previous;
    private ControlValue[]? _previousEffective;
    private ulong _revision;
    private static readonly string[] Fields = ["ui-scale", "volume", "pointer-sensitivity", "invert-y", "grammar", "render-distance", "vegetation"];
    public ClientSettingsControlProvider(Func<ClientSettingsDocument> document, Func<ResolvedClientSettings> effective,
        Action<ClientSettingsDocument> commit, ClientSettingsScopeKey editScope = default,
        ClientProfileRecommendation? recommendation = null, ClientProfileApplicationMode consent = ClientProfileApplicationMode.Ignore)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _effective = effective ?? throw new ArgumentNullException(nameof(effective));
        _commit = commit ?? throw new ArgumentNullException(nameof(commit));
        _scope = editScope; _recommendation = recommendation; _consent = consent;
        var defaults = ClientSettingsResolver.Resolve(ClientSettingsDocument.Empty);
        ControlValueSchema[] schemas = [
            new(ControlValueKind.Float64, ControlValue.Float64(.5), ControlValue.Float64(3)),
            new(ControlValueKind.Float64, ControlValue.Float64(0), ControlValue.Float64(1)),
            new(ControlValueKind.Float64, ControlValue.Float64(.1), ControlValue.Float64(5)),
            new(ControlValueKind.Bool), new(ControlValueKind.Option, options: [new("orbit-command"), new("third-person"), new("independent")]),
            new(ControlValueKind.Int64, ControlValue.Int64(ClientSettingsResolver.MinimumRenderDistanceChunks),
                ControlValue.Int64(ClientSettingsResolver.MaximumRenderDistanceChunks)),
            new(ControlValueKind.Bool)];
        Definition = new(new("client.settings"), Fields.Select((f, i) =>
            new ControlDescriptor(new(f), new(f is "pointer-sensitivity" or "invert-y" or "grammar" ? "Client/Input" : "Client/Presentation"),
                f, f == "volume" ? "Persisted preference; no Audio consumer is implemented." :
                    f == "vegetation" ? "Device-owned, default Off, applies on next world entry." : "Existing user-owned settings resolver.",
                schemas[i], Value(defaults, f), Read, Write, f == "vegetation" ? ControlApplyMode.SessionRestart : ControlApplyMode.Live,
                ControlPersistenceClass.UserSettings)));
        Refresh();
    }
    public ControlProviderDefinition Definition { get; }
    private (ClientSettingsDocument Doc, ResolvedClientSettings Effective) Refresh()
    {
        if (Environment.CurrentManagedThreadId != _owner) throw new InvalidOperationException("Client settings owner thread required.");
        var doc = _document(); var effective = _effective();
        var values = Fields.Select(f => Value(effective, f)).ToArray();
        if (!ReferenceEquals(doc, _previous) || _previousEffective is null || !values.SequenceEqual(_previousEffective))
        { _revision = checked(_revision + 1); _previous = doc; _previousEffective = values; }
        return (doc, effective);
    }
    public ulong Revision { get { Refresh(); return _revision; } }
    private static ControlValue Value(ResolvedClientSettings s, string field) => field switch
    {
        "ui-scale" => ControlValue.Float64(s.UiScale), "volume" => ControlValue.Float64(s.MasterVolume),
        "pointer-sensitivity" => ControlValue.Float64(s.PointerSensitivity), "invert-y" => ControlValue.Bool(s.InvertY),
        "grammar" => ControlValue.Option(new(s.ControlGrammar switch
        { ClientControlGrammar.OrbitCommand => "orbit-command", ClientControlGrammar.ThirdPersonDirect => "third-person", _ => "independent" })),
        "render-distance" => ControlValue.Int64(s.RenderDistanceChunks), "vegetation" => ControlValue.Bool(s.ExperimentalVegetation),
        _ => throw new ArgumentException("Unknown setting.")
    };
    private static bool Defined(ClientSettingsLayer layer, string field) => field switch
    {
        "ui-scale" => layer.UiScale.HasValue, "volume" => layer.MasterVolume.HasValue,
        "pointer-sensitivity" => layer.PointerSensitivity.HasValue, "invert-y" => layer.InvertY.HasValue,
        "grammar" => layer.ControlGrammar.HasValue, "render-distance" => layer.RenderDistanceChunks.HasValue, _ => false
    };
    public ControlState Capture(ControlId id)
    {
        var (doc, effective) = Refresh();
        string f = id.Value!;
        var requested = Value(ClientSettingsResolver.Resolve(doc, _scope), f);
        var actual = Value(effective, f);
        ControlProvenance provenance;
        bool realm = _scope.IsValid && doc.TryGetScope(_scope, out var layer) && Defined(layer, f);
        if (f == "vegetation") provenance = new(ControlProvenanceKind.UserGlobal, "device-owned");
        else if (realm) provenance = new(ControlProvenanceKind.UserRealm, _scope.Value!);
        else provenance = new(Defined(doc.Global, f) ? ControlProvenanceKind.UserGlobal : ControlProvenanceKind.BuiltInDefault);
        var actualProvenance = provenance;
        bool recommendation = f == "grammar" && !realm && _recommendation is not null &&
            _consent != ClientProfileApplicationMode.Ignore;
        if (recommendation)
            actualProvenance = new(ControlProvenanceKind.ServerRecommendation, "explicit client consent");
        var resolvedWithConsent = Value(ClientSettingsResolver.Resolve(doc, _scope, _recommendation, _consent), f);
        if (actual != resolvedWithConsent) actualProvenance = new(ControlProvenanceKind.RuntimeConstraint, "actual consumer state");
        bool pending = f == "vegetation" && requested != actual;
        return new(requested, actual, pending ? requested : null, provenance, actualProvenance,
            pending ? provenance : null, true);
    }
    public ControlOwnerResult Set(ControlRequestContext context, IReadOnlyList<ControlMutation> mutations)
    {
        var (doc, _) = Refresh();
        ClientSettingsLayer old = _scope.IsValid && doc.TryGetScope(_scope, out var scoped) ? scoped : _scope.IsValid ? ClientSettingsLayer.Empty : doc.Global;
        var values = mutations.ToDictionary(m => m.Id.Value!, m => m.Value, StringComparer.Ordinal);
        float? Float(string id, float? prior) => values.TryGetValue(id, out var v) ? (float)v.AsFloat64() : prior;
        bool? invert = values.TryGetValue("invert-y", out var y) ? y.AsBool() : old.InvertY;
        ClientControlGrammar? grammar = old.ControlGrammar;
        if (values.TryGetValue("grammar", out var g)) grammar = g.AsOption().Value switch
        { "orbit-command" => ClientControlGrammar.OrbitCommand, "third-person" => ClientControlGrammar.ThirdPersonDirect,
            "independent" => ClientControlGrammar.IndependentMoveLook, _ => throw new ArgumentException("Invalid option.") };
        int? distance = values.TryGetValue("render-distance", out var d) ? checked((int)d.AsInt64()) : old.RenderDistanceChunks;
        var layer = new ClientSettingsLayer(Float("ui-scale", old.UiScale), Float("volume", old.MasterVolume),
            Float("pointer-sensitivity", old.PointerSensitivity), invert, grammar, old.CameraProfile,
            old.CameraDistance, old.CameraFovDegrees, old.Bindings, distance, old.CameraRig);
        var candidate = _scope.IsValid ? doc.WithScope(_scope, layer) : doc.WithGlobal(layer);
        if (values.TryGetValue("vegetation", out var vegetation)) candidate = candidate.WithExperimentalVegetation(vegetation.AsBool());
        // Exercise the existing exact resolver before the owner commits/persists anything.
        _ = ClientSettingsResolver.Resolve(candidate, _scope, _recommendation, _consent);
        _commit(candidate);
        Refresh(); return ControlOwnerResult.Accepted;
    }
    public ControlValue Capture(DiagnosticId id) => throw new ArgumentException("No Settings diagnostic.");
    public IEnumerable<ControlCollectionRow> Capture(ControlCollectionId id) => throw new ArgumentException("No Settings collection.");
    public ControlOwnerResult Invoke(ControlRequestContext context, ControlActionId id, IReadOnlyList<ControlValue> arguments) =>
        ControlOwnerResult.Reject(ControlResultCode.UnknownPublication);
}

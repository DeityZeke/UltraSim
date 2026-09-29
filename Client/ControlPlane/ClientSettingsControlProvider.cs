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
    private readonly Func<ResolvedClientDisplayConfiguration>? _display;
    private readonly Action<ClientSettingsDocument> _commit;
    private readonly ClientSettingsScopeKey _scope;
    private readonly ClientProfileRecommendation? _recommendation;
    private readonly ClientProfileApplicationMode _consent;
    private ClientSettingsDocument? _previous;
    private ControlValue[]? _previousEffective;
    private ulong _revision;
    private static readonly ClientSemanticAction[] RebindableActions = Enum.GetValues<ClientSemanticAction>()
        .Where(action => !ClientReservedControls.IsReserved(action)).ToArray();
    private static readonly ControlOptionId[] RebindableOptions = RebindableActions
        .Select(action => new ControlOptionId(ActionId(action))).ToArray();
    private static readonly string[] Fields = ["ui-scale", "volume", "pointer-sensitivity", "invert-y", "grammar",
        "camera-profile", "camera-distance", "camera-fov", "render-distance", "vegetation", "display-mode"];
    public ClientSettingsControlProvider(Func<ClientSettingsDocument> document, Func<ResolvedClientSettings> effective,
        Action<ClientSettingsDocument> commit, ClientSettingsScopeKey editScope = default,
        ClientProfileRecommendation? recommendation = null, ClientProfileApplicationMode consent = ClientProfileApplicationMode.Ignore,
        Func<ResolvedClientDisplayConfiguration>? effectiveDisplay = null)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _effective = effective ?? throw new ArgumentNullException(nameof(effective));
        _display = effectiveDisplay;
        _commit = commit ?? throw new ArgumentNullException(nameof(commit));
        _scope = editScope; _recommendation = recommendation; _consent = consent;
        var defaults = ClientSettingsResolver.Resolve(ClientSettingsDocument.Empty);
        ControlValueSchema[] schemas = [
            new(ControlValueKind.Float64, ControlValue.Float64(.5), ControlValue.Float64(3)),
            new(ControlValueKind.Float64, ControlValue.Float64(0), ControlValue.Float64(1)),
            new(ControlValueKind.Float64, ControlValue.Float64(.1), ControlValue.Float64(5)),
            new(ControlValueKind.Bool), new(ControlValueKind.Option, options: [new("orbit-command"), new("third-person"), new("independent")]),
            new(ControlValueKind.Option, options: [new("orbit"), new("third-person-follow"), new("independent-move-look")]),
            new(ControlValueKind.Float64, ControlValue.Float64(2), ControlValue.Float64(80)),
            new(ControlValueKind.Float64, ControlValue.Float64(40), ControlValue.Float64(120)),
            new(ControlValueKind.Int64, ControlValue.Int64(ClientSettingsResolver.MinimumRenderDistanceChunks),
                ControlValue.Int64(ClientSettingsResolver.MaximumRenderDistanceChunks)),
            new(ControlValueKind.Bool),
            new(ControlValueKind.Option, options: [new("windowed"), new("borderless-fullscreen"), new("fullscreen")])];
        Definition = new(new("client.settings"), Fields.Select((f, i) =>
            new ControlDescriptor(new(f), new(f is "pointer-sensitivity" or "invert-y" or "grammar" ? "Client/Input" :
                    f == "display-mode" ? "Client/Display" : f.StartsWith("camera-", StringComparison.Ordinal) ? "Client/Camera" : "Client/Presentation"),
                f, f == "volume" ? "Persisted preference; no Audio consumer is implemented." :
                    f == "vegetation" ? "Device-owned, default Off, applies on next world entry." : "Existing user-owned settings resolver.",
                schemas[i], Value(defaults, ClientDisplaySettings.Default, f), Read, Write,
                f == "vegetation" ? ControlApplyMode.SessionRestart : ControlApplyMode.Live,
                ControlPersistenceClass.UserSettings,
                ControlAudience.User | ControlAudience.Developer | ControlAudience.Test)),
            diagnostics: [new(new("inventory-viewer-contract"), new("Client/Presentation"),
                "Inventory viewer", "P6 will add D2/List viewer preferences; no P5 setting is invented.",
                new(ControlValueKind.Text, maximumText: 96), Read,
                ControlAudience.User | ControlAudience.Developer | ControlAudience.Test)],
            actions: [new(new("rebind"), new("Client/Input"), "Rebind input", "Change one non-reserved user binding. Enter a canonical physical code; collisions reject.",
                [new(ControlValueKind.Option, options: RebindableOptions),
                 new(ControlValueKind.Option, options: [new("keyboard"), new("mouse-button"), new("gamepad-button")]),
                 new(ControlValueKind.Text, maximumText: ClientPhysicalInput.MaximumCodeCharacters),
                 new(ControlValueKind.Option, options: [new("none"), new("shift"), new("control"), new("alt"), new("meta")])],
                Read, Write, ControlAudience.User | ControlAudience.Test)],
            collections: [new(new("bindings"), new("Client/Input"), "Input bindings", "Effective owner-resolved physical bindings.",
                32, [new(new("action"), "Action", new(ControlValueKind.Text, maximumText: 64)),
                     new(new("kind"), "Kind", new(ControlValueKind.Text, maximumText: 32)),
                     new(new("code"), "Code", new(ControlValueKind.Text, maximumText: 32)),
                     new(new("modifiers"), "Modifiers", new(ControlValueKind.Text, maximumText: 32))],
                Read, ControlAudience.User | ControlAudience.Developer | ControlAudience.Test)]);
        Refresh();
    }
    public ControlProviderDefinition Definition { get; }
    private (ClientSettingsDocument Doc, ResolvedClientSettings Effective) Refresh()
    {
        if (Environment.CurrentManagedThreadId != _owner) throw new InvalidOperationException("Client settings owner thread required.");
        var doc = _document(); var effective = _effective();
        var values = Fields.Select(f => Value(effective, _display?.Invoke().DisplayMode ?? doc.Display.DisplayMode, f)).ToArray();
        if (!ReferenceEquals(doc, _previous) || _previousEffective is null || !values.SequenceEqual(_previousEffective))
        { _revision = checked(_revision + 1); _previous = doc; _previousEffective = values; }
        return (doc, effective);
    }
    public ulong Revision { get { Refresh(); return _revision; } }
    private static ControlValue Value(ResolvedClientSettings s, ClientDisplaySettings display, string field) =>
        Value(s, display.DisplayMode, field);
    private static ControlValue Value(ResolvedClientSettings s, ClientDisplayMode displayMode, string field) => field switch
    {
        "ui-scale" => ControlValue.Float64(s.UiScale), "volume" => ControlValue.Float64(s.MasterVolume),
        "pointer-sensitivity" => ControlValue.Float64(s.PointerSensitivity), "invert-y" => ControlValue.Bool(s.InvertY),
        "grammar" => ControlValue.Option(new(s.ControlGrammar switch
        { ClientControlGrammar.OrbitCommand => "orbit-command", ClientControlGrammar.ThirdPersonDirect => "third-person", _ => "independent" })),
        "camera-profile" => ControlValue.Option(new(s.CameraProfile switch
        { ClientCameraProfile.Orbit => "orbit", ClientCameraProfile.ThirdPersonFollow => "third-person-follow", _ => "independent-move-look" })),
        "camera-distance" => ControlValue.Float64(s.CameraDistance), "camera-fov" => ControlValue.Float64(s.CameraFovDegrees),
        "render-distance" => ControlValue.Int64(s.RenderDistanceChunks), "vegetation" => ControlValue.Bool(s.ExperimentalVegetation),
        "display-mode" => ControlValue.Option(new(displayMode switch
        { ClientDisplayMode.Windowed => "windowed", ClientDisplayMode.BorderlessFullscreen => "borderless-fullscreen", _ => "fullscreen" })),
        _ => throw new ArgumentException("Unknown setting.")
    };
    private static bool Defined(ClientSettingsLayer layer, string field) => field switch
    {
        "ui-scale" => layer.UiScale.HasValue, "volume" => layer.MasterVolume.HasValue,
        "pointer-sensitivity" => layer.PointerSensitivity.HasValue, "invert-y" => layer.InvertY.HasValue,
        "grammar" => layer.ControlGrammar.HasValue, "camera-profile" => layer.CameraProfile.HasValue,
        "camera-distance" => layer.CameraDistance.HasValue, "camera-fov" => layer.CameraFovDegrees.HasValue,
        "render-distance" => layer.RenderDistanceChunks.HasValue, _ => false
    };
    public ControlState Capture(ControlId id)
    {
        var (doc, effective) = Refresh();
        string f = id.Value!;
        var requested = Value(ClientSettingsResolver.Resolve(doc, _scope), doc.Display, f);
        var actual = Value(effective, _display?.Invoke().DisplayMode ?? doc.Display.DisplayMode, f);
        ControlProvenance provenance;
        bool realm = _scope.IsValid && doc.TryGetScope(_scope, out var layer) && Defined(layer, f);
        if (f is "vegetation" or "display-mode") provenance = new(ControlProvenanceKind.UserGlobal, "device-owned");
        else if (realm) provenance = new(ControlProvenanceKind.UserRealm, _scope.Value!);
        else provenance = new(Defined(doc.Global, f) ? ControlProvenanceKind.UserGlobal : ControlProvenanceKind.BuiltInDefault);
        var actualProvenance = provenance;
        bool recommendation = f == "grammar" && !realm && _recommendation is not null &&
            _consent != ClientProfileApplicationMode.Ignore;
        if (recommendation)
            actualProvenance = new(ControlProvenanceKind.ServerRecommendation, "explicit client consent");
        var resolvedWithConsent = Value(ClientSettingsResolver.Resolve(doc, _scope, _recommendation, _consent), doc.Display, f);
        if (actual != resolvedWithConsent) actualProvenance = new(ControlProvenanceKind.RuntimeConstraint, "actual consumer state");
        bool pending = f == "vegetation" && requested != actual;
        bool scopeFull = f is not ("vegetation" or "display-mode") && _scope.IsValid && doc.Scopes.Count == ClientSettingsDocument.MaximumScopes &&
            !doc.TryGetScope(_scope, out _);
        return new(requested, actual, pending ? requested : null, provenance, actualProvenance,
            pending ? provenance : null, !scopeFull,
            scopeFull ? ControlResultCode.OwnerRejected : ControlResultCode.Accepted,
            scopeFull ? "Realm settings scope capacity exhausted." : "");
    }
    public ControlOwnerResult Set(ControlRequestContext context, IReadOnlyList<ControlMutation> mutations)
    {
        var (doc, _) = Refresh();
        var values = mutations.ToDictionary(m => m.Id.Value!, m => m.Value, StringComparer.Ordinal);
        bool changesLayer = values.Keys.Any(static key => key is not ("vegetation" or "display-mode"));
        if (changesLayer && _scope.IsValid && doc.Scopes.Count == ClientSettingsDocument.MaximumScopes &&
            !doc.TryGetScope(_scope, out _))
            return ControlOwnerResult.Reject(ControlResultCode.OwnerRejected, "Realm settings scope capacity exhausted.");
        ClientSettingsLayer old = _scope.IsValid && doc.TryGetScope(_scope, out var scoped) ? scoped : _scope.IsValid ? ClientSettingsLayer.Empty : doc.Global;
        float? Float(string id, float? prior) => values.TryGetValue(id, out var v) ? (float)v.AsFloat64() : prior;
        bool? invert = values.TryGetValue("invert-y", out var y) ? y.AsBool() : old.InvertY;
        ClientControlGrammar? grammar = old.ControlGrammar;
        if (values.TryGetValue("grammar", out var g)) grammar = g.AsOption().Value switch
        { "orbit-command" => ClientControlGrammar.OrbitCommand, "third-person" => ClientControlGrammar.ThirdPersonDirect,
            "independent" => ClientControlGrammar.IndependentMoveLook, _ => throw new ArgumentException("Invalid option.") };
        ClientCameraProfile? camera = old.CameraProfile;
        if (values.TryGetValue("camera-profile", out var cp)) camera = cp.AsOption().Value switch
        { "orbit" => ClientCameraProfile.Orbit, "third-person-follow" => ClientCameraProfile.ThirdPersonFollow,
            "independent-move-look" => ClientCameraProfile.IndependentMoveLook, _ => throw new ArgumentException("Invalid camera option.") };
        float? cameraDistance = Float("camera-distance", old.CameraDistance);
        float? cameraFov = Float("camera-fov", old.CameraFovDegrees);
        bool cameraEdited = values.ContainsKey("camera-profile") || values.ContainsKey("camera-distance") || values.ContainsKey("camera-fov");
        ResolvedClientSettings current = ClientSettingsResolver.Resolve(doc, _scope, _recommendation, _consent);
        ClientCameraRig? cameraRig = cameraEdited ? ClientStockCameraRigs.FromLegacy(camera ?? current.CameraProfile)
            .WithLegacyLens(cameraDistance ?? current.CameraDistance, cameraFov ?? current.CameraFovDegrees) : old.CameraRig;
        int? distance = values.TryGetValue("render-distance", out var d) ? checked((int)d.AsInt64()) : old.RenderDistanceChunks;
        var layer = new ClientSettingsLayer(Float("ui-scale", old.UiScale), Float("volume", old.MasterVolume),
            Float("pointer-sensitivity", old.PointerSensitivity), invert, grammar, camera,
            cameraDistance, cameraFov, old.Bindings, distance, cameraRig);
        var candidate = changesLayer ? _scope.IsValid ? doc.WithScope(_scope, layer) : doc.WithGlobal(layer) : doc;
        if (values.TryGetValue("vegetation", out var vegetation)) candidate = candidate.WithExperimentalVegetation(vegetation.AsBool());
        if (values.TryGetValue("display-mode", out var display)) candidate = candidate.WithDisplay(doc.Display with
        {
            DisplayMode = display.AsOption().Value switch
            { "windowed" => ClientDisplayMode.Windowed, "borderless-fullscreen" => ClientDisplayMode.BorderlessFullscreen,
                "fullscreen" => ClientDisplayMode.Fullscreen, _ => throw new ArgumentException("Invalid display mode.") }
        });
        // Exercise the existing exact resolver before the owner commits/persists anything.
        _ = ClientSettingsResolver.Resolve(candidate, _scope, _recommendation, _consent);
        _commit(candidate);
        Refresh(); return ControlOwnerResult.Accepted;
    }
    public ControlValue Capture(DiagnosticId id) => id.Value == "inventory-viewer-contract"
        ? ControlValue.Text("Current viewer; D2/List selection belongs to P6.")
        : throw new ArgumentException("Unknown Settings diagnostic.");
    public IEnumerable<ControlCollectionRow> Capture(ControlCollectionId id)
    {
        if (id.Value != "bindings") throw new ArgumentException("Unknown Settings collection.");
        var (_, effective) = Refresh();
        return effective.Bindings.Select(binding => new ControlCollectionRow(new(ActionId(binding.Action)),
            [ControlValue.Text(binding.Action.ToString()), ControlValue.Text(binding.Input.Kind.ToString()),
             ControlValue.Text(binding.Input.Code!), ControlValue.Text(binding.Input.Modifiers.ToString())])).ToArray();
    }
    public ControlOwnerResult Invoke(ControlRequestContext context, ControlActionId id, IReadOnlyList<ControlValue> arguments)
    {
        if (id.Value != "rebind" || arguments.Count != 4)
            return ControlOwnerResult.Reject(ControlResultCode.UnknownPublication);
        var (doc, effective) = Refresh();
        string requested = arguments[0].AsOption().Value!;
        int actionIndex = Array.FindIndex(RebindableOptions, value => value.Value == requested);
        if (actionIndex < 0) return ControlOwnerResult.Reject(ControlResultCode.InvalidValue);
        ClientSemanticAction action = RebindableActions[actionIndex];
        ClientPhysicalInputKind kind = arguments[1].AsOption().Value switch
        { "keyboard" => ClientPhysicalInputKind.Keyboard, "mouse-button" => ClientPhysicalInputKind.MouseButton,
          "gamepad-button" => ClientPhysicalInputKind.GamepadButton, _ => default };
        ClientInputModifiers modifiers = arguments[3].AsOption().Value switch
        { "none" => ClientInputModifiers.None, "shift" => ClientInputModifiers.Shift,
          "control" => ClientInputModifiers.Control, "alt" => ClientInputModifiers.Alt,
          "meta" => ClientInputModifiers.Meta, _ => (ClientInputModifiers)255 };
        if (!ClientPhysicalInput.TryCreate(kind, arguments[2].AsText(), modifiers, out ClientPhysicalInput input))
            return ControlOwnerResult.Reject(ControlResultCode.InvalidValue, "Invalid physical input.");
        if (effective.Bindings.Any(binding => binding.Action != action && binding.Input == input))
            return ControlOwnerResult.Reject(ControlResultCode.InvalidValue, "Physical input is already bound.");
        if (_scope.IsValid && doc.Scopes.Count == ClientSettingsDocument.MaximumScopes &&
            !doc.TryGetScope(_scope, out _))
            return ControlOwnerResult.Reject(ControlResultCode.OwnerRejected, "Realm settings scope capacity exhausted.");
        ClientSettingsLayer old = _scope.IsValid && doc.TryGetScope(_scope, out ClientSettingsLayer scoped)
            ? scoped : _scope.IsValid ? ClientSettingsLayer.Empty : doc.Global;
        ClientPhysicalBinding[] bindings = old.Bindings.Where(binding => binding.Action != action)
            .Append(new ClientPhysicalBinding(action, input)).ToArray();
        var layer = new ClientSettingsLayer(old.UiScale, old.MasterVolume, old.PointerSensitivity,
            old.InvertY, old.ControlGrammar, old.CameraProfile, old.CameraDistance, old.CameraFovDegrees,
            bindings, old.RenderDistanceChunks, old.CameraRig);
        ClientSettingsDocument candidate = _scope.IsValid ? doc.WithScope(_scope, layer) : doc.WithGlobal(layer);
        _ = ClientSettingsResolver.Resolve(candidate, _scope, _recommendation, _consent);
        _commit(candidate);
        Refresh();
        return ControlOwnerResult.Accepted;
    }

    private static string ActionId(ClientSemanticAction action)
    {
        string name = action.ToString();
        var chars = new List<char>(name.Length + 8);
        foreach (char character in name)
        {
            if (char.IsUpper(character) && chars.Count != 0) chars.Add('-');
            chars.Add(char.ToLowerInvariant(character));
        }
        return new string(chars.ToArray());
    }
}

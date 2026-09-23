using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UltraSim.Client.Core;

public enum ClientControlGrammar : byte
{
    OrbitCommand = 1,
    ThirdPersonDirect = 2,
    IndependentMoveLook = 3,
}

public enum ClientCameraProfile : byte
{
    Orbit = 1,
    ThirdPersonFollow = 2,
    IndependentMoveLook = 3,
}

/// <summary>Finite client action vocabulary earned by the current playable client.</summary>
public enum ClientSemanticAction : byte
{
    ExitApplication = 1,
    OpenClientMenu = 2,
    Disconnect = 3,
    OpenSettings = 4,
    MoveLeft = 5,
    MoveRight = 6,
    MoveForward = 7,
    MoveBackward = 8,
    Select = 9,
    Destination = 10,
    ContextMenu = 11,
    Primary = 12,
    Interact = 13,
    OpenInventory = 14,
    Chat = 15,
    Jump = 16,
    OpenServerMonitor = 17,
    CycleDebug = 18,
    CameraLook = 19,
    CameraZoomIn = 20,
    CameraZoomOut = 21,
}

public enum ClientPhysicalInputKind : byte
{
    Keyboard = 1,
    MouseButton = 2,
    GamepadButton = 3,
}

[Flags]
public enum ClientInputModifiers : byte
{
    None = 0,
    Shift = 1 << 0,
    Control = 1 << 1,
    Alt = 1 << 2,
    Meta = 1 << 3,
}

/// <summary>
/// Engine-neutral canonical name for one physical input. The Godot adapter owns
/// translation to platform key/button codes.
/// </summary>
public readonly record struct ClientPhysicalInput
{
    public const int MaximumCodeCharacters = 32;
    private const ClientInputModifiers KnownModifiers = ClientInputModifiers.Shift |
        ClientInputModifiers.Control | ClientInputModifiers.Alt | ClientInputModifiers.Meta;

    private ClientPhysicalInput(
        ClientPhysicalInputKind kind,
        string code,
        ClientInputModifiers modifiers)
    {
        Kind = kind;
        Code = code;
        Modifiers = modifiers;
    }

    public ClientPhysicalInputKind Kind { get; }
    public string? Code { get; }
    public ClientInputModifiers Modifiers { get; }
    public bool IsValid => Kind is >= ClientPhysicalInputKind.Keyboard and <= ClientPhysicalInputKind.GamepadButton &&
        IsCanonicalCode(Code) && (Modifiers & ~KnownModifiers) == 0 &&
        (Kind == ClientPhysicalInputKind.Keyboard || Modifiers == ClientInputModifiers.None);

    public static bool TryCreate(
        ClientPhysicalInputKind kind,
        string? code,
        ClientInputModifiers modifiers,
        out ClientPhysicalInput input)
    {
        string canonical = code?.Trim().ToUpperInvariant() ?? string.Empty;
        input = new ClientPhysicalInput(kind, canonical, modifiers);
        if (input.IsValid) return true;
        input = default;
        return false;
    }

    private static bool IsCanonicalCode(string? code)
    {
        if (string.IsNullOrEmpty(code) || code.Length > MaximumCodeCharacters) return false;
        foreach (char value in code)
            if (!(value is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-')) return false;
        return true;
    }
}

public readonly record struct ClientPhysicalBinding(
    ClientSemanticAction Action,
    ClientPhysicalInput Input)
{
    public bool IsValid => Action is >= ClientSemanticAction.ExitApplication and <= ClientSemanticAction.CameraZoomOut &&
        Input.IsValid;
}

public static class ClientReservedControls
{
    public static bool IsReserved(ClientSemanticAction action) => action is
        ClientSemanticAction.ExitApplication or ClientSemanticAction.OpenClientMenu or
        ClientSemanticAction.Disconnect or ClientSemanticAction.OpenSettings or
        ClientSemanticAction.CycleDebug;
}

/// <summary>One immutable user-owned settings layer. Null fields inherit the lower layer.</summary>
public sealed class ClientSettingsLayer
{
    private readonly ReadOnlyCollection<ClientPhysicalBinding> _bindings;

    public ClientSettingsLayer(
        float? uiScale = null,
        float? masterVolume = null,
        float? pointerSensitivity = null,
        bool? invertY = null,
        ClientControlGrammar? controlGrammar = null,
        ClientCameraProfile? cameraProfile = null,
        float? cameraDistance = null,
        float? cameraFovDegrees = null,
        IEnumerable<ClientPhysicalBinding>? bindings = null,
        int? renderDistanceChunks = null,
        ClientCameraRig? cameraRig = null)
    {
        UiScale = uiScale;
        MasterVolume = masterVolume;
        PointerSensitivity = pointerSensitivity;
        InvertY = invertY;
        ControlGrammar = controlGrammar;
        CameraProfile = cameraProfile;
        CameraDistance = cameraDistance;
        CameraFovDegrees = cameraFovDegrees;
        CameraRig = cameraRig;
        RenderDistanceChunks = renderDistanceChunks;
        ClientPhysicalBinding[] copy = bindings?.ToArray() ?? [];
        Validate(copy);
        _bindings = Array.AsReadOnly(copy);
    }

    public static ClientSettingsLayer Empty { get; } = new();
    public float? UiScale { get; }
    public float? MasterVolume { get; }
    public float? PointerSensitivity { get; }
    public bool? InvertY { get; }
    public ClientControlGrammar? ControlGrammar { get; }
    public ClientCameraProfile? CameraProfile { get; }
    public float? CameraDistance { get; }
    public float? CameraFovDegrees { get; }
    public ClientCameraRig? CameraRig { get; }
    public int? RenderDistanceChunks { get; }
    public ReadOnlyCollection<ClientPhysicalBinding> Bindings => _bindings;

    private void Validate(ReadOnlySpan<ClientPhysicalBinding> bindings)
    {
        if (UiScale is float uiScale && (!float.IsFinite(uiScale) || uiScale is < 0.5f or > 3f) ||
            MasterVolume is float volume && (!float.IsFinite(volume) || volume is < 0f or > 1f) ||
            PointerSensitivity is float sensitivity &&
                (!float.IsFinite(sensitivity) || sensitivity is < 0.1f or > 5f) ||
            ControlGrammar is ClientControlGrammar grammar &&
                grammar is < ClientControlGrammar.OrbitCommand or > ClientControlGrammar.IndependentMoveLook ||
            CameraProfile is ClientCameraProfile camera &&
                camera is < ClientCameraProfile.Orbit or > ClientCameraProfile.IndependentMoveLook ||
            CameraDistance is float distance && (!float.IsFinite(distance) || distance is < 2f or > 80f) ||
            CameraFovDegrees is float fov && (!float.IsFinite(fov) || fov is < 40f or > 120f) ||
            CameraRig is ClientCameraRig rig && !rig.IsValid ||
            RenderDistanceChunks is int renderDistance &&
                renderDistance is < ClientSettingsResolver.MinimumRenderDistanceChunks or
                    > ClientSettingsResolver.MaximumRenderDistanceChunks ||
            bindings.Length > ClientSettingsResolver.MaximumBindings)
            throw new ArgumentOutOfRangeException(nameof(ClientSettingsLayer));

        Span<bool> actions = stackalloc bool[(int)ClientSemanticAction.CameraZoomOut + 1];
        for (int index = 0; index < bindings.Length; index++)
        {
            ClientPhysicalBinding binding = bindings[index];
            if (!binding.IsValid || actions[(int)binding.Action])
                throw new ArgumentException("Settings bindings must be valid and action-unique.", nameof(bindings));
            actions[(int)binding.Action] = true;
        }
    }
}

/// <summary>
/// Declarative server recommendation. It cannot carry accessibility/audio
/// settings and is never accepted merely because it was received.
/// </summary>
public sealed class ClientProfileRecommendation
{
    private readonly ReadOnlyCollection<ClientPhysicalBinding> _bindings;

    public ClientProfileRecommendation(
        ClientControlGrammar controlGrammar,
        ClientCameraProfile cameraProfile,
        float cameraDistance,
        float cameraFovDegrees,
        IEnumerable<ClientPhysicalBinding>? bindings = null)
    {
        if (controlGrammar is < ClientControlGrammar.OrbitCommand or > ClientControlGrammar.IndependentMoveLook ||
            cameraProfile is < ClientCameraProfile.Orbit or > ClientCameraProfile.IndependentMoveLook ||
            !float.IsFinite(cameraDistance) || cameraDistance is < 2f or > 80f ||
            !float.IsFinite(cameraFovDegrees) || cameraFovDegrees is < 40f or > 120f)
            throw new ArgumentOutOfRangeException(nameof(controlGrammar));
        ControlGrammar = controlGrammar;
        CameraProfile = cameraProfile;
        CameraDistance = cameraDistance;
        CameraFovDegrees = cameraFovDegrees;
        CameraRig = ClientStockCameraRigs.FromLegacy(cameraProfile)
            .WithLegacyLens(cameraDistance, cameraFovDegrees);
        ClientPhysicalBinding[] copy = bindings?.ToArray() ?? [];
        _ = new ClientSettingsLayer(bindings: copy);
        _bindings = Array.AsReadOnly(copy);
    }

    public ClientControlGrammar ControlGrammar { get; }
    public ClientCameraProfile CameraProfile { get; }
    public float CameraDistance { get; }
    public float CameraFovDegrees { get; }
    public ClientCameraRig CameraRig { get; }
    public ReadOnlyCollection<ClientPhysicalBinding> Bindings => _bindings;

    public ClientProfileRecommendation(
        ClientControlGrammar controlGrammar,
        ClientCameraRig cameraRig,
        IEnumerable<ClientPhysicalBinding>? bindings = null)
    {
        if (controlGrammar is < ClientControlGrammar.OrbitCommand or > ClientControlGrammar.IndependentMoveLook ||
            !cameraRig.IsValid)
            throw new ArgumentOutOfRangeException(nameof(cameraRig));
        ControlGrammar = controlGrammar;
        CameraRig = cameraRig;
        CameraProfile = controlGrammar switch
        {
            ClientControlGrammar.OrbitCommand => ClientCameraProfile.Orbit,
            ClientControlGrammar.ThirdPersonDirect => ClientCameraProfile.ThirdPersonFollow,
            _ => ClientCameraProfile.IndependentMoveLook,
        }; // Legacy UI label only; runtime uses CameraRig.
        CameraDistance = cameraRig.DefaultDistanceMillimeters / 1_000f;
        CameraFovDegrees = cameraRig.CanonicalFovTenths / 10f;
        ClientPhysicalBinding[] copy = bindings?.ToArray() ?? [];
        _ = new ClientSettingsLayer(bindings: copy);
        _bindings = Array.AsReadOnly(copy);
    }
}

public enum ClientProfileApplicationMode : byte
{
    Ignore = 1,
    UseServerDefaults = 2,
    KeepMyControls = 3,
    Customize = 4,
}

/// <summary>
/// Opaque persistence scope supplied by composition. It is not a Network
/// endpoint and does not claim authenticated realm identity. Remote composition
/// maps a verified ApplicationRealmId into this store shape; an endpoint never
/// becomes a settings owner.
/// </summary>
public readonly record struct ClientSettingsScopeKey
{
    public const int MaximumCharacters = 96;

    private ClientSettingsScopeKey(string value) => Value = value;

    public string? Value { get; }
    public bool IsValid => IsCanonical(Value);

    public static bool TryCreate(string? value, out ClientSettingsScopeKey key)
    {
        string canonical = value?.Trim() ?? string.Empty;
        key = new ClientSettingsScopeKey(canonical);
        if (key.IsValid) return true;
        key = default;
        return false;
    }

    public override string ToString() => Value ?? string.Empty;

    private static bool IsCanonical(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaximumCharacters) return false;
        foreach (char c in value)
            if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')) return false;
        return true;
    }
}

public readonly record struct ClientScopedSettings(
    ClientSettingsScopeKey Scope,
    ClientSettingsLayer Layer);

/// <summary>
/// Immutable user settings document; server recommendations are deliberately absent.
/// F0.3c persists the consent decision identity separately while the accepted
/// recommendation remains transient application-session state.
/// </summary>
public sealed class ClientSettingsDocument
{
    public const int CurrentVersion = 4;
    public const int MaximumScopes = 128;
    private readonly ReadOnlyCollection<ClientScopedSettings> _scopes;
    private readonly ReadOnlyCollection<ClientNamedControlProfile> _namedProfiles;

    public ClientSettingsDocument(
        ClientSettingsLayer? global = null,
        IEnumerable<ClientScopedSettings>? scopes = null,
        IEnumerable<ClientNamedControlProfile>? namedProfiles = null)
    {
        Global = global ?? ClientSettingsLayer.Empty;
        ClientScopedSettings[] copy = scopes?.OrderBy(value => value.Scope.Value, StringComparer.Ordinal).ToArray() ?? [];
        if (copy.Length > MaximumScopes) throw new ArgumentOutOfRangeException(nameof(scopes));
        for (int index = 0; index < copy.Length; index++)
        {
            if (!copy[index].Scope.IsValid || copy[index].Layer is null ||
                index != 0 && copy[index - 1].Scope == copy[index].Scope)
                throw new ArgumentException("Settings scopes must be valid and unique.", nameof(scopes));
        }
        _scopes = Array.AsReadOnly(copy);
        ClientNamedControlProfile[] named = namedProfiles?
            .OrderBy(static value => value.Name, StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        if (named.Length > ClientNamedControlProfile.MaximumProfiles)
            throw new ArgumentOutOfRangeException(nameof(namedProfiles));
        for (int index = 0; index < named.Length; index++)
            if (named[index] is null || index != 0 && string.Equals(
                    named[index - 1].Name, named[index].Name, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Named profiles must be unique.", nameof(namedProfiles));
        _namedProfiles = Array.AsReadOnly(named);
    }

    public static ClientSettingsDocument Empty { get; } = new();
    public ClientSettingsLayer Global { get; }
    public ReadOnlyCollection<ClientScopedSettings> Scopes => _scopes;
    public ReadOnlyCollection<ClientNamedControlProfile> NamedProfiles => _namedProfiles;

    public bool TryGetNamedProfile(string name, out ClientNamedControlProfile? profile)
    {
        profile = _namedProfiles.FirstOrDefault(value =>
            string.Equals(value.Name, name, StringComparison.OrdinalIgnoreCase));
        return profile is not null;
    }

    public ClientSettingsDocument WithNamedProfile(ClientNamedControlProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ClientNamedControlProfile[] values = _namedProfiles
            .Where(value => !string.Equals(value.Name, profile.Name, StringComparison.OrdinalIgnoreCase))
            .Append(profile).ToArray();
        return new ClientSettingsDocument(Global, _scopes, values);
    }

    public bool TryGetScope(ClientSettingsScopeKey scope, out ClientSettingsLayer layer)
    {
        if (!scope.IsValid)
        {
            layer = ClientSettingsLayer.Empty;
            return false;
        }
        for (int index = 0; index < _scopes.Count; index++)
        {
            int comparison = string.CompareOrdinal(_scopes[index].Scope.Value, scope.Value);
            if (comparison == 0)
            {
                layer = _scopes[index].Layer;
                return true;
            }
            if (comparison > 0) break;
        }
        layer = ClientSettingsLayer.Empty;
        return false;
    }

    public ClientSettingsDocument WithGlobal(ClientSettingsLayer layer) =>
        new(layer ?? throw new ArgumentNullException(nameof(layer)), _scopes, _namedProfiles);

    public ClientSettingsDocument WithScope(ClientSettingsScopeKey scope, ClientSettingsLayer layer)
    {
        if (!scope.IsValid) throw new ArgumentException("A valid scope is required.", nameof(scope));
        ArgumentNullException.ThrowIfNull(layer);
        var values = new List<ClientScopedSettings>(_scopes.Count + 1);
        bool replaced = false;
        foreach (ClientScopedSettings value in _scopes)
        {
            if (value.Scope == scope)
            {
                values.Add(new ClientScopedSettings(scope, layer));
                replaced = true;
            }
            else values.Add(value);
        }
        if (!replaced) values.Add(new ClientScopedSettings(scope, layer));
        return new ClientSettingsDocument(Global, values, _namedProfiles);
    }
}

public sealed class ResolvedClientSettings
{
    private readonly ReadOnlyCollection<ClientPhysicalBinding> _bindings;

    internal ResolvedClientSettings(
        float uiScale,
        float masterVolume,
        float pointerSensitivity,
        bool invertY,
        ClientControlGrammar controlGrammar,
        ClientCameraProfile cameraProfile,
        float cameraDistance,
        float cameraFovDegrees,
        ClientCameraRig cameraRig,
        int renderDistanceChunks,
        ClientPhysicalBinding[] bindings)
    {
        UiScale = uiScale;
        MasterVolume = masterVolume;
        PointerSensitivity = pointerSensitivity;
        InvertY = invertY;
        ControlGrammar = controlGrammar;
        CameraProfile = cameraProfile;
        CameraDistance = cameraDistance;
        CameraFovDegrees = cameraFovDegrees;
        CameraRig = cameraRig;
        RenderDistanceChunks = renderDistanceChunks;
        _bindings = Array.AsReadOnly(bindings);
    }

    public float UiScale { get; }
    public float MasterVolume { get; }
    public float PointerSensitivity { get; }
    public bool InvertY { get; }
    public ClientControlGrammar ControlGrammar { get; }
    public ClientCameraProfile CameraProfile { get; }
    public float CameraDistance { get; }
    public float CameraFovDegrees { get; }
    public ClientCameraRig CameraRig { get; }
    public int RenderDistanceChunks { get; }
    public ReadOnlyCollection<ClientPhysicalBinding> Bindings => _bindings;

    public bool TryGetBinding(ClientSemanticAction action, out ClientPhysicalInput input)
    {
        foreach (ClientPhysicalBinding binding in _bindings)
        {
            if (binding.Action != action) continue;
            input = binding.Input;
            return true;
        }
        input = default;
        return false;
    }

    /// <summary>
    /// Resolves one canonical physical observation through the effective client-owned binding
    /// table. Engine adapters and headless clients use this same direction of lookup; an unmapped
    /// observation is inert and never becomes a Game command.
    /// </summary>
    public bool TryResolveInput(
        in ClientPhysicalInput input,
        out ClientSemanticAction action)
    {
        if (!input.IsValid)
        {
            action = default;
            return false;
        }
        foreach (ClientPhysicalBinding binding in _bindings)
        {
            if (binding.Input != input) continue;
            action = binding.Action;
            return true;
        }
        action = default;
        return false;
    }
}

public static class ClientSettingsResolver
{
    public const int MaximumBindings = 32;
    public const int MinimumRenderDistanceChunks = 1;
    public const int MaximumRenderDistanceChunks = 3;
    public const float DefaultOrbitCameraDistance = 14f;

    public static ResolvedClientSettings Resolve(
        ClientSettingsDocument document,
        ClientSettingsScopeKey scope = default,
        ClientProfileRecommendation? recommendation = null,
        ClientProfileApplicationMode mode = ClientProfileApplicationMode.Ignore,
        string? namedProfile = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (mode is < ClientProfileApplicationMode.Ignore or > ClientProfileApplicationMode.Customize)
            throw new ArgumentOutOfRangeException(nameof(mode));

        MutableSettings current = BuiltIn();
        ApplyUserLayer(current, document.Global);
        if (namedProfile is not null)
        {
            if (!document.TryGetNamedProfile(namedProfile, out ClientNamedControlProfile? named))
                throw new ArgumentException("The named profile is unavailable.", nameof(namedProfile));
            ApplyUserLayer(current, named!.AsLayer());
        }
        if (recommendation is not null && mode != ClientProfileApplicationMode.Ignore)
        {
            current.ControlGrammar = recommendation.ControlGrammar;
            current.CameraProfile = recommendation.CameraProfile;
            current.CameraDistance = recommendation.CameraDistance;
            current.CameraFovDegrees = recommendation.CameraFovDegrees;
            current.CameraRig = recommendation.CameraRig;
            if (mode != ClientProfileApplicationMode.KeepMyControls)
                ApplyRecommendationBindings(current.Bindings, recommendation.Bindings);
        }
        if (scope.IsValid && document.TryGetScope(scope, out ClientSettingsLayer scoped))
            ApplyUserLayer(current, scoped);
        ValidateFinalBindings(current.Bindings);
        ClientPhysicalBinding[] bindings = current.Bindings
            .OrderBy(value => value.Key)
            .Select(value => new ClientPhysicalBinding(value.Key, value.Value))
            .ToArray();
        return new ResolvedClientSettings(
            current.UiScale, current.MasterVolume, current.PointerSensitivity, current.InvertY,
            current.ControlGrammar, current.CameraProfile, current.CameraDistance,
            current.CameraFovDegrees, current.CameraRig, current.RenderDistanceChunks, bindings);
    }

    private static MutableSettings BuiltIn()
    {
        var settings = new MutableSettings
        {
            UiScale = 1f,
            MasterVolume = 0.8f,
            PointerSensitivity = 1f,
            InvertY = false,
            ControlGrammar = ClientControlGrammar.OrbitCommand,
            CameraProfile = ClientCameraProfile.Orbit,
            CameraDistance = DefaultOrbitCameraDistance,
            CameraFovDegrees = 70f,
            CameraRig = ClientStockCameraRigs.Orbit,
            RenderDistanceChunks = 1,
        };
        AddDefault(settings, ClientSemanticAction.ExitApplication, ClientPhysicalInputKind.Keyboard, "F4", ClientInputModifiers.Alt);
        AddDefault(settings, ClientSemanticAction.OpenClientMenu, ClientPhysicalInputKind.Keyboard, "ESCAPE");
        AddDefault(settings, ClientSemanticAction.Disconnect, ClientPhysicalInputKind.Keyboard, "F8");
        AddDefault(settings, ClientSemanticAction.OpenSettings, ClientPhysicalInputKind.Keyboard, "F10");
        AddDefault(settings, ClientSemanticAction.MoveLeft, ClientPhysicalInputKind.Keyboard, "A");
        AddDefault(settings, ClientSemanticAction.MoveRight, ClientPhysicalInputKind.Keyboard, "D");
        AddDefault(settings, ClientSemanticAction.MoveForward, ClientPhysicalInputKind.Keyboard, "W");
        AddDefault(settings, ClientSemanticAction.MoveBackward, ClientPhysicalInputKind.Keyboard, "S");
        AddDefault(settings, ClientSemanticAction.Select, ClientPhysicalInputKind.MouseButton, "LEFT");
        AddDefault(settings, ClientSemanticAction.Destination, ClientPhysicalInputKind.MouseButton, "LEFT_DOUBLE");
        AddDefault(settings, ClientSemanticAction.ContextMenu, ClientPhysicalInputKind.MouseButton, "RIGHT");
        AddDefault(settings, ClientSemanticAction.Primary, ClientPhysicalInputKind.MouseButton, "MIDDLE");
        AddDefault(settings, ClientSemanticAction.Interact, ClientPhysicalInputKind.Keyboard, "E");
        AddDefault(settings, ClientSemanticAction.OpenInventory, ClientPhysicalInputKind.Keyboard, "I");
        AddDefault(settings, ClientSemanticAction.Chat, ClientPhysicalInputKind.Keyboard, "ENTER");
        AddDefault(settings, ClientSemanticAction.Jump, ClientPhysicalInputKind.Keyboard, "SPACE");
        AddDefault(settings, ClientSemanticAction.OpenServerMonitor, ClientPhysicalInputKind.Keyboard, "F4");
        AddDefault(settings, ClientSemanticAction.CycleDebug, ClientPhysicalInputKind.Keyboard, "F11");
        AddDefault(settings, ClientSemanticAction.CameraLook, ClientPhysicalInputKind.MouseButton, "MIDDLE_DRAG");
        AddDefault(settings, ClientSemanticAction.CameraZoomIn, ClientPhysicalInputKind.MouseButton, "WHEELUP");
        AddDefault(settings, ClientSemanticAction.CameraZoomOut, ClientPhysicalInputKind.MouseButton, "WHEELDOWN");
        return settings;
    }

    private static void AddDefault(
        MutableSettings settings,
        ClientSemanticAction action,
        ClientPhysicalInputKind kind,
        string code,
        ClientInputModifiers modifiers = ClientInputModifiers.None)
    {
        if (!ClientPhysicalInput.TryCreate(kind, code, modifiers, out ClientPhysicalInput input))
            throw new InvalidOperationException("A built-in client binding is invalid.");
        settings.Bindings.Add(action, input);
    }

    private static void ApplyUserLayer(MutableSettings current, ClientSettingsLayer layer)
    {
        current.UiScale = layer.UiScale ?? current.UiScale;
        current.MasterVolume = layer.MasterVolume ?? current.MasterVolume;
        current.PointerSensitivity = layer.PointerSensitivity ?? current.PointerSensitivity;
        current.InvertY = layer.InvertY ?? current.InvertY;
        current.ControlGrammar = layer.ControlGrammar ?? current.ControlGrammar;
        current.CameraProfile = layer.CameraProfile ?? current.CameraProfile;
        current.CameraDistance = layer.CameraDistance ?? current.CameraDistance;
        current.CameraFovDegrees = layer.CameraFovDegrees ?? current.CameraFovDegrees;
        if (layer.CameraProfile is ClientCameraProfile legacyProfile)
            current.CameraRig = ClientStockCameraRigs.FromLegacy(legacyProfile);
        if (layer.CameraDistance.HasValue || layer.CameraFovDegrees.HasValue)
            current.CameraRig = current.CameraRig.WithLegacyLens(
                current.CameraDistance, current.CameraFovDegrees);
        if (layer.CameraRig is ClientCameraRig rig)
        {
            current.CameraRig = rig;
            current.CameraDistance = rig.DefaultDistanceMillimeters / 1_000f;
            current.CameraFovDegrees = rig.CanonicalFovTenths / 10f;
        }
        current.RenderDistanceChunks = layer.RenderDistanceChunks ?? current.RenderDistanceChunks;
        foreach (ClientPhysicalBinding binding in layer.Bindings)
            current.Bindings[binding.Action] = binding.Input;
    }

    private static void ApplyRecommendationBindings(
        Dictionary<ClientSemanticAction, ClientPhysicalInput> current,
        IEnumerable<ClientPhysicalBinding> recommendations)
    {
        ClientPhysicalInput[] reserved = current
            .Where(value => ClientReservedControls.IsReserved(value.Key))
            .Select(value => value.Value)
            .ToArray();
        var candidates = new Dictionary<ClientSemanticAction, ClientPhysicalInput>();
        foreach (ClientPhysicalBinding binding in recommendations)
        {
            if (ClientReservedControls.IsReserved(binding.Action) || reserved.Contains(binding.Input))
                continue;
            candidates.Add(binding.Action, binding.Input);
        }

        // A recommendation is a set, not a sequence of individual rebinding operations. Admit
        // swaps/cycles (for example LEFT Select <-> MIDDLE Primary) while preserving every
        // unaffected and reserved binding. Ambiguous duplicate recommendations are ignored, then
        // any candidate colliding with a retained binding is removed to a fixed point.
        ClientPhysicalInput[] duplicates = candidates.Values
            .GroupBy(static input => input)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToArray();
        if (duplicates.Length != 0)
            foreach (ClientSemanticAction action in candidates
                         .Where(value => duplicates.Contains(value.Value))
                         .Select(static value => value.Key)
                         .ToArray())
                candidates.Remove(action);

        bool removed;
        do
        {
            removed = false;
            foreach ((ClientSemanticAction action, ClientPhysicalInput input) in candidates.ToArray())
            {
                bool occupiedByRetainedAction = current.Any(value =>
                    value.Key != action && !candidates.ContainsKey(value.Key) && value.Value == input);
                if (!occupiedByRetainedAction) continue;
                candidates.Remove(action);
                removed = true;
            }
        } while (removed);

        foreach ((ClientSemanticAction action, ClientPhysicalInput input) in candidates)
            current[action] = input;
    }

    private static void ValidateFinalBindings(Dictionary<ClientSemanticAction, ClientPhysicalInput> bindings)
    {
        foreach (ClientSemanticAction action in Enum.GetValues<ClientSemanticAction>())
            if (!bindings.ContainsKey(action))
                throw new InvalidOperationException($"The resolved client profile omitted {action}.");
        if (bindings.Values.Distinct().Count() != bindings.Count)
            throw new InvalidOperationException("The resolved user binding profile contains a physical collision.");
        foreach (ClientSemanticAction action in Enum.GetValues<ClientSemanticAction>())
            if (ClientReservedControls.IsReserved(action) && !bindings[action].IsValid)
                throw new InvalidOperationException($"The reserved client action {action} is unavailable.");
    }

    private sealed class MutableSettings
    {
        internal float UiScale;
        internal float MasterVolume;
        internal float PointerSensitivity;
        internal bool InvertY;
        internal ClientControlGrammar ControlGrammar;
        internal ClientCameraProfile CameraProfile;
        internal float CameraDistance;
        internal float CameraFovDegrees;
        internal ClientCameraRig CameraRig;
        internal int RenderDistanceChunks;
        internal readonly Dictionary<ClientSemanticAction, ClientPhysicalInput> Bindings = [];
    }
}

/// <summary>Current human-readable persistence codec; versioned and replaceable.</summary>
public static class ClientSettingsJsonCodec
{
    private const float VersionTwoOrbitCameraDistance = 28f;
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static string Write(ClientSettingsDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidateDocument(document);
        var value = new DocumentDto
        {
            Version = ClientSettingsDocument.CurrentVersion,
            Global = ToDto(document.Global),
            Scopes = document.Scopes.Select(scope => new ScopeDto
            {
                Scope = scope.Scope.Value!,
                Settings = ToDto(scope.Layer),
            }).ToList(),
            NamedProfiles = document.NamedProfiles.Select(profile => new NamedProfileDto
            {
                Name = profile.Name,
                ControlGrammar = profile.ControlGrammar,
                CameraRig = profile.CameraRig,
                Bindings = profile.Bindings.Select(binding => new BindingDto
                {
                    Action = binding.Action,
                    Kind = binding.Input.Kind,
                    Code = binding.Input.Code!,
                    Modifiers = binding.Input.Modifiers,
                }).ToList(),
            }).ToList(),
        };
        return JsonSerializer.Serialize(value, Options) + Environment.NewLine;
    }

    public static bool TryRead(
        string? json,
        out ClientSettingsDocument document,
        out string? error)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(json)) throw new JsonException("The settings document is empty.");
            DocumentDto value = JsonSerializer.Deserialize<DocumentDto>(json, Options) ??
                throw new JsonException("The settings document was null.");
            if (value.Version is < 1 or > ClientSettingsDocument.CurrentVersion)
                throw new JsonException($"Unsupported client settings version {value.Version}.");
            LayerDto globalDto = value.Global ?? new LayerDto();
            MigrateLegacyDefaults(value.Version, globalDto);
            ClientSettingsLayer global = FromDto(globalDto);
            List<ClientScopedSettings> scopes = [];
            foreach (ScopeDto scope in value.Scopes ?? [])
            {
                if (!ClientSettingsScopeKey.TryCreate(scope.Scope, out ClientSettingsScopeKey key))
                    throw new JsonException("A settings scope key is invalid.");
                LayerDto scopedDto = scope.Settings ?? new LayerDto();
                scopes.Add(new ClientScopedSettings(key, FromDto(scopedDto)));
            }
            List<ClientNamedControlProfile> namedProfiles = [];
            foreach (NamedProfileDto named in value.NamedProfiles ?? [])
            {
                ClientSettingsLayer layer = FromDto(new LayerDto
                {
                    ControlGrammar = named.ControlGrammar,
                    CameraRig = named.CameraRig,
                    Bindings = named.Bindings,
                });
                if (layer.ControlGrammar is not ClientControlGrammar grammar ||
                    layer.CameraRig is not ClientCameraRig rig)
                    throw new JsonException("A named profile omitted its grammar or camera rig.");
                namedProfiles.Add(new ClientNamedControlProfile(
                    named.Name ?? string.Empty, grammar, rig, layer.Bindings));
            }
            document = new ClientSettingsDocument(global, scopes, namedProfiles);
            ValidateDocument(document);
            error = null;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or
            InvalidOperationException or OverflowException)
        {
            document = ClientSettingsDocument.Empty;
            error = exception.Message;
            return false;
        }
    }

    private static void ValidateDocument(ClientSettingsDocument document)
    {
        _ = ClientSettingsResolver.Resolve(document);
        foreach (ClientScopedSettings scoped in document.Scopes)
            _ = ClientSettingsResolver.Resolve(document, scoped.Scope);
        foreach (ClientNamedControlProfile named in document.NamedProfiles)
            _ = ClientSettingsResolver.Resolve(document, namedProfile: named.Name);
    }

    private static void MigrateLegacyDefaults(int version, LayerDto layer)
    {
        // F0.1 persisted the complete Settings form, so merely saving an unrelated option pinned
        // the then-default 28 m Orbit distance as if it were an intentional override. Version 3
        // moves that exact legacy Orbit default to the new closer composition while retaining all
        // genuinely distinct user distances and every other preference.
        if (version <= 2 && layer.CameraDistance == VersionTwoOrbitCameraDistance &&
            layer.CameraProfile is null or ClientCameraProfile.Orbit)
            layer.CameraDistance = ClientSettingsResolver.DefaultOrbitCameraDistance;
    }

    private static LayerDto ToDto(ClientSettingsLayer layer) => new()
    {
        UiScale = layer.UiScale,
        MasterVolume = layer.MasterVolume,
        PointerSensitivity = layer.PointerSensitivity,
        InvertY = layer.InvertY,
        ControlGrammar = layer.ControlGrammar,
        CameraProfile = layer.CameraProfile,
        CameraDistance = layer.CameraDistance,
        CameraFovDegrees = layer.CameraFovDegrees,
        CameraRig = layer.CameraRig,
        RenderDistanceChunks = layer.RenderDistanceChunks,
        Bindings = layer.Bindings.Select(binding => new BindingDto
        {
            Action = binding.Action,
            Kind = binding.Input.Kind,
            Code = binding.Input.Code!,
            Modifiers = binding.Input.Modifiers,
        }).ToList(),
    };

    private static ClientSettingsLayer FromDto(LayerDto value)
    {
        List<ClientPhysicalBinding> bindings = [];
        foreach (BindingDto binding in value.Bindings ?? [])
        {
            if (!ClientPhysicalInput.TryCreate(
                    binding.Kind, binding.Code, binding.Modifiers, out ClientPhysicalInput input))
                throw new JsonException("A persisted physical binding is invalid.");
            bindings.Add(new ClientPhysicalBinding(binding.Action, input));
        }
        return new ClientSettingsLayer(
            value.UiScale, value.MasterVolume, value.PointerSensitivity, value.InvertY,
            value.ControlGrammar, value.CameraProfile, value.CameraDistance,
            value.CameraFovDegrees, bindings, value.RenderDistanceChunks, value.CameraRig);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var result = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        result.Converters.Add(new JsonStringEnumConverter());
        return result;
    }

    private sealed class DocumentDto
    {
        public int Version { get; set; }
        public LayerDto? Global { get; set; }
        public List<ScopeDto>? Scopes { get; set; }
        public List<NamedProfileDto>? NamedProfiles { get; set; }
    }

    private sealed class NamedProfileDto
    {
        public string? Name { get; set; }
        public ClientControlGrammar? ControlGrammar { get; set; }
        public ClientCameraRig? CameraRig { get; set; }
        public List<BindingDto>? Bindings { get; set; }
    }

    private sealed class ScopeDto
    {
        public string? Scope { get; set; }
        public LayerDto? Settings { get; set; }
    }

    private sealed class LayerDto
    {
        public float? UiScale { get; set; }
        public float? MasterVolume { get; set; }
        public float? PointerSensitivity { get; set; }
        public bool? InvertY { get; set; }
        public ClientControlGrammar? ControlGrammar { get; set; }
        public ClientCameraProfile? CameraProfile { get; set; }
        public float? CameraDistance { get; set; }
        public float? CameraFovDegrees { get; set; }
        public ClientCameraRig? CameraRig { get; set; }
        public int? RenderDistanceChunks { get; set; }
        public List<BindingDto>? Bindings { get; set; }
    }

    private sealed class BindingDto
    {
        public ClientSemanticAction Action { get; set; }
        public ClientPhysicalInputKind Kind { get; set; }
        public string? Code { get; set; }
        public ClientInputModifiers Modifiers { get; set; }
    }
}

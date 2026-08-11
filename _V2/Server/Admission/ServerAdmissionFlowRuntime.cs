using UltraSim.Network;
using UltraSim.Server.Scripting;
using UltraSim.UI;

namespace UltraSim.Server.Admission;

public sealed class ServerAdmissionFlowRuntime
{
    public const string DefaultClientConnectedEvent = "admission.client_connected";

    private readonly DefaultUltraSimScriptContext _scriptContext;
    private readonly IAdmissionPanelGateway _panels;
    private readonly IAdmissionCredentialValidator _credentialValidator;
    private readonly IAdmissionRuntimePolicy _runtimePolicy;
    private readonly Dictionary<ConnectionId, SessionAdmissionState> _statesByConnection = new();
    private long _nextPanelSequence;
    private long _nextModelUpdateSequence;

    public ServerAdmissionFlowRuntime(
        DefaultUltraSimScriptContext scriptContext,
        IAdmissionPanelGateway panels,
        IAdmissionCredentialValidator? credentialValidator = null,
        IAdmissionRuntimePolicy? runtimePolicy = null)
    {
        _scriptContext = scriptContext ?? throw new ArgumentNullException(nameof(scriptContext));
        _panels = panels ?? throw new ArgumentNullException(nameof(panels));
        _credentialValidator = credentialValidator ?? AcceptAllAdmissionCredentialValidator.Instance;
        _runtimePolicy = runtimePolicy ?? AdmissionRuntimePolicyDefaults.Instance;
    }

    public long FiredEvents { get; private set; }
    public long OpenedPanels { get; private set; }
    public long CompletedSteps { get; private set; }
    public long RejectedCredentials { get; private set; }
    public long RejectedEvents { get; private set; }

    public bool TryGetState(ConnectionId connectionId, out SessionAdmissionState state)
    {
        return _statesByConnection.TryGetValue(connectionId, out state!);
    }

    public bool TryClearConnection(ConnectionId connectionId, out ulong accountId)
    {
        accountId = 0;
        if (!_statesByConnection.Remove(connectionId, out SessionAdmissionState? state))
            return false;

        accountId = state.AccountId;
        return true;
    }

    public bool TryStart(ConnectionId connectionId, out string error)
    {
        return TryFireEvent(connectionId, DefaultClientConnectedEvent, out error);
    }

    public bool TryFireEvent(ConnectionId connectionId, string eventName, out string error)
    {
        ulong accountId = _statesByConnection.TryGetValue(connectionId, out SessionAdmissionState? previous)
            ? previous.AccountId
            : 0;
        return TryFireEvent(connectionId, eventName, accountId, out error);
    }

    private bool TryFireEvent(ConnectionId connectionId, string eventName, ulong accountId, out string error)
    {
        error = string.Empty;
        if (!connectionId.IsValid)
        {
            error = "Admission event requires a valid connection.";
            RejectedEvents++;
            return false;
        }

        if (!_scriptContext.TryGetAdmissionStep(eventName, out UltraSimScriptAdmissionFlowStepDescriptor step))
        {
            error = $"No admission step is registered for event '{eventName}'.";
            return false;
        }

        if (!_scriptContext.TryGetPanelProvider(step.PanelProviderKey, out UltraSimScriptUiPanelProviderDescriptor provider))
        {
            error = $"Admission step '{step.OwnerKey}' references unknown panel provider '{step.PanelProviderKey}'.";
            RejectedEvents++;
            return false;
        }

        UiBuiltPanel panel;
        try
        {
            panel = provider.Provider();
        }
        catch (Exception ex)
        {
            error = $"Admission step '{step.OwnerKey}' failed to build panel '{step.PanelProviderKey}': {ex.Message}";
            RejectedEvents++;
            return false;
        }

        long sequence = Interlocked.Increment(ref _nextPanelSequence);
        string panelInstanceId = $"admission/{SanitizeStableSegment(step.StartsOnEvent)}/{connectionId.Value}/{sequence}";
        if (!_panels.TryOpenPanel(connectionId, panel, panelInstanceId))
        {
            error = $"Admission step '{step.OwnerKey}' could not open panel '{step.PanelProviderKey}'.";
            RejectedEvents++;
            return false;
        }

        var state = new SessionAdmissionState(
            connectionId,
            step,
            panelInstanceId,
            panel.Schema.Identity,
            panel.Schema.CatalogIdentity,
            step.CompletesWithEvent,
            panel.Schema.Actions
                .Where(static action => action.ContextKind == UiActionContextKind.TrustedCredential)
                .Select(static action => action.ActionId)
                .ToHashSet(StringComparer.Ordinal),
            AccountId: accountId);
        _statesByConnection[connectionId] = state;
        _runtimePolicy.OnStepOpened(new AdmissionStepOpenedContext(
            connectionId,
            state,
            panel,
            _panels,
            NextModelUpdateSequence));
        FiredEvents++;
        OpenedPanels++;
        return true;
    }

    public bool TryCompleteCurrentStep(ConnectionId connectionId, string completedEvent, out string error)
    {
        error = string.Empty;
        if (!_statesByConnection.TryGetValue(connectionId, out SessionAdmissionState? state))
        {
            error = "Connection has no active admission step.";
            return false;
        }

        if (!string.Equals(state.WaitingForEvent, completedEvent, StringComparison.Ordinal))
        {
            error = $"Admission step '{state.Step.OwnerKey}' is waiting for '{state.WaitingForEvent}', not '{completedEvent}'.";
            return false;
        }

        return CompleteCurrentStep(connectionId, state, completedEvent, out error);
    }

    public void ProcessUiAction(ConnectionId connectionId, UiActionRequestDocument request)
    {
        if (!_statesByConnection.TryGetValue(connectionId, out SessionAdmissionState? state))
            return;

        if (!string.Equals(request.PanelInstanceId, state.PanelInstanceId, StringComparison.Ordinal))
            return;

        if (request.ContextKind != UiActionContextKind.TrustedCredential)
        {
            if (request.ContextKind == UiActionContextKind.Row &&
                string.IsNullOrWhiteSpace(request.ContextId))
            {
                return;
            }

            if (state.Step.TryGetActionRoute(request.ActionId, request.ContextKind, out UltraSimScriptAdmissionActionRouteDescriptor route))
            {
                AdmissionActionRouteResult result = _runtimePolicy.EvaluateActionRoute(new AdmissionActionRouteContext(
                    connectionId,
                    state,
                    request,
                    route));
                if (result.ShouldComplete)
                {
                    CompleteCurrentStep(connectionId, state, result.CompletedEvent, out _);
                }
                else
                {
                    TryPublishRouteFailure(connectionId, state, result);
                }
            }

            return;
        }

        if (!state.TrustedCredentialActionIds.Contains(request.ActionId))
            return;

        ServerAdmissionCredentialValidationResult validation =
            _credentialValidator.ValidateTrustedCredential(connectionId, request);
        if (!validation.Accepted)
        {
            RejectedCredentials++;
            TryPublishCredentialFailure(connectionId, state, validation);
            return;
        }

        CompleteCurrentStep(connectionId, state with { AccountId = validation.AccountId }, state.WaitingForEvent, out _);
    }

    public void ProcessCredential(ConnectionId connectionId, AdmissionCredentialRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_statesByConnection.TryGetValue(connectionId, out SessionAdmissionState? state))
            return;

        if (!string.Equals(request.PanelInstanceId, state.PanelInstanceId, StringComparison.Ordinal))
            return;

        if (!state.TrustedCredentialActionIds.Contains(request.ActionId))
            return;

        ServerAdmissionCredentialValidationResult validation =
            _credentialValidator.ValidateTrustedCredential(connectionId, request);
        if (!validation.Accepted)
        {
            RejectedCredentials++;
            TryPublishCredentialFailure(connectionId, state, validation);
            return;
        }

        CompleteCurrentStep(connectionId, state with { AccountId = validation.AccountId }, state.WaitingForEvent, out _);
    }

    private bool CompleteCurrentStep(
        ConnectionId connectionId,
        SessionAdmissionState state,
        string completedEvent,
        out string error)
    {
        error = string.Empty;
        _statesByConnection.Remove(connectionId);
        CompletedSteps++;
        if (TryFireEvent(connectionId, completedEvent, state.AccountId, out string nextError))
            return true;

        if (nextError.StartsWith("No admission step is registered", StringComparison.Ordinal))
        {
            error = string.Empty;
            return true;
        }

        error = nextError;
        return false;
    }

    private void TryPublishCredentialFailure(
        ConnectionId connectionId,
        SessionAdmissionState state,
        ServerAdmissionCredentialValidationResult validation)
    {
        if (string.IsNullOrWhiteSpace(state.Step.FailureStatusSlotId))
            return;

        long sequence = NextModelUpdateSequence();
        var update = new UiModelUpdateDocument(
            state.PanelInstanceId,
            state.SchemaIdentity,
            state.CatalogIdentity,
            sequence,
            UiModelUpdateKind.SlotPatch,
            ScopeId: null,
            SlotValues: [UiModelSlotValue.String(state.Step.FailureStatusSlotId, validation.FailureReason.ToString())],
            ClearedSlotIds: [],
            RowDeltas: []);
        _panels.TryUpdatePanel(connectionId, update);
    }

    private void TryPublishRouteFailure(
        ConnectionId connectionId,
        SessionAdmissionState state,
        AdmissionActionRouteResult result)
    {
        if (string.IsNullOrWhiteSpace(result.FailureStatusSlotId) ||
            string.IsNullOrWhiteSpace(result.FailureStatusValue))
        {
            return;
        }

        long sequence = NextModelUpdateSequence();
        var update = new UiModelUpdateDocument(
            state.PanelInstanceId,
            state.SchemaIdentity,
            state.CatalogIdentity,
            sequence,
            UiModelUpdateKind.SlotPatch,
            ScopeId: null,
            SlotValues: [UiModelSlotValue.String(result.FailureStatusSlotId, result.FailureStatusValue)],
            ClearedSlotIds: [],
            RowDeltas: []);
        _panels.TryUpdatePanel(connectionId, update);
    }

    private long NextModelUpdateSequence()
    {
        return Interlocked.Increment(ref _nextModelUpdateSequence);
    }

    private static string SanitizeStableSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "event";

        Span<char> buffer = stackalloc char[Math.Min(value.Length, 64)];
        int length = 0;
        for (int i = 0; i < value.Length && length < buffer.Length; i++)
        {
            char c = value[i];
            bool valid =
                c is >= 'a' and <= 'z' ||
                c is >= 'A' and <= 'Z' ||
                c is >= '0' and <= '9' ||
                c is '.' or '_' or '-' or ':' or '/';
            buffer[length++] = valid ? c : '_';
        }

        string sanitized = new(buffer[..length]);
        sanitized = sanitized.Trim('/');
        return sanitized.Length == 0 ? "event" : sanitized;
    }
}

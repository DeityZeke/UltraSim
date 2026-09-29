using System.Collections.ObjectModel;

namespace UltraSim.Framework.ControlPlane;

public sealed record ControlViewerItem(ControlProviderId Provider, ulong Generation, ControlPublicationDescriptor Descriptor,
    ControlState? Control, ControlDiagnosticSnapshot? Diagnostic, ControlCollectionSnapshot? Collection,
    bool CanInvoke = false);
public sealed record ControlViewerCategory(ControlCategoryPath Path, ReadOnlyCollection<ControlViewerItem> Items);
public sealed record ControlPendingConfirmation(string Prompt, ControlProviderId Provider,
    ulong Generation, ControlActionId Action, ulong? Revision);

/// <summary>Utilitarian generic viewer model; no owner-specific references, tabs or dispatch branches.</summary>
public sealed class ControlPlaneViewer
{
    private readonly ControlPlaneRegistry _registry;
    private readonly ControlPrincipal _principal;
    private readonly Dictionary<(ControlProviderId, ulong, ControlId), ControlValue> _drafts = new();
    private (ControlPendingConfirmation Ticket, ControlRequestId Request, ControlValue[] Arguments)? _pendingAction;
    public ControlPlaneViewer(ControlPlaneRegistry registry, ControlPrincipal principal)
    { _registry = registry; _principal = principal; }
    public ReadOnlyCollection<ControlViewerCategory> Capture()
    {
        var providers = _registry.Discover(_principal);
        foreach (var key in _drafts.Keys.ToArray())
            if (!providers.Any(p => p.Definition.Id == key.Item1 && p.Generation == key.Item2 &&
                p.Definition.Controls.Any(c => c.Id == key.Item3))) _drafts.Remove(key);
        if (_pendingAction is { } pending && !providers.Any(p =>
                p.Definition.Id == pending.Ticket.Provider && p.Generation == pending.Ticket.Generation &&
                p.Definition.Actions.Any(a => a.Id == pending.Ticket.Action)))
            _pendingAction = null;
        var items = new List<ControlViewerItem>();
        foreach (var p in providers)
        {
            foreach (var d in p.Definition.Controls)
            {
                _registry.Read(_principal, p.Definition.Id, p.Generation, d.Id, out ControlState? s);
                if (s is not null && s.Writable && !_principal.Has(d.WriteCapability))
                    s = new ControlState(s.Requested, s.Effective, s.Pending,
                        s.RequestedProvenance, s.EffectiveProvenance, s.PendingProvenance,
                        writable: false, ControlResultCode.Unauthorized,
                        "This viewer has read access only.")
                    { ProviderGeneration = s.ProviderGeneration, Revision = s.Revision };
                items.Add(new(p.Definition.Id, p.Generation, d, s, null, null));
            }
            foreach (var d in p.Definition.Diagnostics)
            {
                _registry.Read(_principal, p.Definition.Id, p.Generation, d.Id, out ControlDiagnosticSnapshot? s);
                items.Add(new(p.Definition.Id, p.Generation, d, null, s, null));
            }
            foreach (var d in p.Definition.Collections)
            {
                _registry.Read(_principal, p.Definition.Id, p.Generation, d.Id, out ControlCollectionSnapshot? s);
                items.Add(new(p.Definition.Id, p.Generation, d, null, null, s));
            }
            foreach (var d in p.Definition.Actions) items.Add(new(p.Definition.Id, p.Generation, d,
                null, null, null, _principal.Has(d.InvokeCapability)));
        }
        return Array.AsReadOnly(items.GroupBy(i => i.Descriptor.Category).OrderBy(g => g.Key.Value, StringComparer.Ordinal)
            .Select(g => new ControlViewerCategory(g.Key, Array.AsReadOnly(g.ToArray()))).ToArray());
    }
    public bool SetDraft(ControlProviderId provider, ulong generation, ControlId id, ControlValue value)
    {
        ControlProviderSnapshot? published = _registry.Discover(_principal)
            .FirstOrDefault(p => p.Definition.Id == provider && p.Generation == generation);
        ControlDescriptor? descriptor = published?.Definition.Controls.FirstOrDefault(c => c.Id == id);
        if (descriptor is null || !_principal.Has(descriptor.WriteCapability) ||
            descriptor.ApplyMode is ControlApplyMode.ReadOnly or ControlApplyMode.BuildTime ||
            !descriptor.Schema.Accepts(value) ||
            _registry.Read(_principal, provider, generation, id, out ControlState? state) != ControlResultCode.Accepted ||
            state is null || !state.Writable) return false;
        var key = (provider, generation, id);
        if (_drafts.Count == ControlLimits.Batch && !_drafts.ContainsKey(key)) return false;
        _drafts[key] = value; return true;
    }
    public bool TryGetDraft(ControlProviderId provider, ulong generation, ControlId id, out ControlValue value) =>
        _drafts.TryGetValue((provider, generation, id), out value);
    public ControlResult Apply(ControlRequestId request, ControlProviderId provider, ulong generation, ulong? revision = null)
    {
        var mutations = _drafts.Where(p => p.Key.Item1 == provider && p.Key.Item2 == generation)
            .Select(p => new ControlMutation(p.Key.Item3, p.Value)).ToArray();
        var result = _registry.Set(SetControlsRequest.Create(new(request, _principal), provider, generation, mutations, revision));
        if (result.Outcome.Status == ControlResultStatus.Accepted)
            foreach (var mutation in mutations) _drafts.Remove((provider, generation, mutation.Id));
        return result;
    }
    public ControlResult Invoke(ControlRequestId request, ControlProviderId provider, ulong generation,
        ControlActionId action, IEnumerable<ControlValue> arguments, ulong? revision = null) =>
        _registry.Invoke(ControlActionRequest.Create(new(request, _principal), provider, action, generation, arguments, revision));

    public ControlPendingConfirmation? PendingConfirmation => _pendingAction?.Ticket;

    /// <summary>UI confirmation is a separate gesture; authority is checked again at dispatch.</summary>
    public ControlResult? RequestAction(ControlRequestId request, ControlProviderId provider, ulong generation,
        ControlActionId action, IEnumerable<ControlValue> arguments, ulong? revision = null)
    {
        _pendingAction = null;
        ControlProviderSnapshot? published = _registry.Discover(_principal)
            .FirstOrDefault(p => p.Definition.Id == provider && p.Generation == generation);
        ControlActionDescriptor? descriptor = published?.Definition.Actions.FirstOrDefault(a => a.Id == action);
        if (descriptor is null)
            return Invoke(request, provider, generation, action, arguments, revision);
        ControlValue[] copy = ControlLimits.Copy(arguments, ControlLimits.Arguments);
        if (copy.Length != descriptor.Arguments.Count ||
            copy.Where((value, index) => !descriptor.Arguments[index].Accepts(value)).Any())
            return Invoke(request, provider, generation, action, copy, revision);
        if (descriptor.ConfirmationPrompt.Length == 0)
            return Invoke(request, provider, generation, action, copy, revision);
        // A confirmation gesture must not silently authorize a changed owner state.
        _pendingAction = (new(descriptor.ConfirmationPrompt, provider, generation, action,
            revision ?? published!.Revision), request, copy);
        return null;
    }

    public ControlResult ConfirmAction()
    {
        if (_pendingAction is not { } pending)
            throw new InvalidOperationException("No action is awaiting confirmation.");
        _pendingAction = null;
        return Invoke(pending.Request, pending.Ticket.Provider, pending.Ticket.Generation,
            pending.Ticket.Action, pending.Arguments, pending.Ticket.Revision);
    }

    public void CancelAction() => _pendingAction = null;
}

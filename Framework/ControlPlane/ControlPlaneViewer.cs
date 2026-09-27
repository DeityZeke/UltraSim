using System.Collections.ObjectModel;

namespace UltraSim.Framework.ControlPlane;

public sealed record ControlViewerItem(ControlProviderId Provider, ulong Generation, ControlPublicationDescriptor Descriptor,
    ControlState? Control, ControlDiagnosticSnapshot? Diagnostic, ControlCollectionSnapshot? Collection);
public sealed record ControlViewerCategory(ControlCategoryPath Path, ReadOnlyCollection<ControlViewerItem> Items);

/// <summary>Utilitarian generic viewer model; no owner-specific references, tabs or dispatch branches.</summary>
public sealed class ControlPlaneViewer
{
    private readonly ControlPlaneRegistry _registry;
    private readonly ControlPrincipal _principal;
    private readonly Dictionary<(ControlProviderId, ulong, ControlId), ControlValue> _drafts = new();
    public ControlPlaneViewer(ControlPlaneRegistry registry, ControlPrincipal principal)
    { _registry = registry; _principal = principal; }
    public ReadOnlyCollection<ControlViewerCategory> Capture()
    {
        var providers = _registry.Discover(_principal);
        foreach (var key in _drafts.Keys.ToArray())
            if (!providers.Any(p => p.Definition.Id == key.Item1 && p.Generation == key.Item2 &&
                p.Definition.Controls.Any(c => c.Id == key.Item3))) _drafts.Remove(key);
        var items = new List<ControlViewerItem>();
        foreach (var p in providers)
        {
            foreach (var d in p.Definition.Controls)
            {
                _registry.Read(_principal, p.Definition.Id, p.Generation, d.Id, out ControlState? s);
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
            foreach (var d in p.Definition.Actions) items.Add(new(p.Definition.Id, p.Generation, d, null, null, null));
        }
        return Array.AsReadOnly(items.GroupBy(i => i.Descriptor.Category).OrderBy(g => g.Key.Value, StringComparer.Ordinal)
            .Select(g => new ControlViewerCategory(g.Key, Array.AsReadOnly(g.ToArray()))).ToArray());
    }
    public bool SetDraft(ControlProviderId provider, ulong generation, ControlId id, ControlValue value)
    {
        if (_registry.Read(_principal, provider, generation, id, out ControlState? _) != ControlResultCode.Accepted ||
            !value.IsValid) return false;
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
        ControlActionId action, IEnumerable<ControlValue> arguments) =>
        _registry.Invoke(ControlActionRequest.Create(new(request, _principal), provider, action, generation, arguments));
}

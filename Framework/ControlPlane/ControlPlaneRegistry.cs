using System.Collections.ObjectModel;

namespace UltraSim.Framework.ControlPlane;

public sealed record ControlProviderSnapshot(ControlProviderDefinition Definition, ulong Generation);

/// <summary>
/// Explicit management-owner runtime. Dispatch is synchronous and non-reentrant; retirement cannot
/// overtake an owned dispatch. Different subsystem threads require their own owner-bound routing,
/// not locks around subsystem internals. Registering does not modify any owner's hot execution.
/// </summary>
public sealed class ControlPlaneRegistry
{
    private sealed record Entry(IControlProvider Provider, ControlProviderDefinition Definition, ulong Generation);
    private readonly Dictionary<ControlProviderId, Entry> _providers = new();
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly IControlAuditSink? _audit;
    private ulong _generation;
    private bool _dispatch;
    public ControlPlaneRegistry(IControlAuditSink? audit = null) => _audit = audit;
    private void Enter()
    {
        if (Environment.CurrentManagedThreadId != _owner) throw new InvalidOperationException("Management owner thread required.");
        if (_dispatch) throw new InvalidOperationException("Reentrant Control Plane operation.");
        _dispatch = true;
    }
    public bool TryRegister(IControlProvider provider, out ulong generation, out string reason)
    {
        Enter();
        try
        {
            ArgumentNullException.ThrowIfNull(provider);
            ControlProviderDefinition definition = provider.Definition;
            ArgumentNullException.ThrowIfNull(definition);
            generation = 0;
            if (_providers.ContainsKey(definition.Id)) { reason = "duplicate-provider"; return false; }
            if (_providers.Count == ControlLimits.Providers) { reason = "provider-capacity"; return false; }
            if (_generation == ulong.MaxValue) { reason = "generation-exhausted"; return false; }
            generation = ++_generation;
            _providers.Add(definition.Id, new(provider, definition, generation));
            reason = ""; return true;
        }
        finally { _dispatch = false; }
    }
    public bool Retire(ControlProviderId id, ulong generation)
    {
        Enter();
        try
        {
            if (!_providers.TryGetValue(id, out Entry? entry) || entry.Generation != generation) return false;
            return _providers.Remove(id);
        }
        finally { _dispatch = false; }
    }
    private static bool Discoverable(ControlPublicationDescriptor d, ControlPrincipal p) =>
        (d.Audience & p.Audience) != 0 && p.Has(d.ReadCapability);
    public ReadOnlyCollection<ControlProviderSnapshot> Discover(ControlPrincipal principal)
    {
        Enter();
        try
        {
            var result = new List<ControlProviderSnapshot>();
            foreach (Entry e in _providers.Values.OrderBy(e => e.Definition.Id.Value, StringComparer.Ordinal))
            {
                var d = e.Definition;
                var visible = new ControlProviderDefinition(d.Id,
                    d.Controls.Where(c => Discoverable(c, principal)), d.Diagnostics.Where(c => Discoverable(c, principal)),
                    d.Actions.Where(c => Discoverable(c, principal)), d.Collections.Where(c => Discoverable(c, principal)));
                if (visible.Controls.Count + visible.Diagnostics.Count + visible.Actions.Count + visible.Collections.Count != 0)
                    result.Add(new(visible, e.Generation));
            }
            return result.AsReadOnly();
        }
        finally { _dispatch = false; }
    }
    private ControlResultCode Lookup(ControlProviderId id, ulong generation, out Entry? entry)
    {
        if (!_providers.TryGetValue(id, out entry)) return ControlResultCode.UnknownProvider;
        return entry.Generation == generation ? ControlResultCode.Accepted : ControlResultCode.StaleGeneration;
    }
    private static ControlResultCode Access(ControlPublicationDescriptor? d, ControlPrincipal p)
    {
        if (d is null) return ControlResultCode.UnknownPublication;
        if ((d.Audience & p.Audience) == 0) return ControlResultCode.NotVisible;
        return p.Has(d.ReadCapability) ? ControlResultCode.Accepted : ControlResultCode.Unauthorized;
    }
    private static ControlState State(Entry e, ControlDescriptor d)
    {
        ControlState state = e.Provider.Capture(d.Id);
        if (!d.Schema.Accepts(state.Requested) || !d.Schema.Accepts(state.Effective) ||
            state.Pending is { } p && !d.Schema.Accepts(p))
            throw new InvalidOperationException("Owner published state outside schema.");
        return state with { ProviderGeneration = e.Generation, Revision = e.Provider.Revision };
    }
    public ControlResultCode Read(ControlPrincipal principal, ControlProviderId provider, ulong generation,
        ControlId id, out ControlState? state)
    {
        Enter(); state = null;
        try
        {
            var code = Lookup(provider, generation, out Entry? e);
            if (code != ControlResultCode.Accepted) return code;
            var d = e!.Definition.Controls.FirstOrDefault(d => d.Id == id);
            code = Access(d, principal);
            if (code == ControlResultCode.Accepted) state = State(e, d!);
            return code;
        }
        finally { _dispatch = false; }
    }
    public ControlResultCode Read(ControlPrincipal principal, ControlProviderId provider, ulong generation,
        DiagnosticId id, out ControlDiagnosticSnapshot? snapshot)
    {
        Enter(); snapshot = null;
        try
        {
            var code = Lookup(provider, generation, out Entry? e);
            if (code != ControlResultCode.Accepted) return code;
            var d = e!.Definition.Diagnostics.FirstOrDefault(d => d.Id == id);
            code = Access(d, principal);
            if (code != ControlResultCode.Accepted) return code;
            var value = e.Provider.Capture(id);
            if (!d!.Schema.Accepts(value)) throw new InvalidOperationException("Invalid owner diagnostic.");
            snapshot = new(e.Generation, e.Provider.Revision, value); return code;
        }
        finally { _dispatch = false; }
    }
    public ControlResultCode Read(ControlPrincipal principal, ControlProviderId provider, ulong generation,
        ControlCollectionId id, out ControlCollectionSnapshot? snapshot)
    {
        Enter(); snapshot = null;
        try
        {
            var code = Lookup(provider, generation, out Entry? e);
            if (code != ControlResultCode.Accepted) return code;
            var d = e!.Definition.Collections.FirstOrDefault(d => d.Id == id);
            code = Access(d, principal);
            if (code != ControlResultCode.Accepted) return code;
            var rows = ControlLimits.Copy(e.Provider.Capture(id), d!.MaximumRows);
            if (rows.Any(r => r is null) || rows.Select(r => r.Id).Distinct().Count() != rows.Length)
                throw new InvalidOperationException("Invalid owner rows.");
            foreach (var row in rows)
                if (row.Values.Count != d.Columns.Count || row.Values.Where((v, i) => !d.Columns[i].Schema.Accepts(v)).Any())
                    throw new InvalidOperationException("Invalid owner collection cells.");
            snapshot = new(e.Generation, e.Provider.Revision, Array.AsReadOnly(rows)); return code;
        }
        finally { _dispatch = false; }
    }
    private static void ValidateContext(ControlRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Principal);
        if (!context.RequestId.IsValid) throw new ArgumentException("Invalid request identity.");
    }
    private static ControlResult Result(ControlRequestContext c, Entry? e, ControlOwnerResult outcome,
        IEnumerable<ControlState>? states = null) =>
        new(c.RequestId, outcome, e?.Generation ?? 0, e?.Provider.Revision ?? 0, Array.AsReadOnly((states ?? []).ToArray()));
    public ControlResult Set(SetControlRequest request) => Set(SetControlsRequest.Create(request.Context, request.Provider,
        request.ExpectedProviderGeneration, [new(request.Control, request.RequestedValue)], request.ExpectedRevision));
    public ControlResult Set(SetControlsRequest request)
    {
        Enter();
        try
        {
            ValidateContext(request.Context);
            string reason = "";
            var code = Lookup(request.Provider, request.ExpectedProviderGeneration, out Entry? e);
            if (code == ControlResultCode.Accepted)
            {
                if (request.Mutations is null || request.Mutations.Count is < 1 or > ControlLimits.Batch ||
                    request.Mutations.Select(m => m.Id).Distinct().Count() != request.Mutations.Count)
                    code = ControlResultCode.InvalidValue;
                else foreach (var m in request.Mutations)
                {
                    var d = e!.Definition.Controls.FirstOrDefault(d => d.Id == m.Id);
                    code = Access(d, request.Context.Principal);
                    if (code != ControlResultCode.Accepted) break;
                    if (!request.Context.Principal.Has(d!.WriteCapability)) code = ControlResultCode.Unauthorized;
                    else if (!d.Schema.Accepts(m.Value)) code = ControlResultCode.InvalidValue;
                    else
                    {
                        var state = State(e, d);
                        if (!state.Writable || d.ApplyMode is ControlApplyMode.ReadOnly or ControlApplyMode.BuildTime)
                        {
                            code = state.DisabledCode == ControlResultCode.Accepted ? ControlResultCode.NotWritable : state.DisabledCode;
                            reason = state.DisabledReason;
                        }
                    }
                    if (code != ControlResultCode.Accepted) break;
                }
                if (code == ControlResultCode.Accepted && request.ExpectedRevision is { } rev && rev != e!.Provider.Revision)
                    code = ControlResultCode.StaleRevision;
            }
            ControlResult result;
            if (code != ControlResultCode.Accepted) result = Result(request.Context, e, ControlOwnerResult.Reject(code, reason));
            else
            {
                ControlOwnerResult outcome;
                try { outcome = e!.Provider.Set(request.Context, request.Mutations!); }
                catch (Exception failure)
                {
                    AuditFailure(request.Context, request.Provider, null, request.Mutations!, [], e!, failure);
                    throw;
                }
                result = Result(request.Context, e, outcome, outcome.Status == ControlResultStatus.Accepted
                    ? request.Mutations!.Select(m => State(e!, e!.Definition.Controls.First(d => d.Id == m.Id))) : []);
            }
            _audit?.Observe(new(request.Context, request.Provider, null, request.Mutations ?? Array.AsReadOnly(Array.Empty<ControlMutation>()),
                Array.AsReadOnly(Array.Empty<ControlValue>()), result));
            return result;
        }
        finally { _dispatch = false; }
    }
    public ControlResult Invoke(ControlActionRequest request)
    {
        Enter();
        try
        {
            ValidateContext(request.Context);
            var code = Lookup(request.Provider, request.ExpectedProviderGeneration, out Entry? e);
            if (code == ControlResultCode.Accepted)
            {
                var d = e!.Definition.Actions.FirstOrDefault(d => d.Id == request.Action);
                code = Access(d, request.Context.Principal);
                if (code == ControlResultCode.Accepted)
                {
                    if (!request.Context.Principal.Has(d!.InvokeCapability)) code = ControlResultCode.Unauthorized;
                    else if (request.Arguments is null || request.Arguments.Count != d.Arguments.Count ||
                        request.Arguments.Count > ControlLimits.Arguments ||
                        request.Arguments.Where((a, i) => !d.Arguments[i].Accepts(a)).Any()) code = ControlResultCode.InvalidValue;
                    else if (request.ExpectedRevision is { } rev && rev != e.Provider.Revision) code = ControlResultCode.StaleRevision;
                }
            }
            ControlOwnerResult outcome;
            if (code != ControlResultCode.Accepted) outcome = ControlOwnerResult.Reject(code);
            else try { outcome = e!.Provider.Invoke(request.Context, request.Action, request.Arguments!); }
            catch (Exception failure)
            { AuditFailure(request.Context, request.Provider, request.Action, [], request.Arguments!, e!, failure); throw; }
            var result = Result(request.Context, e, outcome);
            _audit?.Observe(new(request.Context, request.Provider, request.Action, Array.AsReadOnly(Array.Empty<ControlMutation>()),
                request.Arguments ?? Array.AsReadOnly(Array.Empty<ControlValue>()), result));
            return result;
        }
        finally { _dispatch = false; }
    }
    private void AuditFailure(ControlRequestContext context, ControlProviderId provider, ControlActionId? action,
        IEnumerable<ControlMutation> mutations, IEnumerable<ControlValue> arguments, Entry e, Exception failure)
    {
        try { _audit?.Observe(new(context, provider, action, Array.AsReadOnly(mutations.ToArray()),
            Array.AsReadOnly(arguments.ToArray()), Result(context, e,
                new(ControlResultStatus.Failed, ControlResultCode.OwnerFailed, "Unexpected owner failure.")))); }
        catch (Exception auditFailure) { throw new AggregateException(failure, auditFailure); }
    }
}

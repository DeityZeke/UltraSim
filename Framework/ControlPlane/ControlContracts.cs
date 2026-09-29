using System.Collections.ObjectModel;

namespace UltraSim.Framework.ControlPlane;

public enum ControlApplyMode : byte { Live, SafePoint, SessionRestart, ClientRestart, ServerRestart, BuildTime, ReadOnly }
public enum ControlPersistenceClass : byte { None, UserSettings, ServerColdConfiguration, OwnerManaged }
public enum ControlProvenanceKind : byte
{ BuiltInDefault, UserGlobal, UserRealm, ServerRecommendation, ServerColdConfig,
  AdminRuntimeOverride, DeveloperOverride, TestOverride, RuntimeConstraint }
public enum ControlPrincipalKind : byte { LocalUser, LocalOperator, RemoteOperator, Developer, TestAutomation }
[Flags]
public enum ControlAudience : byte { User = 1, Operator = 2, Developer = 4, Test = 8, All = 15 }
public enum ControlResultStatus : byte { Accepted, Rejected, Failed }
public enum ControlResultCode : byte
{ Accepted, UnknownProvider, UnknownPublication, Unauthorized, NotVisible, StaleGeneration,
  StaleRevision, InvalidValue, NotWritable, Disabled, UnsupportedBatch, OwnerRejected, OwnerFailed }

public readonly record struct ControlProvenance
{
    public ControlProvenance(ControlProvenanceKind kind, string detail = "")
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentException("Invalid provenance.");
        Kind = kind; Detail = ControlLimits.Bounded(detail, ControlLimits.Label);
    }
    public ControlProvenanceKind Kind { get; }
    public string? Detail { get; }
}

/// <summary>Construct only in trusted composition, never from an untrusted claimed capability list.</summary>
public sealed class ControlPrincipal
{
    private readonly HashSet<ControlCapabilityId> _capabilities;
    public ControlPrincipal(string identity, ControlPrincipalKind kind, IEnumerable<ControlCapabilityId> capabilities,
        string context = "")
    {
        Identity = ControlLimits.IdText(identity);
        if (!Enum.IsDefined(kind)) throw new ArgumentException("Invalid principal kind.");
        Kind = kind; Context = ControlLimits.Bounded(context, ControlLimits.Label);
        var copy = ControlLimits.Copy(capabilities, ControlLimits.Capabilities);
        if (copy.Any(c => !c.IsValid)) throw new ArgumentException("Invalid capability.");
        _capabilities = new(copy);
        Capabilities = Array.AsReadOnly(copy.Distinct().OrderBy(c => c.Value, StringComparer.Ordinal).ToArray());
    }
    public string Identity { get; }
    public string Context { get; }
    public ControlPrincipalKind Kind { get; }
    public ReadOnlyCollection<ControlCapabilityId> Capabilities { get; }
    public bool Has(ControlCapabilityId id) => id.IsValid && _capabilities.Contains(id);
    public ControlAudience Audience => Kind switch
    {
        ControlPrincipalKind.LocalUser => ControlAudience.User,
        ControlPrincipalKind.LocalOperator or ControlPrincipalKind.RemoteOperator => ControlAudience.Operator,
        ControlPrincipalKind.Developer => ControlAudience.Developer,
        _ => ControlAudience.Test
    };
}

public abstract class ControlPublicationDescriptor
{
    protected ControlPublicationDescriptor(ControlCategoryPath category, string label, string description,
        ControlCapabilityId read, ControlAudience audience)
    {
        if (!category.IsValid || !read.IsValid || audience == 0 || (audience & ~ControlAudience.All) != 0)
            throw new ArgumentException("Invalid publication metadata.");
        Category = category; Label = ControlLimits.Bounded(label, ControlLimits.Label);
        Description = ControlLimits.Bounded(description, ControlLimits.Description);
        ReadCapability = read; Audience = audience;
    }
    public ControlCategoryPath Category { get; }
    public string Label { get; }
    public string Description { get; }
    public ControlCapabilityId ReadCapability { get; }
    public ControlAudience Audience { get; }
}
public sealed class ControlDescriptor : ControlPublicationDescriptor
{
    public ControlDescriptor(ControlId id, ControlCategoryPath category, string label, string description,
        ControlValueSchema schema, ControlValue defaultValue, ControlCapabilityId read, ControlCapabilityId write,
        ControlApplyMode apply = ControlApplyMode.Live, ControlPersistenceClass persistence = ControlPersistenceClass.None,
        ControlAudience audience = ControlAudience.All) : base(category, label, description, read, audience)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (!id.IsValid || !write.IsValid || !schema.Accepts(defaultValue) ||
            !Enum.IsDefined(apply) || !Enum.IsDefined(persistence)) throw new ArgumentException("Invalid control.");
        Id = id; Schema = schema; DefaultValue = defaultValue; WriteCapability = write; ApplyMode = apply;
        Persistence = persistence;
    }
    public ControlId Id { get; }
    public ControlValueSchema Schema { get; }
    public ControlValue DefaultValue { get; }
    public ControlCapabilityId WriteCapability { get; }
    public ControlApplyMode ApplyMode { get; }
    public ControlPersistenceClass Persistence { get; }
}
public sealed class DiagnosticDescriptor : ControlPublicationDescriptor
{
    public DiagnosticDescriptor(DiagnosticId id, ControlCategoryPath category, string label, string description,
        ControlValueSchema schema, ControlCapabilityId read, ControlAudience audience = ControlAudience.All)
        : base(category, label, description, read, audience)
    { if (!id.IsValid) throw new ArgumentException("Invalid diagnostic."); Id = id; Schema = schema ?? throw new ArgumentNullException(nameof(schema)); }
    public DiagnosticId Id { get; }
    public ControlValueSchema Schema { get; }
}
public sealed class ControlActionDescriptor : ControlPublicationDescriptor
{
    public ControlActionDescriptor(ControlActionId id, ControlCategoryPath category, string label, string description,
        IEnumerable<ControlValueSchema> arguments, ControlCapabilityId read, ControlCapabilityId invoke,
        ControlAudience audience = ControlAudience.All, string confirmationPrompt = "") : base(category, label, description, read, audience)
    {
        if (!id.IsValid || !invoke.IsValid) throw new ArgumentException("Invalid action.");
        Id = id; InvokeCapability = invoke;
        Arguments = Array.AsReadOnly(ControlLimits.Copy(arguments, ControlLimits.Arguments));
        ConfirmationPrompt = ControlLimits.Bounded(confirmationPrompt, ControlLimits.Description);
        if (Arguments.Any(s => s is null)) throw new ArgumentException("Null action schema.");
    }
    public ControlActionId Id { get; }
    public ControlCapabilityId InvokeCapability { get; }
    public ReadOnlyCollection<ControlValueSchema> Arguments { get; }
    /// <summary>Nonempty for actions which require a separate viewer confirmation gesture.</summary>
    public string ConfirmationPrompt { get; }
}
public sealed class ControlCollectionColumn
{
    public ControlCollectionColumn(ControlId id, string label, ControlValueSchema schema)
    { if (!id.IsValid) throw new ArgumentException("Invalid column."); Id = id;
      Label = ControlLimits.Bounded(label, ControlLimits.Label); Schema = schema ?? throw new ArgumentNullException(nameof(schema)); }
    public ControlId Id { get; }
    public string Label { get; }
    public ControlValueSchema Schema { get; }
}
public sealed class ControlCollectionDescriptor : ControlPublicationDescriptor
{
    public ControlCollectionDescriptor(ControlCollectionId id, ControlCategoryPath category, string label, string description,
        int maximumRows, IEnumerable<ControlCollectionColumn> columns, ControlCapabilityId read,
        ControlAudience audience = ControlAudience.All) : base(category, label, description, read, audience)
    {
        if (!id.IsValid || maximumRows is < 1 or > ControlLimits.Rows) throw new ArgumentException("Invalid collection.");
        Id = id; MaximumRows = maximumRows;
        Columns = Array.AsReadOnly(ControlLimits.Copy(columns, ControlLimits.Columns));
        if (Columns.Count == 0 || Columns.Any(c => c is null) || Columns.Select(c => c.Id).Distinct().Count() != Columns.Count)
            throw new ArgumentException("Invalid columns.");
    }
    public ControlCollectionId Id { get; }
    public int MaximumRows { get; }
    public ReadOnlyCollection<ControlCollectionColumn> Columns { get; }
}

/// <summary>Owner accepted desired state; pending is never an unsaved viewer draft.</summary>
public sealed record ControlState
{
    public ControlState(ControlValue requested, ControlValue effective, ControlValue? pending,
        ControlProvenance requestedProvenance, ControlProvenance effectiveProvenance,
        ControlProvenance? pendingProvenance, bool writable, ControlResultCode disabledCode = ControlResultCode.Accepted,
        string disabledReason = "")
    {
        if (!requested.IsValid || !effective.IsValid || requested.Kind != effective.Kind ||
            pending.HasValue != pendingProvenance.HasValue ||
            pending is { } p && (p.Kind != requested.Kind || p != requested))
            throw new ArgumentException("Invalid owner state.");
        Requested = requested; Effective = effective; Pending = pending;
        RequestedProvenance = requestedProvenance; EffectiveProvenance = effectiveProvenance;
        PendingProvenance = pendingProvenance; Writable = writable; DisabledCode = disabledCode;
        DisabledReason = ControlLimits.Bounded(disabledReason, ControlLimits.Description);
    }
    public ControlValue Requested { get; }
    public ControlValue Effective { get; }
    public ControlValue? Pending { get; }
    public ControlProvenance RequestedProvenance { get; }
    public ControlProvenance EffectiveProvenance { get; }
    public ControlProvenance? PendingProvenance { get; }
    public bool Writable { get; }
    public ControlResultCode DisabledCode { get; }
    public string DisabledReason { get; }
    public ulong ProviderGeneration { get; init; }
    public ulong Revision { get; init; }
}
public readonly record struct ControlMutation(ControlId Id, ControlValue Value);
public sealed record ControlCollectionRow
{
    public ControlCollectionRow(ControlRowId id, IEnumerable<ControlValue> values)
    { if (!id.IsValid) throw new ArgumentException("Invalid row."); Id = id;
      Values = Array.AsReadOnly(ControlLimits.Copy(values, ControlLimits.Columns)); }
    public ControlRowId Id { get; }
    public ReadOnlyCollection<ControlValue> Values { get; }
}
public sealed record ControlDiagnosticSnapshot(ulong ProviderGeneration, ulong Revision, ControlValue Value);
public sealed record ControlCollectionSnapshot(ulong ProviderGeneration, ulong Revision,
    ReadOnlyCollection<ControlCollectionRow> Rows);
public sealed record ControlOwnerResult
{
    public ControlOwnerResult(ControlResultStatus status, ControlResultCode code, string diagnostic = "")
    {
        if (!Enum.IsDefined(status) || !Enum.IsDefined(code) ||
            (status == ControlResultStatus.Accepted) != (code == ControlResultCode.Accepted))
            throw new ArgumentException("Invalid owner result.");
        Status = status; Code = code; Diagnostic = ControlLimits.Bounded(diagnostic, ControlLimits.Description);
    }
    public ControlResultStatus Status { get; }
    public ControlResultCode Code { get; }
    public string Diagnostic { get; }
    public static ControlOwnerResult Accepted { get; } = new(ControlResultStatus.Accepted, ControlResultCode.Accepted);
    public static ControlOwnerResult Reject(ControlResultCode code, string diagnostic = "") => new(ControlResultStatus.Rejected, code, diagnostic);
}
public sealed record ControlRequestContext(ControlRequestId RequestId, ControlPrincipal Principal,
    DateTimeOffset? Timestamp = null, ulong? SimulationTick = null);
public sealed record SetControlRequest(ControlRequestContext Context, ControlProviderId Provider,
    ControlId Control, ulong ExpectedProviderGeneration, ControlValue RequestedValue, ulong? ExpectedRevision = null);
public sealed class SetControlsRequest
{
    private SetControlsRequest(ControlRequestContext context, ControlProviderId provider, ulong generation,
        IEnumerable<ControlMutation> mutations, ulong? revision)
    { Context = context; Provider = provider; ExpectedProviderGeneration = generation;
      Mutations = Array.AsReadOnly(ControlLimits.Copy(mutations, ControlLimits.Batch)); ExpectedRevision = revision; }
    public ControlRequestContext Context { get; }
    public ControlProviderId Provider { get; }
    public ulong ExpectedProviderGeneration { get; }
    public ReadOnlyCollection<ControlMutation> Mutations { get; }
    public ulong? ExpectedRevision { get; }
    public static SetControlsRequest Create(ControlRequestContext context, ControlProviderId provider, ulong generation,
        IEnumerable<ControlMutation> mutations, ulong? revision = null) =>
        new(context, provider, generation, mutations, revision);
}
public sealed class ControlActionRequest
{
    private ControlActionRequest(ControlRequestContext context, ControlProviderId provider, ControlActionId action,
        ulong generation, IEnumerable<ControlValue> arguments, ulong? revision)
    { Context = context; Provider = provider; Action = action; ExpectedProviderGeneration = generation;
      Arguments = Array.AsReadOnly(ControlLimits.Copy(arguments, ControlLimits.Arguments)); ExpectedRevision = revision; }
    public ControlRequestContext Context { get; }
    public ControlProviderId Provider { get; }
    public ControlActionId Action { get; }
    public ulong ExpectedProviderGeneration { get; }
    public ReadOnlyCollection<ControlValue> Arguments { get; }
    public ulong? ExpectedRevision { get; }
    public static ControlActionRequest Create(ControlRequestContext context, ControlProviderId provider,
        ControlActionId action, ulong generation, IEnumerable<ControlValue> arguments, ulong? revision = null) =>
        new(context, provider, action, generation, arguments, revision);
}
public sealed record ControlResult(ControlRequestId RequestId, ControlOwnerResult Outcome,
    ulong ProviderGeneration, ulong Revision, ReadOnlyCollection<ControlState> States);
public sealed record ControlAuditObservation(ControlRequestContext Context, ControlProviderId Provider,
    ControlActionId? Action, ReadOnlyCollection<ControlMutation> Mutations, ReadOnlyCollection<ControlValue> Arguments,
    ControlResult Result);
/// <summary>Cold observation seam, not a database. Implementations must retain bounded/safe data.</summary>
public interface IControlAuditSink { void Observe(ControlAuditObservation observation); }

/// <summary>Owner-bound synchronous cold provider. Unexpected exceptions propagate, never become rejection.</summary>
public interface IControlProvider
{
    ControlProviderDefinition Definition { get; }
    ulong Revision { get; }
    ControlState Capture(ControlId id);
    ControlValue Capture(DiagnosticId id);
    IEnumerable<ControlCollectionRow> Capture(ControlCollectionId id);
    /// <summary>Validate the complete candidate semantically before mutation. Persistence/application
    /// remain owner-owned. Unexpected post-mutation failure must not be returned as Rejected.</summary>
    ControlOwnerResult Set(ControlRequestContext context, IReadOnlyList<ControlMutation> mutations);
    ControlOwnerResult Invoke(ControlRequestContext context, ControlActionId id, IReadOnlyList<ControlValue> arguments);
}

public sealed class ControlProviderDefinition
{
    public ControlProviderDefinition(ControlProviderId id, IEnumerable<ControlDescriptor>? controls = null,
        IEnumerable<DiagnosticDescriptor>? diagnostics = null, IEnumerable<ControlActionDescriptor>? actions = null,
        IEnumerable<ControlCollectionDescriptor>? collections = null)
    {
        if (!id.IsValid) throw new ArgumentException("Invalid provider."); Id = id;
        Controls = Array.AsReadOnly(ControlLimits.Copy(controls ?? [], ControlLimits.Publications));
        Diagnostics = Array.AsReadOnly(ControlLimits.Copy(diagnostics ?? [], ControlLimits.Publications));
        Actions = Array.AsReadOnly(ControlLimits.Copy(actions ?? [], ControlLimits.Publications));
        Collections = Array.AsReadOnly(ControlLimits.Copy(collections ?? [], ControlLimits.Publications));
        if (Controls.Count + Diagnostics.Count + Actions.Count + Collections.Count > ControlLimits.Publications ||
            Controls.Any(c => c is null) || Diagnostics.Any(d => d is null) ||
            Actions.Any(a => a is null) || Collections.Any(c => c is null) ||
            Controls.Select(c => c.Id).Distinct().Count() != Controls.Count ||
            Diagnostics.Select(c => c.Id).Distinct().Count() != Diagnostics.Count ||
            Actions.Select(c => c.Id).Distinct().Count() != Actions.Count ||
            Collections.Select(c => c.Id).Distinct().Count() != Collections.Count ||
            Controls.Select(c => c.Id.Value).Concat(Diagnostics.Select(c => c.Id.Value))
                .Concat(Actions.Select(c => c.Id.Value)).Concat(Collections.Select(c => c.Id.Value))
                .Distinct(StringComparer.Ordinal).Count() != Controls.Count + Diagnostics.Count + Actions.Count + Collections.Count)
            throw new ArgumentException("Duplicate/invalid publication set.");
    }
    public ControlProviderId Id { get; }
    public ReadOnlyCollection<ControlDescriptor> Controls { get; }
    public ReadOnlyCollection<DiagnosticDescriptor> Diagnostics { get; }
    public ReadOnlyCollection<ControlActionDescriptor> Actions { get; }
    public ReadOnlyCollection<ControlCollectionDescriptor> Collections { get; }
}

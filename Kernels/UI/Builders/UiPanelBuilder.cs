using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace UltraSim.UI;

public sealed class UiPanelBuilder
{
    private readonly string _serverIdentity;
    private readonly string _panelType;
    private readonly int _schemaRevision;
    private readonly int _protocolVersion;
    private readonly UiPanelMetadata _metadata;
    private readonly List<UiSchemaNodeDescriptor> _nodes = new();
    private readonly Dictionary<string, UiPayloadSlotDescriptor> _payloadSlots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, UiModelSlotValue> _modelValues = new(StringComparer.Ordinal);
    private readonly Dictionary<string, UiActionDescriptor> _actions = new(StringComparer.Ordinal);
    private readonly List<UiSelectOptionDescriptor> _selectOptions = new();
    private int _nextNodeId = 1;
    private int _rootNodeId;

    private UiPanelBuilder(
        string serverIdentity,
        string panelType,
        int schemaRevision,
        int protocolVersion,
        UiPanelMetadata metadata)
    {
        _serverIdentity = serverIdentity;
        _panelType = panelType;
        _schemaRevision = schemaRevision;
        _protocolVersion = protocolVersion;
        _metadata = metadata;
    }

    public static UiPanelBuilder Create(
        string serverIdentity,
        string panelType,
        int schemaRevision,
        UiPanelMetadata? metadata = null,
        int protocolVersion = 1)
    {
        metadata ??= new UiPanelMetadata(
            panelType,
            UiSurfaceTarget.Menu,
            UiFocusMode.Normal,
            ExclusiveGroup: null,
            ZOrderClass: 1,
            UiOpenPolicy.New,
            UiBackPolicy.CloseAndFocusParent,
            UiTooltipPolicy.Allow,
            BlocksGameplayInput: false,
            PersistenceKey: null);
        return new UiPanelBuilder(serverIdentity, panelType, schemaRevision, protocolVersion, metadata);
    }

    public UiPanelBuilder Panel(Action<UiPanelNodeBuilder> build)
    {
        if (_rootNodeId != 0)
        {
            throw new InvalidOperationException("A UI panel builder can only define one root panel.");
        }

        _rootNodeId = AddContainer(UiNodeKind.Panel, build);
        return this;
    }

    public bool TryBuild(out UiBuiltPanel? panel, out string error)
    {
        panel = null;
        error = string.Empty;
        if (_rootNodeId == 0)
        {
            error = "UI panel builder requires a root panel.";
            return false;
        }

        if (!string.Equals(_metadata.PanelType?.Trim(), _panelType?.Trim(), StringComparison.Ordinal))
        {
            error = "UI panel builder metadata panel type must match the schema panel type.";
            return false;
        }

        string schemaHash = ComputeSchemaHash();
        if (!UiSchemaIdentity.TryCreate(_serverIdentity, _panelType, _schemaRevision, schemaHash, _protocolVersion, out UiSchemaIdentity identity, out error))
        {
            return false;
        }

        var schema = new UiSchemaDocument(
            identity,
            _metadata,
            _rootNodeId,
            _nodes.ToArray(),
            _payloadSlots.Values.ToArray(),
            _actions.Values.ToArray(),
            StateBindings: [],
            CatalogIdentity: null,
            SelectOptions: _selectOptions.ToArray());
        UiValidationResult schemaResult = schema.Validate();
        if (!schemaResult.IsValid)
        {
            error = schemaResult.Error;
            return false;
        }

        var model = new UiModelDocument(identity, _modelValues.Values.ToArray());
        UiValidationResult modelResult = model.ValidateAgainst(schema);
        if (!modelResult.IsValid)
        {
            error = modelResult.Error;
            return false;
        }

        panel = new UiBuiltPanel(schema, model);
        return true;
    }

    public UiBuiltPanel Build()
    {
        if (!TryBuild(out UiBuiltPanel? panel, out string error))
        {
            throw new InvalidOperationException(error);
        }

        return panel!;
    }

    internal int AddContainer(UiNodeKind kind, Action<UiPanelNodeBuilder> build)
    {
        return AddContainer(kind, text: null, build);
    }

    internal int AddContainer(UiNodeKind kind, string? text, Action<UiPanelNodeBuilder> build)
    {
        return AddContainer(kind, slotId: null, text, build);
    }

    internal int AddContainer(UiNodeKind kind, string? slotId, string? text, Action<UiPanelNodeBuilder> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        int nodeId = _nextNodeId++;
        var childBuilder = new UiPanelNodeBuilder(this);
        build(childBuilder);
        _nodes.Add(new UiSchemaNodeDescriptor(nodeId, kind, slotId, text, childBuilder.ChildIds.ToArray()));
        return nodeId;
    }

    internal int AddLeaf(UiNodeKind kind, string? slotId, string? text, string? actionId)
    {
        return AddNode(kind, slotId, text, children: [], actionId);
    }

    internal void DeclarePayloadSlot(UiPayloadSlotDescriptor descriptor)
    {
        UiValidationResult result = descriptor.Validate();
        if (!result.IsValid)
        {
            throw new InvalidOperationException(result.Error);
        }

        if (_payloadSlots.TryGetValue(descriptor.SlotId, out UiPayloadSlotDescriptor? existing))
        {
            if (existing != descriptor)
            {
                throw new InvalidOperationException($"Payload slot '{descriptor.SlotId}' was declared with incompatible metadata.");
            }

            return;
        }

        _payloadSlots.Add(descriptor.SlotId, descriptor);
    }

    internal void SetModelValue(UiModelSlotValue value)
    {
        UiValidationResult result = value.Validate();
        if (!result.IsValid)
        {
            throw new InvalidOperationException(result.Error);
        }

        if (_modelValues.TryGetValue(value.SlotId, out UiModelSlotValue? existing) && existing != value)
        {
            throw new InvalidOperationException($"Model slot '{value.SlotId}' was assigned conflicting values.");
        }

        _modelValues[value.SlotId] = value;
    }

    internal void DeclareAction(UiActionDescriptor descriptor)
    {
        UiValidationResult result = descriptor.Validate();
        if (!result.IsValid)
        {
            throw new InvalidOperationException(result.Error);
        }

        if (_actions.TryGetValue(descriptor.ActionId, out UiActionDescriptor? existing))
        {
            if (existing != descriptor)
            {
                throw new InvalidOperationException($"Action '{descriptor.ActionId}' was declared with incompatible metadata.");
            }

            return;
        }

        _actions.Add(descriptor.ActionId, descriptor);
    }

    internal void DeclareSelectOptions(string slotId, IReadOnlyList<UiSelectOption> options)
    {
        if (options.Count == 0)
        {
            throw new InvalidOperationException($"Select slot '{slotId}' requires at least one option.");
        }

        var values = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < options.Count; i++)
        {
            UiSelectOption option = options[i] ?? throw new InvalidOperationException($"Select slot '{slotId}' cannot contain null options.");
            var descriptor = new UiSelectOptionDescriptor(
                slotId,
                option.Value,
                option.Label,
                option.Disabled,
                option.IconId);
            UiValidationResult result = descriptor.Validate(UiSchemaValidationLimits.Default);
            if (!result.IsValid)
            {
                throw new InvalidOperationException(result.Error);
            }

            if (!values.Add(option.Value))
            {
                throw new InvalidOperationException($"Select slot '{slotId}' contains duplicate option value '{option.Value}'.");
            }

            _selectOptions.Add(descriptor);
        }
    }

    private int AddNode(
        UiNodeKind kind,
        string? slotId,
        string? text,
        IReadOnlyList<int> children,
        string? actionId)
    {
        int nodeId = _nextNodeId++;
        _nodes.Add(new UiSchemaNodeDescriptor(nodeId, kind, slotId, text, children.ToArray(), ActionId: actionId));
        return nodeId;
    }

    private string ComputeSchemaHash()
    {
        var builder = new StringBuilder();
        builder.Append("server=").Append(_serverIdentity).Append('\n');
        builder.Append("panel=").Append(_panelType).Append('\n');
        builder.Append("revision=").Append(_schemaRevision.ToString(CultureInfo.InvariantCulture)).Append('\n');
        builder.Append("protocol=").Append(_protocolVersion.ToString(CultureInfo.InvariantCulture)).Append('\n');
        AppendMetadata(builder);
        for (int i = 0; i < _nodes.Count; i++)
        {
            UiSchemaNodeDescriptor node = _nodes[i];
            builder.Append("node|")
                .Append(node.NodeId.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(node.Kind).Append('|')
                .Append(node.SlotId).Append('|')
                .Append(node.Text).Append('|')
                .Append(node.ActionId).Append('|');
            for (int childIndex = 0; childIndex < node.Children.Count; childIndex++)
            {
                if (childIndex > 0)
                {
                    builder.Append(',');
                }

                builder.Append(node.Children[childIndex].ToString(CultureInfo.InvariantCulture));
            }

            builder.Append('\n');
        }

        foreach (UiPayloadSlotDescriptor slot in _payloadSlots.Values)
        {
            builder.Append("slot|")
                .Append(slot.SlotId).Append('|')
                .Append(slot.ValueKind).Append('|')
                .Append(slot.Required).Append('|')
                .Append(slot.MaxStringLength.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(slot.MinNumber?.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(slot.MaxNumber?.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        foreach (UiActionDescriptor action in _actions.Values)
        {
            builder.Append("action|")
                .Append(action.ActionId).Append('|')
                .Append(action.Label).Append('|')
                .Append(action.IconId).Append('|')
                .Append(action.Role).Append('|')
                .Append(action.ContextKind).Append('|')
                .Append(action.RequiresConfirm).Append('|')
                .Append(action.ChildMenuKey).Append('|')
                .Append(action.Availability).Append('|')
                .Append(action.AvailabilityReason).Append('|')
                .Append(action.MenuGroupKey).Append('\n');
        }

        foreach (UiSelectOptionDescriptor option in _selectOptions)
        {
            builder.Append("select|")
                .Append(option.SlotId).Append('|')
                .Append(option.Value).Append('|')
                .Append(option.Label).Append('|')
                .Append(option.Disabled).Append('|')
                .Append(option.IconId).Append('\n');
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private void AppendMetadata(StringBuilder builder)
    {
        builder.Append("metadata|")
            .Append(_metadata.PanelType).Append('|')
            .Append(_metadata.SurfaceTarget).Append('|')
            .Append(_metadata.FocusMode).Append('|')
            .Append(_metadata.ExclusiveGroup).Append('|')
            .Append(_metadata.ZOrderClass.ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(_metadata.OpenPolicy).Append('|')
            .Append(_metadata.BackPolicy).Append('|')
            .Append(_metadata.TooltipPolicy).Append('|')
            .Append(_metadata.BlocksGameplayInput).Append('|')
            .Append(_metadata.PersistenceKey).Append('\n');
    }
}

namespace UltraSim.UI;

public sealed record UiSchemaDocument(
    UiSchemaIdentity Identity,
    UiPanelMetadata Metadata,
    int RootNodeId,
    IReadOnlyList<UiSchemaNodeDescriptor> Nodes,
    IReadOnlyList<UiPayloadSlotDescriptor> PayloadSlots,
    IReadOnlyList<UiActionDescriptor> Actions,
    IReadOnlyList<UiStateBindingDescriptor> StateBindings,
    UiCatalogIdentity? CatalogIdentity = null,
    IReadOnlyList<UiSelectOptionDescriptor>? SelectOptions = null)
{
    public UiValidationResult Validate(
        UiSchemaValidationLimits? limits = null,
        UiRendererCapabilities? rendererCapabilities = null)
    {
        UiSchemaValidationLimits actualLimits = limits ?? UiSchemaValidationLimits.Default;
        UiValidationResult limitResult = actualLimits.Validate();
        if (!limitResult.IsValid)
        {
            return limitResult;
        }

        if (RootNodeId <= 0)
        {
            return UiValidationResult.Invalid("Root node id must be positive.");
        }

        UiValidationResult metadataResult = Metadata.Validate();
        if (!metadataResult.IsValid)
        {
            return metadataResult;
        }

        if (Identity.ProtocolVersion <= 0)
        {
            return UiValidationResult.Invalid("Schema identity protocol version must be positive.");
        }

        if (Nodes.Count == 0)
        {
            return UiValidationResult.Invalid("Schema must contain at least one node.");
        }

        if (Nodes.Count > actualLimits.MaxNodeCount)
        {
            return UiValidationResult.Invalid($"Schema cannot contain more than {actualLimits.MaxNodeCount} nodes.");
        }

        if (Actions.Count > actualLimits.MaxActions)
        {
            return UiValidationResult.Invalid($"Schema cannot contain more than {actualLimits.MaxActions} actions.");
        }

        if (StateBindings.Count > actualLimits.MaxStateBindings)
        {
            return UiValidationResult.Invalid($"Schema cannot contain more than {actualLimits.MaxStateBindings} state bindings.");
        }

        IReadOnlyList<UiSelectOptionDescriptor> actualSelectOptions = SelectOptions ?? [];
        if (actualSelectOptions.Count > actualLimits.MaxSelectOptions)
        {
            return UiValidationResult.Invalid($"Schema cannot contain more than {actualLimits.MaxSelectOptions} select options.");
        }

        UiValidationResult payloadSlotResult = ValidatePayloadSlots();
        if (!payloadSlotResult.IsValid)
        {
            return payloadSlotResult;
        }

        UiValidationResult rendererResult = rendererCapabilities?.Validate() ?? UiValidationResult.Valid;
        if (!rendererResult.IsValid)
        {
            return rendererResult;
        }

        var nodeById = new Dictionary<int, UiSchemaNodeDescriptor>(Nodes.Count);
        UiCapabilityFlags requiredCapabilities = UiCapabilityFlags.None;
        for (int i = 0; i < Nodes.Count; i++)
        {
            UiSchemaNodeDescriptor node = Nodes[i];
            UiValidationResult nodeResult = node.Validate(actualLimits);
            if (!nodeResult.IsValid)
            {
                return nodeResult;
            }

            if (!nodeById.TryAdd(node.NodeId, node))
            {
                return UiValidationResult.Invalid($"Duplicate node id {node.NodeId}.");
            }

            requiredCapabilities |= node.RequiredCapabilities;
        }

        if (!nodeById.TryGetValue(RootNodeId, out UiSchemaNodeDescriptor? root))
        {
            return UiValidationResult.Invalid($"Root node id {RootNodeId} does not exist.");
        }

        if (root.Kind != UiNodeKind.Panel)
        {
            return UiValidationResult.Invalid("Root node must be a Panel.");
        }

        UiValidationResult capabilityResult = ValidateRequiredCapabilities(requiredCapabilities, rendererCapabilities);
        if (!capabilityResult.IsValid)
        {
            return capabilityResult;
        }

        UiValidationResult graphResult = ValidateGraph(RootNodeId, nodeById, actualLimits);
        if (!graphResult.IsValid)
        {
            return graphResult;
        }

        UiValidationResult actionResult = ValidateActions();
        if (!actionResult.IsValid)
        {
            return actionResult;
        }

        UiValidationResult bindingResult = ValidateStateBindings();
        if (!bindingResult.IsValid)
        {
            return bindingResult;
        }

        UiValidationResult selectOptionResult = ValidateSelectOptions(actualSelectOptions, actualLimits);
        if (!selectOptionResult.IsValid)
        {
            return selectOptionResult;
        }

        UiValidationResult repeaterResult = ValidateRepeaterNodes();
        if (!repeaterResult.IsValid)
        {
            return repeaterResult;
        }

        UiValidationResult slotReferenceResult = ValidateNodeSlotReferences();
        if (!slotReferenceResult.IsValid)
        {
            return slotReferenceResult;
        }

        UiValidationResult actionReferenceResult = ValidateNodeActionReferences();
        if (!actionReferenceResult.IsValid)
        {
            return actionReferenceResult;
        }

        return UiValidationResult.Valid;
    }

    public IReadOnlySet<string> GetRepeaterSlotIds()
    {
        var repeaterSlots = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < Nodes.Count; i++)
        {
            UiSchemaNodeDescriptor node = Nodes[i];
            if (node.Kind == UiNodeKind.Repeater && !string.IsNullOrWhiteSpace(node.SlotId))
            {
                repeaterSlots.Add(node.SlotId);
            }
        }

        return repeaterSlots;
    }

    public IReadOnlyList<UiSelectOptionDescriptor> GetSelectOptions()
    {
        return SelectOptions ?? [];
    }

    private UiValidationResult ValidateRequiredCapabilities(
        UiCapabilityFlags requiredCapabilities,
        UiRendererCapabilities? rendererCapabilities)
    {
        if ((requiredCapabilities & ~UiCapabilitySet.AllKnown) != 0)
        {
            return UiValidationResult.Invalid("Schema requires unknown capabilities.");
        }

        if (rendererCapabilities is null)
        {
            return UiValidationResult.Valid;
        }

        UiCapabilityFlags missing = requiredCapabilities & ~rendererCapabilities.CapabilityFlags;
        if (missing != UiCapabilityFlags.None)
        {
            return UiValidationResult.Invalid($"Renderer is missing required UI capabilities: {missing}.");
        }

        return UiValidationResult.Valid;
    }

    private UiValidationResult ValidateGraph(
        int rootNodeId,
        Dictionary<int, UiSchemaNodeDescriptor> nodeById,
        UiSchemaValidationLimits limits)
    {
        var visiting = new HashSet<int>();
        var visited = new HashSet<int>();
        UiValidationResult walkResult = Visit(rootNodeId, depth: 1);
        if (!walkResult.IsValid)
        {
            return walkResult;
        }

        if (visited.Count != nodeById.Count)
        {
            return UiValidationResult.Invalid("Schema contains nodes that are not reachable from the root.");
        }

        return UiValidationResult.Valid;

        UiValidationResult Visit(int nodeId, int depth)
        {
            if (depth > limits.MaxTreeDepth)
            {
                return UiValidationResult.Invalid($"Schema tree depth cannot exceed {limits.MaxTreeDepth}.");
            }

            if (!nodeById.TryGetValue(nodeId, out UiSchemaNodeDescriptor? node))
            {
                return UiValidationResult.Invalid($"Child node id {nodeId} does not exist.");
            }

            if (visited.Contains(nodeId))
            {
                return UiValidationResult.Valid;
            }

            if (!visiting.Add(nodeId))
            {
                return UiValidationResult.Invalid($"Schema node graph contains a cycle at node {nodeId}.");
            }

            for (int i = 0; i < node.Children.Count; i++)
            {
                UiValidationResult childResult = Visit(node.Children[i], depth + 1);
                if (!childResult.IsValid)
                {
                    return childResult;
                }
            }

            visiting.Remove(nodeId);
            visited.Add(nodeId);
            return UiValidationResult.Valid;
        }
    }

    private UiValidationResult ValidateActions()
    {
        var actionIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < Actions.Count; i++)
        {
            UiActionDescriptor action = Actions[i];
            UiValidationResult result = action.Validate();
            if (!result.IsValid)
            {
                return result;
            }

            if (!actionIds.Add(action.ActionId))
            {
                return UiValidationResult.Invalid($"Duplicate action id '{action.ActionId}'.");
            }
        }

        return UiValidationResult.Valid;
    }

    private UiValidationResult ValidateStateBindings()
    {
        var slotIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < StateBindings.Count; i++)
        {
            UiStateBindingDescriptor binding = StateBindings[i];
            UiValidationResult result = binding.Validate();
            if (!result.IsValid)
            {
                return result;
            }

            if (!slotIds.Add(binding.SlotId))
            {
                return UiValidationResult.Invalid($"Duplicate state binding slot id '{binding.SlotId}'.");
            }
        }

        return UiValidationResult.Valid;
    }

    private UiValidationResult ValidatePayloadSlots()
    {
        var slotIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < PayloadSlots.Count; i++)
        {
            UiPayloadSlotDescriptor slot = PayloadSlots[i];
            UiValidationResult result = slot.Validate();
            if (!result.IsValid)
            {
                return result;
            }

            if (!slotIds.Add(slot.SlotId))
            {
                return UiValidationResult.Invalid($"Duplicate payload slot id '{slot.SlotId}'.");
            }
        }

        return UiValidationResult.Valid;
    }

    private UiValidationResult ValidateSelectOptions(
        IReadOnlyList<UiSelectOptionDescriptor> actualSelectOptions,
        UiSchemaValidationLimits limits)
    {
        var payloadSlotById = new Dictionary<string, UiPayloadSlotDescriptor>(PayloadSlots.Count, StringComparer.Ordinal);
        for (int i = 0; i < PayloadSlots.Count; i++)
        {
            payloadSlotById[PayloadSlots[i].SlotId] = PayloadSlots[i];
        }

        var optionNodeSlots = new HashSet<string>(StringComparer.Ordinal);
        var tabChildCountsBySlot = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < Nodes.Count; i++)
        {
            UiSchemaNodeDescriptor node = Nodes[i];
            if (node.Kind is not (UiNodeKind.Select or UiNodeKind.RadioGroup or UiNodeKind.CheckboxGroup or UiNodeKind.Tabs))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(node.SlotId))
            {
                return UiValidationResult.Invalid($"{node.Kind} nodes require a slot id.");
            }

            if (!payloadSlotById.TryGetValue(node.SlotId, out UiPayloadSlotDescriptor? slot))
            {
                return UiValidationResult.Invalid($"{node.Kind} node slot '{node.SlotId}' is not declared as a payload slot.");
            }

            UiModelValueKind expectedKind = node.Kind == UiNodeKind.CheckboxGroup
                ? UiModelValueKind.StringList
                : UiModelValueKind.String;
            if (slot.ValueKind != expectedKind)
            {
                return UiValidationResult.Invalid($"{node.Kind} node slot '{node.SlotId}' must be a {expectedKind} payload slot.");
            }

            if (node.Kind == UiNodeKind.Tabs)
            {
                if (node.Children.Count == 0)
                {
                    return UiValidationResult.Invalid($"Tabs node slot '{node.SlotId}' requires at least one tab page.");
                }

                tabChildCountsBySlot[node.SlotId] = node.Children.Count;
            }

            optionNodeSlots.Add(node.SlotId);
        }

        var optionValuesBySlot = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        for (int i = 0; i < actualSelectOptions.Count; i++)
        {
            UiSelectOptionDescriptor option = actualSelectOptions[i];
            UiValidationResult result = option.Validate(limits);
            if (!result.IsValid)
            {
                return result;
            }

            if (!optionNodeSlots.Contains(option.SlotId))
            {
                return UiValidationResult.Invalid($"Select option slot '{option.SlotId}' does not reference an option node.");
            }

            if (!optionValuesBySlot.TryGetValue(option.SlotId, out HashSet<string>? values))
            {
                values = new HashSet<string>(StringComparer.Ordinal);
                optionValuesBySlot.Add(option.SlotId, values);
            }

            if (!values.Add(option.Value))
            {
                return UiValidationResult.Invalid($"Select slot '{option.SlotId}' contains duplicate option value '{option.Value}'.");
            }
        }

        foreach (string slotId in optionNodeSlots)
        {
            if (!optionValuesBySlot.TryGetValue(slotId, out HashSet<string>? optionValues))
            {
                return UiValidationResult.Invalid($"Option node slot '{slotId}' requires at least one option.");
            }

            if (tabChildCountsBySlot.TryGetValue(slotId, out int tabPageCount) && optionValues.Count != tabPageCount)
            {
                return UiValidationResult.Invalid($"Tabs node slot '{slotId}' must have exactly one option per tab page.");
            }
        }

        return UiValidationResult.Valid;
    }

    private UiValidationResult ValidateRepeaterNodes()
    {
        var repeaterSlots = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < Nodes.Count; i++)
        {
            UiSchemaNodeDescriptor node = Nodes[i];
            if (node.Kind != UiNodeKind.Repeater)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(node.SlotId))
            {
                return UiValidationResult.Invalid("Repeater nodes require a slot id.");
            }

            if (!repeaterSlots.Add(node.SlotId))
            {
                return UiValidationResult.Invalid($"Duplicate repeater slot id '{node.SlotId}'.");
            }
        }

        return UiValidationResult.Valid;
    }

    private UiValidationResult ValidateNodeSlotReferences()
    {
        var payloadSlots = new HashSet<string>(PayloadSlots.Select(static slot => slot.SlotId), StringComparer.Ordinal);
        var stateBindingSlots = new HashSet<string>(StateBindings.Select(static binding => binding.SlotId), StringComparer.Ordinal);

        for (int i = 0; i < Nodes.Count; i++)
        {
            string? slotId = Nodes[i].SlotId;
            if (string.IsNullOrWhiteSpace(slotId))
            {
                continue;
            }

            if (Nodes[i].Kind == UiNodeKind.Repeater)
            {
                continue;
            }

            if (payloadSlots.Contains(slotId) || stateBindingSlots.Contains(slotId))
            {
                continue;
            }

            return UiValidationResult.Invalid($"Node slot '{slotId}' is not declared as a payload slot or state binding.");
        }

        return UiValidationResult.Valid;
    }

    private UiValidationResult ValidateNodeActionReferences()
    {
        var actionsById = new Dictionary<string, UiActionDescriptor>(Actions.Count, StringComparer.Ordinal);
        for (int i = 0; i < Actions.Count; i++)
        {
            actionsById[Actions[i].ActionId] = Actions[i];
        }

        for (int i = 0; i < Nodes.Count; i++)
        {
            UiSchemaNodeDescriptor node = Nodes[i];
            if (string.IsNullOrWhiteSpace(node.ActionId))
            {
                continue;
            }

            if (node.Kind is not (UiNodeKind.Button or UiNodeKind.IconButton or UiNodeKind.Repeater))
            {
                return UiValidationResult.Invalid($"Node kind '{node.Kind}' cannot declare an action id.");
            }

            if (!actionsById.TryGetValue(node.ActionId, out UiActionDescriptor? action))
            {
                return UiValidationResult.Invalid($"Node action '{node.ActionId}' is not declared in schema actions.");
            }

            if (node.Kind == UiNodeKind.Repeater && action.ContextKind != UiActionContextKind.Row)
            {
                return UiValidationResult.Invalid($"Repeater node action '{node.ActionId}' must use row context.");
            }
        }

        return UiValidationResult.Valid;
    }
}

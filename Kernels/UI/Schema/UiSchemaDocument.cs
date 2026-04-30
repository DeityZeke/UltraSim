namespace UltraSim.UI;

public sealed record UiSchemaDocument(
    UiSchemaIdentity Identity,
    UiPanelMetadata Metadata,
    int RootNodeId,
    IReadOnlyList<UiSchemaNodeDescriptor> Nodes,
    IReadOnlyList<UiPayloadSlotDescriptor> PayloadSlots,
    IReadOnlyList<UiActionDescriptor> Actions,
    IReadOnlyList<UiStateBindingDescriptor> StateBindings,
    UiCatalogIdentity? CatalogIdentity = null)
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
        var actionIds = new HashSet<string>(Actions.Select(static action => action.ActionId), StringComparer.Ordinal);

        for (int i = 0; i < Nodes.Count; i++)
        {
            UiSchemaNodeDescriptor node = Nodes[i];
            if (string.IsNullOrWhiteSpace(node.ActionId))
            {
                continue;
            }

            if (node.Kind is not (UiNodeKind.Button or UiNodeKind.IconButton))
            {
                return UiValidationResult.Invalid($"Node kind '{node.Kind}' cannot declare an action id.");
            }

            if (!actionIds.Contains(node.ActionId))
            {
                return UiValidationResult.Invalid($"Node action '{node.ActionId}' is not declared in schema actions.");
            }
        }

        return UiValidationResult.Valid;
    }
}

namespace UltraSim.UI;

public sealed class UiPanelNodeBuilder
{
    private readonly UiPanelBuilder _owner;
    private readonly List<int> _childIds = new();

    internal UiPanelNodeBuilder(UiPanelBuilder owner)
    {
        _owner = owner;
    }

    internal IReadOnlyList<int> ChildIds => _childIds;

    internal void AddExistingNode(int nodeId)
    {
        _childIds.Add(nodeId);
    }

    public UiPanelNodeBuilder Row(Action<UiPanelNodeBuilder> build)
    {
        _childIds.Add(_owner.AddContainer(UiNodeKind.Row, build));
        return this;
    }

    public UiPanelNodeBuilder Column(Action<UiPanelNodeBuilder> build)
    {
        _childIds.Add(_owner.AddContainer(UiNodeKind.Column, build));
        return this;
    }

    public UiPanelNodeBuilder Group(string? title, Action<UiPanelNodeBuilder> build)
    {
        _childIds.Add(_owner.AddContainer(UiNodeKind.Group, title, build));
        return this;
    }

    public UiPanelNodeBuilder Section(string title, Action<UiPanelNodeBuilder> build)
    {
        _childIds.Add(_owner.AddContainer(UiNodeKind.Section, title, build));
        return this;
    }

    public UiPanelNodeBuilder ScrollRegion(Action<UiPanelNodeBuilder> build)
    {
        _childIds.Add(_owner.AddContainer(UiNodeKind.ScrollRegion, build));
        return this;
    }

    public UiPanelNodeBuilder Tabs(
        string slotId,
        string selectedTabId,
        Action<UiTabsBuilder> build,
        int maxStringLength = UiValidation.MaxIdentityLength,
        bool required = true)
    {
        ArgumentNullException.ThrowIfNull(build);
        _owner.DeclarePayloadSlot(new UiPayloadSlotDescriptor(slotId, UiModelValueKind.String, required, maxStringLength));
        _owner.SetModelValue(UiModelSlotValue.String(slotId, selectedTabId));
        var tabsBuilder = new UiTabsBuilder(_owner);
        build(tabsBuilder);
        if (tabsBuilder.Tabs.Count == 0)
        {
            throw new InvalidOperationException($"Tabs slot '{slotId}' requires at least one tab.");
        }

        _owner.DeclareSelectOptions(slotId, tabsBuilder.Tabs);
        _childIds.Add(_owner.AddContainer(UiNodeKind.Tabs, slotId, text: null, childBuilder =>
        {
            for (int i = 0; i < tabsBuilder.PageNodeIds.Count; i++)
            {
                childBuilder.AddExistingNode(tabsBuilder.PageNodeIds[i]);
            }
        }));
        return this;
    }

    public UiPanelNodeBuilder Pager(
        string currentPageSlotId,
        long currentPage,
        string totalPagesSlotId,
        long totalPages,
        string previousActionId,
        string nextActionId,
        string previousLabel = "Previous",
        string nextLabel = "Next",
        string currentPageLabel = "Page",
        string totalPagesLabel = "Total Pages")
    {
        _childIds.Add(_owner.AddContainer(UiNodeKind.Pager, pager =>
        {
            pager.Button(previousActionId, previousLabel);
            pager.DynamicText(currentPageSlotId, currentPageLabel, currentPage, minNumber: 1);
            pager.Text("/");
            pager.DynamicText(totalPagesSlotId, totalPagesLabel, totalPages, minNumber: 1);
            pager.Button(nextActionId, nextLabel);
        }));
        return this;
    }

    public UiPanelNodeBuilder Text(string text)
    {
        _childIds.Add(_owner.AddLeaf(UiNodeKind.Text, slotId: null, text, actionId: null));
        return this;
    }

    public UiPanelNodeBuilder Image(string imageId)
    {
        _childIds.Add(_owner.AddLeaf(UiNodeKind.Image, slotId: null, imageId, actionId: null));
        return this;
    }

    public UiPanelNodeBuilder Separator()
    {
        _childIds.Add(_owner.AddLeaf(UiNodeKind.Separator, slotId: null, text: null, actionId: null));
        return this;
    }

    public UiPanelNodeBuilder Spacer()
    {
        _childIds.Add(_owner.AddLeaf(UiNodeKind.Spacer, slotId: null, text: null, actionId: null));
        return this;
    }

    public UiPanelNodeBuilder DynamicText(
        string slotId,
        string label,
        string value,
        int maxStringLength = UiValidation.MaxDisplayTextLength,
        bool required = true)
    {
        _owner.DeclarePayloadSlot(new UiPayloadSlotDescriptor(slotId, UiModelValueKind.String, required, maxStringLength));
        _owner.SetModelValue(UiModelSlotValue.String(slotId, value));
        _childIds.Add(_owner.AddLeaf(UiNodeKind.DynamicText, slotId, label, actionId: null));
        return this;
    }

    public UiPanelNodeBuilder DynamicText(
        string slotId,
        string label,
        long value,
        double? minNumber = null,
        double? maxNumber = null,
        bool required = true)
    {
        _owner.DeclarePayloadSlot(new UiPayloadSlotDescriptor(slotId, UiModelValueKind.Integer, required, MinNumber: minNumber, MaxNumber: maxNumber));
        _owner.SetModelValue(UiModelSlotValue.Integer(slotId, value));
        _childIds.Add(_owner.AddLeaf(UiNodeKind.DynamicText, slotId, label, actionId: null));
        return this;
    }

    public UiPanelNodeBuilder DynamicNumber(
        string slotId,
        string label,
        double value,
        double? minNumber = null,
        double? maxNumber = null,
        bool required = true)
    {
        _owner.DeclarePayloadSlot(new UiPayloadSlotDescriptor(slotId, UiModelValueKind.Number, required, MinNumber: minNumber, MaxNumber: maxNumber));
        _owner.SetModelValue(UiModelSlotValue.Number(slotId, value));
        _childIds.Add(_owner.AddLeaf(UiNodeKind.DynamicText, slotId, label, actionId: null));
        return this;
    }

    public UiPanelNodeBuilder DynamicBoolean(string slotId, string label, bool value, bool required = true)
    {
        _owner.DeclarePayloadSlot(new UiPayloadSlotDescriptor(slotId, UiModelValueKind.Boolean, required));
        _owner.SetModelValue(UiModelSlotValue.Boolean(slotId, value));
        _childIds.Add(_owner.AddLeaf(UiNodeKind.DynamicText, slotId, label, actionId: null));
        return this;
    }

    public UiPanelNodeBuilder Toggle(string slotId, string label, bool value, bool required = true)
    {
        _owner.DeclarePayloadSlot(new UiPayloadSlotDescriptor(slotId, UiModelValueKind.Boolean, required));
        _owner.SetModelValue(UiModelSlotValue.Boolean(slotId, value));
        _childIds.Add(_owner.AddLeaf(UiNodeKind.Toggle, slotId, label, actionId: null));
        return this;
    }

    public UiPanelNodeBuilder TextInput(
        string slotId,
        string label,
        string value,
        int maxStringLength = UiValidation.MaxDisplayTextLength,
        bool required = true)
    {
        _owner.DeclarePayloadSlot(new UiPayloadSlotDescriptor(slotId, UiModelValueKind.String, required, maxStringLength));
        _owner.SetModelValue(UiModelSlotValue.String(slotId, value));
        _childIds.Add(_owner.AddLeaf(UiNodeKind.TextInput, slotId, label, actionId: null));
        return this;
    }

    public UiPanelNodeBuilder NumberInput(
        string slotId,
        string label,
        double value,
        double? minNumber = null,
        double? maxNumber = null,
        bool required = true)
    {
        _owner.DeclarePayloadSlot(new UiPayloadSlotDescriptor(slotId, UiModelValueKind.Number, required, MinNumber: minNumber, MaxNumber: maxNumber));
        _owner.SetModelValue(UiModelSlotValue.Number(slotId, value));
        _childIds.Add(_owner.AddLeaf(UiNodeKind.NumberInput, slotId, label, actionId: null));
        return this;
    }

    public UiPanelNodeBuilder Slider(
        string slotId,
        string label,
        double value,
        double? minNumber = null,
        double? maxNumber = null,
        bool required = true)
    {
        _owner.DeclarePayloadSlot(new UiPayloadSlotDescriptor(slotId, UiModelValueKind.Number, required, MinNumber: minNumber, MaxNumber: maxNumber));
        _owner.SetModelValue(UiModelSlotValue.Number(slotId, value));
        _childIds.Add(_owner.AddLeaf(UiNodeKind.Slider, slotId, label, actionId: null));
        return this;
    }

    public UiPanelNodeBuilder Stepper(
        string slotId,
        string label,
        long value,
        double? minNumber = null,
        double? maxNumber = null,
        bool required = true)
    {
        _owner.DeclarePayloadSlot(new UiPayloadSlotDescriptor(slotId, UiModelValueKind.Integer, required, MinNumber: minNumber, MaxNumber: maxNumber));
        _owner.SetModelValue(UiModelSlotValue.Integer(slotId, value));
        _childIds.Add(_owner.AddLeaf(UiNodeKind.Stepper, slotId, label, actionId: null));
        return this;
    }

    public UiPanelNodeBuilder Select(
        string slotId,
        string label,
        string value,
        IReadOnlyList<UiSelectOption> options,
        int maxStringLength = UiValidation.MaxIdentityLength,
        bool required = true)
    {
        _owner.DeclarePayloadSlot(new UiPayloadSlotDescriptor(slotId, UiModelValueKind.String, required, maxStringLength));
        _owner.SetModelValue(UiModelSlotValue.String(slotId, value));
        _owner.DeclareSelectOptions(slotId, options);
        _childIds.Add(_owner.AddLeaf(UiNodeKind.Select, slotId, label, actionId: null));
        return this;
    }

    public UiPanelNodeBuilder RadioGroup(
        string slotId,
        string label,
        string value,
        IReadOnlyList<UiSelectOption> options,
        int maxStringLength = UiValidation.MaxIdentityLength,
        bool required = true)
    {
        _owner.DeclarePayloadSlot(new UiPayloadSlotDescriptor(slotId, UiModelValueKind.String, required, maxStringLength));
        _owner.SetModelValue(UiModelSlotValue.String(slotId, value));
        _owner.DeclareSelectOptions(slotId, options);
        _childIds.Add(_owner.AddLeaf(UiNodeKind.RadioGroup, slotId, label, actionId: null));
        return this;
    }

    public UiPanelNodeBuilder CheckboxGroup(
        string slotId,
        string label,
        IReadOnlyList<string> values,
        IReadOnlyList<UiSelectOption> options,
        int maxStringLength = UiValidation.MaxIdentityLength,
        bool required = true)
    {
        _owner.DeclarePayloadSlot(new UiPayloadSlotDescriptor(slotId, UiModelValueKind.StringList, required, maxStringLength));
        _owner.SetModelValue(UiModelSlotValue.StringList(slotId, values));
        _owner.DeclareSelectOptions(slotId, options);
        _childIds.Add(_owner.AddLeaf(UiNodeKind.CheckboxGroup, slotId, label, actionId: null));
        return this;
    }

    public UiPanelNodeBuilder Repeater(
        string slotId,
        string label,
        string? rowActionId = null,
        string rowActionLabel = "Select",
        UiActionRole rowActionRole = UiActionRole.Normal,
        bool rowActionRequiresConfirm = false,
        string? rowActionIconId = null,
        UiActionAvailability rowActionAvailability = UiActionAvailability.Available,
        string? rowActionAvailabilityReason = null,
        string? rowActionMenuGroupKey = null)
    {
        if (!string.IsNullOrWhiteSpace(rowActionId))
        {
            _owner.DeclareAction(new UiActionDescriptor(
                rowActionId,
                rowActionLabel,
                rowActionIconId,
                rowActionRole,
                UiActionContextKind.Row,
                rowActionRequiresConfirm,
                ChildMenuKey: null,
                rowActionAvailability,
                rowActionAvailabilityReason,
                rowActionMenuGroupKey));
        }

        _childIds.Add(_owner.AddLeaf(UiNodeKind.Repeater, slotId, label, rowActionId));
        return this;
    }

    public UiPanelNodeBuilder Button(
        string actionId,
        string label,
        UiActionRole role = UiActionRole.Normal,
        UiActionContextKind contextKind = UiActionContextKind.Panel,
        bool requiresConfirm = false,
        string? iconId = null,
        string? childMenuKey = null,
        UiActionAvailability availability = UiActionAvailability.Available,
        string? availabilityReason = null,
        string? menuGroupKey = null)
    {
        _owner.DeclareAction(new UiActionDescriptor(
            actionId,
            label,
            iconId,
            role,
            contextKind,
            requiresConfirm,
            childMenuKey,
            availability,
            availabilityReason,
            menuGroupKey));
        _childIds.Add(_owner.AddLeaf(UiNodeKind.Button, slotId: null, label, actionId));
        return this;
    }

    public UiPanelNodeBuilder IconButton(
        string actionId,
        string label,
        string iconId,
        UiActionRole role = UiActionRole.Normal,
        UiActionContextKind contextKind = UiActionContextKind.Panel,
        bool requiresConfirm = false,
        string? childMenuKey = null,
        UiActionAvailability availability = UiActionAvailability.Available,
        string? availabilityReason = null,
        string? menuGroupKey = null)
    {
        _owner.DeclareAction(new UiActionDescriptor(
            actionId,
            label,
            iconId,
            role,
            contextKind,
            requiresConfirm,
            childMenuKey,
            availability,
            availabilityReason,
            menuGroupKey));
        _childIds.Add(_owner.AddLeaf(UiNodeKind.IconButton, slotId: null, label, actionId));
        return this;
    }
}

public sealed record UiSelectOption(
    string Value,
    string Label,
    bool Disabled = false,
    string? IconId = null);

public sealed class UiTabsBuilder
{
    private readonly UiPanelBuilder _owner;
    private readonly List<UiSelectOption> _tabs = new();
    private readonly List<int> _pageNodeIds = new();

    internal UiTabsBuilder(UiPanelBuilder owner)
    {
        _owner = owner;
    }

    internal IReadOnlyList<UiSelectOption> Tabs => _tabs;

    internal IReadOnlyList<int> PageNodeIds => _pageNodeIds;

    public UiTabsBuilder Page(string tabId, string label, Action<UiPanelNodeBuilder> build, bool disabled = false, string? iconId = null)
    {
        _tabs.Add(new UiSelectOption(tabId, label, disabled, iconId));
        _pageNodeIds.Add(_owner.AddContainer(UiNodeKind.Section, label, build));
        return this;
    }
}

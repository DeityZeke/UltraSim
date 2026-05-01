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

    public UiPanelNodeBuilder Text(string text)
    {
        _childIds.Add(_owner.AddLeaf(UiNodeKind.Text, slotId: null, text, actionId: null));
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
}

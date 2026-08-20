using Godot;
using UltraSim.UI;

namespace UltraSim.Client.Godot.UI;

internal static class ActionMenuRenderer
{
    public static bool TryBuild(
        UiActionMenuDescriptor menu,
        Action<UiActionDescriptor> actionPressed,
        out Control root,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(actionPressed);

        UiValidationResult validation = menu.Validate(CreateRendererCapabilities());
        if (!validation.IsValid)
        {
            root = new Control();
            error = validation.Error;
            return false;
        }

        var panel = new PanelContainer
        {
            Name = MakeNodeName(menu.PresentationKind.ToString(), menu.MenuKey),
            MouseFilter = Control.MouseFilterEnum.Stop,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
            CustomMinimumSize = menu.PresentationKind == UiActionMenuPresentationKind.RadialMenu
                ? new Vector2(220.0f, 0.0f)
                : new Vector2(180.0f, 0.0f),
        };
        panel.SetMeta("UiActionMenuKey", menu.MenuKey);
        if (!string.IsNullOrWhiteSpace(menu.ContextId))
        {
            panel.SetMeta("UiContextId", menu.ContextId);
        }

        Control host = menu.PresentationKind == UiActionMenuPresentationKind.RadialMenu
            ? new GridContainer
            {
                Name = "RadialActions",
                Columns = Math.Min(3, Math.Max(1, menu.Actions.Count)),
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
                SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
            }
            : new VBoxContainer
            {
                Name = "ContextActions",
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
                SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
            };

        panel.AddChild(host);
        for (int i = 0; i < menu.Actions.Count; i++)
        {
            UiActionDescriptor action = menu.Actions[i];
            var button = new Button
            {
                Name = MakeNodeName("Action", action.ActionId),
                Text = action.Label,
                Disabled = action.Availability != UiActionAvailability.Available,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            };
            button.SetMeta("UiActionId", action.ActionId);
            if (!string.IsNullOrWhiteSpace(action.MenuGroupKey))
            {
                button.SetMeta("UiMenuGroupKey", action.MenuGroupKey);
            }

            button.Pressed += () => actionPressed(action);
            host.AddChild(button);
        }

        root = panel;
        error = string.Empty;
        return true;
    }

    private static UiRendererCapabilities CreateRendererCapabilities()
    {
        return new UiRendererCapabilities(
            ProtocolVersion: 1,
            CapabilityFlags: UiCapabilityFlags.ContextMenusV1 | UiCapabilityFlags.RadialMenusV1,
            MaxSchemaBytes: 64 * 1024,
            MaxModelBytes: 16 * 1024,
            MaxNodeCount: 512,
            MaxTreeDepth: 32,
            MaxRepeaterRows: 256,
            MaxTableColumns: 24);
    }

    private static string MakeNodeName(string prefix, string value)
    {
        return $"{prefix}_{value.Replace('/', '_').Replace(':', '_').Replace('.', '_')}";
    }
}

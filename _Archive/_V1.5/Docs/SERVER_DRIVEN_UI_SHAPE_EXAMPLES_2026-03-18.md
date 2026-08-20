# Server-Driven UI Shape Examples (2026-03-18)

Purpose: capture a few non-functional authoring examples before implementation, so the server-side panel shape and cache-based definition/wire model can be judged before real code lands.

This is not the final implementation spec. It is a shape study.

Related docs:

- `Docs/UI_SCHEMA_AND_PREFAB_PROTOCOL_SPEC.md`
- `Docs/UI_SUBSYSTEM_PACKAGE_AND_IMPLEMENTATION_PLAN.md`
- `Docs/UI_SCHEMA_AUTHORING_LANGUAGE_SPEC.md`

---

## Preferred Direction

Use typed server-authored panel definitions that produce a compact `OpenPanel` command with:

- `PanelInstanceId`
- `PanelTypeId`
- `Category`
- `PresentationMode`
- `Payload`
- a few small flags like `BringToFront`

Do **not** use a RunUO-style generic `AddButton` / `AddLabel` / `AddHtml` control DSL as the main architecture over the wire.

The main difference from RunUO is:

- RunUO serializes a control tree/layout language
- our first pass should serialize:
- a panel type id
- a compact typed model payload for that panel

That keeps the client architecture aligned with:

- `UiManager`
- `MenuCanvasManager`
- one client definition cache
- one schema renderer

---

## Wire Envelope Shape

The open/update/close/action traffic should look more like this than like a generic layout packet:

```csharp
public readonly record struct UiOpenPanel(
    UiPanelInstanceId PanelInstanceId,
    UiPanelTypeId PanelTypeId,
    UiPanelCategory Category,
    UiPanelPresentationMode PresentationMode,
    ushort DefinitionRevision,
    ReadOnlyMemory<byte> Payload,
    bool BringToFront = true,
    bool HasCancelAction = false);

public readonly record struct UiUpdatePanel(
    UiPanelInstanceId PanelInstanceId,
    ReadOnlyMemory<byte> Payload);

public readonly record struct UiClosePanel(
    UiPanelInstanceId PanelInstanceId,
    string? Reason = null);

public readonly record struct UiPanelResponse(
    UiPanelInstanceId PanelInstanceId,
    uint ActionId,
    ReadOnlyMemory<byte> Payload = default);

public readonly record struct UiPanelDefinition(
    UiPanelTypeId PanelTypeId,
    ushort DefinitionRevision,
    ReadOnlyMemory<byte> DefinitionPayload);
```

For the actual wire, these commands can still be batched into one packet if needed:

```csharp
public enum UiCommandKind : byte
{
    OpenPanel = 1,
    UpdatePanel = 2,
    ClosePanel = 3,
    PanelAction = 4,
    PanelDefinition = 5,
    PanelAck = 6
}
```

So the “single menu packet” idea still exists, but the content is:

- panel type id
- panel instance id
- typed model payload

not a generic control AST.

---

## One Schema System, Cache Negotiated

There should be one schema system and one renderer.

The only question on open is:

- does the client already have `(PanelTypeId, DefinitionRevision)` cached?

If yes:

- render immediately

If no:

- client acknowledges `need_definition`
- server sends `PanelDefinition`
- client caches it and then renders

Client-shipped/common panels are just warm cache entries. Server-sent custom panels are cold-cache fills.

That gives the RunUO-style flexibility without maintaining two different content-delivery architectures.

---

## Example 1: Message Box

This is the first milestone and the cleanest test of the model.

```csharp
public sealed class MessageBoxPanelDefinition : IServerUiPanelDefinition
{
    public UiPanelTypeId PanelTypeId => SharedUiPanelIds.MessageBox;
    public UiPanelCategory Category => UiPanelCategory.Menu;
    public UiPanelPresentationMode PresentationMode => UiPanelPresentationMode.ExclusiveMenuOnly;

    public string Title { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public IReadOnlyList<MessageBoxButtonDefinition> Buttons { get; init; } = Array.Empty<MessageBoxButtonDefinition>();

    public UiOpenPanel Open(UiPanelInstanceId panelInstanceId)
    {
        MessageBoxPayload payload = new(Title, Body, Buttons);
        return new UiOpenPanel(
            panelInstanceId,
            PanelTypeId,
            Category,
            PresentationMode,
            MessageBoxPayloadCodec.Encode(payload),
            BringToFront: true,
            HasCancelAction: true);
    }
}
```

Usage:

```csharp
UiOpenPanel open = new MessageBoxPanelDefinition
{
    Title = "Return To Main Menu",
    Body = "Disconnect from the current session and return to the main menu?",
    Buttons = new[]
    {
        new MessageBoxButtonDefinition(ActionId: 1, Label: "Return", IsDefault: true),
        new MessageBoxButtonDefinition(ActionId: 2, Label: "Stay", IsCancel: true)
    }
}.Open(panelInstanceId);
```

This is close in spirit to RunUO `BaseConfirmGump`, but it stays prefab-first and payload-driven.

---

## Example 2: Simple List Menu

This is the equivalent of a small dialogue / question menu.

```csharp
public sealed class SimpleListMenuDefinition : IServerUiPanelDefinition
{
    public UiPanelTypeId PanelTypeId => SharedUiPanelIds.SimpleListMenu;
    public UiPanelCategory Category => UiPanelCategory.Menu;
    public UiPanelPresentationMode PresentationMode => UiPanelPresentationMode.ExclusiveMenuOnly;

    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public IReadOnlyList<SimpleListOptionDefinition> Options { get; init; } = Array.Empty<SimpleListOptionDefinition>();

    public UiOpenPanel Open(UiPanelInstanceId panelInstanceId)
    {
        SimpleListMenuPayload payload = new(Title, Description, Options, SelectedOptionIndex: -1);
        return new UiOpenPanel(
            panelInstanceId,
            PanelTypeId,
            Category,
            PresentationMode,
            SimpleListMenuPayloadCodec.Encode(payload),
            BringToFront: true,
            HasCancelAction: true);
    }
}
```

Usage:

```csharp
UiOpenPanel open = new SimpleListMenuDefinition
{
    Title = "Village Elder",
    Description = "What do you want to ask?",
    Options = new[]
    {
        new SimpleListOptionDefinition(ActionId: 101, Label: "Who are you?"),
        new SimpleListOptionDefinition(ActionId: 102, Label: "Any work available?"),
        new SimpleListOptionDefinition(ActionId: 103, Label: "Goodbye")
    }
}.Open(panelInstanceId);
```

This is closer to RunUO `QuestionMenu` / `ItemListMenu`.

---

## Example 3: Text Prompt

This is the equivalent of a dedicated prompt flow, not a generic panel builder.

```csharp
public sealed class TextPromptPanelDefinition : IServerUiPanelDefinition
{
    public UiPanelTypeId PanelTypeId => SharedUiPanelIds.TextPrompt;
    public UiPanelCategory Category => UiPanelCategory.Menu;
    public UiPanelPresentationMode PresentationMode => UiPanelPresentationMode.ExclusiveMenuOnly;

    public string Title { get; init; } = string.Empty;
    public string PromptText { get; init; } = string.Empty;
    public string InitialValue { get; init; } = string.Empty;
    public int MaxLength { get; init; } = 64;

    public UiOpenPanel Open(UiPanelInstanceId panelInstanceId)
    {
        TextPromptPayload payload = new(Title, PromptText, InitialValue, MaxLength);
        return new UiOpenPanel(
            panelInstanceId,
            PanelTypeId,
            Category,
            PresentationMode,
            TextPromptPayloadCodec.Encode(payload),
            BringToFront: true,
            HasCancelAction: true);
    }
}
```

Usage:

```csharp
UiOpenPanel open = new TextPromptPanelDefinition
{
    Title = "Rename House",
    PromptText = "Enter a new house name.",
    InitialValue = "Seaside Cottage",
    MaxLength = 40
}.Open(panelInstanceId);
```

This is conceptually closer to RunUO `Prompt` than to a general gump.

---

## Example 4: Cached-Schema Char Info Panel

This is the kind of screen where a single shipped `MessageBox`-style definition stops being enough.

The relevant point is that the server-side authoring can still be structured, but the wire payload should be:

- schema definition packet once
- open/update model payloads afterward

Example shape:

```csharp
SchemaBackedPanelDefinition panel = new()
{
    PanelTypeId = new UiPanelTypeId(0x10F1),
    Schema = new UiSchemaDefinition(
        RootTemplateId: "char_info",
        Nodes: new UiSchemaNode[]
        {
            new UiSectionNode("header", new UiSchemaNode[]
            {
                new UiTextNode("name", "player.name"),
                new UiValueNode("level", "player.level"),
                new UiValueNode("class", "player.class")
            }),
            new UiSectionNode("stats", new UiSchemaNode[]
            {
                new UiValueNode("str", "stats.str"),
                new UiValueNode("dex", "stats.dex"),
                new UiValueNode("int", "stats.int"),
                new UiActionButtonNode("str_up", ActionId: 201, Label: "+"),
                new UiActionButtonNode("dex_up", ActionId: 202, Label: "+"),
                new UiActionButtonNode("int_up", ActionId: 203, Label: "+")
            }),
            new UiRepeaterNode("skills", "skills.rows", new UiSchemaNode[]
            {
                new UiTextNode("skill_name", "name"),
                new UiValueNode("skill_value", "value"),
                new UiActionButtonNode("skill_up", ActionId: 300, Label: "+")
            })
        })
};
```

That is the equivalent of a custom `CharInfoGump`, but without sending raw `AddLabel/AddButton/...` instructions every open.

---

## Example 5: Cached-Schema Point Distribution Panel

This is the other case you pointed at: page navigation + lots of repeated rows + many conditional action buttons.

That shape fits a cached-schema panel better than a one-off hardcoded client panel.

```csharp
SchemaBackedPanelDefinition panel = new()
{
    PanelTypeId = new UiPanelTypeId(0x10F2),
    Schema = new UiSchemaDefinition(
        RootTemplateId: "point_distribution",
        Nodes: new UiSchemaNode[]
        {
            new UiSectionNode("summary", new UiSchemaNode[]
            {
                new UiTextNode("entity_name", "entity.name"),
                new UiValueNode("combat_level", "combat.level"),
                new UiValueNode("stat_points", "points.stats"),
                new UiValueNode("skill_points", "points.skills")
            }),
            new UiRepeaterNode("stat_rows", "stats.rows", new UiSchemaNode[]
            {
                new UiTextNode("stat_name", "name"),
                new UiValueNode("stat_value", "value"),
                new UiActionButtonNode("increase_stat", ActionId: 400, Label: "+")
            }),
            new UiRepeaterNode("skill_rows", "skills.rows", new UiSchemaNode[]
            {
                new UiTextNode("skill_name", "name"),
                new UiValueNode("skill_value", "value"),
                new UiActionButtonNode("increase_skill", ActionId: 500, Label: "+")
            }),
            new UiPagerNode("skill_pager", "skills.currentPage", "skills.totalPages", 601, 602)
        })
};
```

That gives you the “build once, cache it, send deltas later” model you were describing.

---

## Example 6: Tabbed Multiplayer Menu

This is another shape we should explicitly plan for: one menu shell with multiple tabbed views such as:

- `Client List`
- `Favorites`
- `History`

The selected tab is still server-driven state. The client reports the tab-click action and the server responds with an updated model.

```csharp
SchemaBackedPanelDefinition panel = new()
{
    PanelTypeId = new UiPanelTypeId(0x10F3),
    Schema = new UiSchemaDefinition(
        RootTemplateId: "multiplayer_menu",
        Nodes: new UiSchemaNode[]
        {
            new UiSectionNode("header", new UiSchemaNode[]
            {
                new UiTextNode("title", "menu.title")
            }),
            new UiTabSetNode("server_tabs", "tabs.items", "tabs.selected", new UiTabPageNode[]
            {
                new UiTabPageNode("client_list", ActionId: 801, new UiSchemaNode[]
                {
                    new UiRepeaterNode("client_rows", "clientList.rows", new UiSchemaNode[]
                    {
                        new UiTextNode("server_name", "name"),
                        new UiValueNode("server_ping", "ping"),
                        new UiActionButtonNode("connect_btn", ActionId: 820, Label: "Connect")
                    }),
                    new UiPagerNode("client_list_pager", "clientList.currentPage", "clientList.totalPages", 821, 822)
                }),
                new UiTabPageNode("favorites", ActionId: 802, new UiSchemaNode[]
                {
                    new UiRepeaterNode("favorite_rows", "favorites.rows", new UiSchemaNode[]
                    {
                        new UiTextNode("favorite_name", "name"),
                        new UiValueNode("favorite_ping", "ping"),
                        new UiActionButtonNode("favorite_connect_btn", ActionId: 830, Label: "Connect")
                    }),
                    new UiPagerNode("favorites_pager", "favorites.currentPage", "favorites.totalPages", 831, 832)
                })
            })
        })
};
```

That shape exercises:

- tab selection
- server-owned selected tab state
- page navigation within a selected tab
- repeated rows with per-row actions

---

## What This Shape Implies

If this is the direction we keep, then:

- server-side authoring is strongly typed
- payload codecs are per panel type
- panel-specific action ids are explicit
- the client resolves:
  - `PanelTypeId + DefinitionRevision -> cached definition`
- shipped/common panels are just warm cache entries
- the client does need one generic schema renderer, because that is the single server-driven panel system

That means the implementation architecture should optimize for:

- panel instance registry
- action response path
- schema definition cache
- model update path
- `MenuCanvasManager` ownership of server-driven shared/exclusive panels

not for:

- control-by-control server layout emission on every open

---

## Files

The matching compile-safe prototype file is:

- `_tmp/ServerDrivenUiShapeExamples.cs`

That file is intentionally not wired into runtime behavior.

using UltraSim.UI;

namespace UltraSim.Game.UI;

/// <summary>Bounded immutable text owned by one authoritative informational sign.</summary>
public readonly record struct InformationalSignContent(string Title, string Body)
{
    public const int MaximumTitleCharacters = 80;
    public const int MaximumBodyCharacters = 1_024;

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Title) && Title.Length <= MaximumTitleCharacters &&
        !string.IsNullOrWhiteSpace(Body) && Body.Length <= MaximumBodyCharacters;
}

/// <summary>Finite server-authored informational copy for the current Concept Demo world.</summary>
public static class GameInformationalSigns
{
    public static InformationalSignContent ConceptDemoWelcome { get; } = new(
        "WELCOME TO THE ULTRASIM CONCEPT DEMO",
        "This floating world demonstrates intentional void, terrain shells, authoritative movement, " +
        "the Sky Island telepad, and the distant Far Gate. Select a world object and press E to " +
        "interact. Third-person and independent controls can jump; Orbit controls use the telepad.");
}

/// <summary>Compiled informational view shared by every concrete sign-content instance.</summary>
public static class InformationalSignUi
{
    private static readonly Contract s_contract = CreateContract();

    public static CompiledUiDefinition Definition => s_contract.Definition;
    public static UiSlotId Title => s_contract.Title;
    public static UiSlotId Body => s_contract.Body;
    public static UiSlotId CloseLabel => s_contract.CloseLabel;
    public static UiActionId Close => s_contract.Close;

    private static Contract CreateContract()
    {
        var source = new UiDefinitionBuilder("game.informational-sign.gump");
        UiSlotReference title = source.DeclareSlot("title", UiSlotValueKind.Text);
        UiSlotReference body = source.DeclareSlot("body", UiSlotValueKind.Text);
        UiSlotReference closeLabel = source.DeclareSlot("close-label", UiSlotValueKind.Text);
        UiActionReference close = source.DeclareAction("close");
        source.AddCanvas(new UiCanvasOptions(800, 600), canvas =>
        {
            var surface = new UiCanvasPlacement(150, 130, 500, 300);
            canvas.AddPanel(
                "background",
                "informational-sign.surface",
                UiChildLayout.Canvas(surface));
            canvas.AddVertical(
                "content",
                content =>
                {
                    content.AddText("title", title);
                    content.AddText("body", body);
                    content.AddButton("close", close, closeLabel);
                },
                UiChildLayout.Canvas(new UiCanvasPlacement(174, 154, 452, 252)),
                new UiFlowOptions(
                    spacing: 16,
                    padding: new UiPadding(8, 8, 8, 8),
                    alignment: UiAlignment.Stretch));
        });
        CompiledUiDefinition definition = UiDefinitionCompiler.Compile(source);
        return new Contract(
            definition,
            definition.Prepare(title),
            definition.Prepare(body),
            definition.Prepare(closeLabel),
            definition.Prepare(close));
    }

    private sealed record Contract(
        CompiledUiDefinition Definition,
        UiSlotId Title,
        UiSlotId Body,
        UiSlotId CloseLabel,
        UiActionId Close);
}

/// <summary>Result of opening or reusing the one retained informational-sign view.</summary>
public readonly record struct InformationalSignUiOpenResult(
    bool Succeeded,
    UiViewInstanceId View,
    string Message);

/// <summary>
/// Game-owned one-view informational workflow. The client supplies only validated Close intent;
/// authoritative sign text is captured before the view crosses its semantic port.
/// </summary>
public sealed class InformationalSignUiController : IDisposable
{
    private readonly IUiSemanticPort _ui;
    private readonly UiDefinitionCacheKey _cacheKey;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private UiViewInstanceId _openView;
    private UiModel? _openModel;
    private bool _disposed;

    public InformationalSignUiController(IUiSemanticPort ui)
    {
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _cacheKey = new UiDefinitionCacheKey(
            "local-application-realm",
            "game-composition",
            protocolVersion: 1,
            InformationalSignUi.Definition.Key,
            InformationalSignUi.Definition.Version,
            InformationalSignUi.Definition.Hash);
    }

    public bool HasOpenView
    {
        get
        {
            EnsureOperational();
            return _openView.IsValid;
        }
    }

    public InformationalSignUiOpenResult Open(in InformationalSignContent content)
    {
        EnsureOperational();
        if (!content.IsValid)
            return new InformationalSignUiOpenResult(
                false, UiViewInstanceId.Invalid, "The sign content is invalid.");
        if (_openView.IsValid)
            return new InformationalSignUiOpenResult(true, _openView, "Sign already open.");

        var model = new UiModel(InformationalSignUi.Definition);
        model.SetText(InformationalSignUi.Title, content.Title);
        model.SetText(InformationalSignUi.Body, content.Body);
        model.SetText(InformationalSignUi.CloseLabel, "Close");
        UiViewInstanceId view = _ui.OpenView(new UiOpenView(
            _cacheKey,
            InformationalSignUi.Definition,
            model,
            sequence: 1));
        if (!view.IsValid)
            throw new InvalidOperationException("The sign UI port returned an invalid view ID.");
        try
        {
            _ui.BindActions(view, HandleAction);
        }
        catch
        {
            _ = _ui.CloseView(new UiCloseView(view));
            throw;
        }
        _openView = view;
        _openModel = model;
        return new InformationalSignUiOpenResult(true, view, "Sign opened.");
    }

    private void HandleAction(UiActionRequest request)
    {
        EnsureOperational();
        if (!_openView.IsValid || request.Instance != _openView || _openModel is null ||
            !_openModel.TryValidateAction(request) || request.Action != InformationalSignUi.Close)
            return;
        Close();
    }

    public bool Close()
    {
        EnsureOperational();
        if (!_openView.IsValid)
            return false;
        UiViewInstanceId view = _openView;
        _openView = default;
        _openModel = null;
        _ui.UnbindActions(view);
        return _ui.CloseView(new UiCloseView(view));
    }

    public void Dispose()
    {
        EnsureOwner();
        if (_disposed)
            return;
        if (_openView.IsValid)
        {
            UiViewInstanceId view = _openView;
            _openView = default;
            _openModel = null;
            _ui.UnbindActions(view);
            _ = _ui.CloseView(new UiCloseView(view));
        }
        _disposed = true;
    }

    private void EnsureOperational()
    {
        EnsureOwner();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The informational-sign UI controller is owner-thread-bound.");
    }
}

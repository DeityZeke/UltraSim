using UltraSim.UI;

namespace UltraSim.Client.Core;

/// <summary>Local in-process adapter for the renderer-neutral semantic UI port.</summary>
public sealed class LocalUiSemanticPort : IUiSemanticPort, IDisposable
{
    private readonly LocalSemanticLink _link;
    private readonly Dictionary<UiViewInstanceId, Action<UiActionRequest>> _actions = [];
    private bool _disposed;

    public LocalUiSemanticPort(LocalSemanticLink link)
    {
        _link = link ?? throw new ArgumentNullException(nameof(link));
        _link.ActionSubmitted += Forward;
    }

    public void BindActions(UiViewInstanceId instance, Action<UiActionRequest> handler)
    {
        EnsureOperational();
        if (!instance.IsValid) throw new ArgumentException("A valid view instance is required.", nameof(instance));
        ArgumentNullException.ThrowIfNull(handler);
        if (!_actions.TryAdd(instance, handler))
            throw new InvalidOperationException("The view already has an authoritative action endpoint.");
    }
    public void UnbindActions(UiViewInstanceId instance) { EnsureOperational(); _actions.Remove(instance); }
    public UiViewInstanceId OpenView(UiOpenView operation) { EnsureOperational(); return _link.OpenView(operation); }
    public UiViewInstanceId OpenHotView(UiHotOpenView operation) { EnsureOperational(); return _link.OpenHotView(operation); }
    public void UpdateView(UiUpdateView operation) { EnsureOperational(); _link.UpdateView(operation); }
    public bool CloseView(UiCloseView operation) { EnsureOperational(); return _link.CloseView(operation); }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _link.ActionSubmitted -= Forward;
        _actions.Clear();
    }

    private void Forward(UiActionRequest request)
    {
        if (!_disposed && _actions.TryGetValue(request.Instance, out Action<UiActionRequest>? action))
            action(request);
    }

    private void EnsureOperational()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LocalUiSemanticPort));
    }
}

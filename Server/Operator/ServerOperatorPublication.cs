using UltraSim.Framework.ControlPlane;

namespace UltraSim.Server.Operator;

/// <summary>Retires admission before the owner service goes away.</summary>
public sealed class ServerOperatorPublication : IDisposable
{
    private readonly ControlPlaneRegistry _registry;
    private readonly ControlProviderId _id;
    private readonly ulong _generation;
    private bool _disposed;

    public ServerOperatorPublication(ControlPlaneRegistry registry, IControlProvider provider)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        ArgumentNullException.ThrowIfNull(provider);
        if (!_registry.TryRegister(provider, out _generation, out string reason))
            throw new InvalidOperationException("Operator publication rejected: " + reason);
        _id = provider.Definition.Id;
    }

    public ulong Generation => _generation;

    public void Dispose()
    {
        if (_disposed) return;
        _ = _registry.Retire(_id, _generation);
        _disposed = true;
    }
}

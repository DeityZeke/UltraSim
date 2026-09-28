using System.Runtime.ExceptionServices;

namespace UltraSim.Client.Godot;

/// <summary>
/// Strong, owner-thread-bound references to exclusively owned resources. Add dependencies before
/// their consumers; retire native nodes/RIDs before disposing this collection. Never add borrowed
/// resources or use scene-tree removal as a substitute for completed wrapper disposal.
/// </summary>
internal sealed class GodotOwnedResources : IDisposable
{
    private readonly int _ownerThread = System.Environment.CurrentManagedThreadId;
    private readonly List<IDisposable> _resources = [];
    private bool _stopping;
    private ExceptionDispatchInfo? _failure;
    internal bool IsRetired { get; private set; }
    internal int Count => _resources.Count;

    internal T Own<T>(T resource) where T : class, IDisposable
    {
        EnsureOwner();
        ArgumentNullException.ThrowIfNull(resource);
        if (_stopping) throw new InvalidOperationException("Resource ownership is retiring.");
        if (_resources.Any(item => ReferenceEquals(item, resource)))
            throw new InvalidOperationException("Resource ownership cannot be registered twice.");
        _resources.Add(resource);
        return resource;
    }

    public void Dispose()
    {
        EnsureOwner();
        _failure?.Throw();
        if (_stopping) return;
        _stopping = true;
        List<Exception>? failures = null;
        for (int index = _resources.Count - 1; index >= 0; index--)
        {
            try { _resources[index].Dispose(); }
            catch (Exception error) { (failures ??= []).Add(error); }
        }
        if (failures is not null)
        {
            // Keep strong references and preserve failure on repeated disposal. Never advertise
            // retirement, or leave failed wrappers to a later GC, after partial cleanup.
            _failure = ExceptionDispatchInfo.Capture(failures.Count == 1
                ? failures[0] : new AggregateException("Native resource retirement failed.", failures));
            _failure.Throw();
        }
        _resources.Clear();
        IsRetired = true;
    }

    private void EnsureOwner()
    {
        if (_ownerThread != System.Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Native resource ownership is engine-thread-bound.");
    }
}

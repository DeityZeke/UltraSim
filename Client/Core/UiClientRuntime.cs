using UltraSim.UI;

namespace UltraSim.Client.Core;

/// <summary>Bounded client-side retained UI cache and view policy.</summary>
public readonly record struct UiClientLimits(
    int MaximumCachedDefinitionFamilies = 1_024,
    int MaximumOpenViews = 1_024)
{
    public static UiClientLimits Default => new(1_024, 1_024);

    internal void Validate()
    {
        if (MaximumCachedDefinitionFamilies <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaximumCachedDefinitionFamilies));
        if (MaximumOpenViews <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaximumOpenViews));
    }
}

/// <summary>
/// Client-local registry, exact definition cache, and sequence-aware model applier.
/// </summary>
public interface IUiActionSink
{
    void SubmitAction(UiActionRequest request);
}

public sealed class UiClientRuntime : IDisposable
{
    private readonly IUiRenderer _renderer;
    private readonly UiClientLimits _limits;
    private readonly Dictionary<UiDefinitionCacheKey, CompiledUiDefinition> _definitions = [];
    private readonly Dictionary<UiDefinitionFamilyKey, UiDefinitionCacheKey> _currentDefinitions = [];
    private readonly Dictionary<UiViewInstanceId, UiViewState> _views = [];
    private readonly int _ownerThreadId;
    private ulong _nextInstance = 1;
    private IUiActionSink? _actionSink;
    private Exception? _fault;
    private bool _rendererExecuting;
    private bool _disposed;

    public UiClientRuntime(IUiRenderer renderer, UiClientLimits limits = default)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        if (limits == default) limits = UiClientLimits.Default;
        limits.Validate();
        _renderer = renderer;
        _limits = limits;
        _ownerThreadId = Environment.CurrentManagedThreadId;
    }

    public int CachedDefinitionCount
    {
        get
        {
            EnsureOwnerThread();
            return _definitions.Count;
        }
    }

    public int OpenViewCount
    {
        get
        {
            EnsureOwnerThread();
            return _views.Count;
        }
    }

    public bool IsFaulted
    {
        get
        {
            EnsureOwnerThread();
            return _fault is not null;
        }
    }

    public void AttachLink(LocalSemanticLink link)
    {
        AttachActionSink(link);
    }

    public void AttachActionSink(IUiActionSink sink)
    {
        EnsureOperational();
        ArgumentNullException.ThrowIfNull(sink);
        if (_actionSink is not null && !ReferenceEquals(_actionSink, sink))
            throw new InvalidOperationException("A client runtime can have only one semantic action sink.");
        _actionSink = sink;
    }

    public void DetachActionSink(IUiActionSink sink)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(sink);
        if (ReferenceEquals(_actionSink, sink))
            _actionSink = null;
    }

    public UiViewInstanceId OpenView(UiOpenView operation)
    {
        EnsureOperational();
        operation.CacheKey.Validate(operation.Definition);
        UiModel model = operation.SnapshotModel();
        if (!ReferenceEquals(model.Definition, operation.Definition))
            throw new InvalidOperationException("The opening model does not match the compiled definition.");

        UiDefinitionFamilyKey family = operation.CacheKey.Family;
        UiDefinitionCacheKey? superseded = GetSupersededDefinition(operation.CacheKey);
        bool newFamily = !_currentDefinitions.ContainsKey(family);
        if (newFamily && _currentDefinitions.Count >= _limits.MaximumCachedDefinitionFamilies)
            throw new InvalidOperationException("The retained UI definition-family limit was reached.");
        (UiViewInstanceId instance, bool generated) = SelectInstance(
            operation.Instance,
            superseded.HasValue ? family : null);
        if (superseded.HasValue)
            Supersede(family, superseded.Value);
        if (_views.Count >= _limits.MaximumOpenViews)
            throw new InvalidOperationException("The retained UI view limit was reached.");
        IUiRenderHandle handle = CreateRendererHandle(instance, operation.Definition, model);

        _definitions[operation.CacheKey] = operation.Definition;
        _currentDefinitions[family] = operation.CacheKey;
        _views.Add(instance, new UiViewState(
            instance,
            operation.CacheKey,
            operation.Definition,
            model,
            operation.Sequence,
            handle));
        CommitInstance(instance, generated);
        return instance;
    }

    public UiViewInstanceId OpenHotView(UiHotOpenView operation)
    {
        EnsureOperational();
        if (!_definitions.TryGetValue(operation.CacheKey, out CompiledUiDefinition? definition))
        {
            if (_currentDefinitions.TryGetValue(operation.CacheKey.Family, out UiDefinitionCacheKey current) &&
                current != operation.CacheKey)
            {
                throw new UiViewSupersededException(operation.CacheKey.Family);
            }
            throw new KeyNotFoundException("The exact UI definition is not present in the client cache.");
        }
        operation.CacheKey.Validate(definition);
        UiModel model = operation.SnapshotModel();
        if (!ReferenceEquals(model.Definition, definition))
            throw new InvalidOperationException("The opening model does not match the cached definition.");
        if (_views.Count >= _limits.MaximumOpenViews)
            throw new InvalidOperationException("The retained UI view limit was reached.");

        (UiViewInstanceId instance, bool generated) = SelectInstance(operation.Instance);
        IUiRenderHandle handle = CreateRendererHandle(instance, definition, model);
        _views.Add(instance, new UiViewState(
            instance,
            operation.CacheKey,
            definition,
            model,
            operation.Sequence,
            handle));
        CommitInstance(instance, generated);
        return instance;
    }

    public void UpdateView(UiUpdateView operation)
    {
        EnsureOperational();
        if (!_views.TryGetValue(operation.Instance, out UiViewState? view))
            throw new KeyNotFoundException("The UI view instance is not open.");
        UiModelDelta delta = operation.Delta;
        if (delta.Definition != view.Definition.Handle)
            throw new InvalidOperationException("The UI update definition does not match the open view.");
        if (delta.Sequence <= view.LastSequence)
            throw new InvalidOperationException("The UI update sequence is stale or duplicated.");
        delta.ValidateAgainst(view.Definition);

        UiModel next = view.Model.Clone();
        ApplyDelta(next, view.Definition, delta);
        ApplyRenderer(view, next, delta);
        view.Model = next;
        view.LastSequence = delta.Sequence;
    }

    public bool CloseView(UiCloseView operation)
    {
        EnsureOperational();
        if (!_views.TryGetValue(operation.Instance, out UiViewState? view))
            return false;
        CloseRenderer(view.RenderHandle);
        _views.Remove(operation.Instance);
        return true;
    }

    public bool TryGetView(UiViewInstanceId instance, out UiViewInfo info)
    {
        EnsureOwnerThread();
        if (_views.TryGetValue(instance, out UiViewState? view))
        {
            info = new UiViewInfo(
                view.Instance,
                view.CacheKey,
                view.Definition.Handle,
                view.LastSequence);
            return true;
        }
        info = default;
        return false;
    }

    public UiViewInfo GetView(UiViewInstanceId instance)
    {
        if (!TryGetView(instance, out UiViewInfo info))
            throw new KeyNotFoundException("The UI view instance is not open.");
        return info;
    }

    public bool TryGetCachedDefinition(
        UiDefinitionCacheKey cacheKey,
        out CompiledUiDefinition? definition)
    {
        EnsureOperational();
        return _definitions.TryGetValue(cacheKey, out definition);
    }

    /// <summary>
    /// Publishes one exact passive definition into the bounded client cache without opening a
    /// view. Authoritative model values and action endpoints are deliberately not part of this
    /// operation.
    /// </summary>
    public bool CacheDefinition(
        UiDefinitionCacheKey cacheKey,
        CompiledUiDefinition definition)
    {
        EnsureOperational();
        cacheKey.Validate(definition);
        if (_definitions.ContainsKey(cacheKey)) return false;

        UiDefinitionFamilyKey family = cacheKey.Family;
        UiDefinitionCacheKey? superseded = GetSupersededDefinition(cacheKey);
        bool newFamily = !_currentDefinitions.ContainsKey(family);
        if (newFamily && _currentDefinitions.Count >= _limits.MaximumCachedDefinitionFamilies)
            throw new InvalidOperationException("The retained UI definition-family limit was reached.");
        if (superseded.HasValue)
            Supersede(family, superseded.Value);
        _definitions.Add(cacheKey, definition);
        _currentDefinitions.Add(family, cacheKey);
        return true;
    }

    public UiModel SnapshotModel(UiViewInstanceId instance) => GetViewState(instance).Model.Clone();

    public UiValue GetValue(UiViewInstanceId instance, UiSlotId slot) =>
        GetViewState(instance).Model.Get(slot);

    public ReadOnlySpan<UiListRow> GetRows(UiViewInstanceId instance, UiSlotId slot) =>
        GetViewState(instance).Model.GetRows(slot);

    public UiActionState GetActionState(UiViewInstanceId instance, UiActionId action) =>
        GetViewState(instance).Model.GetActionState(action);

    /// <summary>Validates and emits client intent through the local semantic link.</summary>
    public void SubmitAction(
        UiViewInstanceId instance,
        UiActionId action,
        UiRowId row = default,
        UiValue input = default,
        ulong clientSequence = 0,
        UiElementId? source = null)
    {
        EnsureOperational();
        UiViewState view = GetViewState(instance);
        var request = new UiActionRequest(
            instance,
            view.Definition.Handle,
            action,
            row,
            input,
            clientSequence,
            source);
        SubmitActionFromRenderer(request);
    }

    private void SubmitActionFromRenderer(UiActionRequest request)
    {
        EnsureOperational();
        if (_rendererExecuting)
        {
            throw new InvalidOperationException(
                "A UI renderer cannot submit action intent synchronously while Create, Apply, or Close is executing.");
        }
        UiViewState view = GetViewState(request.Instance);
        view.Definition.ValidateAction(request, view.Model);
        if (_actionSink is null)
            throw new InvalidOperationException("The UI client is not attached to a semantic action sink.");
        _actionSink.SubmitAction(request);
    }

    private UiViewState GetViewState(UiViewInstanceId instance)
    {
        EnsureOwnerThread();
        if (!_views.TryGetValue(instance, out UiViewState? view))
            throw new KeyNotFoundException("The UI view instance is not open.");
        return view;
    }

    private (UiViewInstanceId Instance, bool Generated) SelectInstance(
        UiViewInstanceId requested,
        UiDefinitionFamilyKey? replacingFamily = null)
    {
        if (requested.IsValid)
        {
            if (_views.TryGetValue(requested, out UiViewState? existing))
            {
                if (!replacingFamily.HasValue || existing.CacheKey.Family != replacingFamily.Value)
                    throw new InvalidOperationException("The UI view instance ID is already open.");
                return (requested, false);
            }
            if (_nextInstance == 0 || requested.Value < _nextInstance)
                throw new InvalidOperationException("The UI view instance ID is retired or below the allocation high-water mark.");
            return (requested, true);
        }

        if (_nextInstance == 0)
            throw new InvalidOperationException("The UI view instance ID space is exhausted.");
        return (new UiViewInstanceId(_nextInstance), true);
    }

    private void CommitInstance(UiViewInstanceId instance, bool advancesHighWater)
    {
        if (!advancesHighWater)
            return;
        if (instance.Value == ulong.MaxValue)
            _nextInstance = 0;
        else
            _nextInstance = instance.Value + 1;
    }

    private UiDefinitionCacheKey? GetSupersededDefinition(UiDefinitionCacheKey candidate)
    {
        if (!_currentDefinitions.TryGetValue(candidate.Family, out UiDefinitionCacheKey current) ||
            current == candidate)
        {
            return null;
        }

        if (candidate.DefinitionVersion < current.DefinitionVersion)
            throw new UiViewSupersededException(candidate.Family);
        if (candidate.DefinitionVersion == current.DefinitionVersion)
        {
            throw new InvalidDataException(
                "A UI definition family cannot publish two different hashes under the same version.");
        }
        return current;
    }

    private void Supersede(UiDefinitionFamilyKey family, UiDefinitionCacheKey oldKey)
    {
        UiViewInstanceId[] staleViews = _views.Values
            .Where(view => view.CacheKey.Family == family)
            .Select(view => view.Instance)
            .ToArray();
        foreach (UiViewInstanceId instance in staleViews)
            CloseView(new UiCloseView(instance));
        _definitions.Remove(oldKey);
        _currentDefinitions.Remove(family);
    }

    private static void ApplyDelta(
        UiModel model,
        CompiledUiDefinition definition,
        UiModelDelta delta)
    {
        foreach (UiScalarSlotPatch patch in delta.ScalarSlots)
            model.Set(patch.Slot, patch.Value);

        ReadOnlySpan<UiListPatch> listPatches = delta.Lists;
        Span<byte> processedSlots = definition.SlotCount <= 1_024
            ? stackalloc byte[definition.SlotCount]
            : new byte[definition.SlotCount];
        foreach (UiListPatch patch in listPatches)
        {
            if (processedSlots[patch.Slot.Value] != 0)
                continue;
            processedSlots[patch.Slot.Value] = 1;
            ApplyListPatches(model, patch.Slot, listPatches);
        }

        foreach (UiActionStatePatch patch in delta.Actions)
            model.SetActionState(patch.Action, patch.State);

        // Force validation of the exact handle even for an empty delta.
        if (delta.Definition != definition.Handle)
            throw new InvalidOperationException("The UI update definition does not match the compiled definition.");
    }

    private static void ApplyListPatches(
        UiModel model,
        UiSlotId slot,
        ReadOnlySpan<UiListPatch> patches)
    {
        int matchingCount = 0;
        UiListPatch only = default;
        foreach (UiListPatch patch in patches)
        {
            if (patch.Slot != slot)
                continue;
            matchingCount++;
            only = patch;
        }
        if (matchingCount == 1 && only.Kind == UiListDeltaKind.Replace)
        {
            model.ReplaceRows(slot, only.Rows);
            return;
        }

        var rows = model.GetRows(slot).ToArray().ToList();
        foreach (UiListPatch patch in patches)
        {
            if (patch.Slot != slot)
                continue;
            int index = rows.FindIndex(row => row.Id == patch.RowId);
            switch (patch.Kind)
            {
                case UiListDeltaKind.Add when index < 0:
                    rows.Add(patch.Row);
                    break;
                case UiListDeltaKind.Update when index >= 0:
                    rows[index] = patch.Row;
                    break;
                case UiListDeltaKind.Remove when index >= 0:
                    rows.RemoveAt(index);
                    break;
                case UiListDeltaKind.Add:
                    throw new InvalidDataException($"The UI list row '{patch.RowId.Value}' already exists.");
                case UiListDeltaKind.Update:
                case UiListDeltaKind.Remove:
                    throw new InvalidDataException($"The UI list row '{patch.RowId.Value}' does not exist.");
                default:
                    throw new InvalidDataException("Unsupported UI list delta kind.");
            }
        }
        model.ReplaceRows(slot, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(rows));
    }

    private sealed class UiViewState
    {
        internal UiViewState(
            UiViewInstanceId instance,
            UiDefinitionCacheKey cacheKey,
            CompiledUiDefinition definition,
            UiModel model,
            ulong lastSequence,
            IUiRenderHandle renderHandle)
        {
            Instance = instance;
            CacheKey = cacheKey;
            Definition = definition;
            Model = model;
            LastSequence = lastSequence;
            RenderHandle = renderHandle;
        }

        internal UiViewInstanceId Instance { get; }
        internal UiDefinitionCacheKey CacheKey { get; }
        internal CompiledUiDefinition Definition { get; }
        internal UiModel Model { get; set; }
        internal ulong LastSequence { get; set; }
        internal IUiRenderHandle RenderHandle { get; }
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The UI client runtime is bound to its creating owner thread.");
    }

    private void EnsureOperational()
    {
        EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_fault is not null)
            throw new UiClientRuntimeFaultedException(_fault);
    }

    public void Dispose()
    {
        EnsureOwnerThread();
        if (_disposed) return;
        _disposed = true;
        _actionSink = null;
        Exception? firstFailure = null;
        foreach (UiViewState view in _views.Values)
        {
            _rendererExecuting = true;
            try { _renderer.Close(view.RenderHandle); }
            catch (Exception exception) { firstFailure ??= exception; }
            finally { _rendererExecuting = false; }
        }
        _views.Clear();
        _definitions.Clear();
        _currentDefinitions.Clear();
        if (firstFailure is not null)
            _fault = firstFailure;
    }

    private IUiRenderHandle CreateRendererHandle(
        UiViewInstanceId instance,
        CompiledUiDefinition definition,
        UiModel model)
    {
        _rendererExecuting = true;
        try
        {
            return _renderer.Create(
                instance,
                definition,
                new UiModelView(model),
                SubmitActionFromRenderer) ??
                throw new InvalidOperationException("The UI renderer returned a null render handle.");
        }
        catch (Exception exception)
        {
            _fault = exception;
            throw new UiClientRuntimeFaultedException("Create", exception);
        }
        finally
        {
            _rendererExecuting = false;
        }
    }

    private void ApplyRenderer(UiViewState view, UiModel next, UiModelDelta delta)
    {
        _rendererExecuting = true;
        try
        {
            _renderer.Apply(
                view.RenderHandle,
                view.Definition,
                new UiModelView(view.Model),
                new UiModelView(next),
                delta,
                SubmitActionFromRenderer);
        }
        catch (Exception exception)
        {
            _fault = exception;
            throw new UiClientRuntimeFaultedException("Apply", exception);
        }
        finally
        {
            _rendererExecuting = false;
        }
    }

    private void CloseRenderer(IUiRenderHandle handle)
    {
        _rendererExecuting = true;
        try
        {
            _renderer.Close(handle);
        }
        catch (Exception exception)
        {
            _fault = exception;
            throw new UiClientRuntimeFaultedException("Close", exception);
        }
        finally
        {
            _rendererExecuting = false;
        }
    }
}

/// <summary>Read-only diagnostic identity and sequence for one retained client view.</summary>
public readonly record struct UiViewInfo(
    UiViewInstanceId Instance,
    UiDefinitionCacheKey CacheKey,
    UiDefinitionHandle Definition,
    ulong LastSequence);

/// <summary>
/// In-process logical bridge. It exposes semantic view operations only; it does
/// not encode packets, serialize JSON, open sockets, or own server authority.
/// </summary>
public sealed class LocalSemanticLink : IUiActionSink
{
    private readonly UiClientRuntime _client;

    public LocalSemanticLink(UiClientRuntime client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _client.AttachLink(this);
    }

    public event Action<UiActionRequest>? ActionSubmitted;

    public UiViewInstanceId OpenView(UiOpenView operation) => _client.OpenView(operation);

    public UiViewInstanceId OpenHotView(UiHotOpenView operation) => _client.OpenHotView(operation);

    public void UpdateView(UiUpdateView operation) => _client.UpdateView(operation);

    public bool CloseView(UiCloseView operation) => _client.CloseView(operation);

    internal void SubmitAction(UiActionRequest request)
    {
        Action<UiActionRequest>? target = ActionSubmitted;
        if (target is null)
            throw new InvalidOperationException("The local UI link has no authority action endpoint attached.");
        target.Invoke(request);
    }

    void IUiActionSink.SubmitAction(UiActionRequest request) => SubmitAction(request);
}

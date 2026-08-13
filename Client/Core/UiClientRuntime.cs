using UltraSim.UI;

namespace UltraSim.Client.Core;

/// <summary>
/// Client-local registry, exact definition cache, and sequence-aware model applier.
/// </summary>
public sealed class UiClientRuntime
{
    private readonly IUiRenderer _renderer;
    private readonly Dictionary<UiDefinitionCacheKey, CompiledUiDefinition> _definitions = [];
    private readonly Dictionary<UiDefinitionFamilyKey, UiDefinitionCacheKey> _currentDefinitions = [];
    private readonly Dictionary<UiViewInstanceId, UiViewState> _views = [];
    private readonly int _ownerThreadId;
    private ulong _nextInstance = 1;
    private LocalSemanticLink? _link;
    private Exception? _fault;
    private bool _rendererExecuting;

    public UiClientRuntime(IUiRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _renderer = renderer;
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
        EnsureOperational();
        ArgumentNullException.ThrowIfNull(link);
        if (_link is not null && !ReferenceEquals(_link, link))
            throw new InvalidOperationException("A client runtime can have only one local semantic link.");
        _link = link;
    }

    public UiViewInstanceId OpenView(UiOpenView operation)
    {
        EnsureOperational();
        operation.CacheKey.Validate(operation.Definition);
        UiModel model = operation.CloneModel();
        if (!ReferenceEquals(model.Definition, operation.Definition))
            throw new InvalidOperationException("The opening model does not match the compiled definition.");

        UiDefinitionFamilyKey family = operation.CacheKey.Family;
        UiDefinitionCacheKey? superseded = GetSupersededDefinition(operation.CacheKey);
        (UiViewInstanceId instance, bool generated) = SelectInstance(
            operation.Instance,
            superseded.HasValue ? family : null);
        if (superseded.HasValue)
            Supersede(family, superseded.Value);
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
        UiModel model = operation.CloneModel();
        if (!ReferenceEquals(model.Definition, definition))
            throw new InvalidOperationException("The opening model does not match the cached definition.");

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
        ulong clientSequence = 0)
    {
        EnsureOperational();
        UiViewState view = GetViewState(instance);
        var request = new UiActionRequest(
            instance,
            view.Definition.Handle,
            action,
            row,
            input,
            clientSequence);
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
        if (_link is null)
            throw new InvalidOperationException("The UI client is not attached to a local semantic link.");
        _link.SubmitAction(request);
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

        foreach (UiListPatch patch in delta.Lists)
        {
            switch (patch.Kind)
            {
                case UiListDeltaKind.Replace:
                    model.ReplaceRows(patch.Slot, patch.Rows);
                    break;
                case UiListDeltaKind.Add:
                    ApplyRowAdd(model, patch);
                    break;
                case UiListDeltaKind.Update:
                    ApplyRowUpdate(model, patch);
                    break;
                case UiListDeltaKind.Remove:
                    ApplyRowRemove(model, patch);
                    break;
                default:
                    throw new InvalidDataException("Unsupported UI list delta kind.");
            }
        }

        foreach (UiActionStatePatch patch in delta.Actions)
            model.SetActionState(patch.Action, patch.State);

        // Force validation of the exact handle even for an empty delta.
        if (delta.Definition != definition.Handle)
            throw new InvalidOperationException("The UI update definition does not match the compiled definition.");
    }

    private static void ApplyRowAdd(UiModel model, UiListPatch patch)
    {
        UiListRow[] current = model.GetRows(patch.Slot).ToArray();
        if (current.Any(row => row.Id == patch.RowId))
            throw new InvalidDataException($"The UI list row '{patch.RowId.Value}' already exists.");
        var next = new UiListRow[current.Length + 1];
        current.CopyTo(next, 0);
        next[^1] = patch.Row.Clone();
        model.ReplaceRows(patch.Slot, next);
    }

    private static void ApplyRowUpdate(UiModel model, UiListPatch patch)
    {
        UiListRow[] next = model.GetRows(patch.Slot).ToArray();
        int index = Array.FindIndex(next, row => row.Id == patch.RowId);
        if (index < 0)
            throw new InvalidDataException($"The UI list row '{patch.RowId.Value}' does not exist.");
        next[index] = patch.Row.Clone();
        model.ReplaceRows(patch.Slot, next);
    }

    private static void ApplyRowRemove(UiModel model, UiListPatch patch)
    {
        UiListRow[] current = model.GetRows(patch.Slot).ToArray();
        int index = Array.FindIndex(current, row => row.Id == patch.RowId);
        if (index < 0)
            throw new InvalidDataException($"The UI list row '{patch.RowId.Value}' does not exist.");
        var next = new UiListRow[current.Length - 1];
        if (index > 0)
            Array.Copy(current, 0, next, 0, index);
        if (index < current.Length - 1)
            Array.Copy(current, index + 1, next, index, current.Length - index - 1);
        model.ReplaceRows(patch.Slot, next);
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
        if (_fault is not null)
            throw new UiClientRuntimeFaultedException(_fault);
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

public readonly record struct UiViewInfo(
    UiViewInstanceId Instance,
    UiDefinitionCacheKey CacheKey,
    UiDefinitionHandle Definition,
    ulong LastSequence);

/// <summary>
/// In-process logical bridge. It exposes semantic view operations only; it does
/// not encode packets, serialize JSON, open sockets, or own server authority.
/// </summary>
public sealed class LocalSemanticLink
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
}

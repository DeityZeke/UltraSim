using System.Buffers.Binary;
using System.Text;

namespace UltraSim.UI;

/// <summary>Bounded admission limits for one encoded semantic UI operation.</summary>
public readonly record struct UiSemanticWireLimits(
    int MaximumOperationBytes,
    UiCompileLimits MaximumDefinitionLimits)
{
    public static UiSemanticWireLimits Default => new(16_384, UiCompileLimits.Default);

    public void Validate()
    {
        if (MaximumOperationBytes <= 0 || MaximumOperationBytes > 16_777_216)
            throw new ArgumentOutOfRangeException(nameof(MaximumOperationBytes));
        UiCompileLimits limits = MaximumDefinitionLimits;
        if (limits.MaximumDepth <= 0 || limits.MaximumLayoutNodes <= 0 ||
            limits.MaximumElements <= 0 || limits.MaximumSlots <= 0 ||
            limits.MaximumActions <= 0 || limits.MaximumTextCharacters <= 0 ||
            limits.MaximumRowsPerList <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaximumDefinitionLimits));
    }
}

/// <summary>
/// Canonical bounded binary codec for renderer-neutral UI semantic operations.
/// It is transport-independent; Network carries the resulting bytes.
/// </summary>
public static class UiSemanticWireCodec
{
    private const ulong DefinitionArtifactMagic = 0x5553554944454632UL; // USUIDEF2
    private const uint DefinitionArtifactVersion = 2;

    internal static bool TryWriteDefinitionArtifactCore(
        Span<byte> destination,
        CompiledUiDefinition definition,
        out int written,
        UiDefinitionArtifactLimits limits)
    {
        ArgumentNullException.ThrowIfNull(definition);
        written = 0;
        limits.Validate();
        if (!Within(definition.Limits, limits.MaximumDefinitionLimits)) return false;
        if (destination.Length > limits.MaximumArtifactBytes)
            destination = destination[..limits.MaximumArtifactBytes];
        try
        {
            var writer = new WireWriter(destination);
            if (!writer.TryWriteUInt64(DefinitionArtifactMagic) ||
                !writer.TryWriteUInt32(DefinitionArtifactVersion) ||
                !writer.TryWriteUInt64(definition.Hash.Low) ||
                !writer.TryWriteUInt64(definition.Hash.High) ||
                !WriteDefinition(ref writer, definition)) return false;
            written = writer.WrittenCount;
            return true;
        }
        catch (Exception exception) when (IsDecodeFailure(exception)) { return false; }
    }

    internal static bool TryReadDefinitionArtifactCore(
        ReadOnlySpan<byte> source,
        out CompiledUiDefinition? definition,
        UiDefinitionArtifactLimits limits)
    {
        definition = null;
        limits.Validate();
        if (source.Length < 32 || source.Length > limits.MaximumArtifactBytes) return false;
        try
        {
            var reader = new WireReader(source);
            if (!reader.TryReadUInt64(out ulong magic) || magic != DefinitionArtifactMagic ||
                !reader.TryReadUInt32(out uint version) || version != DefinitionArtifactVersion ||
                !reader.TryReadUInt64(out ulong hashLow) ||
                !reader.TryReadUInt64(out ulong hashHigh) || (hashLow | hashHigh) == 0 ||
                !ReadDefinition(ref reader, new UiDefinitionHash(hashLow, hashHigh),
                    limits.MaximumDefinitionLimits, out definition) || !reader.IsComplete)
            {
                definition = null;
                return false;
            }
            byte[] canonical = new byte[source.Length];
            if (!TryWriteDefinitionArtifactCore(canonical, definition!, out int written, limits) ||
                written != source.Length || !source.SequenceEqual(canonical))
            {
                definition = null;
                return false;
            }
            return true;
        }
        catch (Exception exception) when (IsDecodeFailure(exception))
        {
            definition = null;
            return false;
        }
    }

    public static bool TryWriteColdOpen(
        Span<byte> destination,
        in UiOpenView operation,
        UiSemanticWireLimits limits,
        out int written)
    {
        UiOpenView copy = operation;
        return TryWrite(destination, limits, out written, (ref WireWriter writer) =>
        {
            UiModel model = copy.SnapshotModel();
            return writer.TryWriteUInt64(copy.Instance.Value) &&
                writer.TryWriteUInt64(copy.Sequence) &&
                WriteCacheKey(ref writer, copy.CacheKey) &&
                WriteDefinition(ref writer, copy.Definition) &&
                WriteModel(ref writer, model);
        });
    }

    public static bool TryReadColdOpen(
        ReadOnlySpan<byte> source,
        UiSemanticWireLimits limits,
        out UiOpenView operation)
    {
        operation = default;
        if (!TryBeginRead(source, limits, out WireReader reader)) return false;
        try
        {
            if (!reader.TryReadUInt64(out ulong instanceValue) || instanceValue == 0 ||
                !reader.TryReadUInt64(out ulong sequence) ||
                !ReadCacheKey(ref reader, out UiDefinitionCacheKey cacheKey) ||
                !ReadDefinition(ref reader, cacheKey.DefinitionHash, limits.MaximumDefinitionLimits,
                    out CompiledUiDefinition? definition) ||
                definition!.Key != cacheKey.DefinitionKey ||
                definition.Version != cacheKey.DefinitionVersion ||
                !ReadModel(ref reader, definition, out UiModel? model) || !reader.IsComplete)
                return false;
            cacheKey.Validate(definition);
            operation = new UiOpenView(
                cacheKey, definition!, model!, new UiViewInstanceId(instanceValue), sequence);
            return true;
        }
        catch (Exception exception) when (IsDecodeFailure(exception)) { return false; }
    }

    public static bool TryWriteHotOpen(
        Span<byte> destination,
        in UiHotOpenView operation,
        UiSemanticWireLimits limits,
        out int written)
    {
        UiHotOpenView copy = operation;
        return TryWrite(destination, limits, out written, (ref WireWriter writer) =>
        {
            UiModel model = copy.SnapshotModel();
            return writer.TryWriteUInt64(copy.Instance.Value) &&
                writer.TryWriteUInt64(copy.Sequence) &&
                WriteCacheKey(ref writer, copy.CacheKey) &&
                WriteModel(ref writer, model);
        });
    }

    public static bool TryReadHotOpen(
        ReadOnlySpan<byte> source,
        UiSemanticWireLimits limits,
        Func<UiDefinitionCacheKey, CompiledUiDefinition?> resolveDefinition,
        out UiHotOpenView operation)
    {
        ArgumentNullException.ThrowIfNull(resolveDefinition);
        operation = default;
        if (!TryBeginRead(source, limits, out WireReader reader)) return false;
        try
        {
            if (!reader.TryReadUInt64(out ulong instanceValue) || instanceValue == 0 ||
                !reader.TryReadUInt64(out ulong sequence) ||
                !ReadCacheKey(ref reader, out UiDefinitionCacheKey cacheKey))
                return false;
            CompiledUiDefinition? definition = resolveDefinition(cacheKey);
            if (definition is null || !ReadModel(ref reader, definition, out UiModel? model) ||
                !reader.IsComplete)
                return false;
            operation = new UiHotOpenView(
                cacheKey, model!, new UiViewInstanceId(instanceValue), sequence);
            return true;
        }
        catch (Exception exception) when (IsDecodeFailure(exception)) { return false; }
    }

    public static bool TryWriteUpdate(
        Span<byte> destination,
        in UiUpdateView operation,
        UiSemanticWireLimits limits,
        out int written)
    {
        UiUpdateView copy = operation;
        return TryWrite(destination, limits, out written, (ref WireWriter writer) =>
            writer.TryWriteUInt64(copy.Instance.Value) &&
            WriteDelta(ref writer, copy.Delta, limits.MaximumDefinitionLimits));
    }

    public static bool TryReadUpdate(
        ReadOnlySpan<byte> source,
        UiSemanticWireLimits limits,
        out UiUpdateView operation)
    {
        operation = default;
        if (!TryBeginRead(source, limits, out WireReader reader)) return false;
        try
        {
            if (!reader.TryReadUInt64(out ulong instanceValue) || instanceValue == 0 ||
                !ReadDelta(ref reader, limits.MaximumDefinitionLimits, out UiModelDelta? delta) ||
                !reader.IsComplete)
                return false;
            operation = new UiUpdateView(new UiViewInstanceId(instanceValue), delta!);
            return true;
        }
        catch (Exception exception) when (IsDecodeFailure(exception)) { return false; }
    }

    public static bool TryWriteClose(
        Span<byte> destination,
        in UiCloseView operation,
        UiSemanticWireLimits limits,
        out int written)
    {
        UiCloseView copy = operation;
        return TryWrite(destination, limits, out written,
            (ref WireWriter writer) => writer.TryWriteUInt64(copy.Instance.Value));
    }

    public static bool TryReadClose(
        ReadOnlySpan<byte> source,
        UiSemanticWireLimits limits,
        out UiCloseView operation)
    {
        operation = default;
        if (!TryBeginRead(source, limits, out WireReader reader) ||
            !reader.TryReadUInt64(out ulong instanceValue) || instanceValue == 0 || !reader.IsComplete)
            return false;
        try { operation = new UiCloseView(new UiViewInstanceId(instanceValue)); return true; }
        catch (Exception exception) when (IsDecodeFailure(exception)) { return false; }
    }

    public static bool TryWriteAction(
        Span<byte> destination,
        in UiActionRequest request,
        UiSemanticWireLimits limits,
        out int written)
    {
        UiActionRequest copy = request;
        return TryWrite(destination, limits, out written, (ref WireWriter writer) =>
            writer.TryWriteUInt64(copy.Instance.Value) &&
            writer.TryWriteUInt64(copy.Definition.Low) &&
            writer.TryWriteUInt64(copy.Definition.High) &&
            writer.TryWriteUInt16(copy.Action.Value) &&
            writer.TryWriteUInt64(copy.Row.Value) &&
            WriteValue(ref writer, copy.Input) &&
            writer.TryWriteUInt64(copy.ClientSequence) &&
            writer.TryWriteUInt16(copy.Source.Value));
    }

    public static bool TryReadAction(
        ReadOnlySpan<byte> source,
        UiSemanticWireLimits limits,
        out UiActionRequest request)
    {
        request = default;
        if (!TryBeginRead(source, limits, out WireReader reader)) return false;
        try
        {
            if (!reader.TryReadUInt64(out ulong instance) || instance == 0 ||
                !reader.TryReadUInt64(out ulong definitionLow) ||
                !reader.TryReadUInt64(out ulong definitionHigh) ||
                (definitionLow | definitionHigh) == 0 ||
                !reader.TryReadUInt16(out ushort action) || action == ushort.MaxValue ||
                !reader.TryReadUInt64(out ulong row) ||
                !ReadValue(ref reader, out UiValue input) ||
                !reader.TryReadUInt64(out ulong clientSequence) ||
                !reader.TryReadUInt16(out ushort sourceElement) || !reader.IsComplete)
                return false;
            request = new UiActionRequest(
                new UiViewInstanceId(instance),
                new UiDefinitionHandle(definitionLow, definitionHigh),
                new UiActionId(action),
                row == 0 ? UiRowId.Invalid : new UiRowId(row),
                input,
                clientSequence,
                new UiElementId(sourceElement));
            return true;
        }
        catch (Exception exception) when (IsDecodeFailure(exception)) { return false; }
    }

    private delegate bool WriteOperation(ref WireWriter writer);

    private static bool TryWrite(
        Span<byte> destination,
        UiSemanticWireLimits limits,
        out int written,
        WriteOperation operation)
    {
        written = 0;
        limits.Validate();
        if (destination.Length > limits.MaximumOperationBytes)
            destination = destination[..limits.MaximumOperationBytes];
        try
        {
            var writer = new WireWriter(destination);
            if (!operation(ref writer)) return false;
            written = writer.WrittenCount;
            return true;
        }
        catch (Exception exception) when (IsDecodeFailure(exception)) { return false; }
    }

    private static bool TryBeginRead(
        ReadOnlySpan<byte> source,
        UiSemanticWireLimits limits,
        out WireReader reader)
    {
        limits.Validate();
        if (source.Length == 0 || source.Length > limits.MaximumOperationBytes)
        {
            reader = default;
            return false;
        }
        reader = new WireReader(source);
        return true;
    }

    private static bool WriteCacheKey(ref WireWriter writer, in UiDefinitionCacheKey key) =>
        writer.TryWriteString(key.ServerIdentity, UiDefinitionCacheKey.MaximumIdentityLength) &&
        writer.TryWriteString(key.CompositionIdentity, UiDefinitionCacheKey.MaximumIdentityLength) &&
        writer.TryWriteUInt16(key.ProtocolVersion) &&
        writer.TryWriteString(key.DefinitionKey.Value, UiDefinitionKey.MaximumLength) &&
        writer.TryWriteUInt16(key.DefinitionVersion) &&
        writer.TryWriteUInt64(key.DefinitionHash.Low) &&
        writer.TryWriteUInt64(key.DefinitionHash.High);

    private static bool ReadCacheKey(ref WireReader reader, out UiDefinitionCacheKey key)
    {
        key = default;
        if (!reader.TryReadString(UiDefinitionCacheKey.MaximumIdentityLength, out string? server) ||
            !reader.TryReadString(UiDefinitionCacheKey.MaximumIdentityLength, out string? composition) ||
            !reader.TryReadUInt16(out ushort protocolVersion) || protocolVersion == 0 ||
            !reader.TryReadString(UiDefinitionKey.MaximumLength, out string? definitionKey) ||
            !reader.TryReadUInt16(out ushort definitionVersion) || definitionVersion == 0 ||
            !reader.TryReadUInt64(out ulong hashLow) ||
            !reader.TryReadUInt64(out ulong hashHigh) || (hashLow | hashHigh) == 0)
            return false;
        key = new UiDefinitionCacheKey(
            server,
            composition,
            protocolVersion,
            new UiDefinitionKey(definitionKey),
            definitionVersion,
            new UiDefinitionHash(hashLow, hashHigh));
        return true;
    }

    private static bool WriteDefinition(ref WireWriter writer, CompiledUiDefinition definition)
    {
        UiCompileLimits limits = definition.Limits;
        if (!writer.TryWriteString(definition.Key.Value, UiDefinitionKey.MaximumLength) ||
            !writer.TryWriteUInt16(definition.Version) ||
            !WriteLimits(ref writer, limits) ||
            !writer.TryWriteByte((byte)definition.OutsidePointerBehavior) ||
            !writer.TryWriteUInt16(definition.OutsideClickAction.Value) ||
            !writer.TryWriteInt32(definition.LayoutNodeCount) ||
            !writer.TryWriteInt32(definition.ElementCount) ||
            !writer.TryWriteInt32(definition.SlotCount) ||
            !writer.TryWriteInt32(definition.ActionCount))
            return false;
        foreach (UiCompiledLayoutNode node in definition.LayoutNodes)
        {
            if (!writer.TryWriteInt32(node.Parent.Value) ||
                !writer.TryWriteInt32(node.SiblingOrder) ||
                !writer.TryWriteByte((byte)node.Kind) ||
                !writer.TryWriteString(node.Key, UiDefinitionKey.MaximumLength) ||
                !WriteChildLayout(ref writer, node.Placement) ||
                !WriteFlow(ref writer, node.Flow) ||
                !WriteCanvas(ref writer, node.Canvas)) return false;
        }
        foreach (UiCompiledElement element in definition.Elements)
        {
            if (!writer.TryWriteInt32(element.Parent.Value) ||
                !writer.TryWriteInt32(element.SiblingOrder) ||
                !writer.TryWriteByte((byte)element.Kind) ||
                !writer.TryWriteString(element.Key, UiDefinitionKey.MaximumLength) ||
                !WriteChildLayout(ref writer, element.Placement) ||
                !writer.TryWriteUInt16(element.Slot.Value) ||
                !writer.TryWriteUInt16(element.Action.Value) ||
                !writer.TryWriteString(element.StyleRole, limits.MaximumTextCharacters) ||
                !writer.TryWriteString(element.AssetRole, limits.MaximumTextCharacters)) return false;
        }
        foreach (UiSlotDefinition slot in definition.Slots)
            if (!writer.TryWriteString(slot.Key, UiDefinitionKey.MaximumLength) ||
                !writer.TryWriteByte((byte)slot.ValueKind)) return false;
        foreach (UiActionDefinition action in definition.Actions)
            if (!writer.TryWriteString(action.Key, UiDefinitionKey.MaximumLength) ||
                !writer.TryWriteBoolean(action.AcceptsRowContext) ||
                !writer.TryWriteByte((byte)action.InputKind)) return false;
        return true;
    }

    private static bool ReadDefinition(
        ref WireReader reader,
        UiDefinitionHash expectedHash,
        UiCompileLimits admitted,
        out CompiledUiDefinition? definition)
    {
        definition = null;
        if (!reader.TryReadString(UiDefinitionKey.MaximumLength, out string? keyText) ||
            !reader.TryReadUInt16(out ushort version) || version == 0 ||
            !ReadLimits(ref reader, admitted, out UiCompileLimits limits) ||
            !reader.TryReadByte(out byte outsideBehavior) ||
            outsideBehavior > (byte)UiOutsidePointerBehavior.Close ||
            !reader.TryReadUInt16(out ushort outsideAction) ||
            !reader.TryReadInt32(out int nodeCount) || nodeCount <= 0 || nodeCount > limits.MaximumLayoutNodes ||
            !reader.TryReadInt32(out int elementCount) || elementCount < 0 || elementCount > limits.MaximumElements ||
            !reader.TryReadInt32(out int slotCount) || slotCount < 0 || slotCount > limits.MaximumSlots ||
            !reader.TryReadInt32(out int actionCount) || actionCount < 0 || actionCount > limits.MaximumActions)
            return false;
        var nodes = new UiCompiledLayoutNode[nodeCount];
        for (int index = 0; index < nodes.Length; index++)
        {
            if (!reader.TryReadInt32(out int parent) ||
                !reader.TryReadInt32(out int sibling) ||
                !reader.TryReadByte(out byte kind) || kind > (byte)UiLayoutKind.Scroll ||
                !reader.TryReadString(UiDefinitionKey.MaximumLength, out string? key) ||
                !ReadChildLayout(ref reader, out UiChildLayout placement) ||
                !ReadFlow(ref reader, out UiFlowOptions flow) ||
                !ReadCanvas(ref reader, allowDefault: index != 0, out UiCanvasOptions canvas)) return false;
            nodes[index] = new UiCompiledLayoutNode(
                new UiLayoutNodeId(index), new UiLayoutNodeId(parent), sibling,
                (UiLayoutKind)kind, key, placement, flow, canvas);
        }
        var elements = new UiCompiledElement[elementCount];
        for (int index = 0; index < elements.Length; index++)
        {
            if (!reader.TryReadInt32(out int parent) ||
                !reader.TryReadInt32(out int sibling) ||
                !reader.TryReadByte(out byte kind) || kind > (byte)UiElementKind.List ||
                !reader.TryReadString(UiDefinitionKey.MaximumLength, out string? key) ||
                !ReadChildLayout(ref reader, out UiChildLayout placement) ||
                !reader.TryReadUInt16(out ushort slot) ||
                !reader.TryReadUInt16(out ushort action) ||
                !reader.TryReadString(limits.MaximumTextCharacters, out string? style) ||
                !reader.TryReadString(limits.MaximumTextCharacters, out string? asset)) return false;
            elements[index] = new UiCompiledElement(
                new UiElementId(checked((ushort)index)), new UiLayoutNodeId(parent), sibling,
                (UiElementKind)kind, key, placement, new UiSlotId(slot), new UiActionId(action), style, asset);
        }
        var slots = new UiSlotDefinition[slotCount];
        for (int index = 0; index < slots.Length; index++)
        {
            if (!reader.TryReadString(UiDefinitionKey.MaximumLength, out string? key) ||
                !reader.TryReadByte(out byte kind) || kind > (byte)UiSlotValueKind.List) return false;
            slots[index] = new UiSlotDefinition(new UiSlotId(checked((ushort)index)), key, (UiSlotValueKind)kind);
        }
        var actions = new UiActionDefinition[actionCount];
        for (int index = 0; index < actions.Length; index++)
        {
            if (!reader.TryReadString(UiDefinitionKey.MaximumLength, out string? key) ||
                !reader.TryReadBoolean(out bool rows) ||
                !reader.TryReadByte(out byte input) || input > (byte)UiValueKind.Boolean) return false;
            actions[index] = new UiActionDefinition(
                new UiActionId(checked((ushort)index)), key, rows, (UiValueKind)input);
        }
        var definitionKey = new UiDefinitionKey(keyText);
        definition = UiDefinitionCompiler.RestoreCompiled(
            definitionKey, version, limits, (UiOutsidePointerBehavior)outsideBehavior,
            new UiActionId(outsideAction), nodes, elements, slots, actions, expectedHash);
        return true;
    }

    private static bool Within(UiCompileLimits value, UiCompileLimits admitted) =>
        value.MaximumDepth <= admitted.MaximumDepth &&
        value.MaximumLayoutNodes <= admitted.MaximumLayoutNodes &&
        value.MaximumElements <= admitted.MaximumElements &&
        value.MaximumSlots <= admitted.MaximumSlots &&
        value.MaximumActions <= admitted.MaximumActions &&
        value.MaximumTextCharacters <= admitted.MaximumTextCharacters &&
        value.MaximumRowsPerList <= admitted.MaximumRowsPerList;

    private static bool WriteModel(ref WireWriter writer, UiModel model)
    {
        CompiledUiDefinition definition = model.Definition;
        if (!writer.TryWriteUInt16(checked((ushort)definition.SlotCount))) return false;
        foreach (UiSlotDefinition slot in definition.Slots)
        {
            bool list = slot.ValueKind == UiSlotValueKind.List ||
                (slot.ValueKind == UiSlotValueKind.Any && model.GetRows(slot.Id).Length != 0);
            if (!writer.TryWriteByte(list ? (byte)1 : (byte)0)) return false;
            if (list)
            {
                ReadOnlySpan<UiListRow> rows = model.GetRows(slot.Id);
                if (!writer.TryWriteInt32(rows.Length)) return false;
                foreach (UiListRow row in rows)
                    if (!WriteRow(ref writer, row)) return false;
            }
            else if (!WriteValue(ref writer, model.Get(slot.Id))) return false;
        }
        if (!writer.TryWriteUInt16(checked((ushort)definition.ActionCount))) return false;
        foreach (UiActionDefinition action in definition.Actions)
            if (!WriteActionState(ref writer, model.GetActionState(action.Id), definition.Limits)) return false;
        return true;
    }

    private static bool ReadModel(
        ref WireReader reader,
        CompiledUiDefinition definition,
        out UiModel? model)
    {
        model = null;
        if (!reader.TryReadUInt16(out ushort slotCount) || slotCount != definition.SlotCount)
            return false;
        var candidate = new UiModel(definition);
        foreach (UiSlotDefinition slot in definition.Slots)
        {
            if (!reader.TryReadByte(out byte storage) || storage > 1) return false;
            if (storage == 1)
            {
                if (slot.ValueKind is not (UiSlotValueKind.List or UiSlotValueKind.Any) ||
                    !reader.TryReadInt32(out int rowCount) || rowCount < 0 ||
                    rowCount > definition.Limits.MaximumRowsPerList) return false;
                var rows = new UiListRow[rowCount];
                for (int index = 0; index < rows.Length; index++)
                    if (!ReadRow(ref reader, definition.Limits, out rows[index])) return false;
                candidate.ReplaceRows(slot.Id, rows);
            }
            else
            {
                if (slot.ValueKind == UiSlotValueKind.List ||
                    !ReadValue(ref reader, out UiValue value)) return false;
                candidate.Set(slot.Id, value);
            }
        }
        if (!reader.TryReadUInt16(out ushort actionCount) || actionCount != definition.ActionCount)
            return false;
        foreach (UiActionDefinition action in definition.Actions)
        {
            if (!ReadActionState(ref reader, definition.Limits, out UiActionState state)) return false;
            candidate.SetActionState(action.Id, state);
        }
        model = candidate;
        return true;
    }

    private static bool WriteDelta(
        ref WireWriter writer,
        UiModelDelta delta,
        UiCompileLimits limits)
    {
        if (!writer.TryWriteUInt64(delta.Definition.Low) ||
            !writer.TryWriteUInt64(delta.Definition.High) ||
            !writer.TryWriteUInt64(delta.Sequence) ||
            !writer.TryWriteInt32(delta.ScalarSlots.Length) ||
            !writer.TryWriteInt32(delta.Lists.Length) ||
            !writer.TryWriteInt32(delta.Actions.Length)) return false;
        foreach (UiScalarSlotPatch patch in delta.ScalarSlots)
            if (!writer.TryWriteUInt16(patch.Slot.Value) || !WriteValue(ref writer, patch.Value)) return false;
        foreach (UiListPatch patch in delta.Lists)
        {
            if (!writer.TryWriteUInt16(patch.Slot.Value) ||
                !writer.TryWriteByte((byte)patch.Kind) ||
                !writer.TryWriteUInt64(patch.RowId.Value)) return false;
            if (patch.Kind == UiListDeltaKind.Replace)
            {
                if (!writer.TryWriteInt32(patch.Rows.Length)) return false;
                foreach (UiListRow row in patch.Rows)
                    if (!WriteRow(ref writer, row)) return false;
            }
            else if (patch.Kind is UiListDeltaKind.Add or UiListDeltaKind.Update)
            {
                if (!WriteRow(ref writer, patch.Row)) return false;
            }
        }
        foreach (UiActionStatePatch patch in delta.Actions)
            if (!writer.TryWriteUInt16(patch.Action.Value) ||
                !WriteActionState(ref writer, patch.State, limits)) return false;
        return true;
    }

    private static bool ReadDelta(
        ref WireReader reader,
        UiCompileLimits limits,
        out UiModelDelta? delta)
    {
        delta = null;
        if (!reader.TryReadUInt64(out ulong low) || !reader.TryReadUInt64(out ulong high) ||
            (low | high) == 0 || !reader.TryReadUInt64(out ulong sequence) ||
            !reader.TryReadInt32(out int scalarCount) || scalarCount < 0 ||
                scalarCount > limits.MaximumSlots ||
            !reader.TryReadInt32(out int listCount) || listCount < 0 ||
            !reader.TryReadInt32(out int actionCount) || actionCount < 0 ||
                actionCount > limits.MaximumActions ||
            (long)scalarCount * 3L + (long)listCount * 11L + (long)actionCount * 4L >
                reader.RemainingCount)
            return false;
        var scalar = new UiScalarSlotPatch[scalarCount];
        for (int index = 0; index < scalar.Length; index++)
        {
            if (!reader.TryReadUInt16(out ushort slot) || slot == ushort.MaxValue ||
                !ReadValue(ref reader, out UiValue value)) return false;
            scalar[index] = new UiScalarSlotPatch(new UiSlotId(slot), value);
        }
        var lists = new UiListPatch[listCount];
        for (int index = 0; index < lists.Length; index++)
        {
            if (!reader.TryReadUInt16(out ushort slot) || slot == ushort.MaxValue ||
                !reader.TryReadByte(out byte kind) || kind > (byte)UiListDeltaKind.Remove ||
                !reader.TryReadUInt64(out ulong rowId)) return false;
            var slotId = new UiSlotId(slot);
            switch ((UiListDeltaKind)kind)
            {
                case UiListDeltaKind.Replace:
                    if (rowId != 0 || !reader.TryReadInt32(out int count) || count < 0 ||
                        count > limits.MaximumRowsPerList ||
                        (long)count * 10L > reader.RemainingCount)
                        return false;
                    var replacement = new UiListRow[count];
                    for (int row = 0; row < count; row++)
                        if (!ReadRow(ref reader, limits, out replacement[row])) return false;
                    lists[index] = UiListPatch.Replace(slotId, replacement);
                    break;
                case UiListDeltaKind.Add:
                case UiListDeltaKind.Update:
                    if (rowId == 0 || !ReadRow(ref reader, limits, out UiListRow value) ||
                        value.Id.Value != rowId) return false;
                    lists[index] = kind == (byte)UiListDeltaKind.Add
                        ? UiListPatch.Add(slotId, value)
                        : UiListPatch.Update(slotId, value);
                    break;
                default:
                    if (rowId == 0) return false;
                    lists[index] = UiListPatch.Remove(slotId, new UiRowId(rowId));
                    break;
            }
        }
        var actions = new UiActionStatePatch[actionCount];
        for (int index = 0; index < actions.Length; index++)
        {
            if (!reader.TryReadUInt16(out ushort action) || action == ushort.MaxValue ||
                !ReadActionState(ref reader, limits, out UiActionState state)) return false;
            actions[index] = new UiActionStatePatch(new UiActionId(action), state);
        }
        delta = new UiModelDelta(
            new UiDefinitionHandle(low, high), sequence, scalar, lists, actions);
        return true;
    }

    private static bool WriteRow(ref WireWriter writer, in UiListRow row)
    {
        bool attached = row.Attachment.IsValid;
        if (!writer.TryWriteUInt64(row.Id.Value) ||
            !writer.TryWriteUInt16(checked((ushort)(row.Values.Length | (attached ? 0x8000 : 0))))) return false;
        foreach (UiValue value in row.Values)
            if (!WriteValue(ref writer, value)) return false;
        return !attached ||
            (writer.TryWriteUInt16(row.Attachment.Kind) &&
             writer.TryWriteUInt16(checked((ushort)row.Attachment.Payload.Length)) &&
             writer.TryWriteBytes(row.Attachment.Payload));
    }

    private static bool ReadRow(
        ref WireReader reader,
        UiCompileLimits limits,
        out UiListRow row)
    {
        row = default;
        if (!reader.TryReadUInt64(out ulong id) || id == 0 ||
            !reader.TryReadUInt16(out ushort rawCount)) return false;
        bool attached = (rawCount & 0x8000) != 0;
        int count = rawCount & 0x7fff;
        if (count > UiListRow.MaximumValues ||
            count > reader.RemainingCount) return false;
        var values = new UiValue[count];
        for (int index = 0; index < values.Length; index++)
            if (!ReadValue(ref reader, out values[index]) ||
                (values[index].Kind == UiValueKind.Text &&
                    values[index].Text!.Length > limits.MaximumTextCharacters)) return false;
        UiRowAttachment attachment = default;
        if (attached)
        {
            if (!reader.TryReadUInt16(out ushort kind) || kind == 0 ||
                !reader.TryReadUInt16(out ushort length) ||
                length is 0 or > UiRowAttachment.MaximumBytes ||
                !reader.TryReadBytes(length, out ReadOnlySpan<byte> payload)) return false;
            attachment = new UiRowAttachment(kind, payload);
        }
        row = new UiListRow(new UiRowId(id), values, attachment);
        return true;
    }

    private static bool WriteValue(ref WireWriter writer, in UiValue value)
    {
        if (!writer.TryWriteByte((byte)value.Kind)) return false;
        return value.Kind switch
        {
            UiValueKind.Unset => true,
            UiValueKind.Text => writer.TryWriteString(value.Text!, UiValue.MaximumTextCharacters),
            UiValueKind.Number => writer.TryWriteUInt64(unchecked((ulong)BitConverter.DoubleToInt64Bits(value.Number))),
            UiValueKind.Boolean => writer.TryWriteBoolean(value.Boolean),
            _ => false,
        };
    }

    private static bool ReadValue(ref WireReader reader, out UiValue value)
    {
        value = default;
        if (!reader.TryReadByte(out byte kind) || kind > (byte)UiValueKind.Boolean) return false;
        switch ((UiValueKind)kind)
        {
            case UiValueKind.Unset: value = UiValue.Unset; return true;
            case UiValueKind.Text:
                if (!reader.TryReadString(UiValue.MaximumTextCharacters, out string? text)) return false;
                value = UiValue.FromText(text); return true;
            case UiValueKind.Number:
                if (!reader.TryReadUInt64(out ulong bits)) return false;
                double number = BitConverter.Int64BitsToDouble(unchecked((long)bits));
                if (!double.IsFinite(number)) return false;
                value = UiValue.FromNumber(number); return true;
            case UiValueKind.Boolean:
                if (!reader.TryReadBoolean(out bool boolean)) return false;
                value = UiValue.FromBoolean(boolean); return true;
            default: return false;
        }
    }

    private static bool WriteActionState(
        ref WireWriter writer,
        in UiActionState state,
        UiCompileLimits limits) =>
        writer.TryWriteBoolean(state.Enabled) &&
        writer.TryWriteBoolean(state.UnavailableReason is not null) &&
        (state.UnavailableReason is null ||
            writer.TryWriteString(state.UnavailableReason, limits.MaximumTextCharacters));

    private static bool ReadActionState(
        ref WireReader reader,
        UiCompileLimits limits,
        out UiActionState state)
    {
        state = default;
        if (!reader.TryReadBoolean(out bool enabled) || !reader.TryReadBoolean(out bool hasReason))
            return false;
        string? reason = null;
        if (hasReason && !reader.TryReadString(limits.MaximumTextCharacters, out reason)) return false;
        state = new UiActionState(enabled, reason);
        return true;
    }

    private static bool WriteLimits(ref WireWriter writer, UiCompileLimits limits) =>
        writer.TryWriteInt32(limits.MaximumDepth) &&
        writer.TryWriteInt32(limits.MaximumLayoutNodes) &&
        writer.TryWriteInt32(limits.MaximumElements) &&
        writer.TryWriteInt32(limits.MaximumSlots) &&
        writer.TryWriteInt32(limits.MaximumActions) &&
        writer.TryWriteInt32(limits.MaximumTextCharacters) &&
        writer.TryWriteInt32(limits.MaximumRowsPerList);

    private static bool ReadLimits(
        ref WireReader reader,
        UiCompileLimits admitted,
        out UiCompileLimits limits)
    {
        limits = default;
        if (!reader.TryReadInt32(out int depth) || depth <= 0 || depth > admitted.MaximumDepth ||
            !reader.TryReadInt32(out int nodes) || nodes <= 0 || nodes > admitted.MaximumLayoutNodes ||
            !reader.TryReadInt32(out int elements) || elements <= 0 || elements > admitted.MaximumElements ||
            !reader.TryReadInt32(out int slots) || slots <= 0 || slots > admitted.MaximumSlots ||
            !reader.TryReadInt32(out int actions) || actions <= 0 || actions > admitted.MaximumActions ||
            !reader.TryReadInt32(out int text) || text <= 0 || text > admitted.MaximumTextCharacters ||
            !reader.TryReadInt32(out int rows) || rows <= 0 || rows > admitted.MaximumRowsPerList)
            return false;
        limits = new UiCompileLimits(depth, nodes, elements, slots, actions, text, rows);
        return true;
    }

    private static bool WriteFlow(ref WireWriter writer, UiFlowOptions flow) =>
        writer.TryWriteSingle(flow.Spacing) && WritePadding(ref writer, flow.Padding) &&
        writer.TryWriteByte((byte)flow.Sizing) && writer.TryWriteByte((byte)flow.Alignment);

    private static bool ReadFlow(ref WireReader reader, out UiFlowOptions flow)
    {
        flow = default;
        if (!reader.TryReadSingle(out float spacing) || !ReadPadding(ref reader, out UiPadding padding) ||
            !reader.TryReadByte(out byte sizing) || sizing > (byte)UiSizing.Fill || sizing == (byte)UiSizing.Fixed ||
            !reader.TryReadByte(out byte alignment) || alignment > (byte)UiAlignment.Stretch) return false;
        flow = new UiFlowOptions(spacing, padding, (UiSizing)sizing, (UiAlignment)alignment);
        return true;
    }

    private static bool WritePadding(ref WireWriter writer, UiPadding padding) =>
        writer.TryWriteSingle(padding.Left) && writer.TryWriteSingle(padding.Top) &&
        writer.TryWriteSingle(padding.Right) && writer.TryWriteSingle(padding.Bottom);

    private static bool ReadPadding(ref WireReader reader, out UiPadding padding)
    {
        padding = default;
        if (!reader.TryReadSingle(out float left) || !reader.TryReadSingle(out float top) ||
            !reader.TryReadSingle(out float right) || !reader.TryReadSingle(out float bottom)) return false;
        padding = new UiPadding(left, top, right, bottom);
        return true;
    }

    private static bool WriteCanvas(ref WireWriter writer, UiCanvasOptions canvas) =>
        writer.TryWriteSingle(canvas.Width) && writer.TryWriteSingle(canvas.Height);

    private static bool ReadCanvas(ref WireReader reader, bool allowDefault, out UiCanvasOptions canvas)
    {
        canvas = default;
        if (!reader.TryReadSingle(out float width) || !reader.TryReadSingle(out float height)) return false;
        if (allowDefault && width == 0 && height == 0) return true;
        canvas = new UiCanvasOptions(width, height);
        return true;
    }

    private static bool WriteChildLayout(ref WireWriter writer, UiChildLayout layout)
    {
        if (!writer.TryWriteByte((byte)layout.Sizing) ||
            !writer.TryWriteByte((byte)layout.Alignment) ||
            !writer.TryWriteBoolean(layout.HasCanvasPlacement)) return false;
        return !layout.HasCanvasPlacement || WriteCanvasPlacement(ref writer, layout.CanvasPlacement);
    }

    private static bool ReadChildLayout(ref WireReader reader, out UiChildLayout layout)
    {
        layout = default;
        if (!reader.TryReadByte(out byte sizing) || sizing > (byte)UiSizing.Fill ||
            !reader.TryReadByte(out byte alignment) || alignment > (byte)UiAlignment.Stretch ||
            !reader.TryReadBoolean(out bool canvas)) return false;
        if (canvas)
        {
            if (!ReadCanvasPlacement(ref reader, out UiCanvasPlacement placement)) return false;
            layout = UiChildLayout.Canvas(placement);
            return sizing == (byte)UiSizing.Fixed;
        }
        if (sizing == (byte)UiSizing.Fixed) return false;
        layout = UiChildLayout.Flow((UiSizing)sizing, (UiAlignment)alignment);
        return true;
    }

    private static bool WriteCanvasPlacement(ref WireWriter writer, UiCanvasPlacement value) =>
        writer.TryWriteSingle(value.X) && writer.TryWriteSingle(value.Y) &&
        writer.TryWriteSingle(value.Width) && writer.TryWriteSingle(value.Height) &&
        writer.TryWriteSingle(value.AnchorMinX) && writer.TryWriteSingle(value.AnchorMinY) &&
        writer.TryWriteSingle(value.AnchorMaxX) && writer.TryWriteSingle(value.AnchorMaxY) &&
        writer.TryWriteSingle(value.PivotX) && writer.TryWriteSingle(value.PivotY) &&
        writer.TryWriteInt32(value.ZOrder);

    private static bool ReadCanvasPlacement(ref WireReader reader, out UiCanvasPlacement value)
    {
        value = default;
        if (!reader.TryReadSingle(out float x) || !reader.TryReadSingle(out float y) ||
            !reader.TryReadSingle(out float width) || !reader.TryReadSingle(out float height) ||
            !reader.TryReadSingle(out float minX) || !reader.TryReadSingle(out float minY) ||
            !reader.TryReadSingle(out float maxX) || !reader.TryReadSingle(out float maxY) ||
            !reader.TryReadSingle(out float pivotX) || !reader.TryReadSingle(out float pivotY) ||
            !reader.TryReadInt32(out int z)) return false;
        value = new UiCanvasPlacement(x, y, width, height, minX, minY, maxX, maxY, pivotX, pivotY, z);
        return true;
    }

    private static bool IsDecodeFailure(Exception exception) =>
        exception is ArgumentException or InvalidOperationException or InvalidDataException or
            OverflowException or DecoderFallbackException or EncoderFallbackException;

    private ref struct WireWriter
    {
        private Span<byte> _destination;
        private int _position;
        internal WireWriter(Span<byte> destination) { _destination = destination; _position = 0; }
        internal readonly int WrittenCount => _position;
        internal bool TryWriteByte(byte value)
        {
            if (_position >= _destination.Length) return false;
            _destination[_position++] = value; return true;
        }
        internal bool TryWriteBytes(ReadOnlySpan<byte> value)
        {
            if (value.Length > _destination.Length - _position) return false;
            value.CopyTo(_destination[_position..]); _position += value.Length; return true;
        }
        internal bool TryWriteBoolean(bool value) => TryWriteByte(value ? (byte)1 : (byte)0);
        internal bool TryWriteUInt16(ushort value)
        {
            if (_destination.Length - _position < 2) return false;
            BinaryPrimitives.WriteUInt16BigEndian(_destination[_position..], value); _position += 2; return true;
        }
        internal bool TryWriteInt32(int value) => TryWriteUInt32(unchecked((uint)value));
        internal bool TryWriteUInt32(uint value)
        {
            if (_destination.Length - _position < 4) return false;
            BinaryPrimitives.WriteUInt32BigEndian(_destination[_position..], value); _position += 4; return true;
        }
        internal bool TryWriteUInt64(ulong value)
        {
            if (_destination.Length - _position < 8) return false;
            BinaryPrimitives.WriteUInt64BigEndian(_destination[_position..], value); _position += 8; return true;
        }
        internal bool TryWriteSingle(float value) => TryWriteUInt32(unchecked((uint)BitConverter.SingleToInt32Bits(value)));
        internal bool TryWriteString(string value, int maximumCharacters)
        {
            if (value is null || value.Length > maximumCharacters) return false;
            int bytes;
            try { bytes = StrictUtf8.GetByteCount(value); }
            catch (EncoderFallbackException) { return false; }
            if (bytes > maximumCharacters * 4 || _destination.Length - _position < 4 + bytes) return false;
            WireWriter staged = this;
            if (!staged.TryWriteInt32(bytes)) return false;
            try { staged._position += StrictUtf8.GetBytes(value, staged._destination[staged._position..]); }
            catch (EncoderFallbackException) { return false; }
            this = staged; return true;
        }
    }

    private ref struct WireReader
    {
        private ReadOnlySpan<byte> _source;
        private int _position;
        internal WireReader(ReadOnlySpan<byte> source) { _source = source; _position = 0; }
        internal readonly bool IsComplete => _position == _source.Length;
        internal readonly int RemainingCount => _source.Length - _position;
        internal bool TryReadByte(out byte value)
        {
            if (_position >= _source.Length) { value = 0; return false; }
            value = _source[_position++]; return true;
        }
        internal bool TryReadBytes(int length, out ReadOnlySpan<byte> value)
        {
            if (length < 0 || length > _source.Length - _position)
            { value = default; return false; }
            value = _source.Slice(_position, length); _position += length; return true;
        }
        internal bool TryReadBoolean(out bool value)
        {
            if (!TryReadByte(out byte raw) || raw > 1) { value = false; return false; }
            value = raw != 0; return true;
        }
        internal bool TryReadUInt16(out ushort value)
        {
            if (_source.Length - _position < 2) { value = 0; return false; }
            value = BinaryPrimitives.ReadUInt16BigEndian(_source[_position..]); _position += 2; return true;
        }
        internal bool TryReadInt32(out int value)
        {
            bool read = TryReadUInt32(out uint raw); value = unchecked((int)raw); return read;
        }
        internal bool TryReadUInt32(out uint value)
        {
            if (_source.Length - _position < 4) { value = 0; return false; }
            value = BinaryPrimitives.ReadUInt32BigEndian(_source[_position..]); _position += 4; return true;
        }
        internal bool TryReadUInt64(out ulong value)
        {
            if (_source.Length - _position < 8) { value = 0; return false; }
            value = BinaryPrimitives.ReadUInt64BigEndian(_source[_position..]); _position += 8; return true;
        }
        internal bool TryReadSingle(out float value)
        {
            bool read = TryReadUInt32(out uint raw);
            value = BitConverter.Int32BitsToSingle(unchecked((int)raw));
            return read && float.IsFinite(value);
        }
        internal bool TryReadString(int maximumCharacters, out string value)
        {
            value = string.Empty;
            if (!TryReadInt32(out int bytes) || bytes < 0 || bytes > maximumCharacters * 4 ||
                bytes > _source.Length - _position) return false;
            ReadOnlySpan<byte> encoded = _source.Slice(_position, bytes);
            try { value = StrictUtf8.GetString(encoded); }
            catch (DecoderFallbackException) { return false; }
            if (value.Length > maximumCharacters) { value = string.Empty; return false; }
            _position += bytes; return true;
        }
    }

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
}

using UltraSim.Game.Admission;
using UltraSim.UI;

namespace UltraSim.Game.UI;

public enum ServerMonitorConnectionKind : byte
{
    Local = 1,
    Remote = 2,
}

/// <summary>One sanitized entered-participant row in the read-only monitor.</summary>
public readonly record struct ServerMonitorParticipant(
    ParticipantId Participant,
    uint PublicActorIndex,
    uint PublicActorGeneration,
    string DisplayName,
    ServerMonitorConnectionKind Connection,
    int RoundTripMilliseconds,
    int ChunkX,
    int ChunkZ)
{
    public bool IsValid => Participant.IsValid && PublicActorGeneration != 0 &&
        !string.IsNullOrWhiteSpace(DisplayName) && DisplayName.Length <= 24 &&
        Connection is ServerMonitorConnectionKind.Local or ServerMonitorConnectionKind.Remote &&
        (Connection == ServerMonitorConnectionKind.Local
            ? RoundTripMilliseconds == -1
            : RoundTripMilliseconds is >= 0 and <= 60_000);
}

/// <summary>
/// Bounded authority-authored read model. It deliberately excludes endpoints, credentials,
/// deployment keys, exact local positions, private owner state, and administrative actions.
/// </summary>
public sealed class ServerMonitorSnapshot
{
    public const int MaximumParticipantRows = 64;
    private readonly ServerMonitorParticipant[] _participants;

    public ServerMonitorSnapshot(
        ulong revision,
        ulong generationId,
        ulong simulationTick,
        int simulationFrequencyHz,
        long generationUptimeSeconds,
        long recentTickMicroseconds,
        long p95TickMicroseconds,
        int entityCount,
        int maximumParticipants,
        ulong joins,
        ulong leaves,
        string? testMarker,
        ReadOnlySpan<ServerMonitorParticipant> participants)
    {
        if (revision == 0 || generationId == 0 || simulationFrequencyHz is <= 0 or > 1_000 ||
            generationUptimeSeconds < 0 || recentTickMicroseconds < 0 || p95TickMicroseconds < 0 ||
            entityCount < 0 || maximumParticipants is <= 0 or > MaximumParticipantRows ||
            participants.Length > maximumParticipants || participants.Length > MaximumParticipantRows ||
            testMarker?.Length > 96)
            throw new ArgumentOutOfRangeException(nameof(ServerMonitorSnapshot));
        _participants = participants.ToArray();
        for (int index = 0; index < _participants.Length; index++)
        {
            if (!_participants[index].IsValid)
                throw new ArgumentException("A Server Monitor participant row is invalid.", nameof(participants));
            for (int prior = 0; prior < index; prior++)
                if (_participants[prior].Participant == _participants[index].Participant ||
                    (_participants[prior].PublicActorIndex == _participants[index].PublicActorIndex &&
                     _participants[prior].PublicActorGeneration ==
                        _participants[index].PublicActorGeneration))
                    throw new ArgumentException(
                        "Server Monitor participant identities must be unique.", nameof(participants));
        }
        Revision = revision;
        GenerationId = generationId;
        SimulationTick = simulationTick;
        SimulationFrequencyHz = simulationFrequencyHz;
        GenerationUptimeSeconds = generationUptimeSeconds;
        RecentTickMicroseconds = recentTickMicroseconds;
        P95TickMicroseconds = p95TickMicroseconds;
        EntityCount = entityCount;
        MaximumParticipants = maximumParticipants;
        Joins = joins;
        Leaves = leaves;
        TestMarker = testMarker;
    }

    public ulong Revision { get; }
    public ulong GenerationId { get; }
    public ulong SimulationTick { get; }
    public int SimulationFrequencyHz { get; }
    public long GenerationUptimeSeconds { get; }
    public long RecentTickMicroseconds { get; }
    public long P95TickMicroseconds { get; }
    public int EntityCount { get; }
    public int MaximumParticipants { get; }
    public ulong Joins { get; }
    public ulong Leaves { get; }
    public string? TestMarker { get; }
    public ReadOnlySpan<ServerMonitorParticipant> Participants => _participants;
}

/// <summary>Compiled read-only Server Monitor definition.</summary>
public static class ServerMonitorUi
{
    private static readonly Contract s_contract = CreateContract();

    public static CompiledUiDefinition Definition => s_contract.Definition;
    public static UiSlotId Title => s_contract.Title;
    public static UiSlotId Summary => s_contract.Summary;
    public static UiSlotId Timing => s_contract.Timing;
    public static UiSlotId Participants => s_contract.Participants;
    public static UiSlotId Marker => s_contract.Marker;
    public static UiSlotId CloseLabel => s_contract.CloseLabel;
    public static UiActionId Close => s_contract.Close;

    private static Contract CreateContract()
    {
        var source = new UiDefinitionBuilder("game.server-monitor.gump");
        UiSlotReference title = source.DeclareSlot("title", UiSlotValueKind.Text);
        UiSlotReference summary = source.DeclareSlot("summary", UiSlotValueKind.Text);
        UiSlotReference timing = source.DeclareSlot("timing", UiSlotValueKind.Text);
        UiSlotReference participants = source.DeclareSlot("participants", UiSlotValueKind.List);
        UiSlotReference marker = source.DeclareSlot("marker", UiSlotValueKind.Text);
        UiSlotReference closeLabel = source.DeclareSlot("close-label", UiSlotValueKind.Text);
        UiActionReference close = source.DeclareAction("close");
        source.AddCanvas(new UiCanvasOptions(800, 600), canvas =>
        {
            canvas.AddPanel(
                "background",
                "server-monitor.surface",
                UiChildLayout.Canvas(new UiCanvasPlacement(90, 70, 620, 460)));
            canvas.AddVertical(
                "content",
                content =>
                {
                    content.AddText("title", title);
                    content.AddText("summary", summary);
                    content.AddText("timing", timing);
                    content.AddList("participants", participants);
                    content.AddText("marker", marker);
                    content.AddButton("close", close, closeLabel);
                },
                UiChildLayout.Canvas(new UiCanvasPlacement(114, 94, 572, 412)),
                new UiFlowOptions(
                    spacing: 10,
                    padding: new UiPadding(8, 8, 8, 8),
                    alignment: UiAlignment.Stretch));
        });
        CompiledUiDefinition definition = UiDefinitionCompiler.Compile(source);
        return new Contract(
            definition,
            definition.Prepare(title),
            definition.Prepare(summary),
            definition.Prepare(timing),
            definition.Prepare(participants),
            definition.Prepare(marker),
            definition.Prepare(closeLabel),
            definition.Prepare(close));
    }

    private sealed record Contract(
        CompiledUiDefinition Definition,
        UiSlotId Title,
        UiSlotId Summary,
        UiSlotId Timing,
        UiSlotId Participants,
        UiSlotId Marker,
        UiSlotId CloseLabel,
        UiActionId Close);
}

public readonly record struct ServerMonitorUiOpenResult(
    bool Succeeded,
    UiViewInstanceId View,
    string Message);

/// <summary>
/// Game-owned read-only monitor workflow. The caller supplies sanitized authority observations;
/// the only admitted client action is Close.
/// </summary>
public sealed class ServerMonitorUiController : IDisposable
{
    private readonly IUiSemanticPort _ui;
    private readonly UiDefinitionCacheKey _cacheKey;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private UiViewInstanceId _openView;
    private UiModel? _model;
    private ulong _sequence;
    private bool _disposed;

    public ServerMonitorUiController(
        IUiSemanticPort ui,
        string serverIdentity = "local-server",
        string compositionIdentity = "game-composition")
    {
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _cacheKey = new UiDefinitionCacheKey(
            serverIdentity,
            compositionIdentity,
            protocolVersion: 1,
            ServerMonitorUi.Definition.Key,
            ServerMonitorUi.Definition.Version,
            ServerMonitorUi.Definition.Hash);
    }

    public bool HasOpenView { get { EnsureOperational(); return _openView.IsValid; } }

    public ServerMonitorUiOpenResult Open(ServerMonitorSnapshot snapshot)
    {
        EnsureOperational();
        ArgumentNullException.ThrowIfNull(snapshot);
        if (_openView.IsValid)
            return new ServerMonitorUiOpenResult(true, _openView, "Server Monitor already open.");
        UiModel model = CreateModel(snapshot);
        UiViewInstanceId view = _ui.OpenView(new UiOpenView(
            _cacheKey, ServerMonitorUi.Definition, model, sequence: 1));
        if (!view.IsValid)
            throw new InvalidOperationException("The Server Monitor UI port returned an invalid view ID.");
        try { _ui.BindActions(view, HandleAction); }
        catch
        {
            _ = _ui.CloseView(new UiCloseView(view));
            throw;
        }
        _openView = view;
        _model = model;
        _sequence = 1;
        return new ServerMonitorUiOpenResult(true, view, "Server Monitor opened.");
    }

    public bool Refresh(ServerMonitorSnapshot snapshot)
    {
        EnsureOperational();
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!_openView.IsValid || _model is null) return false;
        UiListRow[] rows = ToRows(snapshot.Participants);
        ulong sequence = checked(_sequence + 1);
        var delta = new UiModelDelta(
            ServerMonitorUi.Definition.Handle,
            sequence,
            [
                new UiScalarSlotPatch(ServerMonitorUi.Summary, UiValue.FromText(FormatSummary(snapshot))),
                new UiScalarSlotPatch(ServerMonitorUi.Timing, UiValue.FromText(FormatTiming(snapshot))),
                new UiScalarSlotPatch(ServerMonitorUi.Marker, UiValue.FromText(FormatMarker(snapshot))),
            ],
            [UiListPatch.Replace(ServerMonitorUi.Participants, rows)]);
        _ui.UpdateView(new UiUpdateView(_openView, delta));
        _sequence = sequence;
        _model.SetText(ServerMonitorUi.Summary, FormatSummary(snapshot));
        _model.SetText(ServerMonitorUi.Timing, FormatTiming(snapshot));
        _model.SetText(ServerMonitorUi.Marker, FormatMarker(snapshot));
        _model.ReplaceRows(ServerMonitorUi.Participants, rows);
        return true;
    }

    public bool Close()
    {
        EnsureOperational();
        if (!_openView.IsValid) return false;
        UiViewInstanceId view = _openView;
        _openView = default;
        _model = null;
        _sequence = 0;
        _ui.UnbindActions(view);
        return _ui.CloseView(new UiCloseView(view));
    }

    public void Dispose()
    {
        EnsureOwner();
        if (_disposed) return;
        if (_openView.IsValid) _ = Close();
        _disposed = true;
    }

    private void HandleAction(UiActionRequest request)
    {
        EnsureOperational();
        if (_openView.IsValid && request.Instance == _openView && _model is not null &&
            _model.TryValidateAction(request) && request.Action == ServerMonitorUi.Close)
            _ = Close();
    }

    private static UiModel CreateModel(ServerMonitorSnapshot snapshot)
    {
        var model = new UiModel(ServerMonitorUi.Definition);
        model.SetText(ServerMonitorUi.Title, "UltraSim Server Monitor — Read Only");
        model.SetText(ServerMonitorUi.Summary, FormatSummary(snapshot));
        model.SetText(ServerMonitorUi.Timing, FormatTiming(snapshot));
        model.ReplaceRows(ServerMonitorUi.Participants, ToRows(snapshot.Participants));
        model.SetText(ServerMonitorUi.Marker, FormatMarker(snapshot));
        model.SetText(ServerMonitorUi.CloseLabel, "Close");
        return model;
    }

    private static string FormatSummary(ServerMonitorSnapshot snapshot) =>
        $"Generation {snapshot.GenerationId} • tick {snapshot.SimulationTick} @ " +
        $"{snapshot.SimulationFrequencyHz} Hz • uptime {TimeSpan.FromSeconds(snapshot.GenerationUptimeSeconds):c}\n" +
        $"{snapshot.Participants.Length}/{snapshot.MaximumParticipants} entered • " +
        $"{snapshot.EntityCount} ECS entities • joins {snapshot.Joins} • leaves {snapshot.Leaves}";

    private static string FormatTiming(ServerMonitorSnapshot snapshot) =>
        $"Authoritative tick: {snapshot.RecentTickMicroseconds} µs recent / " +
        $"{snapshot.P95TickMicroseconds} µs p95";

    private static string FormatMarker(ServerMonitorSnapshot snapshot) =>
        string.IsNullOrEmpty(snapshot.TestMarker)
            ? "Test marker: none"
            : $"Test marker: {snapshot.TestMarker}";

    private static UiListRow[] ToRows(ReadOnlySpan<ServerMonitorParticipant> participants)
    {
        var rows = new UiListRow[participants.Length];
        for (int index = 0; index < rows.Length; index++)
        {
            ref readonly ServerMonitorParticipant participant = ref participants[index];
            string latency = participant.Connection == ServerMonitorConnectionKind.Local
                ? "Local"
                : participant.RoundTripMilliseconds == 0
                    ? "<1 ms"
                    : $"{participant.RoundTripMilliseconds} ms";
            rows[index] = new UiListRow(
                new UiRowId(participant.Participant.Value),
                [UiValue.FromText(
                    $"{participant.DisplayName} • Entered • {participant.Connection} • " +
                    $"Chunk ({participant.ChunkX},{participant.ChunkZ}) • {latency}")]);
        }
        return rows;
    }

    private void EnsureOperational()
    {
        EnsureOwner();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The Server Monitor UI belongs to another thread.");
    }
}

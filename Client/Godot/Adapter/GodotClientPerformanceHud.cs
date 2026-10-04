using System.Diagnostics;
using System.Text.Json;
using Godot;
using UltraSim.Client.Core;

namespace UltraSim.Client.Godot;

/// <summary>Cold, owner-thread context attached to a frame capture; never polled per frame.</summary>
public readonly record struct GodotClientPerformanceContext(
    string SpatialSpace, int RenderRadius, int RenderedChunks, int RenderNodes,
    int RenderResources, int DetailedActors, int BatchedActors,
    int VegetationPatches, int VegetationBlades, int PendingBuilds,
    int PendingRetirements, bool VegetationEnabled, int OutputWidth, int OutputHeight);

/// <summary>Cold, reported runtime presentation state; not a CPU/GPU throughput measurement.</summary>
internal readonly record struct GodotClientPresentationPacingSnapshot(
    string DisplayServerName, string VSyncMode, double? DisplayRefreshRateHz,
    int EngineFpsCap, string RenderingMethod, string RenderingDriver,
    string VideoAdapterName, string? XdgSessionType, string? XdgCurrentDesktop,
    string? DesktopSession)
{
    internal static GodotClientPresentationPacingSnapshot Capture()
    {
        double refresh = DisplayServer.ScreenGetRefreshRate(
            DisplayServer.WindowGetCurrentScreen());
        return new GodotClientPresentationPacingSnapshot(
            DisplayServer.GetName(), DisplayServer.WindowGetVsyncMode().ToString(),
            double.IsFinite(refresh) && refresh > 0 ? refresh : null,
            Engine.MaxFps, RenderingServer.GetCurrentRenderingMethod(),
            RenderingServer.GetCurrentRenderingDriverName(),
            RenderingServer.GetVideoAdapterName(),
            System.Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"),
            System.Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP"),
            System.Environment.GetEnvironmentVariable("DESKTOP_SESSION"));
    }
}

/// <summary>Developer-only local trace. Control Plane receives no frame-rate traffic.</summary>
public sealed partial class GodotClientPerformanceHud : PanelContainer
{
    private const int GraphColumns = 600;
    private const float PanelMargin = 12f;
    private readonly ClientFrameIntervalSampler _sampler;
    private readonly Func<GodotClientPerformanceContext> _context;
    private readonly string _edition;
    private readonly StyleBoxFlat _background;
    private readonly Label _title;
    private readonly Label _headline;
    private readonly Label _summary;
    private readonly Label _contextLabel;
    private readonly Label _status;
    private readonly GodotFrameIntervalGraph _graph;
    private readonly ClientFrameGraphColumn[] _columns = new ClientFrameGraphColumn[GraphColumns];
    private readonly uint[] _captureSamples = new uint[ClientFrameIntervalSampler.Capacity];
    private long _lastRefresh;
    private ulong _captureStartMicroseconds;
    private int _captureCount;
    private ulong _captureCoveredMicroseconds;
    private bool _capturing;
    private bool _captureComplete;
    private GodotClientPerformanceContext _latestContext;
    private ClientFrameIntervalSummary _latestSummary;
    private GodotClientPerformanceContext _captureContext;
    private ClientFrameIntervalSummary _captureSummary;
    private bool _dragging;
    private Vector2 _dragOffset;

    public GodotClientPerformanceHud(ClientFrameIntervalSampler sampler,
        Func<GodotClientPerformanceContext> context, string edition)
    {
        _sampler = sampler;
        _context = context;
        _edition = edition;
        Name = "DebugPerformance";
        Position = new Vector2(24, 72);
        CustomMinimumSize = new Vector2(850, 0);
        MouseFilter = MouseFilterEnum.Stop;
        _background = new StyleBoxFlat
        {
            BgColor = new Color(0.055f, 0.075f, 0.09f, 1f),
            BorderColor = new Color(0.35f, 0.6f, 0.7f, 1f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            ContentMarginLeft = 16,
            ContentMarginTop = 12,
            ContentMarginRight = 16,
            ContentMarginBottom = 12,
        };
        AddThemeStyleboxOverride("panel", _background);
        var body = new VBoxContainer { Name = "PerformanceBody" };
        body.AddThemeConstantOverride("separation", 8);
        AddChild(body);
        _title = new Label
        {
            Name = "PerformanceDragHandle",
            Text = "Client Performance — wall frame interval  ·  drag here to move  ·  F11 changes page",
            MouseFilter = MouseFilterEnum.Stop,
        };
        _title.GuiInput += OnTitleInput;
        body.AddChild(_title);
        _headline = new Label();
        body.AddChild(_headline);
        body.AddChild(new Label { Text = "Frame interval · last 30 eligible seconds · peaks above 50 ms clipped in graph only" });
        _graph = new GodotFrameIntervalGraph(_columns);
        body.AddChild(_graph);
        _summary = new Label();
        body.AddChild(_summary);
        _contextLabel = new Label();
        body.AddChild(_contextLabel);
        var actions = new HBoxContainer { Name = "PerformanceActions" };
        body.AddChild(actions);
        AddAction("ResetPerformance", "Reset", actions, Reset);
        AddAction("CapturePerformance30Seconds", "Capture 30s", actions, StartCapture);
        AddAction("SavePerformanceSnapshot", "Save snapshot", actions, SaveSnapshot);
        _status = new Label { Text = "Eligible focused frames only · gameplay hitches retained" };
        body.AddChild(_status);
        Refresh();
    }

    /// <summary>Per-frame gate only; expensive summary and renderer census run at most 4 Hz.</summary>
    public void Update(long timestamp)
    {
        if (_capturing && _sampler.TotalEligibleMicroseconds - _captureStartMicroseconds >= 30_000_000)
        {
            _captureCount = _sampler.CopyWindowSamples(_captureSamples, 30,
                out _captureCoveredMicroseconds);
            _captureComplete = true;
            _capturing = false;
            _captureContext = _context();
            _captureSummary = _sampler.Summarize(30);
            _status.Text = $"30s capture ready ({_captureCount:N0} frames); Save snapshot";
        }
        if (_lastRefresh != 0 && timestamp - _lastRefresh < Stopwatch.Frequency / 4) return;
        _lastRefresh = timestamp;
        Refresh();
    }

    private void Refresh()
    {
        _latestSummary = _sampler.Summarize();
        _latestContext = _context();
        GodotClientPresentationPacingSnapshot pacing =
            GodotClientPresentationPacingSnapshot.Capture();
        _sampler.FillGraphColumns(_columns);
        _graph.QueueRedraw();
        using Process process = Process.GetCurrentProcess();
        ClientFrameIntervalSummary stats = _latestSummary;
        _headline.Text = stats.Samples == 0
            ? "Warming · no eligible intervals yet"
            : $"Now  {stats.CurrentFps:0.0} FPS / {stats.CurrentMilliseconds:0.00} ms" +
              $"        Average  {stats.AverageFps:0.0} FPS / {stats.AverageMilliseconds:0.00} ms\n" +
              $"1% low  {Low(stats.OnePercentLowFps)} ({stats.OnePercentLowFrameCount:N0} frames)" +
              $"        0.1% low  {Low(stats.PointOnePercentLowFps)} ({stats.PointOnePercentLowFrameCount:N0} frames)";
        _summary.Text = $"P95 {Ms(stats.P95Milliseconds)}     P99 {Ms(stats.P99Milliseconds)}     " +
            $"P99.9 {Ms(stats.P999Milliseconds)}     Max {Ms(stats.MaximumMilliseconds)}\n" +
            $"Spikes  >33 ms: {stats.Over33Milliseconds}     >50 ms: {stats.Over50Milliseconds}" +
            $"     >100 ms: {stats.Over100Milliseconds}     " +
            $"{stats.Samples:N0} frames / {stats.EligibleMilliseconds / 1000:0.0}s eligible";
        _contextLabel.Text = $"Space {_latestContext.SpatialSpace}  Radius {_latestContext.RenderRadius}  " +
            $"Output {_latestContext.OutputWidth}×{_latestContext.OutputHeight}\n" +
            $"Pacing  VSync {pacing.VSyncMode}  FPS cap {pacing.EngineFpsCap}  " +
            $"Display {((pacing.DisplayRefreshRateHz is double hz) ? $"{hz:0.00} Hz" : "unknown")}\n" +
            $"Backend  {pacing.DisplayServerName}  " +
            $"{pacing.RenderingMethod}/{pacing.RenderingDriver}\n" +
            $"Chunks {_latestContext.RenderedChunks}  Nodes {_latestContext.RenderNodes}  " +
            $"Resources {_latestContext.RenderResources}  Actors detailed/batched " +
            $"{_latestContext.DetailedActors}/{_latestContext.BatchedActors}\n" +
            $"Vegetation {(_latestContext.VegetationEnabled ? "On" : "Off")}  " +
            $"Patches {_latestContext.VegetationPatches:N0}  Blades {_latestContext.VegetationBlades:N0}  " +
            $"Build/retire {_latestContext.PendingBuilds}/{_latestContext.PendingRetirements}\n" +
            $"Memory  RSS {process.WorkingSet64 / 1_048_576d:0.0} MiB  " +
            $"Managed {GC.GetTotalMemory(false) / 1_048_576d:0.0} MiB";
        if (_capturing)
            _status.Text = $"Capturing eligible frames… " +
                $"{(_sampler.TotalEligibleMicroseconds - _captureStartMicroseconds) / 1_000_000d:0.0}/30s";
    }

    private void Reset()
    {
        _sampler.Reset();
        _capturing = false;
        _captureComplete = false;
        _captureCount = 0;
        _lastRefresh = 0;
        _status.Text = "Reset · warming";
        Refresh();
    }

    private void StartCapture()
    {
        _captureStartMicroseconds = _sampler.TotalEligibleMicroseconds;
        _captureComplete = false;
        _capturing = true;
        _status.Text = "Capturing eligible frames… 0.0/30s";
    }

    private void SaveSnapshot()
    {
        try
        {
            string path = ProjectSettings.GlobalizePath(
                $"user://diagnostics/client-performance-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.json");
            int count = SaveDiagnosticSnapshot(path);
            _status.Text = $"Saved {count:N0} frames: {path}";
        }
        catch (Exception error)
        {
            _status.Text = $"Save failed: {error.Message}";
        }
    }

    /// <summary>Cold explicit export seam, also used by native acceptance with its own scratch path.</summary>
    public int SaveDiagnosticSnapshot(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        uint[] samples;
        ulong covered;
        if (_captureComplete)
        {
            samples = _captureSamples.AsSpan(0, _captureCount).ToArray();
            covered = _captureCoveredMicroseconds;
        }
        else
        {
            var scratch = new uint[ClientFrameIntervalSampler.Capacity];
            int count = _sampler.CopyWindowSamples(scratch, 30, out covered);
            samples = scratch.AsSpan(0, count).ToArray();
        }
        GodotClientPerformanceContext context = _captureComplete ? _captureContext : _context();
        using Process process = Process.GetCurrentProcess();
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var capture = new
        {
            Format = "UltraSimClientFrameInterval/v1",
            TimestampUtc = DateTime.UtcNow,
            Edition = _edition,
            Commit = System.Environment.GetEnvironmentVariable("ULTRASIM_COMMIT") ?? "unrecorded",
            GodotVersion = Engine.GetVersionInfo().ToString(),
            Os = OS.GetName(),
            Window = new { GetWindow().Size.X, GetWindow().Size.Y, GetWindow().Mode },
            Vsync = DisplayServer.WindowGetVsyncMode().ToString(),
            FrameCap = Engine.MaxFps,
            PresentationPacing = GodotClientPresentationPacingSnapshot.Capture(),
            Context = context,
            ProcessWorkingSetBytes = process.WorkingSet64,
            ManagedHeapBytes = GC.GetTotalMemory(false),
            IntervalUnit = "microseconds",
            EligibleCoveredMicroseconds = covered,
            CaptureComplete = _captureComplete,
            Summary = _captureComplete ? _captureSummary : _sampler.Summarize(30),
            Samples = samples,
        };
        File.WriteAllText(fullPath, JsonSerializer.Serialize(capture,
            new JsonSerializerOptions { WriteIndented = true }));
        return samples.Length;
    }

    private static string Ms(double? value) => value is null ? "warming" : $"{value:0.00} ms";
    private static string Low(double? value) => value is null ? "warming" : $"{value:0.0} FPS";

    private void OnTitleInput(InputEvent input)
    {
        switch (input)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                _dragging = button.Pressed;
                if (_dragging) _dragOffset = button.GlobalPosition - Position;
                _title.AcceptEvent();
                break;
            case InputEventMouseMotion motion when _dragging:
                Place(motion.GlobalPosition - _dragOffset);
                _title.AcceptEvent();
                break;
        }
    }

    internal void DragForAcceptance(Vector2 requested) => Place(requested);
    internal string DiagnosticContextText => _contextLabel.Text;

    private void Place(Vector2 requested)
    {
        Vector2 viewport = GetViewportRect().Size;
        Vector2 size = GetCombinedMinimumSize();
        Position = new Vector2(
            Math.Clamp(requested.X, PanelMargin, Math.Max(PanelMargin, viewport.X - size.X - PanelMargin)),
            Math.Clamp(requested.Y, PanelMargin, Math.Max(PanelMargin, viewport.Y - size.Y - PanelMargin)));
    }

    internal void RetireNativeResources() => _background.Dispose();
    internal StyleBoxFlat DiagnosticBackground => _background;

    private static void AddAction(string name, string text, Node parent, Action action)
    {
        var button = new Button { Name = name, Text = text };
        button.Pressed += action;
        parent.AddChild(button);
    }
}

/// <summary>One Control, not one node per sample. Min/max bars preserve single-frame spikes.</summary>
internal sealed partial class GodotFrameIntervalGraph : Control
{
    private const float ScaleMaximumMs = 50f;
    private readonly ClientFrameGraphColumn[] _columns;
    internal GodotFrameIntervalGraph(ClientFrameGraphColumn[] columns)
    {
        _columns = columns;
        Name = "FrameIntervalGraph";
        CustomMinimumSize = new Vector2(800, 270);
        MouseFilter = MouseFilterEnum.Ignore;
        AddGuide("Guide120", "8.33 ms", 8.333f);
        AddGuide("Guide60", "16.67 ms", 16.667f);
        AddGuide("Guide30", "33.33 ms", 33.333f);
    }

    public override void _Draw()
    {
        float width = Size.X;
        float height = Size.Y;
        float plotHeight = height - 12;
        DrawReference(8.333f);
        DrawReference(16.667f);
        DrawReference(33.333f);
        for (int index = 0; index < _columns.Length; index++)
        {
            ClientFrameGraphColumn column = _columns[index];
            if (!column.HasSamples) continue;
            float x = index * width / _columns.Length;
            float top = plotHeight * (1 - Math.Min(column.MaximumMicroseconds / 1000f, ScaleMaximumMs) / ScaleMaximumMs);
            float bottom = plotHeight * (1 - Math.Min(column.MinimumMicroseconds / 1000f, ScaleMaximumMs) / ScaleMaximumMs);
            DrawLine(new Vector2(x, top), new Vector2(x, Math.Max(top + 1, bottom)),
                column.MaximumMicroseconds > 33_000 ? new Color(1f, 0.48f, 0.26f) :
                new Color(0.34f, 0.91f, 0.67f), 1f);
        }

        void DrawReference(float milliseconds)
        {
            float y = plotHeight * (1 - milliseconds / ScaleMaximumMs);
            DrawLine(new Vector2(76, y), new Vector2(width, y),
                new Color(0.45f, 0.57f, 0.66f, 0.7f), 1f);
        }
    }

    private void AddGuide(string name, string text, float milliseconds)
    {
        AddChild(new Label
        {
            Name = name,
            Text = text,
            Position = new Vector2(2, (270 - 12) * (1 - milliseconds / ScaleMaximumMs) - 11),
            MouseFilter = MouseFilterEnum.Ignore,
        });
    }
}

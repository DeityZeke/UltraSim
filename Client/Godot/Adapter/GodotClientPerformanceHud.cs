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

/// <summary>Developer-only local trace. Control Plane receives no frame-rate traffic.</summary>
public sealed partial class GodotClientPerformanceHud : PanelContainer
{
    private const int GraphColumns = 600;
    private readonly ClientFrameIntervalSampler _sampler;
    private readonly Func<GodotClientPerformanceContext> _context;
    private readonly string _edition;
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

    public GodotClientPerformanceHud(ClientFrameIntervalSampler sampler,
        Func<GodotClientPerformanceContext> context, string edition)
    {
        _sampler = sampler;
        _context = context;
        _edition = edition;
        Name = "DebugPerformance";
        Position = new Vector2(12, 48);
        CustomMinimumSize = new Vector2(640, 400);
        MouseFilter = MouseFilterEnum.Stop;
        var body = new VBoxContainer { Name = "PerformanceBody" };
        AddChild(body);
        body.AddChild(new Label { Text = "Client Performance · frame interval (wall) · guides: 120 / 60 / 30 FPS" });
        _headline = new Label();
        body.AddChild(_headline);
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
        _status = new Label { Text = "Eligible focused frames only · F11 changes page" };
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
        _sampler.FillGraphColumns(_columns);
        _graph.QueueRedraw();
        using Process process = Process.GetCurrentProcess();
        ClientFrameIntervalSummary stats = _latestSummary;
        _headline.Text = stats.Samples == 0
            ? "Warming · no eligible intervals yet"
            : $"{stats.CurrentFps:0.0} FPS  {stats.CurrentMilliseconds:0.00} ms    " +
              $"Avg {stats.AverageFps:0.0} FPS  {stats.AverageMilliseconds:0.00} ms    " +
              $"1% low {Low(stats.OnePercentLowFps)}  0.1% low {Low(stats.PointOnePercentLowFps)}";
        _summary.Text = $"P95 {Ms(stats.P95Milliseconds)}  P99 {Ms(stats.P99Milliseconds)}  " +
            $"P99.9 {Ms(stats.P999Milliseconds)}  Max {Ms(stats.MaximumMilliseconds)}\n" +
            $">33 ms {stats.Over33Milliseconds}  >50 ms {stats.Over50Milliseconds}  " +
            $">100 ms {stats.Over100Milliseconds}  Samples {stats.Samples:N0} " +
            $"({stats.EligibleMilliseconds / 1000:0.0}s eligible)";
        _contextLabel.Text = $"Space {_latestContext.SpatialSpace}  Radius {_latestContext.RenderRadius}  " +
            $"Output {_latestContext.OutputWidth}×{_latestContext.OutputHeight}\n" +
            $"Chunks {_latestContext.RenderedChunks}  Nodes {_latestContext.RenderNodes}  " +
            $"Resources {_latestContext.RenderResources}  Actors detailed/batched " +
            $"{_latestContext.DetailedActors}/{_latestContext.BatchedActors}\n" +
            $"Vegetation {(_latestContext.VegetationEnabled ? "On" : "Off")}  " +
            $"Patches {_latestContext.VegetationPatches:N0}  Blades {_latestContext.VegetationBlades:N0}  " +
            $"Build/retire {_latestContext.PendingBuilds}/{_latestContext.PendingRetirements}  " +
            $"RSS {process.WorkingSet64 / 1_048_576d:0.0} MiB  " +
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
    private readonly ClientFrameGraphColumn[] _columns;
    internal GodotFrameIntervalGraph(ClientFrameGraphColumn[] columns)
    {
        _columns = columns;
        Name = "FrameIntervalGraph";
        CustomMinimumSize = new Vector2(600, 170);
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
        const float scaleMaximumMs = 100f;
        DrawReference(8.333f);
        DrawReference(16.667f);
        DrawReference(33.333f);
        for (int index = 0; index < _columns.Length; index++)
        {
            ClientFrameGraphColumn column = _columns[index];
            if (!column.HasSamples) continue;
            float x = index * width / _columns.Length;
            float top = plotHeight * (1 - Math.Min(column.MaximumMicroseconds / 1000f, scaleMaximumMs) / scaleMaximumMs);
            float bottom = plotHeight * (1 - Math.Min(column.MinimumMicroseconds / 1000f, scaleMaximumMs) / scaleMaximumMs);
            DrawLine(new Vector2(x, top), new Vector2(x, Math.Max(top + 1, bottom)),
                column.MaximumMicroseconds > 33_000 ? new Color(1f, 0.48f, 0.26f) :
                new Color(0.34f, 0.91f, 0.67f), 1f);
        }

        void DrawReference(float milliseconds)
        {
            float y = plotHeight * (1 - milliseconds / scaleMaximumMs);
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
            Position = new Vector2(2, (170 - 12) * (1 - milliseconds / 100) - 11),
            MouseFilter = MouseFilterEnum.Ignore,
        });
    }
}

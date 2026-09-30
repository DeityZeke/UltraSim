using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Godot;
using ChildProcess = System.Diagnostics.Process;

namespace UltraSim.Server.Operator.Godot;

/// <summary>
/// Optional local operator window. The headless child remains the only authority: this process
/// owns no Game/Network/Persistence object and communicates over inherited bounded stdio only.
/// All action frames are resolved by the child's Control Plane owner thread.
/// </summary>
public partial class ServerGuiRoot : Control
{
    private const int MaximumPendingLines = 512;
    private const int MaximumVisibleLines = 400;
    private readonly ConcurrentQueue<(bool Error, string Text)> _incoming = new();
    private readonly Queue<string> _visibleLines = new();
    private ChildProcess? _child;
    private string? _headlessBinary;
    private string? _identityDirectory;
    private Label _banner = null!;
    private TextEdit _status = null!;
    private TextEdit _participants = null!;
    private TextEdit _events = null!;
    private TextEdit _actions = null!;
    private TextEdit _backups = null!;
    private TextEdit _console = null!;
    private LineEdit _command = null!;
    private LineEdit _kickTarget = null!;
    private ConfirmationDialog _confirmation = null!;
    private string? _pendingAction;
    private ulong _pendingTarget;
    private ulong _requestId;
    private int _pendingCount;
    private int _droppedLines;
    private int _inspectorRunning;
    private double _refreshSeconds;
    private bool _ready;
    private bool _shutdownRequested;
    private bool _quitting;
    private bool _smoke;
    private bool _restartSmoke;
    private int _smokeStage;
    private long _smokeDeadline;

    public override void _Ready()
    {
        GetTree().AutoAcceptQuit = false;
        BuildInterface();
        string[] args = OS.GetCmdlineUserArgs();
        _restartSmoke = args.Contains("--p7-gui-restart-smoke", StringComparer.Ordinal);
        _smoke = _restartSmoke || args.Contains("--p7-gui-smoke", StringComparer.Ordinal);
        _smokeDeadline = System.Environment.TickCount64 + 60_000;
        try { StartChild(args); }
        catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException)
        {
            _banner.Text = "Server launch failed: " + error.Message;
            AddVisible("Launch failed: " + error);
            if (_smoke) SmokeFailure("launch-failed");
        }
    }

    public override void _Process(double delta)
    {
        for (int i = 0; i < 64 && _incoming.TryDequeue(out (bool Error, string Text) line); i++)
        {
            Interlocked.Decrement(ref _pendingCount);
            if (line.Error) AddVisible("stderr: " + line.Text);
            else ConsumeOutput(line.Text);
        }
        int dropped = Interlocked.Exchange(ref _droppedLines, 0);
        if (dropped != 0) AddVisible("[bounded GUI output queue dropped " + dropped + " lines]");

        if (_child is { HasExited: true } child && !_quitting)
        {
            int exit = child.ExitCode;
            _banner.Text = "Headless server exited with code " + exit;
            AddVisible("Headless server exit code: " + exit);
            child.Dispose();
            _child = null;
            if (_smoke)
            {
                if ((_smokeStage == 4 || _smokeStage == 11) && exit == 0)
                {
                    GD.Print(_restartSmoke
                        ? "PASS P7_GodotServerGuiSupervisedRestart reopened=true shutdown=clean"
                        : "PASS P7_GodotServerGuiPhysicalActions save=complete backup=complete shutdown=clean");
                    _ = QuitAfterRetirement();
                }
                else SmokeFailure("server-exit-" + exit);
            }
            else if (_shutdownRequested && exit == 0)
                _ = QuitAfterRetirement();
        }

        if (!_ready || _child is null || _shutdownRequested) return;
        _refreshSeconds += delta;
        if (_refreshSeconds >= (_smoke ? .25 : 2.0) && Volatile.Read(ref _pendingCount) < 64)
        {
            _refreshSeconds = 0;
            Send("status");
            if (!_smoke) { Send("participants"); Send("log"); }
        }
        if (_smoke && System.Environment.TickCount64 > _smokeDeadline) SmokeFailure("timeout");
    }

    public override void _Notification(int what)
    {
        if (what != NotificationWMCloseRequest) return;
        if (_child is { HasExited: false })
        {
            _shutdownRequested = true;
            Send("action", "shutdown", confirmed: true);
            _banner.Text = "Graceful server shutdown requested; waiting for durable completion.";
        }
        else _ = QuitAfterRetirement();
    }

    public override void _ExitTree()
    {
        // If the GUI is forcibly killed, EOF on the inherited stdin is itself a host-owned
        // graceful-shutdown request. The separate child cannot touch this Godot native lifetime.
        try { _child?.StandardInput.Close(); }
        catch (Exception error) when (error is IOException or InvalidOperationException or ObjectDisposedException)
        { /* the separate process has already retired its inherited pipe */ }
        _child?.Dispose();
        _child = null;
    }

    private void BuildInterface()
    {
        var layout = new VBoxContainer { Name = "OperatorLayout", OffsetLeft = 12,
            OffsetTop = 12, OffsetRight = -12, OffsetBottom = -12 };
        layout.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(layout);
        _banner = new Label { Text = "Starting the dedicated server…" };
        layout.AddChild(_banner);
        var toolbar = new HBoxContainer { Name = "OperatorActions" };
        layout.AddChild(toolbar);
        AddButton(toolbar, "Save Now", () => Send("action", "save-now"));
        AddButton(toolbar, "Backup Now", () => Send("action", "backup-now"));
        AddButton(toolbar, "Restart with Save", () => Confirm("restart-with-save", 0,
            "Save, retire and reopen this deployment?"));
        AddButton(toolbar, "Shutdown", () => Confirm("shutdown", 0,
            "Save and stop this server?"));
        AddButton(toolbar, "Refresh", () => { Send("status"); Send("participants");
            Send("log"); Send("audit"); RefreshBackups(); });
        _kickTarget = new LineEdit { PlaceholderText = "Participant ID", MaxLength = 20,
            CustomMinimumSize = new Vector2(130, 0) };
        toolbar.AddChild(_kickTarget);
        AddButton(toolbar, "Kick", () =>
        {
            if (ulong.TryParse(_kickTarget.Text, NumberStyles.None,
                    CultureInfo.InvariantCulture, out ulong id) && id != 0)
                Confirm("kick", id, "Disconnect exact participant " + id + "?");
            else AddVisible("Kick requires a positive participant ID from the participant view.");
        });
        _confirmation = new ConfirmationDialog { Name = "OperatorConfirmation" };
        _confirmation.Confirmed += () =>
        {
            if (_pendingAction is { } action) Send("action", action, _pendingTarget, true);
            _pendingAction = null;
        };
        AddChild(_confirmation);

        var tabs = new TabContainer { Name = "OperatorCategories",
            SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        layout.AddChild(tabs);
        _status = AddPage(tabs, "Status");
        _participants = AddPage(tabs, "Participants");
        _events = AddPage(tabs, "Live Log and Audit");
        _backups = AddPage(tabs, "Recovery Candidates");
        _actions = AddPage(tabs, "Control Plane");
        _console = AddPage(tabs, "Console Output");
        var input = new HBoxContainer { Name = "OperatorCommandInput" };
        layout.AddChild(input);
        input.AddChild(new Label { Text = "Command" });
        _command = new LineEdit { MaxLength = 256, PlaceholderText = "help / status / save / backup / …",
            SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _command.TextSubmitted += command =>
        {
            Send("command", command: command);
            _command.Clear();
        };
        input.AddChild(_command);
        AddButton(input, "Send", () => { Send("command", command: _command.Text); _command.Clear(); });
    }

    private static TextEdit AddPage(TabContainer tabs, string title)
    {
        var page = new TextEdit { Name = title, Editable = false,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        tabs.AddChild(page);
        return page;
    }

    private static void AddButton(Node parent, string text, Action action)
    {
        var button = new Button { Text = text };
        button.Pressed += action;
        parent.AddChild(button);
    }

    private void Confirm(string action, ulong target, string prompt)
    {
        _pendingAction = action;
        _pendingTarget = target;
        _confirmation.DialogText = prompt;
        _confirmation.PopupCentered();
    }

    private void StartChild(string[] args)
    {
        string executableDirectory = Path.GetDirectoryName(OS.GetExecutablePath()) ??
            throw new InvalidOperationException("The Godot executable has no parent directory.");
        string launcher = args.FirstOrDefault(arg => arg.StartsWith("--p7-server-launcher=", StringComparison.Ordinal))?
            ["--p7-server-launcher=".Length..] ??
            Path.Combine(executableDirectory, "headless", "run-managed.sh");
        if (!File.Exists(launcher))
            throw new IOException("The packaged headless launcher is missing: " + launcher);
        string identity = args.FirstOrDefault(arg => arg.StartsWith("--p7-identity-dir=", StringComparison.Ordinal))?
            ["--p7-identity-dir=".Length..] ?? Path.Combine(OS.GetUserDataDir(), "server");
        if (string.IsNullOrWhiteSpace(identity)) throw new ArgumentException("Server identity path is empty.");
        _identityDirectory = Path.GetFullPath(identity);
        _headlessBinary = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(launcher))!,
            "UltraSim.Server.DirectConnect");
        var start = new ProcessStartInfo(Path.GetFullPath(launcher))
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("--identity-dir=" + Path.GetFullPath(identity));
        start.ArgumentList.Add("--shutdown-on-stdin-eof");
        foreach (string argument in args.Where(arg => arg.StartsWith("--p7-server-arg=", StringComparison.Ordinal)))
        {
            string value = argument["--p7-server-arg=".Length..];
            if (value.Length is < 3 or > 256 || !value.StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("One forwarded server option is invalid.");
            start.ArgumentList.Add(value);
        }
        _child = ChildProcess.Start(start) ?? throw new IOException("The headless server did not start.");
        _child.OutputDataReceived += (_, data) => Queue(false, data.Data);
        _child.ErrorDataReceived += (_, data) => Queue(true, data.Data);
        _child.BeginOutputReadLine();
        _child.BeginErrorReadLine();
        _banner.Text = "Starting headless server using " + launcher;
        AddVisible("Deployment identity: " + identity);
        RefreshBackups();
    }

    private void Queue(bool error, string? value)
    {
        if (value is null) return;
        if (Interlocked.Increment(ref _pendingCount) > MaximumPendingLines)
        {
            Interlocked.Decrement(ref _pendingCount);
            Interlocked.Increment(ref _droppedLines);
            return;
        }
        _incoming.Enqueue((error, value));
    }

    private void ConsumeOutput(string line)
    {
        if (line.StartsWith("P7BACKUPS ", StringComparison.Ordinal))
        {
            try
            {
                InspectorReply? reply = JsonSerializer.Deserialize<InspectorReply>(line.AsSpan(10));
                _backups.Text = (reply?.Text ?? "Backup inspection returned no result.") +
                    "\nSelected restore is stopped-server maintenance: " +
                    "--restore-backup-sequence=<validated sequence>.";
            }
            catch (JsonException error) { _backups.Text = "Invalid backup inspection: " + error.Message; }
            return;
        }
        if (line.StartsWith("P7GUI ", StringComparison.Ordinal))
        {
            try
            {
                OperatorGuiReply? reply = JsonSerializer.Deserialize<OperatorGuiReply>(line.AsSpan(6));
                if (reply is null) throw new JsonException("Empty GUI response.");
                switch (reply.Kind)
                {
                    case "discover": _actions.Text = reply.Text; break;
                    case "status": _status.Text = reply.Text; SmokeStatus(reply.Text); break;
                    case "participants": _participants.Text = reply.Text; break;
                    case "log": _events.Text = reply.Text; break;
                    case "audit": _events.Text += "\nAudit:\n" + reply.Text; break;
                    default: AddVisible(reply.Kind + ": " + reply.Text); break;
                }
                if (_smoke && reply.Kind == "action") SmokeAction(reply);
            }
            catch (JsonException error) { AddVisible("Invalid bounded GUI response: " + error.Message); }
            return;
        }
        AddVisible(line);
        if (_restartSmoke && _smokeStage == 10 &&
            line.Contains("server supervisor: reopening the saved deployment.", StringComparison.Ordinal))
            _ready = false;
        if (!_ready && line.Contains("UltraSim Direct Connect Concept Demo server ready.",
                StringComparison.Ordinal))
        {
            _ready = true;
            _banner.Text = "Dedicated server running (local Control Plane frontend).";
            Send("discover"); Send("status"); Send("participants"); Send("log");
            if (_restartSmoke)
            {
                if (_smokeStage == 10)
                {
                    Send("action", "shutdown", confirmed: true);
                    _shutdownRequested = true;
                    _smokeStage = 11;
                }
                else
                {
                    Send("action", "restart-with-save", confirmed: true);
                    _smokeStage = 10;
                }
            }
            else if (_smoke) { Send("action", "save-now"); _smokeStage = 1; }
        }
    }

    private void AddVisible(string line)
    {
        if (line.Length > 512) line = line[..512] + "…";
        _visibleLines.Enqueue(line);
        while (_visibleLines.Count > MaximumVisibleLines) _visibleLines.Dequeue();
        _console.Text = string.Join('\n', _visibleLines);
    }

    private void RefreshBackups()
    {
        if (_headlessBinary is null || _identityDirectory is null ||
            Interlocked.CompareExchange(ref _inspectorRunning, 1, 0) != 0) return;
        string binary = _headlessBinary;
        string identity = _identityDirectory;
        _backups.Text = "Validating published backup candidates off the authority thread…";
        _ = Task.Run(() =>
        {
            string result;
            try
            {
                var start = new ProcessStartInfo(binary)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                start.ArgumentList.Add("--identity-dir=" + identity);
                start.ArgumentList.Add("--list-backups");
                using ChildProcess process = ChildProcess.Start(start) ??
                    throw new IOException("Backup inspection did not start.");
                Task<string> output = process.StandardOutput.ReadToEndAsync();
                Task<string> error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(30_000))
                {
                    process.Kill(entireProcessTree: true);
                    result = "Backup inspection exceeded its 30-second frontend bound.";
                }
                else
                {
                    result = output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult();
                    if (process.ExitCode != 0) result = "Inspection rejected: " + result;
                }
            }
            catch (Exception failure)
            {
                // The detached frontend scan must never become an unobserved task fault.
                // Unexpected failures are surfaced verbatim as a visible product error.
                result = "Backup inspection failed: " + failure;
            }
            finally { Interlocked.Exchange(ref _inspectorRunning, 0); }
            if (result.Length > 32768) result = result[..32768] + "\n[truncated]";
            Queue(false, "P7BACKUPS " + JsonSerializer.Serialize(new InspectorReply(result)));
        });
    }

    private void Send(string kind, string? action = null, ulong target = 0,
        bool confirmed = false, string? command = null)
    {
        if (_child is null || _child.HasExited || _requestId == ulong.MaxValue) return;
        ulong request = ++_requestId;
        string frame = "@P7GUI " + JsonSerializer.Serialize(new OperatorGuiFrame(
            request, kind, action, target, confirmed, command));
        if (frame.Length > 1024) { AddVisible("Request exceeds local frontend limit."); return; }
        try { _child.StandardInput.WriteLine(frame); _child.StandardInput.Flush(); }
        catch (IOException error) { AddVisible("Server input is unavailable: " + error.Message); }
    }

    private void SmokeAction(OperatorGuiReply reply)
    {
        if (reply.Status != "accepted")
            SmokeFailure("action-rejected: " + reply.Text);
    }

    private void SmokeStatus(string text)
    {
        if (!_smoke) return;
        if (_smokeStage == 1 && text.Contains("last-save=1", StringComparison.Ordinal) &&
            text.Contains("save-in-progress=false", StringComparison.Ordinal))
        {
            Send("action", "backup-now");
            _smokeStage = 2;
        }
        else if (_smokeStage == 2 && text.Contains("last-backup=1", StringComparison.Ordinal) &&
            text.Contains("backup-in-progress=false", StringComparison.Ordinal))
        {
            Send("action", "shutdown", confirmed: true);
            _shutdownRequested = true;
            _smokeStage = 4;
        }
    }

    private void SmokeFailure(string reason)
    {
        GD.PrintErr("FAIL P7_GodotServerGuiPhysicalActions " + reason);
        foreach (string line in _visibleLines.TakeLast(16)) GD.PrintErr("P7 GUI child: " + line);
        GetTree().Quit(1);
    }

    private async Task QuitAfterRetirement()
    {
        if (_quitting) return;
        _quitting = true;
        foreach (Node child in GetChildren()) child.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (_smoke && (_status.NativeInstance != IntPtr.Zero ||
            _confirmation.NativeInstance != IntPtr.Zero))
        {
            GD.PrintErr("FAIL P7_GodotServerGuiNativeRetirement owned nodes survived deletion queue");
            GetTree().Quit(1);
            return;
        }
        GetTree().Quit();
    }

    private sealed record OperatorGuiFrame(ulong RequestId, string Kind, string? Action,
        ulong Target, bool Confirmed, string? Command);
    private sealed record OperatorGuiReply(ulong RequestId, string Kind, string Status, string Text);
    private sealed record InspectorReply(string Text);
}

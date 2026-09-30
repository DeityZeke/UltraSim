using Godot;

namespace UltraSim.Client.Godot;

public abstract partial class ClientRoot
{
    private readonly GodotApplicationLifetime _lifetime = new();
    private Task? _shutdownTask;
    private bool _applicationCleanupStarted;
    private GodotClientAudioAdapter? _retiredAudioAdapter;

    public GodotApplicationPhase ApplicationPhase => _lifetime.Phase;
    public Task ShutdownCompletion => _lifetime.ShutdownCompletion;
    protected bool CanStartApplicationWork => _lifetime.CanStartWork;

    public override void _Process(double delta)
    {
        if (!CanStartApplicationWork || _audioAdapter is null) return;
        if (!_shell.Snapshot().SessionActive && _audioAdapter.GetSnapshot().SessionActive)
            _audioAdapter.EndSession();
        _audioAdapter.ProcessFrame();
    }

    protected void BeginApplicationStartup()
    {
        GetTree().AutoAcceptQuit = false;
        _lifetime.BeginStartup();
    }

    protected void CompleteApplicationStartup() => _lifetime.CompleteStartup();

    /// <summary>Coalesces exit requests. Actual retirement runs after the current callback unwinds.</summary>
    protected void RequestApplicationExit(int exitCode = 0)
    {
        if (!_lifetime.RequestStop(exitCode)) return;
        ProcessMode = ProcessModeEnum.Disabled;
        Callable.From(() => { _shutdownTask = ShutdownApplicationAsync(); }).CallDeferred();
    }

    private async Task ShutdownApplicationAsync()
    {
        if (!_lifetime.BeginStop()) return;
        SceneTree tree = GetTree();
        try
        {
            RetireApplication();
            // The deferred callback can run before this frame's process_frame signal. Observe
            // two boundaries to guarantee an intervening end-of-frame deletion-queue flush.
            // This acknowledges Godot's queue; it does not wait for or promise a GC/finalizer drain.
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            if (_lifetime.Phase != GodotApplicationPhase.Stopping) return;
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            if (_lifetime.Phase != GodotApplicationPhase.Stopping) return;
            if (_retiredAudioAdapter is not null)
            {
                // AudioServer stops playbacks asynchronously: its mix thread fades them,
                // then main-thread cleanup releases its RefCounted references. Observe
                // actual native reference retirement, not a fixed shutdown sleep.
                int audioFrames = 0;
                while (!_retiredAudioAdapter.PlaybackOwnershipRetired && audioFrames++ < 240)
                {
                    await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
                    if (_lifetime.Phase != GodotApplicationPhase.Stopping) return;
                }
                if (!_retiredAudioAdapter.PlaybackOwnershipRetired)
                    throw new InvalidOperationException(
                        "AudioServer retained stopped playback beyond the bounded shutdown window.");
            }
            _retiredAudioAdapter?.CompleteRetirement();
            _retiredAudioAdapter = null;
            // Audio playback retirement is native-server work, distinct from the scene
            // deletion queue. Give its final resource release a frame boundary as well.
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            if (_lifetime.Phase != GodotApplicationPhase.Stopping) return;
            _lifetime.CompleteStop();
            tree.Quit(_lifetime.ExitCode);
        }
        catch (Exception error)
        {
            _lifetime.FailStop(error);
            // A cleanup failure is observable and must not be published as successful retirement.
            // Keep the engine available rather than tearing it down under failed native owners.
            System.Console.Error.WriteLine($"UltraSim application retirement failed: {error}");
            GD.PushError($"UltraSim application retirement failed: {error}");
        }
    }

    private void RetireApplication()
    {
        if (_applicationCleanupStarted) return;
        _applicationCleanupStarted = true;
        List<Exception>? failures = null;
        Run(RetireCurrentSession);
        Run(RetireApplicationServices);
        Run(RetireAdapterControl);
        Run(() => { _uiComposition?.Dispose(); _uiComposition = null; });
        if (failures is not null)
            throw new AggregateException("Application owners did not all retire.", failures);

        void Run(Action retirement)
        {
            try { retirement(); }
            catch (Exception error) { (failures ??= []).Add(error); }
        }
    }

    /// <summary>Known product services retire after presentation and before persistent shell UI.</summary>
    protected virtual void RetireApplicationServices()
    {
        if (_uiComposition is not null)
            _uiComposition.Gumps.WindowChanged -= OnGumpWindowChanged;
        _audioAdapter?.BeginRetirement();
        _retiredAudioAdapter = _audioAdapter;
        _audioAdapter = null;
    }

    protected void RetireOnTreeExit()
    {
        if (_lifetime.Phase == GodotApplicationPhase.Stopped) return;
        // Fallback for external tree destruction. Normal window/menu/test exits use the owned task.
        try
        {
            RetireApplication();
            _retiredAudioAdapter?.CompleteRetirement();
            _retiredAudioAdapter = null;
        }
        catch (Exception error) { _lifetime.FailStop(error); throw; }
        _lifetime.FailStop(new InvalidOperationException(
            "Godot exited the tree before application retirement completed."));
    }
}

namespace UltraSim.Client.Godot;

/// <summary>Application-owned milestones; no event bus, listener list, timers, or engine objects.</summary>
public enum GodotApplicationPhase { Created, Starting, Running, StopRequested, Stopping, Stopped, Faulted }

internal sealed class GodotApplicationLifetime
{
    private readonly int _ownerThread = System.Environment.CurrentManagedThreadId;
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal GodotApplicationPhase Phase { get; private set; }
    internal int ExitCode { get; private set; }
    internal Task ShutdownCompletion => _completion.Task;
    internal bool CanStartWork => Phase is GodotApplicationPhase.Starting or GodotApplicationPhase.Running;

    internal void BeginStartup()
    {
        EnsureOwner();
        if (Phase != GodotApplicationPhase.Created) throw new InvalidOperationException("Application already started.");
        Phase = GodotApplicationPhase.Starting;
    }

    internal void CompleteStartup()
    {
        EnsureOwner();
        // A close request during startup wins; startup must not resurrect a stopping application.
        if (Phase == GodotApplicationPhase.Starting) Phase = GodotApplicationPhase.Running;
    }

    internal bool RequestStop(int exitCode)
    {
        EnsureOwner();
        if (Phase is GodotApplicationPhase.Stopped or GodotApplicationPhase.Faulted) return false;
        if (exitCode != 0 && ExitCode == 0) ExitCode = exitCode;
        if (Phase is GodotApplicationPhase.StopRequested or GodotApplicationPhase.Stopping) return false;
        Phase = GodotApplicationPhase.StopRequested;
        return true;
    }

    internal bool BeginStop()
    {
        EnsureOwner();
        if (Phase != GodotApplicationPhase.StopRequested) return false;
        Phase = GodotApplicationPhase.Stopping;
        return true;
    }

    internal void CompleteStop()
    {
        EnsureOwner();
        if (Phase != GodotApplicationPhase.Stopping) throw new InvalidOperationException("Application is not stopping.");
        Phase = GodotApplicationPhase.Stopped;
        _completion.SetResult();
    }

    internal void FailStop(Exception error)
    {
        EnsureOwner();
        ArgumentNullException.ThrowIfNull(error);
        if (Phase is GodotApplicationPhase.Stopped or GodotApplicationPhase.Faulted) return;
        Phase = GodotApplicationPhase.Faulted;
        _completion.SetException(error);
    }

    private void EnsureOwner()
    {
        if (_ownerThread != System.Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Application lifecycle is engine-thread-bound.");
    }
}

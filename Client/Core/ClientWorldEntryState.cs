namespace UltraSim.Client.Core;

/// <summary>Presentation-only progress for one world-entry attempt. No Game truth is owned here.</summary>
public enum ClientWorldEntryStage : byte
{
    None = 0,
    Resolving = 1,
    StartingAuthority = 2,
    PreparingContent = 3,
    PreparingMaterials = 4,
    PreparingMeshes = 5,
    BuildingWorld = 6,
    WarmingPresentation = 7,
    Finalizing = 8,
    Ready = 9,
    Cancelled = 10,
    Failed = 11,
}

public readonly record struct ClientWorldEntryProgress(
    ClientWorldEntryStage Stage,
    string Detail,
    int CompletedUnits = 0,
    int? TotalUnits = null,
    bool CanCancel = true,
    string? Failure = null)
{
    public bool IsTerminal => Stage is ClientWorldEntryStage.Ready or
        ClientWorldEntryStage.Cancelled or ClientWorldEntryStage.Failed;

    public void Validate()
    {
        if (Stage is < ClientWorldEntryStage.Resolving or > ClientWorldEntryStage.Failed ||
            string.IsNullOrWhiteSpace(Detail) ||
            Detail.Length > 256 || CompletedUnits < 0 ||
            TotalUnits is < 1 || TotalUnits is null && CompletedUnits != 0 ||
            TotalUnits is int total && CompletedUnits > total ||
            (Stage == ClientWorldEntryStage.Failed) != (Failure is not null) ||
            Failure is { Length: > 1024 } ||
            Failure is not null && string.IsNullOrWhiteSpace(Failure) ||
            IsTerminal && CanCancel)
            throw new ArgumentException("Invalid world-entry progress.");
    }
}

/// <summary>Owner-thread, finite and monotone entry status; renderer-independent.</summary>
public sealed class ClientWorldEntryCoordinator
{
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private ClientWorldEntryProgress _progress;
    public ClientWorldEntryProgress Progress { get { EnsureOwner(); return _progress; } }

    public void Publish(ClientWorldEntryProgress progress)
    {
        EnsureOwner();
        progress.Validate();
        if (_progress.IsTerminal || progress.Stage < _progress.Stage)
            throw new InvalidOperationException("World entry cannot regress or leave a terminal state.");
        _progress = progress;
    }

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("World-entry progress is owner-thread bound.");
    }
}

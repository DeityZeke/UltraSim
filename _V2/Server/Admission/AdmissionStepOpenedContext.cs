using UltraSim.Network;
using UltraSim.UI;

namespace UltraSim.Server.Admission;

public sealed class AdmissionStepOpenedContext
{
    private readonly Func<long> _nextModelUpdateSequence;

    public AdmissionStepOpenedContext(
        ConnectionId connectionId,
        SessionAdmissionState state,
        UiBuiltPanel panel,
        IAdmissionPanelGateway panels,
        Func<long> nextModelUpdateSequence)
    {
        ConnectionId = connectionId;
        State = state ?? throw new ArgumentNullException(nameof(state));
        Panel = panel ?? throw new ArgumentNullException(nameof(panel));
        Panels = panels ?? throw new ArgumentNullException(nameof(panels));
        _nextModelUpdateSequence = nextModelUpdateSequence ?? throw new ArgumentNullException(nameof(nextModelUpdateSequence));
    }

    public ConnectionId ConnectionId { get; }
    public SessionAdmissionState State { get; }
    public UiBuiltPanel Panel { get; }
    public IAdmissionPanelGateway Panels { get; }

    public long NextModelUpdateSequence()
    {
        return _nextModelUpdateSequence();
    }
}

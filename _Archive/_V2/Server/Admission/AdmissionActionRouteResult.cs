namespace UltraSim.Server.Admission;

public readonly record struct AdmissionActionRouteResult(
    bool ShouldComplete,
    string CompletedEvent,
    string? FailureStatusSlotId,
    string? FailureStatusValue)
{
    public static AdmissionActionRouteResult Complete(string completedEvent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(completedEvent);
        return new AdmissionActionRouteResult(true, completedEvent.Trim(), null, null);
    }

    public static AdmissionActionRouteResult StayOnStep(string? failureStatusSlotId = null, string? failureStatusValue = null)
    {
        return new AdmissionActionRouteResult(
            false,
            string.Empty,
            string.IsNullOrWhiteSpace(failureStatusSlotId) ? null : failureStatusSlotId.Trim(),
            string.IsNullOrWhiteSpace(failureStatusValue) ? null : failureStatusValue.Trim());
    }
}

namespace UltraSim.UI;

public enum UiActionAvailability : byte
{
    Available = 1,
    Disabled = 2,
    StaleTarget = 3,
    TargetMissing = 4,
    OutOfRange = 5,
    Unauthorized = 6,
    RateLimited = 7,
    Busy = 8,
}

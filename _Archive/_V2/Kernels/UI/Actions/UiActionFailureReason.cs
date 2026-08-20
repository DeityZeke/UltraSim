namespace UltraSim.UI;

public enum UiActionFailureReason : byte
{
    None = 0,
    UnknownAction = 1,
    InvalidPanel = 2,
    InvalidContext = 3,
    StalePanel = 4,
    StaleTarget = 5,
    TargetMissing = 6,
    OutOfRange = 7,
    Unauthorized = 8,
    RateLimited = 9,
    ValidationFailed = 10,
    Conflict = 11,
    ServerError = 12,
}

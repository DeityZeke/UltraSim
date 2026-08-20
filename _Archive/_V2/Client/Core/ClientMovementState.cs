namespace UltraSim.Client;

/// <summary>
/// Minimal authoritative/predicted 2D movement state for the first client/network slice.
/// </summary>
public readonly record struct ClientMovementState(float PositionX, float PositionY, float VelocityX, float VelocityY);

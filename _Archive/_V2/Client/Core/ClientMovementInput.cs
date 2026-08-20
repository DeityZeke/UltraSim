namespace UltraSim.Client;

/// <summary>
/// Minimal local movement input contract for the first client/network vertical slice.
/// </summary>
public readonly record struct ClientMovementInput(short MoveX, short MoveY, byte Flags = 0);

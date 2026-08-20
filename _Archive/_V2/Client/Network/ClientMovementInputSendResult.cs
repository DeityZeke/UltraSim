namespace UltraSim.Client;

public readonly record struct ClientMovementInputSendResult(
    ushort Sequence,
    int PayloadBytes);

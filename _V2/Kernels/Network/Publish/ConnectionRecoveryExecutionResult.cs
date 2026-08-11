namespace UltraSim.Network;

public readonly record struct ConnectionRecoveryExecutionResult(
    ConnectionRecoveryMode Mode,
    int ReplayEntryCount,
    int DatagramCount,
    int CompressedCount,
    int EncryptedCount,
    StateBaselineDescriptor BaselineDescriptor,
    ConnectionScheduleState NextState);

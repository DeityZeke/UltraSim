namespace UltraSim.Network;

public readonly record struct ControlPlaneMetricsSnapshot(
    int InboundDatagrams,
    int DecodeFailures,
    int DispatchMisses,
    int HandshakeStarts,
    int HandshakeAccepts,
    int HandshakeRejects,
    int AdmissionRejects,
    int PingProbesSent,
    int PingProbesReceived,
    int PongProbesSent,
    int PongProbesReceived);

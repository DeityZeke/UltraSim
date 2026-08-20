using UltraSim.Network;

namespace UltraSim.Client.Engines.Networking
{
    public readonly struct ClientPredictionAckResult<TState>
    {
        public readonly ushort AcknowledgedSequence;
        public readonly int RemovedInputs;
        public readonly int RemovedPredictions;
        public readonly bool HasAuthoritativePredictedState;
        public readonly PredictedStateSample<TState> AuthoritativePredictedState;

        public ClientPredictionAckResult(
            ushort acknowledgedSequence,
            int removedInputs,
            int removedPredictions,
            bool hasAuthoritativePredictedState,
            in PredictedStateSample<TState> authoritativePredictedState)
        {
            AcknowledgedSequence = acknowledgedSequence;
            RemovedInputs = removedInputs;
            RemovedPredictions = removedPredictions;
            HasAuthoritativePredictedState = hasAuthoritativePredictedState;
            AuthoritativePredictedState = authoritativePredictedState;
        }
    }
}

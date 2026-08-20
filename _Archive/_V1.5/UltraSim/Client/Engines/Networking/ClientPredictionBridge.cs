using System;
using UltraSim.Network;

namespace UltraSim.Client.Engines.Networking
{
    public sealed class ClientPredictionBridge<TInput, TState>
    {
        private readonly ClientInputSendBridge<TInput> _inputSendBridge;
        private readonly ClientPredictionStore<TState> _predictionStore;

        public ClientPredictionBridge(
            ClientInputSendBridge<TInput> inputSendBridge,
            ClientPredictionStore<TState>? predictionStore = null)
        {
            ArgumentNullException.ThrowIfNull(inputSendBridge);

            _inputSendBridge = inputSendBridge;
            _predictionStore = predictionStore ?? new ClientPredictionStore<TState>();
        }

        public ClientInputSendBridge<TInput> InputSendBridge => _inputSendBridge;

        public ClientPredictionStore<TState> PredictionStore => _predictionStore;

        public int PendingInputCount => _inputSendBridge.Sequencer.PendingCount;

        public int PredictedStateCount => _predictionStore.Count;

        public ClientInputSample<TInput> QueueInputWithPrediction(
            in TInput input,
            long clientTick,
            in TState predictedState)
        {
            ClientInputSample<TInput> sample = _inputSendBridge.QueueInput(in input, clientTick);
            _predictionStore.Record(sample.Sequence, clientTick, in predictedState);
            return sample;
        }

        public bool TrySendInputWithPrediction(
            in TInput input,
            long clientTick,
            in TState predictedState,
            out ClientInputSendResult sendResult)
        {
            if (!_inputSendBridge.TrySendInput(in input, clientTick, out sendResult))
                return false;

            _predictionStore.Record(sendResult.Sequence, clientTick, in predictedState);
            return true;
        }

        public ClientPredictionAckResult<TState> AcknowledgeUpTo(ushort authoritativeSequence)
        {
            bool hasPredicted = _predictionStore.TryGet(authoritativeSequence, out PredictedStateSample<TState> predicted);
            int removedInputs = _inputSendBridge.AcknowledgeUpTo(authoritativeSequence);
            int removedPredictions = _predictionStore.RemoveAcknowledgedUpTo(authoritativeSequence);
            return new ClientPredictionAckResult<TState>(
                authoritativeSequence,
                removedInputs,
                removedPredictions,
                hasPredicted,
                in predicted);
        }
    }
}

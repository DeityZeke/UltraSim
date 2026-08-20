using System.Collections.Generic;
using Client.Godot.Prefabs.Mobiles;
using Godot;
using UltraSim.Client.Engines.Networking;
using UltraSim.Client.Runtime;
using UltraSim.Network;
using UltraSim.Network.Protocol;
using UltraSim.Shared.Gameplay;
using UltraSim.UI.Contracts;

namespace Client.Godot
{
    public sealed class GodotClientNetworkRuntime
    {
        private const float ChatBubbleTtlSeconds = 5.0f;
        private const int MaxChatFeedLines = 12;
        private const long InputIntervalUsec = 33_333;
        private const long PingIntervalUsec = 1_000_000;
        private const long PingTimeoutUsec = 5_000_000;
        private const int RemoteInterpolationBackTicks = 4;
        private const int RemoteInterpolationMaxSamples = 8;
        private const float LocalCorrectionSharpness = 7.0f;
        private const float LocalCorrectionDeadband = 0.10f;
        private const float LocalCorrectionSnapDistance = 1.25f;
        private const float RemoteVisualSharpness = 10.0f;
        private const float RemoteVisualSnapDistance = 1.5f;
        private const float LocalMoveSpeedMetersPerSecond =
            UltraSim.Shared.Engines.Networking.MovementProtocolConstants.PlayerMoveSpeedMetersPerSecond;

        private readonly GodotClientSession _session;
        private readonly INetworkTransport _transport;
        private readonly System.Func<int> _assignedConnectionIdResolver;
        private readonly ClientInputSendBridge<EntityFloat3InputCommand> _inputSendBridge;
        private readonly ClientPredictionBridge<EntityFloat3InputCommand, EntityFloat3PredictedState> _predictionBridge;
        private readonly Dictionary<ulong, ClientInterpolationBuffer<EntityFloat3InterpolatedPose>> _remoteInterpolationBuffers = new();
        private readonly Dictionary<ushort, long> _pingSentUsecBySequence = new();
        private readonly List<ushort> _expiredPingScratch = new();
        private readonly List<string> _chatFeedLines = new();
        private readonly Dictionary<ulong, ushort> _definedEntityIds = new();
        private readonly Dictionary<ulong, NameplateState> _nameplateStatesByEntity = new();
        private readonly Dictionary<ulong, string> _displayNamesByEntity = new();
        private long _clientInputTick;
        private long _nextInputDueUsec;
        private long _nextPingDueUsec;
        private long _latestRemoteServerTick;
        private ushort _nextPingSequence;
        private float _lastPingMs;
        private float _smoothedPingMs;
        private ClientWorldPose _localAuthoritativePose;
        private bool _hasLocalAuthoritativePose;
        private Vector3 _localPredictedPosition;
        private float _localPredictedYawRadians;
        private bool _hasLocalPredictedPose;
        private short _lastSentMoveX;
        private short _lastSentMoveZ;
        private bool _hasLastSentMoveInput;
        private bool _acceptWorldReplication;

        public GodotClientNetworkRuntime(
            GodotClientSession session,
            INetworkTransport transport,
            System.Func<int>? assignedConnectionIdResolver = null)
        {
            _session = session ?? throw new System.ArgumentNullException(nameof(session));
            _transport = transport ?? throw new System.ArgumentNullException(nameof(transport));
            _assignedConnectionIdResolver = assignedConnectionIdResolver ?? (() => 0);
            _inputSendBridge = new ClientInputSendBridge<EntityFloat3InputCommand>(
                _transport,
                connectionId: 1,
                encoder: EntityFloat3InputCommandCodec.Encode,
                channel: ChannelType.ReliableOrdered);
            _predictionBridge = new ClientPredictionBridge<EntityFloat3InputCommand, EntityFloat3PredictedState>(_inputSendBridge);
        }

        public float LastPingMs => _lastPingMs;
        public float SmoothedPingMs => _smoothedPingMs;
        public IReadOnlyList<string> ChatFeedLines => _chatFeedLines;

        public void Reset()
        {
            ClearWorldTrackingState();
            _clientInputTick = 0;
            _nextInputDueUsec = 0;
            _nextPingDueUsec = 0;
            _pingSentUsecBySequence.Clear();
            _expiredPingScratch.Clear();
            _chatFeedLines.Clear();
            _latestRemoteServerTick = 0;
            _nextPingSequence = 0;
            _lastPingMs = 0.0f;
            _smoothedPingMs = 0.0f;
        }

        public void EnterPreGameState()
        {
            ClearWorldTrackingState();
            _session.CharacterWorld.ClearAllActors();
        }

        public void OnConnected(long nowUsec)
        {
            Reset();
            _nextInputDueUsec = nowUsec + InputIntervalUsec;
            _nextPingDueUsec = nowUsec + PingIntervalUsec;
        }

        public void Tick(bool connected, float deltaSeconds)
        {
            if (!connected)
                return;

            ApplyLocalPredictedMotion(deltaSeconds);
            ApplyRemoteInterpolation();
            TrySendLocalInput();
            TrySendPingProbe();
        }

        public void HandleInboundMessage(in EntityFloat3InboundMessage inbound)
        {
            switch (inbound.Kind)
            {
                case EntityFloat3InboundMessageKind.Correction:
                    HandleInboundCorrection(inbound.Correction);
                    break;
                case EntityFloat3InboundMessageKind.Despawn:
                    HandleInboundDespawn(inbound.DespawnEntityId);
                    break;
                case EntityFloat3InboundMessageKind.Chat:
                    HandleInboundChat(inbound.Chat);
                    break;
                case EntityFloat3InboundMessageKind.ProbePong:
                    HandleInboundProbePong(inbound.ProbePong);
                    break;
                case EntityFloat3InboundMessageKind.UiPanelOpen:
                    HandleInboundUiPanelOpen(inbound.UiPanelOpen);
                    break;
                case EntityFloat3InboundMessageKind.UiPanelUpdate:
                    HandleInboundUiPanelUpdate(inbound.UiPanelUpdate);
                    break;
                case EntityFloat3InboundMessageKind.UiPanelClose:
                    HandleInboundUiPanelClose(inbound.UiPanelClose);
                    break;
                case EntityFloat3InboundMessageKind.UiPanelDefinition:
                    HandleInboundUiPanelDefinition(inbound.UiPanelDefinition);
                    break;
                case EntityFloat3InboundMessageKind.EnterGame:
                    _acceptWorldReplication = true;
                    _session.OnEnterGame?.Invoke(inbound.EnterGameDetail ?? string.Empty);
                    break;
                case EntityFloat3InboundMessageKind.LoginResponse:
                    _session.OnLoginResponse?.Invoke(inbound.LoginResponse);
                    break;
                case EntityFloat3InboundMessageKind.Nameplate:
                    HandleInboundNameplate(inbound.Nameplate);
                    break;
            }
        }

        public bool TrySendChatText(string rawText)
        {
            byte[] payload = new byte[NetworkControlMessageCodec.ClientChatHeaderBytes + NetworkControlMessageCodec.ChatMaxUtf8Bytes];
            if (!NetworkControlMessageCodec.TryEncodeClientChat(payload, rawText, out int written) || written <= 0)
                return false;

            _transport.EnqueueOutbound(new OutboundDatagram(
                connectionId: 1,
                ChannelType.ReliableOrdered,
                new System.ReadOnlyMemory<byte>(payload, 0, written)));
            return true;
        }

        public bool TrySendReturnToCharacterSelect()
        {
            byte[] payload = new byte[NetworkControlMessageCodec.ClientReturnToCharacterSelectPayloadBytes];
            if (!NetworkControlMessageCodec.TryEncodeReturnToCharacterSelect(payload))
                return false;

            _transport.EnqueueOutbound(new OutboundDatagram(
                connectionId: 1,
                ChannelType.ReliableOrdered,
                payload));
            return true;
        }

        public bool TrySendUiPanelAck(UiPanelAck ack)
        {
            byte[] payload = new byte[UiPanelNetworkCodec.UiPanelAckPayloadBytes];
            if (!UiPanelNetworkCodec.TryEncodePanelAck(payload, in ack))
                return false;

            _transport.EnqueueOutbound(new OutboundDatagram(
                connectionId: 1,
                ChannelType.ReliableOrdered,
                payload));
            return true;
        }

        public bool TrySendUiPanelResponse(UiPanelResponse response)
        {
            byte[] payload = new byte[UiPanelNetworkCodec.UiPanelResponseHeaderBytes + response.Payload.Length];
            if (!UiPanelNetworkCodec.TryEncodePanelResponse(payload, in response, out int written) || written <= 0)
                return false;

            _transport.EnqueueOutbound(new OutboundDatagram(
                connectionId: 1,
                ChannelType.ReliableOrdered,
                new System.ReadOnlyMemory<byte>(payload, 0, written)));
            return true;
        }

        /// <summary>
        /// Encodes and sends a <c>LoginRequestPacket (0xE8)</c> to the server.
        /// Called by the client-side LoginPanel packet binding after computing the SHA-256 challenge-response.
        /// </summary>
        public bool TrySendLoginRequest(
            UiPanelInstanceId instanceId,
            System.ReadOnlySpan<byte> usernameUtf8,
            System.ReadOnlySpan<byte> passwordHash32,
            System.ReadOnlySpan<byte> challengeResponse32)
        {
            int size = 6 + usernameUtf8.Length
                       + LoginPacketCodec.PasswordHashBytes
                       + LoginPacketCodec.ChallengeResponseBytes;
            byte[] payload = new byte[size];
            if (!LoginPacketCodec.TryEncodeLoginRequest(payload, instanceId, usernameUtf8, passwordHash32, challengeResponse32, out int written) || written <= 0)
                return false;

            _transport.EnqueueOutbound(new OutboundDatagram(
                connectionId: 1,
                ChannelType.ReliableOrdered,
                new System.ReadOnlyMemory<byte>(payload, 0, written)));
            return true;
        }

        private void HandleInboundCorrection(ClientInboundCorrection<EntityFloat3Correction> inbound)
        {
            if (!_acceptWorldReplication)
                return;

            ref readonly EntityFloat3Correction correction = ref inbound.Correction;
            EnsureActorDefinitionQueued(correction.EntityId.Value, correction.ActorDefinitionId);
            int assignedConnectionId = _assignedConnectionIdResolver();
            bool isLocalOwnedEntity = false;
            if (assignedConnectionId > 0 &&
                correction.OwnerConnectionId == assignedConnectionId &&
                correction.EntityId.Value != 0)
            {
                _session.CharacterWorld.SetLocalControlledEntityId(correction.EntityId.Value);
                _remoteInterpolationBuffers.Remove(correction.EntityId.Value);
                isLocalOwnedEntity = true;
            }

            if (inbound.AuthoritativeSequence != 0)
            {
                ClientPredictionAckResult<EntityFloat3PredictedState> ack =
                    _predictionBridge.AcknowledgeUpTo(inbound.AuthoritativeSequence);
                if ((ack.RemovedInputs > 0 || ack.HasAuthoritativePredictedState) && correction.EntityId.Value != 0)
                {
                    _session.CharacterWorld.SetLocalControlledEntityId(correction.EntityId.Value);
                    _remoteInterpolationBuffers.Remove(correction.EntityId.Value);
                    isLocalOwnedEntity = true;
                }
            }

            isLocalOwnedEntity = isLocalOwnedEntity ||
                (_session.CharacterWorld.LocalControlledEntityId != 0 &&
                 _session.CharacterWorld.LocalControlledEntityId == correction.EntityId.Value);
            EnsureNameplateQueued(correction.EntityId.Value, correction.OwnerConnectionId, isLocalOwnedEntity);

            ClientWorldPose pose = new(
                correction.X,
                correction.Y,
                correction.Z,
                correction.YawRadians,
                0.0f,
                0.0f);

            ulong localEntityId = _session.CharacterWorld.LocalControlledEntityId;
            if (localEntityId != 0 && correction.EntityId.Value == localEntityId)
            {
                ClientEntityViewId localViewId = new(correction.EntityId.Value);
                if (_session.CharacterWorld.TryGetCharacter(localViewId) == null)
                    _session.CharacterWorld.SetActorPose(localViewId, in pose);

                _localAuthoritativePose = pose;
                _hasLocalAuthoritativePose = true;
                if (!_hasLocalPredictedPose)
                {
                    _localPredictedPosition = new Vector3(pose.X, pose.Y, pose.Z);
                    _localPredictedYawRadians = pose.YawRadians;
                    _hasLocalPredictedPose = true;
                }
                return;
            }

            if (inbound.ServerTick > _latestRemoteServerTick)
                _latestRemoteServerTick = inbound.ServerTick;

            ClientInterpolationBuffer<EntityFloat3InterpolatedPose> buffer = GetOrCreateRemoteBuffer(correction.EntityId.Value);
            buffer.AddSample(inbound.ServerTick, new EntityFloat3InterpolatedPose(pose));
            buffer.TrimToMaxCount(RemoteInterpolationMaxSamples);
        }

        private void HandleInboundDespawn(NetworkEntityId entityId)
        {
            if (!_acceptWorldReplication)
                return;

            if (entityId.Value == 0)
                return;

            if (entityId.Value == _session.CharacterWorld.LocalControlledEntityId)
            {
                _hasLocalAuthoritativePose = false;
                _hasLocalPredictedPose = false;
            }

            _definedEntityIds.Remove(entityId.Value);
            _nameplateStatesByEntity.Remove(entityId.Value);
            _displayNamesByEntity.Remove(entityId.Value);
            if (_remoteInterpolationBuffers.Remove(entityId.Value, out ClientInterpolationBuffer<EntityFloat3InterpolatedPose>? removed))
                removed.Clear();
            _session.QueueDespawnActor(new ClientEntityViewId(entityId.Value));
        }

        private void HandleInboundChat(ServerChatMessage chat)
        {
            if (string.IsNullOrWhiteSpace(chat.Text))
                return;

            AppendChatFeedLine(chat.Text);
            if (chat.Scope == EntityFloat3InboundMessageCodec.ChatScopeLocal &&
                chat.SenderEntityId.Value != 0)
            {
                _session.QueueShowChatBubble(
                    new ClientEntityViewId(chat.SenderEntityId.Value),
                    chat.Text,
                    ChatBubbleTtlSeconds);
                return;
            }

            _session.Logger.Info($"network chat scope={chat.Scope} sender={chat.SenderEntityId.Value} text={chat.Text}");
        }

        private void HandleInboundProbePong(ProbePongMessage pong)
        {
            if (!_pingSentUsecBySequence.Remove(pong.Sequence, out long sentUsec))
                return;

            long nowUsec = _session.Clock.GetMonotonicTicksUsec();
            if (nowUsec <= sentUsec)
                return;

            _lastPingMs = (nowUsec - sentUsec) / 1000.0f;
            _smoothedPingMs = _smoothedPingMs <= 0.0f
                ? _lastPingMs
                : ((_smoothedPingMs * 0.8f) + (_lastPingMs * 0.2f));
        }

        private void HandleInboundNameplate(ServerNameplateMessage nameplate)
        {
            ulong entityId = nameplate.EntityId.Value;
            if (entityId == 0 || string.IsNullOrWhiteSpace(nameplate.PrimaryText))
                return;

            string primaryText = nameplate.PrimaryText.Trim();
            _displayNamesByEntity[entityId] = primaryText;
            NameplateState nextState = _nameplateStatesByEntity.TryGetValue(entityId, out NameplateState existingState)
                ? existingState with { PrimaryText = primaryText, Visible = true }
                : new NameplateState(primaryText, Visible: true);

            if (_nameplateStatesByEntity.TryGetValue(entityId, out NameplateState currentState) &&
                currentState.Equals(nextState))
            {
                return;
            }

            _nameplateStatesByEntity[entityId] = nextState;
            _session.QueueUpdateNameplate(new ClientEntityViewId(entityId), nextState);
        }

        private void HandleInboundUiPanelDefinition(UiPanelDefinition definition)
        {
            _session.MenuCanvasManager?.AcceptPanelDefinition(definition);
        }

        private void HandleInboundUiPanelOpen(UiPanelOpenCommand command)
        {
            _session.MenuCanvasManager?.OpenPanel(command);
        }

        private void HandleInboundUiPanelUpdate(UiPanelUpdateCommand command)
        {
            _session.MenuCanvasManager?.UpdatePanel(command);
        }

        private void HandleInboundUiPanelClose(UiPanelCloseCommand command)
        {
            _session.MenuCanvasManager?.ClosePanel(command);
        }

        private void ApplyLocalPredictedMotion(float deltaSeconds)
        {
            ulong localEntityId = _session.CharacterWorld.LocalControlledEntityId;
            if (localEntityId == 0)
                return;

            ClientEntityViewId localViewId = new(localEntityId);
            CharacterRoot? localCharacter = _session.CharacterWorld.TryGetCharacter(localViewId);
            if (!_hasLocalPredictedPose)
            {
                if (_hasLocalAuthoritativePose)
                {
                    _localPredictedPosition = new Vector3(_localAuthoritativePose.X, _localAuthoritativePose.Y, _localAuthoritativePose.Z);
                    _localPredictedYawRadians = _localAuthoritativePose.YawRadians;
                    _hasLocalPredictedPose = true;
                }
                else if (localCharacter != null)
                {
                    _localPredictedPosition = localCharacter.GlobalPosition;
                    _localPredictedYawRadians = Mathf.DegToRad(localCharacter.PresentationFacingYawDegrees);
                    _hasLocalPredictedPose = true;
                }
            }

            if (!_hasLocalPredictedPose)
                return;

            if (_session.LocalPlayerController != null)
            {
                Vector3 moveDirection = _session.LocalPlayerController.GetDesiredMoveDirection();
                if (moveDirection.LengthSquared() > 0.0001f)
                {
                    if (moveDirection.LengthSquared() > 1.0f)
                        moveDirection = moveDirection.Normalized();
                    Vector3 step = new(moveDirection.X * LocalMoveSpeedMetersPerSecond * deltaSeconds, 0.0f, moveDirection.Z * LocalMoveSpeedMetersPerSecond * deltaSeconds);
                    _localPredictedPosition += step;
                    _localPredictedYawRadians = Mathf.Atan2(moveDirection.X, moveDirection.Z);
                }
            }

            if (_hasLocalAuthoritativePose)
            {
                Vector3 authoritativePosition = new(_localAuthoritativePose.X, _localAuthoritativePose.Y, _localAuthoritativePose.Z);
                float distance = _localPredictedPosition.DistanceTo(authoritativePosition);
                if (distance >= LocalCorrectionSnapDistance)
                {
                    _localPredictedPosition = authoritativePosition;
                    _localPredictedYawRadians = _localAuthoritativePose.YawRadians;
                }
                else if (distance > LocalCorrectionDeadband)
                {
                    float alpha = 1.0f - Mathf.Exp(-LocalCorrectionSharpness * deltaSeconds);
                    _localPredictedPosition = _localPredictedPosition.Lerp(authoritativePosition, alpha);
                    _localPredictedYawRadians = Mathf.LerpAngle(_localPredictedYawRadians, _localAuthoritativePose.YawRadians, alpha);
                }
            }

            _session.CharacterWorld.SetActorPose(
                localViewId,
                new ClientWorldPose(
                    _localPredictedPosition.X,
                    _localPredictedPosition.Y,
                    _localPredictedPosition.Z,
                    _localPredictedYawRadians,
                    0.0f,
                    0.0f));
        }

        private void TrySendLocalInput()
        {
            if (_session.LocalPlayerController == null)
                return;

            Vector3 moveDirection = _session.LocalPlayerController.GetDesiredMoveDirection();
            if (moveDirection.LengthSquared() > 1.0f)
                moveDirection = moveDirection.Normalized();

            short moveX = UltraSim.Shared.Engines.Networking.MovementQuantization.QuantizeDirection(moveDirection.X);
            short moveZ = UltraSim.Shared.Engines.Networking.MovementQuantization.QuantizeDirection(moveDirection.Z);
            bool hasMovement = moveX != 0 || moveZ != 0;
            bool inputChanged = !_hasLastSentMoveInput || moveX != _lastSentMoveX || moveZ != _lastSentMoveZ;

            if (!hasMovement && !_hasLastSentMoveInput)
                return;

            ulong localEntityId = _session.CharacterWorld.LocalControlledEntityId;
            CharacterRoot? localCharacter = localEntityId == 0
                ? null
                : _session.CharacterWorld.TryGetCharacter(new ClientEntityViewId(localEntityId));

            long nowUsec = _session.Clock.GetMonotonicTicksUsec();
            if (!inputChanged)
            {
                if (!hasMovement)
                    return;
                if (_nextInputDueUsec > 0 && nowUsec < _nextInputDueUsec)
                    return;
            }

            _nextInputDueUsec = nowUsec + InputIntervalUsec;
            _lastSentMoveX = moveX;
            _lastSentMoveZ = moveZ;
            _hasLastSentMoveInput = true;

            if (localCharacter == null)
            {
                _clientInputTick++;
                _predictionBridge.InputSendBridge.TrySendInput(
                    new EntityFloat3InputCommand(moveX, moveZ),
                    _clientInputTick,
                    out _);
                return;
            }

            float yawRadians = Mathf.Atan2(moveDirection.X, moveDirection.Z);
            var predicted = new EntityFloat3PredictedState(
                _hasLocalPredictedPose ? _localPredictedPosition.X : localCharacter.GlobalPosition.X,
                _hasLocalPredictedPose ? _localPredictedPosition.Y : localCharacter.GlobalPosition.Y,
                _hasLocalPredictedPose ? _localPredictedPosition.Z : localCharacter.GlobalPosition.Z,
                yawRadians);
            _clientInputTick++;
            _predictionBridge.TrySendInputWithPrediction(
                new EntityFloat3InputCommand(moveX, moveZ),
                _clientInputTick,
                predicted,
                out _);
        }

        private void TrySendPingProbe()
        {
            long nowUsec = _session.Clock.GetMonotonicTicksUsec();
            if (nowUsec < _nextPingDueUsec)
                return;

            _nextPingDueUsec = nowUsec + PingIntervalUsec;
            _nextPingSequence++;
            if (_nextPingSequence == 0)
                _nextPingSequence = 1;

            byte[] payload = new byte[NetworkControlMessageCodec.PingProbePayloadBytes];
            if (!NetworkControlMessageCodec.TryEncodePingProbe(payload, _nextPingSequence, nowUsec))
                return;

            ChannelType pingChannel = ChannelType.ReliableOrdered;
            if (_transport is TcpFramedNetworkTransport tcpTransport && tcpTransport.IsUdpEnabled)
                pingChannel = ChannelType.UnreliableSequenced;

            _transport.EnqueueOutbound(new OutboundDatagram(
                connectionId: 1,
                pingChannel,
                payload));
            _pingSentUsecBySequence[_nextPingSequence] = nowUsec;

            _expiredPingScratch.Clear();
            foreach ((ushort sequence, long sentUsec) in _pingSentUsecBySequence)
            {
                if ((nowUsec - sentUsec) > PingTimeoutUsec)
                    _expiredPingScratch.Add(sequence);
            }

            for (int i = 0; i < _expiredPingScratch.Count; i++)
                _pingSentUsecBySequence.Remove(_expiredPingScratch[i]);
        }

        private void ApplyRemoteInterpolation()
        {
            if (_latestRemoteServerTick <= 0 || _remoteInterpolationBuffers.Count == 0)
                return;

            long targetTick = _latestRemoteServerTick - RemoteInterpolationBackTicks;
            if (targetTick < 0)
                targetTick = 0;

            foreach ((ulong entityId, ClientInterpolationBuffer<EntityFloat3InterpolatedPose> buffer) in _remoteInterpolationBuffers)
            {
                if (!buffer.TrySample(targetTick, LerpInterpolatedPose, out EntityFloat3InterpolatedPose pose))
                    continue;

                ClientEntityViewId viewId = new(entityId);
                CharacterRoot? character = _session.CharacterWorld.TryGetCharacter(viewId);
                if (character == null)
                {
                    _session.CharacterWorld.SetActorPose(viewId, pose.Pose);
                    continue;
                }

                ClientWorldPose sampledPose = pose.Pose;
                _session.CharacterWorld.SetActorRenderTargetPose(
                    viewId,
                    in sampledPose,
                    RemoteVisualSharpness,
                    RemoteVisualSnapDistance);
            }

            long pruneBefore = targetTick - 2;
            foreach (ClientInterpolationBuffer<EntityFloat3InterpolatedPose> buffer in _remoteInterpolationBuffers.Values)
                buffer.PruneBefore(pruneBefore);
        }

        private ClientInterpolationBuffer<EntityFloat3InterpolatedPose> GetOrCreateRemoteBuffer(ulong entityId)
        {
            if (_remoteInterpolationBuffers.TryGetValue(entityId, out ClientInterpolationBuffer<EntityFloat3InterpolatedPose>? existing))
                return existing;

            ClientInterpolationBuffer<EntityFloat3InterpolatedPose> created = new();
            _remoteInterpolationBuffers.Add(entityId, created);
            return created;
        }

        private static EntityFloat3InterpolatedPose LerpInterpolatedPose(
            in EntityFloat3InterpolatedPose from,
            in EntityFloat3InterpolatedPose to,
            float alpha)
        {
            ClientWorldPose fromPose = from.Pose;
            ClientWorldPose toPose = to.Pose;
            return new EntityFloat3InterpolatedPose(new ClientWorldPose(
                Mathf.Lerp(fromPose.X, toPose.X, alpha),
                Mathf.Lerp(fromPose.Y, toPose.Y, alpha),
                Mathf.Lerp(fromPose.Z, toPose.Z, alpha),
                Mathf.LerpAngle(fromPose.YawRadians, toPose.YawRadians, alpha),
                Mathf.LerpAngle(fromPose.PitchRadians, toPose.PitchRadians, alpha),
                Mathf.LerpAngle(fromPose.RollRadians, toPose.RollRadians, alpha)));
        }

        private void AppendChatFeedLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            _chatFeedLines.Add(line.Trim());
            if (_chatFeedLines.Count <= MaxChatFeedLines)
                return;

            _chatFeedLines.RemoveRange(0, _chatFeedLines.Count - MaxChatFeedLines);
        }

        private void EnsureActorDefinitionQueued(ulong entityId, ushort actorDefinitionId)
        {
            if (entityId == 0)
                return;

            if (_definedEntityIds.TryGetValue(entityId, out ushort existingDefinitionId) &&
                existingDefinitionId == actorDefinitionId)
            {
                return;
            }

            _definedEntityIds[entityId] = actorDefinitionId;
            _session.QueueSetActorDefinition(
                new ClientEntityViewId(entityId),
                ClientActorDefinitionIds.FromSharedId(actorDefinitionId));
        }

        private void EnsureNameplateQueued(ulong entityId, int ownerConnectionId, bool isLocalOwnedEntity)
        {
            if (entityId == 0)
                return;

            string primaryText = ResolvePrimaryNameplateText(entityId, ownerConnectionId);
            NameplateState nextState = _nameplateStatesByEntity.TryGetValue(entityId, out NameplateState previousState)
                ? previousState with { PrimaryText = primaryText, Visible = true }
                : new NameplateState(primaryText, Visible: true);

            if (_nameplateStatesByEntity.TryGetValue(entityId, out NameplateState existingState) &&
                existingState.Equals(nextState))
            {
                return;
            }

            _nameplateStatesByEntity[entityId] = nextState;
            _session.QueueUpdateNameplate(new ClientEntityViewId(entityId), nextState);
        }

        private string ResolvePrimaryNameplateText(ulong entityId, int ownerConnectionId)
        {
            if (_displayNamesByEntity.TryGetValue(entityId, out string? primaryText) &&
                !string.IsNullOrWhiteSpace(primaryText))
            {
                return primaryText;
            }

            return ownerConnectionId > 0
                ? $"P{ownerConnectionId}"
                : $"Entity {entityId}";
        }

        private void ClearWorldTrackingState()
        {
            foreach (ClientInterpolationBuffer<EntityFloat3InterpolatedPose> buffer in _remoteInterpolationBuffers.Values)
                buffer.Clear();

            _remoteInterpolationBuffers.Clear();
            _definedEntityIds.Clear();
            _nameplateStatesByEntity.Clear();
            _displayNamesByEntity.Clear();
            _latestRemoteServerTick = 0;
            _localAuthoritativePose = default;
            _hasLocalAuthoritativePose = false;
            _localPredictedPosition = default;
            _localPredictedYawRadians = 0.0f;
            _hasLocalPredictedPose = false;
            _lastSentMoveX = 0;
            _lastSentMoveZ = 0;
            _hasLastSentMoveInput = false;
            _acceptWorldReplication = false;
        }
    }
}

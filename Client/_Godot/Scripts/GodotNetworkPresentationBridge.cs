using System.Buffers.Binary;
using Godot;
using UltraSim.Network;
using UltraSim.Client.Godot.World;

namespace UltraSim.Client.Godot;

public sealed class GodotNetworkPresentationBridge
{
    private readonly ClientCharacterWorld _characterWorld;

    public GodotNetworkPresentationBridge(ClientCharacterWorld characterWorld)
    {
        _characterWorld = characterWorld ?? throw new ArgumentNullException(nameof(characterWorld));
    }

    public void ApplyOwnerMovement(in ClientMovementStateReceiveResult result)
    {
        if (result.NetworkEntityId == 0)
        {
            return;
        }

        ulong actorId = result.NetworkEntityId;
        Vector3 position = ToGodotPosition(result.AuthoritativeState.PositionX, result.AuthoritativeState.PositionY);
        float yawDegrees = ResolveYawDegrees(result.AuthoritativeState.VelocityX, result.AuthoritativeState.VelocityY);
        _characterWorld.SetActorPose(actorId, position, yawDegrees);
        _characterWorld.SetLocalControlledEntityId(actorId);
    }

    public void ApplyObserverState(ulong itemKey, ulong sequence, ReadOnlySpan<byte> payload)
    {
        ulong actorId;
        float x;
        float y;
        float velocityX;
        float velocityY;
        if (TryDecodeMovementStatePayload(payload, out MovementStatePacket movementState))
        {
            if (movementState.NetworkEntityId == 0)
            {
                return;
            }

            actorId = movementState.NetworkEntityId;
            x = movementState.PositionX;
            y = movementState.PositionY;
            velocityX = movementState.VelocityX;
            velocityY = movementState.VelocityY;
        }
        else if (TryDecodeMovementLikePayload(payload, out x, out y, out velocityX, out velocityY))
        {
            actorId = itemKey;
        }
        else
        {
            return;
        }

        Vector3 position = ToGodotPosition(x, y);
        float yawDegrees = ResolveYawDegrees(velocityX, velocityY);
        _characterWorld.SetActorPose(actorId, position, yawDegrees);
    }

    public void ResetObserverState()
    {
        ulong localActorId = _characterWorld.LocalControlledEntityId;
        _characterWorld.ClearAllActors();
        if (localActorId != 0)
        {
            _characterWorld.SetLocalControlledEntityId(localActorId);
        }
    }

    private static Vector3 ToGodotPosition(float x, float y)
    {
        return new Vector3(x, 0.0f, y);
    }

    private static float ResolveYawDegrees(float velocityX, float velocityY)
    {
        if ((velocityX * velocityX) + (velocityY * velocityY) <= 0.0001f)
        {
            return 0.0f;
        }

        return Mathf.RadToDeg(Mathf.Atan2(velocityX, velocityY));
    }

    private static bool TryDecodeMovementLikePayload(
        ReadOnlySpan<byte> payload,
        out float x,
        out float y,
        out float velocityX,
        out float velocityY)
    {
        if (payload.Length < 8)
        {
            x = y = velocityX = velocityY = 0.0f;
            return false;
        }

        x = ReadSingleLittleEndian(payload, 0);
        y = ReadSingleLittleEndian(payload, 4);
        velocityX = payload.Length >= 12 ? ReadSingleLittleEndian(payload, 8) : 0.0f;
        velocityY = payload.Length >= 16 ? ReadSingleLittleEndian(payload, 12) : 0.0f;
        return IsFinite(x) && IsFinite(y) && IsFinite(velocityX) && IsFinite(velocityY);
    }

    private static bool TryDecodeMovementStatePayload(
        ReadOnlySpan<byte> payload,
        out MovementStatePacket packet)
    {
        const int encodedLength = sizeof(uint) + sizeof(uint) + sizeof(ushort) + (sizeof(float) * 4);
        if (payload.Length != encodedLength)
        {
            packet = default;
            return false;
        }

        try
        {
            var reader = new BitReader(payload);
            packet = MovementStatePacket.Deserialize(ref reader);
            return packet.NetworkEntityId != 0 &&
                IsFinite(packet.PositionX) &&
                IsFinite(packet.PositionY) &&
                IsFinite(packet.VelocityX) &&
                IsFinite(packet.VelocityY);
        }
        catch (InvalidOperationException)
        {
            packet = default;
            return false;
        }
    }

    private static float ReadSingleLittleEndian(ReadOnlySpan<byte> payload, int offset)
    {
        int bits = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(offset, sizeof(int)));
        return BitConverter.Int32BitsToSingle(bits);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}

using System.Buffers.Binary;
using Godot;
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
        if (!TryDecodeMovementLikePayload(payload, out float x, out float y, out float velocityX, out float velocityY))
        {
            return;
        }

        ulong actorId = itemKey;
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

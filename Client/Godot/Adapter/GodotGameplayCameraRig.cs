using Godot;
using UltraSim.Client.Core;

namespace UltraSim.Client.Godot;

public readonly record struct GodotGameplayCameraSnapshot(
    ClientCameraRig Rig,
    float YawRadians,
    float PitchRadians,
    float DistanceMeters,
    float PivotMeters,
    ClientCanonicalViewProjection TargetFrameProjection,
    float NativeOutputVerticalFovDegrees,
    float NativeOutputOrthographicSizeMeters);

/// <summary>
/// One Godot realization of the Core camera rig for both Local and Remote. The camera renders
/// into the independent Gameplay Output viewport, so no main-viewport compensation applies.
/// </summary>
public sealed class GodotGameplayCameraRig
{
    private readonly Camera3D _camera;
    private ClientCameraRig _rig;
    private GameplayFrameLayout _layout;
    private ClientCanonicalViewProjection _projection;
    private float _yaw;
    private float _pitch;
    private float _distance;
    private float _pivot;
    private Vector3 _targetOffset;

    public GodotGameplayCameraRig(Camera3D camera, ClientCameraRig rig,
        in GameplayFrameLayout layout)
    {
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        ApplyRig(rig, layout);
    }

    public Camera3D Camera => _camera;
    public ClientCameraRig Rig => _rig;
    public float YawRadians => _yaw;
    public float PitchRadians => _pitch;
    public float DistanceMeters => _distance;
    public float PivotMeters => _pivot;
    public ClientCanonicalViewProjection TargetFrameProjection => _projection;

    public void ApplyRig(ClientCameraRig rig, in GameplayFrameLayout layout)
    {
        if (!rig.IsValid) throw new ArgumentException("A finite camera rig is required.", nameof(rig));
        _rig = rig;
        _yaw = Mathf.DegToRad(rig.DefaultYawTenths / 10f);
        _pitch = Mathf.DegToRad(rig.DefaultPitchTenths / 10f);
        _distance = rig.DefaultDistanceMillimeters / 1_000f;
        _pivot = rig.PivotHeightMillimeters / 1_000f;
        // A rig change is also a lens change even when the frame geometry is unchanged.
        _layout = default;
        ApplyFrame(layout);
        UpdatePose();
    }

    public void ApplyFrame(in GameplayFrameLayout layout)
    {
        if (_layout == layout) return;
        _projection = ClientCanonicalViewProjection.ForFrame(_rig, layout);
        _layout = layout;
        // Core FOV is vertical. KeepHeight makes that explicit even on wide viewports.
        _camera.KeepAspect = Camera3D.KeepAspectEnum.Height;
        UpdateProjection();
    }

    /// <summary>Presentation target relative to the authoritative world-render origin.</summary>
    public void SetTargetOffset(Vector3 offset)
    {
        if (!float.IsFinite(offset.X) || !float.IsFinite(offset.Y) ||
            !float.IsFinite(offset.Z))
            throw new ArgumentOutOfRangeException(nameof(offset));
        _targetOffset = offset;
        UpdatePose();
    }

    public void Look(Vector2 relativePixels, float pointerSensitivity, bool invertY)
    {
        if (!float.IsFinite(relativePixels.X) || !float.IsFinite(relativePixels.Y) ||
            !float.IsFinite(pointerSensitivity) || pointerSensitivity is < 0.1f or > 5f)
            throw new ArgumentOutOfRangeException(nameof(relativePixels));
        float radiansPerPixel = Mathf.DegToRad(_rig.LookMillidegreesPerPixel / 1_000f) *
            pointerSensitivity;
        if (_rig.YawMode == ClientCameraAxisMode.Free)
            _yaw -= relativePixels.X * radiansPerPixel;
        if (_rig.PitchMode == ClientCameraAxisMode.Free)
        {
            float direction = invertY ? -1f : 1f;
            _pitch = Math.Clamp(_pitch - relativePixels.Y * radiansPerPixel * direction,
                Mathf.DegToRad(_rig.MinimumPitchTenths / 10f),
                Mathf.DegToRad(_rig.MaximumPitchTenths / 10f));
        }
        UpdatePose();
    }

    public void Zoom(bool inward)
    {
        if (_rig.Zoom != ClientCameraZoom.Stepped) return;
        float step = _rig.ZoomStepMillimeters / 1_000f;
        _distance = Math.Clamp(_distance + (inward ? -step : step),
            _rig.MinimumDistanceMillimeters / 1_000f,
            _rig.MaximumDistanceMillimeters / 1_000f);
        if (_rig.Projection == ClientCameraProjection.Orthographic)
            UpdateProjection();
        UpdatePose();
    }

    private void UpdateProjection()
    {
        if (_rig.Projection == ClientCameraProjection.Perspective)
        {
            _camera.SetPerspective(
                (float)_projection.TargetVerticalFovDegrees,
                _camera.Near, _camera.Far);
            return;
        }
        // Orthographic magnification is lens size, not camera distance. Distance remains the
        // bounded zoom control's scalar so the same profile min/default/max values apply.
        float zoomRatio = _distance / (_rig.DefaultDistanceMillimeters / 1_000f);
        _camera.SetOrthogonal(
            (float)_projection.TargetVerticalExtent * zoomRatio,
            _camera.Near, _camera.Far);
    }

    public void UpdatePose()
    {
        float horizontal = MathF.Cos(_pitch) * _distance;
        Vector3 pivot = _targetOffset + new Vector3(0, _pivot, 0);
        _camera.Position = _targetOffset + new Vector3(
            MathF.Sin(_yaw) * horizontal,
            MathF.Sin(_pitch) * _distance + _pivot,
            MathF.Cos(_yaw) * horizontal);
        _camera.LookAt(pivot, Vector3.Up);
    }

    public ClientHorizontalCameraBasis HorizontalBasis()
    {
        Vector3 right = _camera.GlobalTransform.Basis.X;
        Vector3 forward = -_camera.GlobalTransform.Basis.Z;
        right.Y = 0;
        forward.Y = 0;
        right = right.Normalized();
        forward = forward.Normalized();
        return new ClientHorizontalCameraBasis(right.X, right.Z, forward.X, forward.Z);
    }

    public GodotGameplayCameraSnapshot GetSnapshot() => new(
        _rig, _yaw, _pitch, _distance, _pivot, _projection,
        _rig.Projection == ClientCameraProjection.Perspective ? _camera.Fov : 0f,
        _rig.Projection == ClientCameraProjection.Orthographic ? _camera.Size : 0f);
}

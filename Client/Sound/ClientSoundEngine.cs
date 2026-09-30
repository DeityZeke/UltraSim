using UltraSim.Audio;

namespace UltraSim.Client.Sound;

/// <summary>What the client observed, not an asset name or a server-issued play command.</summary>
public enum ClientSoundEvent : byte
{
    Footstep = 1, Jump, Land, DoorOpen, DoorClose, Pickup, Drop, Equip,
    PrimaryAction, UiClick, UiOpen, UiClose, Notification, ForestAmbience,
}

public enum SoundCueId : byte
{
    FootstepGrass = 1, Jump, Land, DoorWoodOpen, DoorWoodClose, Pickup,
    Drop, Equip, PrimaryAction, UiClick, UiOpen, UiClose, Notification,
    ForestWind,
}

public readonly record struct SoundCue(SoundCueId Id, AudioCategory Category,
    AudioSpatialMode SpatialMode, byte Priority, float Gain, bool Loop);

public static class ClientSoundEngine
{
    public static bool TryResolve(ClientSoundEvent semanticEvent, out SoundCue cue)
    {
        cue = semanticEvent switch
        {
            ClientSoundEvent.Footstep => new(SoundCueId.FootstepGrass, AudioCategory.Sfx,
                AudioSpatialMode.World3D, 30, .55f, false),
            ClientSoundEvent.Jump => new(SoundCueId.Jump, AudioCategory.Sfx,
                AudioSpatialMode.World3D, 50, .6f, false),
            ClientSoundEvent.Land => new(SoundCueId.Land, AudioCategory.Sfx,
                AudioSpatialMode.World3D, 55, .65f, false),
            ClientSoundEvent.DoorOpen => new(SoundCueId.DoorWoodOpen, AudioCategory.Sfx,
                AudioSpatialMode.World3D, 70, .7f, false),
            ClientSoundEvent.DoorClose => new(SoundCueId.DoorWoodClose, AudioCategory.Sfx,
                AudioSpatialMode.World3D, 70, .7f, false),
            ClientSoundEvent.Pickup => new(SoundCueId.Pickup, AudioCategory.Sfx,
                AudioSpatialMode.World3D, 50, .6f, false),
            ClientSoundEvent.Drop => new(SoundCueId.Drop, AudioCategory.Sfx,
                AudioSpatialMode.World3D, 50, .6f, false),
            ClientSoundEvent.Equip => new(SoundCueId.Equip, AudioCategory.Sfx,
                AudioSpatialMode.World3D, 55, .6f, false),
            ClientSoundEvent.PrimaryAction => new(SoundCueId.PrimaryAction, AudioCategory.Sfx,
                AudioSpatialMode.World3D, 65, .7f, false),
            ClientSoundEvent.UiClick => new(SoundCueId.UiClick, AudioCategory.Ui,
                AudioSpatialMode.NonSpatial, 80, .38f, false),
            ClientSoundEvent.UiOpen => new(SoundCueId.UiOpen, AudioCategory.Ui,
                AudioSpatialMode.NonSpatial, 80, .4f, false),
            ClientSoundEvent.UiClose => new(SoundCueId.UiClose, AudioCategory.Ui,
                AudioSpatialMode.NonSpatial, 80, .4f, false),
            ClientSoundEvent.Notification => new(SoundCueId.Notification, AudioCategory.Ui,
                AudioSpatialMode.NonSpatial, 90, .55f, false),
            ClientSoundEvent.ForestAmbience => new(SoundCueId.ForestWind, AudioCategory.Ambient,
                AudioSpatialMode.NonSpatial, 15, .28f, true),
            _ => default,
        };
        return cue.Id != 0;
    }

    public static float EffectiveGain(float master, float category, float cueGain)
    {
        if (!float.IsFinite(master) || master is < 0 or > 1 ||
            !float.IsFinite(category) || category is < 0 or > 1 ||
            !float.IsFinite(cueGain) || cueGain is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(master));
        return master * category * cueGain;
    }
}

/// <summary>Presentation-distance cadence, independent of server tick and input packet rate.</summary>
public sealed class ClientFootstepCadence
{
    private long _x, _z, _lastStepMilliseconds;
    private long _distance;
    private bool _hasSample;

    public bool Observe(long xMillimeters, long zMillimeters, long nowMilliseconds,
        bool moving, bool grounded)
    {
        if (!_hasSample)
        {
            _hasSample = true; _x = xMillimeters; _z = zMillimeters;
            _lastStepMilliseconds = nowMilliseconds;
            return false;
        }
        // Widen before subtraction: malformed/extreme presentation coordinates must not wrap.
        double dx = Math.Abs((double)xMillimeters - _x);
        double dz = Math.Abs((double)zMillimeters - _z);
        _x = xMillimeters; _z = zMillimeters;
        if (!moving || !grounded || dx > 3_000 || dz > 3_000)
        {
            _distance = 0;
            return false;
        }
        _distance = Math.Min(1_800, _distance + (long)Math.Max(dx, dz));
        if (_distance < 850 || nowMilliseconds - _lastStepMilliseconds < 210) return false;
        _distance -= 850;
        _lastStepMilliseconds = nowMilliseconds;
        return true;
    }

    public void Reset() { _hasSample = false; _distance = 0; }
}

namespace UltraSim.Audio;

/// <summary>Device-independent presentation categories. Master is a gain, not a voice.</summary>
public enum AudioCategory : byte { Sfx = 1, Ambient = 2, Ui = 3 }
public enum AudioSpatialMode : byte { NonSpatial = 1, World3D = 2 }

public readonly record struct AudioSourceHandle(ushort Slot, ulong Generation)
{
    public bool IsValid => Generation != 0;
}

public readonly record struct AudioListenerFrame(long XMillimeters, long YMillimeters,
    long ZMillimeters);

public readonly record struct AudioPlaybackRequest(
    AudioCategory Category, AudioSpatialMode SpatialMode, byte Priority,
    float Gain, float Pitch, bool Loop, long XMillimeters = 0,
    long YMillimeters = 0, long ZMillimeters = 0)
{
    public bool IsValid => Enum.IsDefined(Category) && Enum.IsDefined(SpatialMode) &&
        float.IsFinite(Gain) && Gain is >= 0f and <= 1f &&
        float.IsFinite(Pitch) && Pitch is >= .5f and <= 2f &&
        (SpatialMode != AudioSpatialMode.NonSpatial ||
            XMillimeters == 0 && YMillimeters == 0 && ZMillimeters == 0);
}

/// <summary>Fixed owner-local voice admission; exhaustion may drop presentation, never Game work.</summary>
public sealed class AudioSourceBudget
{
    private readonly bool[] _occupied;
    private readonly byte[] _priority;
    private readonly ulong[] _generation;
    private int _active;
    private int _peak;
    private ulong _dropped;

    public AudioSourceBudget(int capacity)
    {
        if (capacity is < 1 or > 128) throw new ArgumentOutOfRangeException(nameof(capacity));
        _occupied = new bool[capacity];
        _priority = new byte[capacity];
        _generation = new ulong[capacity];
    }

    public int Capacity => _occupied.Length;
    public int Active => _active;
    public int Peak => _peak;
    public ulong Dropped => _dropped;

    public bool TryAdmit(byte priority, out AudioSourceHandle handle, out AudioSourceHandle replaced)
    {
        replaced = default;
        int slot = Array.IndexOf(_occupied, false);
        bool vacant = slot >= 0;
        if (slot < 0)
        {
            slot = 0;
            for (int i = 1; i < _priority.Length; i++)
                if (_priority[i] < _priority[slot]) slot = i;
            if (_priority[slot] >= priority)
            {
                if (_dropped != ulong.MaxValue) _dropped++;
                handle = default;
                return false;
            }
            replaced = new((ushort)slot, _generation[slot]);
        }
        if (_generation[slot] == ulong.MaxValue)
            throw new InvalidOperationException("The audio source generation space is exhausted.");
        if (vacant) { _occupied[slot] = true; _active++; _peak = Math.Max(_peak, _active); }
        _priority[slot] = priority;
        // A stale handle must never become current again because a small counter wrapped.
        _generation[slot] = checked(_generation[slot] + 1);
        handle = new((ushort)slot, _generation[slot]);
        return true;
    }

    public bool Retire(AudioSourceHandle handle)
    {
        if (!handle.IsValid || handle.Slot >= _occupied.Length ||
            !_occupied[handle.Slot] || _generation[handle.Slot] != handle.Generation)
            return false;
        _occupied[handle.Slot] = false;
        _active--;
        return true;
    }

    public void Clear()
    {
        Array.Clear(_occupied);
        Array.Clear(_priority);
        _active = 0;
    }
}

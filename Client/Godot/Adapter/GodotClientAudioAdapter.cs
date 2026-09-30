using Godot;
using UltraSim.Audio;
using UltraSim.Client.Content;
using UltraSim.Client.Sound;

namespace UltraSim.Client.Godot;

public readonly record struct GodotClientAudioSnapshot(
    int SourceBudget, int ActiveSources, int PeakSources, int ActiveLoops,
    ulong DroppedRequests, int LoadedCues, float MasterVolume,
    bool Muted, bool SessionActive, long ListenerXMillimeters, long ListenerZMillimeters,
    bool CatalogBacked = false, float SfxVolume = 1, float AmbientVolume = 1,
    float UiVolume = 1, ulong NativeFailures = 0);

/// <summary>
/// The sole Godot realization of the engine-neutral sound cue/source contracts. All methods
/// run on the Godot owner thread. Native streams remain strongly owned until players stop and
/// their scene nodes retire; neither a stream nor a player is left to finalization.
/// </summary>
public sealed class GodotClientAudioAdapter : IDisposable
{
    private const int MaximumSources = 16;
    private const float MaximumDistanceMeters = 28;
    private readonly AudioSourceBudget _budget = new(MaximumSources);
    private readonly Node3D _root = new() { Name = "ClientAudio" };
    private readonly AudioListener3D _listener = new() { Name = "AudioListener" };
    private readonly AudioStreamPlayer[] _nonspatial = new AudioStreamPlayer[MaximumSources];
    private readonly AudioStreamPlayer3D[] _spatial = new AudioStreamPlayer3D[MaximumSources];
    private readonly AudioSourceHandle[] _handles = new AudioSourceHandle[MaximumSources];
    // Godot's AudioServer retains stopped playbacks until its mix/main-thread cleanup.
    // Strong wrappers let shutdown prove that native ownership has actually retired.
    private readonly AudioStreamPlayback?[] _playbacks = new AudioStreamPlayback[128];
    private readonly AudioStreamWav?[] _playbackStreams = new AudioStreamWav[128];
    private int _trackedPlaybacks;
    private readonly SoundCue[] _playing = new SoundCue[MaximumSources];
    private readonly AudioStreamWav?[] _streams = new AudioStreamWav[15];
    private readonly AudioStreamWav?[] _retiringStreams = new AudioStreamWav[15];
    private readonly ulong[] _retireAfterFrame = new ulong[15];
    private readonly ContentHash[] _clipHashes = new ContentHash[15];
    private readonly ContentCatalogGenerationId[] _clipGenerations =
        new ContentCatalogGenerationId[15];
    private readonly ulong[] _cueRequests = new ulong[15];
    private readonly int _ownerThread = System.Environment.CurrentManagedThreadId;
    private AudioListenerFrame _listenerFrame;
    private float _master = .8f;
    private float _sfxVolume = 1f;
    private float _ambientVolume = 1f;
    private float _uiVolume = 1f;
    private bool _sessionActive;
    private bool _disposed;
    private bool _retiring;
    private bool _unavailable;
    private ulong _nativeFailures;
    private ulong _culled;
    private readonly bool _catalogBacked;

    public GodotClientAudioAdapter(Node parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        try
        {
            parent.AddChild(_root);
            _root.AddChild(_listener);
            _listener.MakeCurrent();
            for (int i = 0; i < MaximumSources; i++)
            {
                var local = new AudioStreamPlayer { Name = $"UiSource{i}" };
                var world = new AudioStreamPlayer3D
                {
                    Name = $"WorldSource{i}", MaxDistance = MaximumDistanceMeters,
                    UnitSize = 5,
                };
                _root.AddChild(local);
                _root.AddChild(world);
                _nonspatial[i] = local;
                _spatial[i] = world;
            }
            byte[][] ownedWav = GodotAudioContentBank.LoadOrGenerate(out _catalogBacked);
            for (int i = 1; i < _streams.Length; i++)
            {
                byte[] wav = ownedWav[i];
                if (!ContentAudioArtifact.TryInspect(wav, out ContentAudioArtifactInfo info))
                    throw new InvalidOperationException("An owned generated audio clip failed passive validation.");
                _streams[i] = CreateStream(wav, info, i == (int)SoundCueId.ForestWind);
                _clipHashes[i] = ContentHash.Compute(wav);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void SetMasterVolume(float volume)
    {
        RequireOwner();
        if (!float.IsFinite(volume) || volume is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(volume));
        _master = volume;
        if (volume == 0)
        {
            for (int i = 0; i < MaximumSources; i++) RetireSlot(i);
        }
        else
        {
            for (int i = 0; i < MaximumSources; i++)
                if (_handles[i].IsValid) ApplyGain(i, _playing[i]);
            if (_sessionActive) EnsureAmbience();
        }
    }

    public void SetCategoryVolume(AudioCategory category, float volume)
    {
        RequireOwner();
        if (!Enum.IsDefined(category) || !float.IsFinite(volume) || volume is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(volume));
        switch (category)
        {
            case AudioCategory.Sfx: _sfxVolume = volume; break;
            case AudioCategory.Ambient: _ambientVolume = volume; break;
            case AudioCategory.Ui: _uiVolume = volume; break;
        }
        for (int i = 0; i < MaximumSources; i++)
        {
            if (!_handles[i].IsValid || _playing[i].Category != category) continue;
            if (volume == 0) RetireSlot(i);
            else ApplyGain(i, _playing[i]);
        }
        if (category == AudioCategory.Ambient && volume > 0 && _sessionActive)
            EnsureAmbience();
    }

    public void BeginSession()
    {
        RequireOwner();
        _sessionActive = true;
        EnsureAmbience();
    }

    public void EndSession()
    {
        RequireOwner();
        _sessionActive = false;
        for (int i = 0; i < MaximumSources; i++) RetireSlot(i);
        _listenerFrame = default;
    }

    public void SetListener(AudioListenerFrame frame)
    {
        RequireOwner();
        _listenerFrame = frame;
    }

    public bool TryPlay(ClientSoundEvent semanticEvent, long xMillimeters = 0,
        long yMillimeters = 0, long zMillimeters = 0)
    {
        RequireOwner();
        if (!ClientSoundEngine.TryResolve(semanticEvent, out SoundCue cue) ||
            (!_sessionActive && cue.Category != AudioCategory.Ui)) return false;
        if (_cueRequests[(int)cue.Id] != ulong.MaxValue) _cueRequests[(int)cue.Id]++;
        if (_unavailable || _master == 0 || CategoryVolume(cue.Category) == 0 ||
            _streams[(int)cue.Id] is null) return false;
        if (cue.SpatialMode == AudioSpatialMode.World3D &&
            (Math.Abs((double)xMillimeters - _listenerFrame.XMillimeters) > MaximumDistanceMeters * 1000 ||
             Math.Abs((double)zMillimeters - _listenerFrame.ZMillimeters) > MaximumDistanceMeters * 1000))
        {
            if (_culled != ulong.MaxValue) _culled++;
            return false;
        }
        if (cue.Loop)
            for (int i = 0; i < MaximumSources; i++)
                if (_handles[i].IsValid && _playing[i].Id == cue.Id) return true;
        RetireReleasedPlaybacks();
        int playbackSlot = -1;
        for (int i = 0; i < _playbacks.Length; i++)
            if (_playbacks[i] is null) { playbackSlot = i; break; }
        if (playbackSlot < 0)
        {
            if (_culled != ulong.MaxValue) _culled++;
            return false;
        }
        if (!_budget.TryAdmit(cue.Priority, out AudioSourceHandle handle, out _)) return false;
        int slot = handle.Slot;
        StopPlayers(slot);
        _handles[slot] = handle;
        _playing[slot] = cue;
        AudioStreamWav stream = _streams[(int)cue.Id]!;
        try
        {
            if (cue.SpatialMode == AudioSpatialMode.World3D)
            {
                AudioStreamPlayer3D player = _spatial[slot];
                player.Position = new Vector3(
                    (float)((xMillimeters - (double)_listenerFrame.XMillimeters) * .001),
                    (float)((yMillimeters - (double)_listenerFrame.YMillimeters) * .001),
                    (float)((zMillimeters - (double)_listenerFrame.ZMillimeters) * .001));
                player.Stream = stream;
                ApplyGain(slot, cue);
                player.Play();
                _playbacks[playbackSlot] = player.GetStreamPlayback();
            }
            else
            {
                AudioStreamPlayer player = _nonspatial[slot];
                player.Stream = stream;
                ApplyGain(slot, cue);
                player.Play();
                _playbacks[playbackSlot] = player.GetStreamPlayback();
            }
            if (_playbacks[playbackSlot] is null)
                throw new InvalidOperationException("Godot did not provide an audio playback owner.");
            _playbackStreams[playbackSlot] = stream;
            _trackedPlaybacks++;
            return true;
        }
        catch (Exception error)
        {
            if (_nativeFailures != ulong.MaxValue) _nativeFailures++;
            _unavailable = true;
            try { RetireSlot(slot); }
            catch (Exception cleanupError)
            {
                GD.PushWarning($"Native audio source cleanup also failed: {cleanupError}");
            }
            // Audio is optional presentation. Preserve Game execution and make the one-way
            // adapter failure visible; source retirement still runs during session shutdown.
            GD.PushWarning($"Native audio became unavailable; cues will be skipped: {error}");
            return false;
        }
    }

    public void ProcessFrame()
    {
        RequireOwner();
        RetireReleasedPlaybacks();
        ulong frame = Engine.GetProcessFrames();
        for (int i = 1; i < _retiringStreams.Length; i++)
        {
            AudioStreamWav? old = _retiringStreams[i];
            if (old is null || frame < _retireAfterFrame[i]) continue;
            bool stillOwned = false;
            for (int playback = 0; playback < _playbacks.Length; playback++)
                if (_playbacks[playback] is not null &&
                    ReferenceEquals(_playbackStreams[playback], old))
                { stillOwned = true; break; }
            if (stillOwned) continue;
            _retiringStreams[i]!.Dispose();
            _retiringStreams[i] = null;
        }
        for (int i = 0; i < MaximumSources; i++)
        {
            if (!_handles[i].IsValid || _playing[i].Loop) continue;
            if (!_nonspatial[i].Playing && !_spatial[i].Playing) RetireSlot(i);
        }
        if (_sessionActive && _master > 0) EnsureAmbience();
    }

    /// <summary>True only after the native AudioServer no longer owns any stopped playback.</summary>
    public bool PlaybackOwnershipRetired
    {
        get
        {
            if (System.Environment.CurrentManagedThreadId != _ownerThread)
                throw new InvalidOperationException("Audio retirement requires its Godot owner thread.");
            RetireReleasedPlaybacks();
            return _trackedPlaybacks == 0;
        }
    }

    private void RetireReleasedPlaybacks()
    {
        if (_trackedPlaybacks == 0) return;
        for (int i = 0; i < _playbacks.Length; i++)
        {
            AudioStreamPlayback? playback = _playbacks[i];
            if (playback is null || playback.GetReferenceCount() > 1) continue;
            playback.Dispose();
            _playbacks[i] = null;
            _playbackStreams[i] = null;
            _trackedPlaybacks--;
        }
    }

    public GodotClientAudioSnapshot GetSnapshot()
    {
        RequireOwner();
        int loops = 0;
        for (int i = 0; i < MaximumSources; i++)
            if (_handles[i].IsValid && _playing[i].Loop) loops++;
        int loaded = 0;
        for (int i = 1; i < _streams.Length; i++) if (_streams[i] is not null) loaded++;
        ulong dropped = _budget.Dropped > ulong.MaxValue - _culled
            ? ulong.MaxValue : _budget.Dropped + _culled;
        return new(MaximumSources, _budget.Active, _budget.Peak, loops,
            dropped, loaded, _master, _master == 0,
            _sessionActive, _listenerFrame.XMillimeters, _listenerFrame.ZMillimeters,
            _catalogBacked, _sfxVolume, _ambientVolume, _uiVolume, _nativeFailures);
    }

    public ulong NativeFailures => _nativeFailures;

    internal AudioStreamWav? GetOwnedStreamForNativeTest(SoundCueId id)
    {
        RequireOwner();
        return id is >= SoundCueId.FootstepGrass and <= SoundCueId.ForestWind
            ? _streams[(int)id] : null;
    }

    internal ulong GetCueRequestsForNativeTest(SoundCueId id)
    {
        RequireOwner();
        return id is >= SoundCueId.FootstepGrass and <= SoundCueId.ForestWind
            ? _cueRequests[(int)id] : 0;
    }

    /// <summary>
    /// Replaces one already-authorized passive clip at a new catalog generation. Players
    /// holding the old stream are stopped first; its wrapper remains strongly owned across
    /// two engine frames before explicit disposal. A second replacement waits for retirement.
    /// </summary>
    public bool TryReplaceClip(SoundCueId cue, in ResolvedContentAudioClip clip)
    {
        RequireOwner();
        int index = (int)cue;
        if (index is < 1 or >= 15 || clip.Id != GodotAudioContentBank.CueId(cue) ||
            !clip.CatalogGeneration.IsValid || !clip.CatalogFingerprint.IsValid ||
            !clip.BlobHash.IsValid || _retiringStreams[index] is not null ||
            !ContentAudioArtifact.TryInspect(clip.WavBytes, out ContentAudioArtifactInfo info) ||
            ContentHash.Compute(clip.WavBytes) != clip.BlobHash)
            return false;
        if (_clipGenerations[index] == clip.CatalogGeneration &&
            _clipHashes[index] == clip.BlobHash) return true;
        AudioStreamWav replacement = CreateStream(clip.WavBytes, info,
            cue == SoundCueId.ForestWind);
        for (int slot = 0; slot < MaximumSources; slot++)
            if (_handles[slot].IsValid && _playing[slot].Id == cue) RetireSlot(slot);
        _retiringStreams[index] = _streams[index];
        _retireAfterFrame[index] = Engine.GetProcessFrames() + 2;
        _streams[index] = replacement;
        _clipHashes[index] = clip.BlobHash;
        _clipGenerations[index] = clip.CatalogGeneration;
        if (_sessionActive && cue == SoundCueId.ForestWind && _master > 0) EnsureAmbience();
        return true;
    }

    private static AudioStreamWav CreateStream(byte[] wav, in ContentAudioArtifactInfo info,
        bool loop) => new()
    {
        Data = wav.AsSpan(info.DataOffset).ToArray(),
        Format = AudioStreamWav.FormatEnum.Format16Bits,
        MixRate = info.SampleRate,
        Stereo = false,
        LoopMode = loop ? AudioStreamWav.LoopModeEnum.Forward :
            AudioStreamWav.LoopModeEnum.Disabled,
        LoopBegin = 0,
        LoopEnd = info.Samples,
    };

    private void EnsureAmbience()
    {
        if (_ambientVolume == 0) return;
        for (int i = 0; i < MaximumSources; i++)
            if (_handles[i].IsValid && _playing[i].Id == SoundCueId.ForestWind) return;
        if (_budget.Active == _budget.Capacity) return;
        _ = TryPlay(ClientSoundEvent.ForestAmbience);
    }

    private void ApplyGain(int slot, SoundCue cue)
    {
        float effective = ClientSoundEngine.EffectiveGain(_master,
            CategoryVolume(cue.Category), cue.Gain);
        float decibels = effective <= 0 ? -80 : Mathf.LinearToDb(effective);
        if (cue.SpatialMode == AudioSpatialMode.World3D) _spatial[slot].VolumeDb = decibels;
        else _nonspatial[slot].VolumeDb = decibels;
    }

    private float CategoryVolume(AudioCategory category) => category switch
    {
        AudioCategory.Sfx => _sfxVolume,
        AudioCategory.Ambient => _ambientVolume,
        AudioCategory.Ui => _uiVolume,
        _ => 0,
    };

    private void RetireSlot(int slot)
    {
        if (slot >= _nonspatial.Length || _nonspatial[slot] is null) return;
        StopPlayers(slot);
        if (_handles[slot].IsValid) _budget.Retire(_handles[slot]);
        _handles[slot] = default;
        _playing[slot] = default;
    }

    private void StopPlayers(int slot)
    {
        _nonspatial[slot].Stop();
        _spatial[slot].Stop();
        _nonspatial[slot].Stream = null;
        _spatial[slot].Stream = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        BeginRetirement();
        CompleteRetirement();
    }

    /// <summary>First phase: no native player can still acquire or retain a stream.</summary>
    public void BeginRetirement()
    {
        if (_retiring) return;
        RequireOwner();
        _retiring = true;
        EndSessionForDisposal();
        if (GodotObject.IsInstanceValid(_root))
        {
            _root.GetParent()?.RemoveChild(_root);
            _root.Free();
        }
    }

    /// <summary>Second phase, after the engine's deletion-queue frame boundary.</summary>
    public void CompleteRetirement()
    {
        if (_disposed) return;
        if (!_retiring) throw new InvalidOperationException("Audio players must retire before their streams.");
        if (System.Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("Audio stream retirement requires its Godot owner thread.");
        // A direct test/owner disposal may run before the AudioServer cleanup cycle. The
        // application shutdown path waits for PlaybackOwnershipRetired first.
        for (int i = 0; i < _playbacks.Length; i++)
        {
            _playbacks[i]?.Dispose();
            _playbacks[i] = null;
            _playbackStreams[i] = null;
        }
        _trackedPlaybacks = 0;
        for (int i = _streams.Length - 1; i >= 1; i--)
        {
            _streams[i]?.Dispose();
            _streams[i] = null;
            _retiringStreams[i]?.Dispose();
            _retiringStreams[i] = null;
        }
        _disposed = true;
    }

    private void EndSessionForDisposal()
    {
        _sessionActive = false;
        for (int i = 0; i < MaximumSources; i++) RetireSlot(i);
    }

    private void RequireOwner()
    {
        if (System.Environment.CurrentManagedThreadId != _ownerThread || _disposed || _retiring)
            throw new InvalidOperationException("Audio adapter access requires its live Godot owner thread.");
    }
}

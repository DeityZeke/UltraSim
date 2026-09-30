using System.Buffers.Binary;

namespace UltraSim.Client.Sound;

/// <summary>
/// UltraSim-owned deterministic placeholder audio. These finite PCM assets are presentation
/// data, not gameplay definitions; a later compatible content selection can replace them.
/// </summary>
public static class GeneratedSoundClips
{
    public const int SampleRate = 22_050;

    public static byte[] CreateWav(SoundCueId cue)
    {
        if (cue is < SoundCueId.FootstepGrass or > SoundCueId.ForestWind)
            throw new ArgumentOutOfRangeException(nameof(cue));
        bool ambient = cue == SoundCueId.ForestWind;
        int sampleCount = ambient ? SampleRate * 4 : cue switch
        {
            SoundCueId.DoorWoodOpen or SoundCueId.DoorWoodClose => SampleRate / 2,
            SoundCueId.Notification => SampleRate * 2 / 5,
            _ => SampleRate / 5,
        };
        byte[] wav = new byte[44 + sampleCount * 2];
        "RIFF"u8.CopyTo(wav);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(4), wav.Length - 8);
        "WAVEfmt "u8.CopyTo(wav.AsSpan(8));
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(20), 1);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(22), 1);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(24), SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(28), SampleRate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(32), 2);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(34), 16);
        "data"u8.CopyTo(wav.AsSpan(36));
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(40), sampleCount * 2);

        int seed = 0x1371 + (int)cue * 0x43d;
        double baseFrequency = cue switch
        {
            SoundCueId.FootstepGrass => 110,
            SoundCueId.Jump => 390,
            SoundCueId.Land => 95,
            SoundCueId.DoorWoodOpen => 165,
            SoundCueId.DoorWoodClose => 130,
            SoundCueId.Pickup => 590,
            SoundCueId.Drop => 220,
            SoundCueId.Equip => 470,
            SoundCueId.PrimaryAction => 190,
            SoundCueId.UiClick => 850,
            SoundCueId.UiOpen => 660,
            SoundCueId.UiClose => 440,
            SoundCueId.Notification => 780,
            _ => 42,
        };
        double filteredNoise = 0;
        for (int i = 0; i < sampleCount; i++)
        {
            seed = unchecked(seed * 1664525 + 1013904223);
            double noise = (unchecked((uint)seed) / (double)uint.MaxValue) * 2 - 1;
            filteredNoise = filteredNoise * (ambient ? .993 : .76) +
                noise * (ambient ? .007 : .24);
            double phase = 2 * Math.PI * baseFrequency * i / SampleRate;
            double envelope = ambient
                ? Math.Pow(Math.Sin(Math.PI * i / sampleCount), 2)
                : Math.Min(1, i / (SampleRate * .006)) *
                    Math.Pow(1 - i / (double)sampleCount, cue is SoundCueId.DoorWoodOpen or
                        SoundCueId.DoorWoodClose ? 1.4 : 2.6);
            double tonal = Math.Sin(phase) + .27 * Math.Sin(phase * 1.99);
            double signal = cue is SoundCueId.FootstepGrass or SoundCueId.Land or
                SoundCueId.ForestWind or SoundCueId.DoorWoodClose
                    ? filteredNoise * (ambient ? 8 : 2.3) + tonal * .27
                    : tonal * .7 + filteredNoise * .45;
            short value = (short)Math.Clamp((int)(signal * envelope *
                (ambient ? 4100 : 9500)), short.MinValue, short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(44 + i * 2), value);
        }
        return wav;
    }
}

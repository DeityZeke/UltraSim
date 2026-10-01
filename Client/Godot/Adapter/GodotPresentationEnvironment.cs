using Godot;

namespace UltraSim.Client.Godot;

public enum GodotDiagnosticTimeOfDay : byte { Dawn, Noon, Dusk, Night }
public enum GodotDiagnosticAtmosphere : byte { Temperate, Dry, Cold }
public enum GodotDiagnosticLightingMood : byte { Balanced, BrightClear, DarkOppressive }

public readonly record struct GodotPresentationEnvironmentSnapshot(Vector3 SunRotationDegrees,
    float SunEnergy, bool Shadows, Color BackgroundColor, Color AmbientColor, float AmbientEnergy,
    GodotDiagnosticLightingMood Mood = GodotDiagnosticLightingMood.Balanced);

/// <summary>Finite presentation-only preview; not Game time, weather, visibility or lighting authority.</summary>
public sealed class GodotPresentationEnvironment : IDisposable
{
    private readonly DirectionalLight3D _sun;
    private readonly WorldEnvironment _world;
    private readonly global::Godot.Environment _environment;
    private bool _disposed;
    public GodotPresentationEnvironment(Node3D host)
    {
        string[] arguments = OS.GetCmdlineUserArgs();
        var time = GodotDiagnosticTimeOfDay.Noon; var atmosphere = GodotDiagnosticAtmosphere.Temperate;
        var mood = GodotDiagnosticLightingMood.Balanced;
        foreach (string argument in arguments)
        {
            if (argument.StartsWith("--f1-11-time="))
            {
                if (!Enum.TryParse(argument[13..], true, out time) || !Enum.IsDefined(time))
                    throw new ArgumentException("Use dawn/noon/dusk/night for diagnostic time.");
            }
            if (argument.StartsWith("--f1-11-atmosphere="))
            {
                if (!Enum.TryParse(argument[19..], true, out atmosphere) || !Enum.IsDefined(atmosphere))
                    throw new ArgumentException("Use temperate/dry/cold for diagnostic atmosphere.");
            }
            if (argument.StartsWith("--p9-lighting-mood="))
            {
                if (!Enum.TryParse(argument[19..], true, out mood) || !Enum.IsDefined(mood))
                    throw new ArgumentException("Use Balanced/BrightClear/DarkOppressive for P9 lighting.");
            }
        }
        _sun = new() { Name = "DiagnosticSun" };
        _environment = new()
        {
            BackgroundMode = global::Godot.Environment.BGMode.Color,
            AmbientLightSource = global::Godot.Environment.AmbientSource.Color
        };
        _world = new() { Name = "DiagnosticAtmosphere", Environment = _environment };
        host.AddChild(_sun); host.AddChild(_world);
        Apply(time, atmosphere, !arguments.Contains("--f1-11-no-shadows"), mood);
    }
    public void Apply(GodotDiagnosticTimeOfDay time, GodotDiagnosticAtmosphere atmosphere,
        bool shadows, GodotDiagnosticLightingMood mood = GodotDiagnosticLightingMood.Balanced)
    {
        if (!Enum.IsDefined(time) || !Enum.IsDefined(atmosphere) || !Enum.IsDefined(mood))
            throw new ArgumentOutOfRangeException(nameof(time));
        Mood = mood;
        _sun.RotationDegrees = new(time switch
        {
            GodotDiagnosticTimeOfDay.Dawn => -12,
            GodotDiagnosticTimeOfDay.Noon => -55,
            GodotDiagnosticTimeOfDay.Dusk => -8,
            _ => 20
        }, -35, 0);
        float moodScale = mood switch
        {
            GodotDiagnosticLightingMood.BrightClear => 1.15f,
            GodotDiagnosticLightingMood.DarkOppressive => 0.68f,
            _ => 1f,
        };
        _sun.LightEnergy = (time == GodotDiagnosticTimeOfDay.Night ? 0 :
            time == GodotDiagnosticTimeOfDay.Noon ? 1.15f : .55f) * moodScale;
        _sun.LightColor = time is GodotDiagnosticTimeOfDay.Dawn or GodotDiagnosticTimeOfDay.Dusk ? new(1, .68f, .42f) : Colors.White;
        _sun.ShadowEnabled = shadows;
        Color color = atmosphere switch
        {
            GodotDiagnosticAtmosphere.Dry => new(.6f, .52f, .38f),
            GodotDiagnosticAtmosphere.Cold => new(.52f, .65f, .78f),
            _ => new(.65f, .72f, .68f)
        };
        _environment.BackgroundColor = time switch
        {
            GodotDiagnosticTimeOfDay.Dawn => new(.36f, .32f, .37f),
            GodotDiagnosticTimeOfDay.Noon => new(.45f, .56f, .58f),
            GodotDiagnosticTimeOfDay.Dusk => new(.30f, .27f, .36f),
            _ => new(.018f, .026f, .045f),
        };
        _environment.AmbientLightColor = color;
        _environment.AmbientLightEnergy =
            (time == GodotDiagnosticTimeOfDay.Night ? .12f : .55f) * moodScale;
    }
    public GodotDiagnosticLightingMood Mood { get; private set; }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (GodotObject.IsInstanceValid(_sun)) _sun.Free();
        if (GodotObject.IsInstanceValid(_world)) _world.Free();
        _environment.Dispose();
    }

    public GodotPresentationEnvironmentSnapshot GetSnapshot() => new(_sun.RotationDegrees,
        _sun.LightEnergy, _sun.ShadowEnabled, _environment.BackgroundColor,
        _environment.AmbientLightColor, _environment.AmbientLightEnergy, Mood);
}

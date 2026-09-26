using Godot;

namespace UltraSim.Client.Godot;

public enum GodotDiagnosticTimeOfDay : byte { Dawn, Noon, Dusk, Night }
public enum GodotDiagnosticAtmosphere : byte { Temperate, Dry, Cold }

public readonly record struct GodotPresentationEnvironmentSnapshot(Vector3 SunRotationDegrees,
    float SunEnergy, bool Shadows, Color BackgroundColor, Color AmbientColor, float AmbientEnergy);

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
        }
        _sun = new() { Name = "DiagnosticSun" };
        _environment = new()
        {
            BackgroundMode = global::Godot.Environment.BGMode.Color,
            AmbientLightSource = global::Godot.Environment.AmbientSource.Color
        };
        _world = new() { Name = "DiagnosticAtmosphere", Environment = _environment };
        host.AddChild(_sun); host.AddChild(_world);
        Apply(time, atmosphere, !arguments.Contains("--f1-11-no-shadows"));
    }
    public void Apply(GodotDiagnosticTimeOfDay time, GodotDiagnosticAtmosphere atmosphere, bool shadows)
    {
        if (!Enum.IsDefined(time) || !Enum.IsDefined(atmosphere)) throw new ArgumentOutOfRangeException(nameof(time));
        _sun.RotationDegrees = new(time switch
        {
            GodotDiagnosticTimeOfDay.Dawn => -12,
            GodotDiagnosticTimeOfDay.Noon => -55,
            GodotDiagnosticTimeOfDay.Dusk => -8,
            _ => 20
        }, -35, 0);
        _sun.LightEnergy = time == GodotDiagnosticTimeOfDay.Night ? 0 : time == GodotDiagnosticTimeOfDay.Noon ? 1.15f : .55f;
        _sun.LightColor = time is GodotDiagnosticTimeOfDay.Dawn or GodotDiagnosticTimeOfDay.Dusk ? new(1, .68f, .42f) : Colors.White;
        _sun.ShadowEnabled = shadows;
        Color color = atmosphere switch
        {
            GodotDiagnosticAtmosphere.Dry => new(.6f, .52f, .38f),
            GodotDiagnosticAtmosphere.Cold => new(.52f, .65f, .78f),
            _ => new(.65f, .72f, .68f)
        };
        _environment.BackgroundColor = time == GodotDiagnosticTimeOfDay.Night ? new(.015f, .025f, .04f) : color * .20f;
        _environment.AmbientLightColor = color;
        _environment.AmbientLightEnergy = time == GodotDiagnosticTimeOfDay.Night ? .12f : .65f;
    }
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
        _environment.AmbientLightColor, _environment.AmbientLightEnergy);
}

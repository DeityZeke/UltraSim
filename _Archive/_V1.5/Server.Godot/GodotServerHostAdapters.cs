using Godot;
using UltraSim.Server;

namespace Server.Godot
{
    public sealed class GodotServerLogger : IServerLogger
    {
        public void Info(string message) => GD.Print(message);
        public void Warn(string message) => GD.PushWarning(message);
        public void Error(string message) => GD.PushError(message);
    }
}

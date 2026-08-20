namespace UltraSim.Server
{
    /// <summary>
    /// Engine-agnostic logging interface for server host output.
    /// Implementations wire to GD.Print (Godot), Console.WriteLine (headless), etc.
    /// </summary>
    public interface IServerLogger
    {
        void Info(string message);
        void Warn(string message);
        void Error(string message);
    }
}

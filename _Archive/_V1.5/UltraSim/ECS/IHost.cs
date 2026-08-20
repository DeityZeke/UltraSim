namespace UltraSim.ECS
{
    public enum LogLevel : byte
    {
        Trace = 0,
        Info = 1,
        Warning = 2,
        Error = 3,
    }

    public interface IHost
    {
        EnvironmentType Environment { get; }
        void Log(LogLevel level, string message);
    }

    public sealed class NoOpHost : IHost
    {
        public static readonly NoOpHost Instance = new NoOpHost();

        private NoOpHost()
        {
        }

        public EnvironmentType Environment => EnvironmentType.Hybrid;

        public void Log(LogLevel level, string message)
        {
        }
    }
}

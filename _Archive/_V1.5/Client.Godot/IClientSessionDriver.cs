namespace Client.Godot
{
    public interface IClientSessionDriver
    {
        void Initialize(GodotClientSession session);

        void Tick(float deltaSeconds);

        void Shutdown();
    }
}

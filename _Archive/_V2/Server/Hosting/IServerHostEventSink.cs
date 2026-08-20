namespace UltraSim.Server.Hosting;

public interface IServerHostEventSink
{
    void OnEvent(ServerApplicationEvent evt);
}

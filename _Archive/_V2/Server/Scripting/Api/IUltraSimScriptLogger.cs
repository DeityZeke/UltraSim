namespace UltraSim.Server.Scripting;

public interface IUltraSimScriptLogger
{
    void Info(string message);
    void Warning(string message);
    void Error(string message);
}

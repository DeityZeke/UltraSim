namespace UltraSim.Server.Scripting;

public interface IUltraSimScriptModule
{
    void Configure(IUltraSimScriptContext context);

    void Initialize(IUltraSimScriptContext context)
    {
    }
}

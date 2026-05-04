namespace UltraSim.Server.Scripting;

public interface IUltraSimScriptContentGroupRegistry
{
    UltraSimRuntimeContentGroupOperationResult Register(
        UltraSimRuntimeContentGroupDescriptor descriptor,
        bool mounted = true);

    UltraSimRuntimeContentGroupOperationResult Mount(string key);

    UltraSimRuntimeContentGroupOperationResult Unmount(string key);
}

namespace UltraSim.Server.Operator;

/// <summary>
/// One process owns a deployment identity and its live SaveSet at a time. Cold backup recovery
/// and an active Direct Connect server use the same lease; neither can race the other.
/// </summary>
public sealed class ServerOperatorInstanceLease : IDisposable
{
    private readonly FileStream _file;
    private readonly bool _locked;
    private bool _disposed;

    private ServerOperatorInstanceLease(FileStream file, bool locked)
    { _file = file; _locked = locked; }

    public static ServerOperatorInstanceLease Acquire(string identityDirectory)
    {
        string root = Path.GetFullPath(identityDirectory);
        Directory.CreateDirectory(root);
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("The server identity directory is a reparse point.");
        string path = Path.Combine(root, "INSTANCE.LOCK");
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("The server instance lease is a reparse point.");
        FileStream file = new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            bool locked = !OperatingSystem.IsMacOS();
            if (locked) file.Lock(0, 1);
            return new ServerOperatorInstanceLease(file, locked);
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_locked && !OperatingSystem.IsMacOS()) _file.Unlock(0, 1);
        }
        finally { _file.Dispose(); }
    }
}

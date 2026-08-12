using System.Runtime.InteropServices;

namespace UltraSim.Server.Persistence;

public interface IDurableFileSystem
{
    Stream CreateDurableFile(string path);
    void FlushFile(Stream stream);
    void FlushDirectory(string path);
    void MoveDirectory(string source, string destination);
    void ReplaceFile(string source, string destination);
}

public sealed class DurableFileSystem : IDurableFileSystem
{
    public Stream CreateDurableFile(string path) => new FileStream(path, FileMode.CreateNew, FileAccess.Write,
        FileShare.None, 64 * 1024, FileOptions.WriteThrough | FileOptions.SequentialScan);
    public void FlushFile(Stream stream)
    {
        if (stream is not FileStream file) throw new ArgumentException("Durable flush requires a FileStream.", nameof(stream));
        file.Flush(flushToDisk: true);
    }
    public void FlushDirectory(string path)
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            int directoryFlag = OperatingSystem.IsMacOS() ? 0x100000 : 0x10000;
            int descriptor = open(path, directoryFlag);
            if (descriptor < 0) throw new IOException($"Unable to open directory '{path}' for durable flush (errno {Marshal.GetLastPInvokeError()}).");
            try { if (fsync(descriptor) != 0) throw new IOException($"Unable to fsync directory '{path}' (errno {Marshal.GetLastPInvokeError()})."); }
            finally { _ = close(descriptor); }
            return;
        }
        throw new PlatformNotSupportedException("Crash-durable directory flush requires a platform-specific adapter.");
    }
    public void MoveDirectory(string source, string destination) => Directory.Move(source, destination);
    public void ReplaceFile(string source, string destination) => File.Move(source, destination, overwrite: true);

    [DllImport("libc", SetLastError = true)] private static extern int open(string pathname, int flags);
    [DllImport("libc", SetLastError = true)] private static extern int fsync(int fd);
    [DllImport("libc", SetLastError = true)] private static extern int close(int fd);
}

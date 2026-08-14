using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace UltraSim.Server.Persistence;

/// <summary>Injectable filesystem operations required by crash-durable save-set publication.</summary>
/// <remarks>Implementations must preserve the flush/rename semantics implied by each operation.</remarks>
public interface IDurableFileSystem
{
    void CreateDirectory(string path);
    bool DirectoryExists(string path);
    void DeleteDirectory(string path, bool recursive);
    IEnumerable<string> EnumerateDirectories(string path, string pattern);
    IEnumerable<string> EnumerateFiles(string path, string pattern);
    bool FileExists(string path);
    void DeleteFile(string path);
    long GetFileLength(string path);
    string ReadAllText(string path);
    IDisposable AcquireExclusiveFile(string path);
    Stream CreateDurableFile(string path);
    Stream OpenRead(string path);
    void FlushFile(Stream stream);
    void FlushDirectory(string path);
    void MoveDirectory(string source, string destination);
    void ReplaceFile(string source, string destination);
}

/// <summary>Windows/Linux/macOS filesystem implementation with file and directory durability barriers.</summary>
public sealed class DurableFileSystem : IDurableFileSystem
{
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public void DeleteDirectory(string path, bool recursive) => Directory.Delete(path, recursive);
    public IEnumerable<string> EnumerateDirectories(string path, string pattern) =>
        Directory.EnumerateDirectories(path, pattern);
    public IEnumerable<string> EnumerateFiles(string path, string pattern) =>
        Directory.EnumerateFiles(path, pattern);
    public bool FileExists(string path) => File.Exists(path);
    public void DeleteFile(string path) => File.Delete(path);
    public long GetFileLength(string path) => new FileInfo(path).Length;
    public string ReadAllText(string path) => File.ReadAllText(path);
    public IDisposable AcquireExclusiveFile(string path) => new FileStream(
        path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
        1, FileOptions.WriteThrough);
    public Stream CreateDurableFile(string path) => new FileStream(path, FileMode.CreateNew, FileAccess.Write,
        FileShare.None, 64 * 1024, FileOptions.WriteThrough | FileOptions.SequentialScan);
    public Stream OpenRead(string path) => new FileStream(path, FileMode.Open, FileAccess.Read,
        FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
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
        if (OperatingSystem.IsWindows())
        {
            using SafeFileHandle handle = CreateFileW(
                path,
                GenericWrite,
                FileShareRead | FileShareWrite | FileShareDelete,
                IntPtr.Zero,
                OpenExisting,
                FileFlagBackupSemantics,
                IntPtr.Zero);
            if (handle.IsInvalid)
                throw new IOException($"Unable to open directory '{path}' for durable flush.",
                    Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error()));
            if (!FlushFileBuffers(handle))
                throw new IOException($"Unable to flush directory '{path}'.",
                    Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error()));
            return;
        }
        throw new PlatformNotSupportedException(
            "Crash-durable save sets are supported on Windows, Linux, and macOS only.");
    }
    public void MoveDirectory(string source, string destination) => Directory.Move(source, destination);
    public void ReplaceFile(string source, string destination) => File.Move(source, destination, overwrite: true);

    [DllImport("libc", SetLastError = true)] private static extern int open(string pathname, int flags);
    [DllImport("libc", SetLastError = true)] private static extern int fsync(int fd);
    [DllImport("libc", SetLastError = true)] private static extern int close(int fd);

    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlushFileBuffers(SafeFileHandle fileHandle);
}

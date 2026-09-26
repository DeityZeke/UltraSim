using System.Diagnostics;
using System.Globalization;
using Godot;

namespace UltraSim.Client.Godot;

/// <summary>Cold opt-in evidence only. Linux fields are explicitly unavailable on other platforms.</summary>
internal static class GodotDiagnosticProcessMemory
{
    internal static object Capture()
    {
        using Process process = Process.GetCurrentProcess();
        GCMemoryInfo gc = GC.GetGCMemoryInfo();
        return new
        {
            Utc = DateTime.UtcNow, process.WorkingSet64, process.PrivateMemorySize64,
            ManagedBytes = GC.GetTotalMemory(false), AllocatedBytes = GC.GetTotalAllocatedBytes(),
            gc.HeapSizeBytes, gc.FragmentedBytes, gc.TotalAvailableMemoryBytes,
            Gen0 = GC.CollectionCount(0), Gen1 = GC.CollectionCount(1), Gen2 = GC.CollectionCount(2),
            NativeNodes = Performance.GetMonitor(Performance.Monitor.ObjectNodeCount),
            NativeOrphans = Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount),
            NativeResources = Performance.GetMonitor(Performance.Monitor.ObjectResourceCount),
            NativeStaticBytes = Performance.GetMonitor(Performance.Monitor.MemoryStatic),
            LinuxStatusBytes = Read("/proc/self/status", ["VmRSS", "RssAnon", "RssFile", "VmSwap"]),
            LinuxRollupBytes = Read("/proc/self/smaps_rollup",
                ["Rss", "Pss", "Anonymous", "Private_Clean", "Private_Dirty", "Shared_Clean", "Shared_Dirty", "Swap"]),
        };
    }

    private static object Read(string path, string[] fields)
    {
        var values = new Dictionary<string, long?>(fields.Length, StringComparer.Ordinal);
        foreach (string field in fields) values.Add(field, null);
        if (!OperatingSystem.IsLinux()) return new { Available = false, Reason = "Not Linux", values };
        try
        {
            return new { Available = true, Reason = (string?)null, values = ParseFields(File.ReadLines(path), fields) };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { return new { Available = false, Reason = error.GetType().Name, values }; }
    }

    internal static IReadOnlyDictionary<string, long?> ParseFields(IEnumerable<string> lines, string[] fields)
    {
        var values = new Dictionary<string, long?>(fields.Length, StringComparer.Ordinal);
        foreach (string field in fields) values.Add(field, null);
        foreach (string line in lines)
        {
            int colon = line.IndexOf(':');
            if (colon < 0 || !values.ContainsKey(line[..colon])) continue;
            string[] parts = line[(colon + 1)..].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && parts[1] == "kB" &&
                long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out long kib) &&
                kib <= long.MaxValue / 1024)
                values[line[..colon]] = kib * 1024;
        }
        return values;
    }
}

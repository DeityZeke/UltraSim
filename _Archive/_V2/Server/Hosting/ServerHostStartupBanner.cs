using System.Reflection;

namespace UltraSim.Server.Hosting;

public static class ServerHostStartupBanner
{
    public static IReadOnlyList<string> Create(string shellName, Assembly? versionAssembly = null)
    {
        if (string.IsNullOrWhiteSpace(shellName))
            throw new ArgumentException("Shell name cannot be empty.", nameof(shellName));

        Version version = (versionAssembly ?? typeof(ServerHostStartupBanner).Assembly).GetName().Version ?? new Version(0, 0, 0, 0);
        Version runtime = Environment.Version;
        int processors = Environment.ProcessorCount;
        return new[]
        {
            $"UltraSim - {shellName.Trim()} - Version {version.Major}.{version.Minor}.{version.Build}.{version.Revision}",
            $"Core: Running on .NET {runtime.Major}.{runtime.Minor}.{runtime.Build}",
            $"Core: Optimizing for {processors} {(processors == 1 ? "processor" : "processors")}",
        };
    }
}

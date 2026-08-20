using System;

namespace UltraSim.Server
{
    public sealed class ServerRuntimeHooks
    {
        public Action<ServerRuntime>? Initialize { get; init; }
        public Action<ServerRuntime>? Start { get; init; }
        public Action<ServerRuntime>? Load { get; init; }
        public Action<ServerRuntime, float>? Tick { get; init; }
        public Action<ServerRuntime>? Save { get; init; }
        public Func<ServerRuntime, IServerRuntimeSaveSnapshot?>? CaptureSaveSnapshot { get; init; }
        public Action<IServerRuntimeSaveSnapshot>? WriteSaveSnapshot { get; init; }
        public Action<ServerRuntime>? Stop { get; init; }
    }
}

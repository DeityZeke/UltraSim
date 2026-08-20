using System;

namespace UltraSim.Client.Runtime
{
    public interface IClientClock
    {
        long GetMonotonicTicksUsec();

        float GetFrameDeltaSeconds();
    }

    public interface IClientLogger
    {
        void Info(string message);

        void Warn(string message);

        void Error(string message);
    }

    public interface IContentResolver
    {
        bool TryResolve(ClientAssetKind kind, string logicalId, out ClientResolvedAssetRef asset);
    }

    public interface IClientInputSource<TInputCommand>
    {
        bool TryDequeueInput(out TInputCommand command);
    }

    public readonly record struct ClientResolvedAssetRef(string Value)
    {
        public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

        public override string ToString() => Value ?? string.Empty;
    }

    public enum ClientAssetKind : byte
    {
        Unknown = 0,
        Model = 1,
        Animation = 2,
        Vfx = 3,
        Sfx = 4,
        UiPanel = 5,
        UiTheme = 6,
        UiIcon = 7,
    }
}

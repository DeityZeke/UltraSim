using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;

namespace UltraSim.ECS
{
    public readonly struct EcsLogEntry
    {
        public readonly DateTime TimestampUtc;
        public readonly LogLevel Level;
        public readonly string Message;
        public readonly string? Source;

        public EcsLogEntry(LogLevel level, string message, string? source = null)
        {
            TimestampUtc = DateTime.UtcNow;
            Level = level;
            Message = message;
            Source = source;
        }
    }

    public static class EcsLog
    {
        private static readonly ConcurrentQueue<EcsLogEntry> Queue = new();
        private static LogLevel _minLevel = LogLevel.Info;

        public static LogLevel MinLevel
        {
            get => _minLevel;
            set => _minLevel = value;
        }

        public static bool IsEmpty => Queue.IsEmpty;

        public static void Log(LogLevel level, string message, string? source = null)
        {
            if (level < _minLevel || string.IsNullOrWhiteSpace(message))
                return;

            Queue.Enqueue(new EcsLogEntry(level, message, source));
        }

        public static void Info(string message, string? source = null) => Log(LogLevel.Info, message, source);
        public static void Warning(string message, string? source = null) => Log(LogLevel.Warning, message, source);
        public static void Error(string message, string? source = null) => Log(LogLevel.Error, message, source);
        public static void Trace(string message, string? source = null) => Log(LogLevel.Trace, message, source);

        public static bool TryDequeue(out EcsLogEntry entry) => Queue.TryDequeue(out entry);

        public static int DrainTo(IHost host, int maxEntries = 8192)
        {
            if (host == null || maxEntries <= 0)
                return 0;

            int drained = 0;
            while (drained < maxEntries && Queue.TryDequeue(out var entry))
            {
                string message = entry.Source == null
                    ? entry.Message
                    : $"[{entry.Source}] {entry.Message}";

                host.Log(entry.Level, message);
                drained++;
            }

            return drained;
        }

        public static void Clear()
        {
            while (Queue.TryDequeue(out _))
            {
            }
        }

        public static IDisposable CaptureConsole(LogLevel stdoutLevel = LogLevel.Info, LogLevel stderrLevel = LogLevel.Error, string? source = "Console")
            => new ConsoleCapture(stdoutLevel, stderrLevel, source);

        private sealed class ConsoleCapture : IDisposable
        {
            private readonly TextWriter _oldOut;
            private readonly TextWriter _oldError;
            private bool _disposed;

            public ConsoleCapture(LogLevel stdoutLevel, LogLevel stderrLevel, string? source)
            {
                _oldOut = Console.Out;
                _oldError = Console.Error;

                Console.SetOut(new QueueTextWriter(stdoutLevel, source));
                Console.SetError(new QueueTextWriter(stderrLevel, source));
            }

            public void Dispose()
            {
                if (_disposed)
                    return;

                Console.SetOut(_oldOut);
                Console.SetError(_oldError);
                _disposed = true;
            }
        }

        private sealed class QueueTextWriter : TextWriter
        {
            private readonly object _sync = new();
            private readonly StringBuilder _buffer = new(256);
            private readonly LogLevel _level;
            private readonly string? _source;

            public QueueTextWriter(LogLevel level, string? source)
            {
                _level = level;
                _source = source;
            }

            public override Encoding Encoding => Encoding.UTF8;

            public override void Write(char value)
            {
                lock (_sync)
                {
                    if (value == '\n')
                    {
                        FlushBufferNoLock();
                        return;
                    }

                    if (value != '\r')
                        _buffer.Append(value);
                }
            }

            public override void Write(string? value)
            {
                if (string.IsNullOrEmpty(value))
                    return;

                lock (_sync)
                {
                    for (int i = 0; i < value.Length; i++)
                    {
                        char c = value[i];
                        if (c == '\n')
                        {
                            FlushBufferNoLock();
                        }
                        else if (c != '\r')
                        {
                            _buffer.Append(c);
                        }
                    }
                }
            }

            public override void WriteLine(string? value)
            {
                if (!string.IsNullOrEmpty(value))
                    Write(value);

                lock (_sync)
                    FlushBufferNoLock();
            }

            private void FlushBufferNoLock()
            {
                if (_buffer.Length == 0)
                    return;

                EcsLog.Log(_level, _buffer.ToString(), _source);
                _buffer.Clear();
            }
        }
    }
}

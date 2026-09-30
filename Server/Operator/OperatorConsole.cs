using System.Text;
using UltraSim.Framework.ControlPlane;

namespace UltraSim.Server.Operator;

/// <summary>Cold text adapter. It discovers and invokes owner-published typed Control Plane actions.</summary>
public sealed class OperatorConsole
{
    private readonly ControlPlaneRegistry _registry;
    private readonly ControlPrincipal _principal;
    private ulong _sequence;

    public OperatorConsole(ControlPlaneRegistry registry, ControlPrincipal principal)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _principal = principal ?? throw new ArgumentNullException(nameof(principal));
    }

    public string Execute(string line)
    {
        if (line is null || line.Length is < 1 or > 256 || line.Any(char.IsControl))
            return "Rejected: command must be 1–256 printable characters.";
        string[] tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length is < 1 or > 4) return "Rejected: invalid command shape.";
        if (tokens[0] is "help" or "?") return Help();
        if (tokens[0] == "status" && tokens.Length == 1) return Status();
        if (tokens[0] == "participants" && tokens.Length == 1) return Collection("participants");
        if (tokens[0] == "audit" && tokens.Length == 1) return Collection("audit");
        if (tokens[0] == "logs" && tokens.Length == 1) return Collection("log");

        ControlActionId action;
        ControlValue[] arguments = [];
        bool confirmed = false;
        switch (tokens[0])
        {
            case "save" when tokens.Length == 1: action = ServerOperatorProvider.SaveNow; break;
            case "backup" when tokens.Length == 1: action = ServerOperatorProvider.BackupNow; break;
            case "shutdown" when tokens.Length == 2 && tokens[1] == "confirm":
                action = ServerOperatorProvider.Shutdown; confirmed = true; break;
            case "restart" when tokens.Length == 2 && tokens[1] == "confirm":
                action = ServerOperatorProvider.RestartWithSave; confirmed = true; break;
            case "kick" when tokens.Length == 3 && tokens[2] == "confirm" &&
                ulong.TryParse(tokens[1], out ulong participant) && participant != 0:
                action = ServerOperatorProvider.Kick;
                arguments = [ControlValue.UInt64(participant)]; confirmed = true; break;
            default: return "Rejected: unknown command or missing confirmation. Type help.";
        }
        ControlProviderSnapshot? provider = Find();
        if (provider is null) return "Unavailable: server operator provider is not visible.";
        ControlActionDescriptor? descriptor = provider.Definition.Actions.FirstOrDefault(d => d.Id == action);
        if (descriptor is null) return "Unauthorized or unavailable action.";
        if (descriptor.ConfirmationPrompt.Length != 0 && !confirmed)
            return "Rejected: action requires explicit confirmation.";
        if (_sequence == ulong.MaxValue) return "Rejected: console request identity exhausted.";
        var context = new ControlRequestContext(new("console." + (++_sequence)), _principal,
            DateTimeOffset.UtcNow);
        ControlResult result = _registry.Invoke(ControlActionRequest.Create(context,
            ServerOperatorProvider.ProviderId, action, provider.Generation, arguments,
            provider.Revision));
        return $"{result.Outcome.Status}: {result.Outcome.Code}" +
            (result.Outcome.Diagnostic.Length == 0 ? "" : " — " + result.Outcome.Diagnostic);
    }

    private string Help()
    {
        ControlProviderSnapshot? provider = Find();
        if (provider is null) return "No operator actions are visible.";
        return "status | participants | audit | logs | help\n" + string.Join('\n',
            provider.Definition.Actions.Select(d => d.Id.Value switch
            {
                "save-now" => "save",
                "backup-now" => "backup",
                "shutdown" => "shutdown confirm",
                "restart-with-save" => "restart confirm",
                "kick" => "kick <participant-id> confirm",
                _ => d.Id.Value
            }));
    }

    private string Status()
    {
        ControlProviderSnapshot? provider = Find();
        if (provider is null) return "No operator status is visible.";
        var builder = new StringBuilder(256);
        foreach (DiagnosticDescriptor diagnostic in provider.Definition.Diagnostics)
        {
            ControlResultCode code = _registry.Read(_principal, provider.Definition.Id,
                provider.Generation, diagnostic.Id, out ControlDiagnosticSnapshot? snapshot);
            if (code != ControlResultCode.Accepted || snapshot is null) continue;
            if (builder.Length != 0) builder.Append(' ');
            builder.Append(diagnostic.Id.Value).Append('=');
            ControlValue value = snapshot.Value;
            builder.Append(value.Kind switch
            {
                ControlValueKind.UInt64 => value.AsUInt64().ToString(),
                ControlValueKind.Bool => value.AsBool() ? "true" : "false",
                ControlValueKind.Text => value.AsText(),
                _ => "n/a"
            });
        }
        return builder.ToString();
    }

    private string Collection(string name)
    {
        ControlProviderSnapshot? provider = Find();
        if (provider is null) return "No operator collection is visible.";
        var id = new ControlCollectionId(name);
        ControlResultCode code = _registry.Read(_principal, provider.Definition.Id,
            provider.Generation, id, out ControlCollectionSnapshot? snapshot);
        if (code != ControlResultCode.Accepted || snapshot is null)
            return "Unavailable: " + code;
        var builder = new StringBuilder();
        foreach (ControlCollectionRow row in snapshot.Rows)
        {
            if (builder.Length != 0) builder.Append('\n');
            builder.Append(row.Id.Value).Append(' ');
            for (int i = 0; i < row.Values.Count; i++)
            {
                if (i != 0) builder.Append(' ');
                ControlValue value = row.Values[i];
                builder.Append(value.Kind switch
                {
                    ControlValueKind.UInt64 => value.AsUInt64().ToString(),
                    ControlValueKind.Text => value.AsText(),
                    _ => "n/a"
                });
            }
        }
        return builder.Length == 0 ? "(empty)" : builder.ToString();
    }

    private ControlProviderSnapshot? Find() => _registry.Discover(_principal)
        .FirstOrDefault(d => d.Definition.Id == ServerOperatorProvider.ProviderId);
}

/// <summary>
/// Bounded input reader. Only this background reader touches stdin; the owner loop drains text
/// and invokes Control Plane on its own thread. The background thread never touches authority.
/// </summary>
public sealed class BoundedOperatorConsoleInput
{
    private const int MaximumLine = 256;
    private const int MaximumQueued = 32;
    private readonly Queue<string> _lines = new();
    private readonly object _gate = new();
    private readonly TextReader _reader;
    private readonly Thread _worker;
    private bool _eof;
    private ulong _dropped;

    public BoundedOperatorConsoleInput(TextReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _worker = new Thread(ReadLoop) { IsBackground = true, Name = "UltraSim operator stdin" };
        _worker.Start();
    }

    public bool TryDequeue(out string? line)
    {
        lock (_gate)
        {
            if (_lines.Count != 0) { line = _lines.Dequeue(); return true; }
            line = null; return false;
        }
    }

    public (bool Eof, ulong Dropped, int Pending) Snapshot()
    {
        lock (_gate) return (_eof, _dropped, _lines.Count);
    }

    private void ReadLoop()
    {
        var line = new StringBuilder(MaximumLine);
        bool overflow = false;
        try
        {
            while (true)
            {
                int c = _reader.Read();
                if (c < 0)
                {
                    if (line.Length != 0 || overflow) Publish(overflow ? "" : line.ToString());
                    lock (_gate) _eof = true;
                    return;
                }
                if (c == '\r') continue;
                if (c == '\n')
                {
                    Publish(overflow ? "" : line.ToString());
                    line.Clear(); overflow = false;
                }
                else if (!overflow && line.Length < MaximumLine) line.Append((char)c);
                else overflow = true;
            }
        }
        catch (IOException)
        {
            lock (_gate) _eof = true;
        }
    }

    private void Publish(string line)
    {
        lock (_gate)
        {
            if (_lines.Count == MaximumQueued)
            {
                if (_dropped != ulong.MaxValue) _dropped++;
                return;
            }
            _lines.Enqueue(line);
        }
    }
}

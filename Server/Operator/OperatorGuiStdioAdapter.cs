using System.Text;
using System.Text.Json;
using UltraSim.Framework.ControlPlane;

namespace UltraSim.Server.Operator;

/// <summary>
/// Local child-process frontend framing. The inherited stdin/stdout pipes carry bounded requests;
/// this adapter runs only on the server owner thread and invokes the same Control Plane registry as
/// the console. It is not a network listener or a second server-action implementation.
/// </summary>
public sealed class OperatorGuiStdioAdapter
{
    public const string RequestPrefix = "@P7GUI ";
    public const string ResponsePrefix = "P7GUI ";
    private const int MaximumRequestLength = 1024;
    private const int MaximumResponseText = 32768;
    private readonly ControlPlaneRegistry _registry;
    private readonly ControlPrincipal _principal;
    private readonly OperatorConsole _console;
    private ulong _lastRequestId;

    public OperatorGuiStdioAdapter(ControlPlaneRegistry registry, ControlPrincipal principal)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _principal = principal ?? throw new ArgumentNullException(nameof(principal));
        _console = new OperatorConsole(registry, principal);
    }

    public bool TryExecute(string line, out string response)
    {
        response = string.Empty;
        if (!line.StartsWith(RequestPrefix, StringComparison.Ordinal)) return false;
        OperatorGuiRequest? request = null;
        try
        {
            if (line.Length > MaximumRequestLength)
                throw new ArgumentException("GUI request exceeds the bounded input limit.");
            request = JsonSerializer.Deserialize<OperatorGuiRequest>(line.AsSpan(RequestPrefix.Length));
            if (request is null || request.RequestId == 0 || request.RequestId <= _lastRequestId)
                throw new ArgumentException("GUI request identity is invalid or stale.");
            _lastRequestId = request.RequestId;
            string text = request.Kind switch
            {
                "discover" => Discover(),
                "status" => ReadStatus(),
                "participants" => ReadCollection("participants", 64),
                "log" => ReadCollection("log", 64),
                "audit" => ReadCollection("audit", 64),
                "command" => _console.Execute(request.Command ?? string.Empty),
                "action" => Invoke(request),
                _ => throw new ArgumentException("Unknown GUI request kind.")
            };
            string status = request.Kind == "action" ?
                text.StartsWith("Accepted:", StringComparison.Ordinal) ? "accepted" :
                text.StartsWith("Failed:", StringComparison.Ordinal) ? "failed" : "rejected" :
                text.StartsWith("Rejected:", StringComparison.Ordinal) ? "rejected" : "ok";
            response = Format(new(request.RequestId, request.Kind, status, Bounded(text)));
        }
        catch (Exception error) when (error is ArgumentException or JsonException or
            OverflowException)
        {
            response = Format(new(request?.RequestId ?? 0, request?.Kind ?? "invalid", "rejected",
                Bounded(error.Message)));
        }
        return true;
    }

    private string Discover()
    {
        ControlProviderSnapshot? provider = Find();
        if (provider is null) return "Unavailable: server operator provider is not visible.";
        var builder = new StringBuilder(512);
        foreach (ControlActionDescriptor action in provider.Definition.Actions)
            builder.Append(action.Category.Value).Append(" / ").Append(action.Label).Append(" [")
                .Append(action.Id.Value).Append(']')
                .Append(action.ConfirmationPrompt.Length == 0 ? "" : " (confirmation required)")
                .AppendLine();
        foreach (ControlDescriptor control in provider.Definition.Controls)
            builder.Append(control.Category.Value).Append(" / ").Append(control.Label)
                .Append(" [").Append(control.Id.Value).Append("] ")
                .Append(control.ApplyMode).AppendLine();
        foreach (ControlCollectionDescriptor collection in provider.Definition.Collections)
            builder.Append(collection.Category.Value).Append(" / ").Append(collection.Label)
                .Append(" [").Append(collection.Id.Value).AppendLine("]");
        return builder.ToString();
    }

    private string ReadStatus()
    {
        ControlProviderSnapshot? provider = Find();
        if (provider is null) return "Unavailable: server operator provider is not visible.";
        var builder = new StringBuilder(512);
        foreach (DiagnosticDescriptor diagnostic in provider.Definition.Diagnostics)
        {
            if (_registry.Read(_principal, provider.Definition.Id, provider.Generation,
                    diagnostic.Id, out ControlDiagnosticSnapshot? snapshot) !=
                ControlResultCode.Accepted || snapshot is null) continue;
            builder.Append(diagnostic.Id.Value).Append('=').Append(FormatValue(snapshot.Value)).AppendLine();
        }
        foreach (ControlDescriptor control in provider.Definition.Controls)
        {
            if (_registry.Read(_principal, provider.Definition.Id, provider.Generation,
                    control.Id, out ControlState? snapshot) !=
                ControlResultCode.Accepted || snapshot is null) continue;
            builder.Append(control.Id.Value).Append('=').Append(FormatValue(snapshot.Effective));
            if (snapshot.DisabledCode == ControlResultCode.Disabled)
                builder.Append(" (disabled: ").Append(snapshot.DisabledReason).Append(')');
            else if (control.Persistence == ControlPersistenceClass.ServerColdConfiguration)
                builder.Append(" (cold configuration)");
            builder.AppendLine();
        }
        return builder.ToString();
    }

    private string ReadCollection(string name, int maximumRows)
    {
        ControlProviderSnapshot? provider = Find();
        if (provider is null) return "Unavailable: server operator provider is not visible.";
        ControlResultCode code = _registry.Read(_principal, provider.Definition.Id,
            provider.Generation, new ControlCollectionId(name), out ControlCollectionSnapshot? snapshot);
        if (code != ControlResultCode.Accepted || snapshot is null)
            return "Unavailable: " + code;
        var builder = new StringBuilder();
        foreach (ControlCollectionRow row in snapshot.Rows.TakeLast(maximumRows))
        {
            builder.Append(row.Id.Value);
            foreach (ControlValue value in row.Values)
                builder.Append(" | ").Append(FormatValue(value));
            builder.AppendLine();
            if (builder.Length >= MaximumResponseText) break;
        }
        return builder.Length == 0 ? "(empty)" : builder.ToString();
    }

    private string Invoke(OperatorGuiRequest request)
    {
        if (request.Action is null || request.Action.Length is < 1 or > 64)
            throw new ArgumentException("GUI action identity is invalid.");
        ControlProviderSnapshot? provider = Find();
        if (provider is null) return "Unavailable: server operator provider is not visible.";
        ControlActionDescriptor? action = provider.Definition.Actions
            .FirstOrDefault(candidate => candidate.Id.Value == request.Action);
        if (action is null) return "Rejected: action is not published to this local operator.";
        if (action.ConfirmationPrompt.Length != 0 && !request.Confirmed)
            return "Rejected: action requires explicit confirmation.";
        ControlValue[] arguments = action.Id == ServerOperatorProvider.Kick
            ? request.Target is > 0 ? [ControlValue.UInt64(request.Target)] :
                throw new ArgumentException("Kick requires one positive participant ID.")
            : [];
        var context = new ControlRequestContext(new("gui." + request.RequestId), _principal,
            DateTimeOffset.UtcNow);
        ControlResult result = _registry.Invoke(ControlActionRequest.Create(context,
            ServerOperatorProvider.ProviderId, action.Id, provider.Generation, arguments,
            provider.Revision));
        return result.Outcome.Status + ": " + result.Outcome.Code +
            (result.Outcome.Diagnostic.Length == 0 ? "" : " — " + result.Outcome.Diagnostic);
    }

    private ControlProviderSnapshot? Find() => _registry.Discover(_principal)
        .FirstOrDefault(provider => provider.Definition.Id == ServerOperatorProvider.ProviderId);

    private static string FormatValue(ControlValue value) => value.Kind switch
    {
        ControlValueKind.Bool => value.AsBool() ? "true" : "false",
        ControlValueKind.Int64 => value.AsInt64().ToString(),
        ControlValueKind.UInt64 => value.AsUInt64().ToString(),
        ControlValueKind.Float64 => value.AsFloat64().ToString(System.Globalization.CultureInfo.InvariantCulture),
        ControlValueKind.Option => value.AsOption().Value ?? "",
        ControlValueKind.Text => value.AsText(),
        _ => "n/a"
    };

    private static string Bounded(string text) => text.Length <= MaximumResponseText
        ? text : text[..MaximumResponseText] + "\n[truncated]";
    private static string Format(OperatorGuiResponse result) => ResponsePrefix +
        JsonSerializer.Serialize(result);
}

public sealed record OperatorGuiRequest(ulong RequestId, string Kind, string? Action = null,
    ulong Target = 0, bool Confirmed = false, string? Command = null);

public sealed record OperatorGuiResponse(ulong RequestId, string Kind, string Status, string Text);

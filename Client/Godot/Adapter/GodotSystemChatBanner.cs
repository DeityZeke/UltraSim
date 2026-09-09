using Godot;
using UltraSim.Client.Core;

namespace UltraSim.Client.Godot;

public readonly record struct GodotSystemChatBannerSnapshot(
    bool Visible,
    string? Text,
    ulong ExpiresAtMilliseconds,
    ulong Presented);

/// <summary>
/// Presentation-local transient projection of an authority-originated system Chat occurrence.
/// Chat state retains the fact; this adapter alone owns native layout and display lifetime.
/// </summary>
public sealed class GodotSystemChatBanner : IDisposable
{
    public const ulong DisplayDurationMilliseconds = 4_500;
    private readonly PanelContainer _panel;
    private readonly Label _label;
    private readonly int _ownerThreadId = System.Environment.CurrentManagedThreadId;
    private ulong _expiresAtMilliseconds;
    private ulong _presented;
    private ulong _observedRecipientSequence;
    private bool _disposed;

    public GodotSystemChatBanner(Control host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _panel = new PanelContainer
        {
            Name = "SystemChatBanner",
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            OffsetLeft = -320,
            OffsetTop = 20,
            OffsetRight = 320,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        _label = new Label
        {
            Name = "SystemChatBannerText",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _panel.AddChild(_label);
        host.AddChild(_panel);
    }

    public bool TryPresent(in ClientChatOccurrence occurrence, ulong nowMilliseconds)
    {
        EnsureOperational();
        if (!occurrence.IsSystemBroadcast)
            return false;
        _label.Text = occurrence.Text;
        _panel.Visible = true;
        _expiresAtMilliseconds = nowMilliseconds <= ulong.MaxValue - DisplayDurationMilliseconds
            ? nowMilliseconds + DisplayDurationMilliseconds
            : ulong.MaxValue;
        if (_presented != ulong.MaxValue) _presented++;
        return true;
    }

    /// <summary>
    /// Projects the newest previously unseen system occurrence from one bounded Chat-state copy.
    /// Recipient sequence is monotonic across Game-generation replacement, so retained old rows
    /// cannot replay when the same mixed-generation copy is observed again. A later player
    /// occurrence in the batch cannot hide the banner canary.
    /// </summary>
    public void Observe(ReadOnlySpan<ClientChatOccurrence> occurrences, ulong nowMilliseconds)
    {
        EnsureOperational();
        foreach (ref readonly ClientChatOccurrence occurrence in occurrences)
        {
            if (!occurrence.IsSystemBroadcast) continue;
            if (occurrence.RecipientSequence <= _observedRecipientSequence) continue;
            _ = TryPresent(in occurrence, nowMilliseconds);
            _observedRecipientSequence = occurrence.RecipientSequence;
        }
    }

    public void Update(ulong nowMilliseconds)
    {
        EnsureOperational();
        if (_panel.Visible && nowMilliseconds >= _expiresAtMilliseconds)
            _panel.Visible = false;
    }

    public GodotSystemChatBannerSnapshot GetSnapshot()
    {
        EnsureOperational();
        return new GodotSystemChatBannerSnapshot(
            _panel.Visible,
            _label.Text,
            _expiresAtMilliseconds,
            _presented);
    }

    public void Dispose()
    {
        EnsureOwner();
        if (_disposed) return;
        _disposed = true;
        if (GodotObject.IsInstanceValid(_panel)) _panel.QueueFree();
    }

    private void EnsureOperational()
    {
        EnsureOwner();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void EnsureOwner()
    {
        if (System.Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The system Chat banner is engine-thread-bound.");
    }
}

using System.Text;
using UltraSim.Game.World;

namespace UltraSim.Client.Core;

public enum ClientChatOccurrenceKind : byte
{
    SpatialSpeech = 1,
    PrivateSystem = 2,
    PrivateError = 3,
}

public enum ClientChatSpeechMode : byte
{
    None = 0,
    Whisper = 1,
    Say = 2,
    Yell = 3,
}

public enum ClientChatApplyStatus : byte
{
    Applied = 1,
    Duplicate = 2,
    Rejected = 3,
}

public readonly record struct ClientChatLimits(
    int MaximumRecentOccurrences,
    int MaximumTextUtf8Bytes,
    int MaximumDisplayLabelUtf8Bytes)
{
    public static ClientChatLimits Default => new(64, 256, 128);

    public void Validate()
    {
        if (MaximumRecentOccurrences <= 0 || MaximumTextUtf8Bytes <= 0 ||
            MaximumDisplayLabelUtf8Bytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(ClientChatLimits));
    }
}

/// <summary>One engine-neutral, already authenticated Chat presentation fact.</summary>
public readonly record struct ClientChatOccurrence(
    ulong ServerGenerationId,
    ulong DeliveryId,
    ulong RecipientSequence,
    ulong SpeechFactId,
    ulong SimulationTick,
    ClientChatOccurrenceKind Kind,
    ClientChatSpeechMode SpeechMode,
    WorldPublicEntityId PublicAnchor,
    string DisplayLabel,
    string Text)
{
    public bool IsSpatial => Kind == ClientChatOccurrenceKind.SpatialSpeech;
    public bool CanPresentOverhead(ulong residentServerGenerationId) =>
        IsSpatial && PublicAnchor.IsValid && residentServerGenerationId != 0 &&
        ServerGenerationId == residentServerGenerationId;
}

public readonly record struct ClientChatStateSnapshot(
    ulong ApplicationSessionEpoch,
    ulong ServerGenerationId,
    ulong NextRecipientSequence,
    int RetainedOccurrences,
    ulong Applied,
    ulong Duplicates,
    ulong Rejected,
    ulong PresentationEvictions,
    long KnownArrayReservedBytes);

/// <summary>
/// Bounded append-only Chat application state. The retained ring is local
/// presentation history; evicting an applied row never changes server proof.
/// </summary>
public sealed class ClientChatState
{
    private static readonly UTF8Encoding s_strictUtf8 = new(false, true);
    private readonly ulong _applicationSessionEpoch;
    private readonly ClientChatLimits _limits;
    private readonly ClientChatOccurrence[] _rows;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private ulong _latestOriginGenerationId;
    private ulong _nextRecipientSequence = 1;
    private int _head;
    private int _count;
    private ulong _applied;
    private ulong _duplicates;
    private ulong _rejected;
    private ulong _evictions;

    public ClientChatState(ulong applicationSessionEpoch, ClientChatLimits limits = default)
    {
        if (applicationSessionEpoch == 0)
            throw new ArgumentOutOfRangeException(nameof(applicationSessionEpoch));
        if (limits == default) limits = ClientChatLimits.Default;
        limits.Validate();
        _applicationSessionEpoch = applicationSessionEpoch;
        _limits = limits;
        _rows = new ClientChatOccurrence[limits.MaximumRecentOccurrences];
    }

    public ClientChatApplyStatus TryApply(in ClientChatOccurrence occurrence)
    {
        EnsureOwnerThread();
        if (!IsCanonical(in occurrence))
        {
            SaturatingIncrement(ref _rejected);
            return ClientChatApplyStatus.Rejected;
        }
        if (occurrence.RecipientSequence < _nextRecipientSequence)
        {
            SaturatingIncrement(ref _duplicates);
            return ClientChatApplyStatus.Duplicate;
        }
        if (occurrence.RecipientSequence != _nextRecipientSequence)
        {
            SaturatingIncrement(ref _rejected);
            return ClientChatApplyStatus.Rejected;
        }

        if (_count == _rows.Length)
        {
            _rows[_head] = default;
            _head = (_head + 1) % _rows.Length;
            _count--;
            SaturatingIncrement(ref _evictions);
        }
        int slot = (_head + _count) % _rows.Length;
        _rows[slot] = occurrence;
        _count++;
        _latestOriginGenerationId = Math.Max(
            _latestOriginGenerationId, occurrence.ServerGenerationId);
        _nextRecipientSequence = occurrence.RecipientSequence == ulong.MaxValue
            ? 0
            : occurrence.RecipientSequence + 1;
        SaturatingIncrement(ref _applied);
        return ClientChatApplyStatus.Applied;
    }

    public int CopyRecent(Span<ClientChatOccurrence> destination)
    {
        EnsureOwnerThread();
        int count = Math.Min(destination.Length, _count);
        int skip = _count - count;
        int start = (_head + skip) % _rows.Length;
        int first = Math.Min(count, _rows.Length - start);
        _rows.AsSpan(start, first).CopyTo(destination);
        if (first != count) _rows.AsSpan(0, count - first).CopyTo(destination[first..]);
        return count;
    }

    public ClientChatStateSnapshot GetSnapshot()
    {
        EnsureOwnerThread();
        return new ClientChatStateSnapshot(
            _applicationSessionEpoch, _latestOriginGenerationId, _nextRecipientSequence, _count,
            _applied, _duplicates, _rejected, _evictions,
            (long)_rows.Length * System.Runtime.CompilerServices.Unsafe.SizeOf<ClientChatOccurrence>());
    }

    private bool IsCanonical(in ClientChatOccurrence value)
    {
        bool spatial = value.Kind == ClientChatOccurrenceKind.SpatialSpeech;
        return value.ServerGenerationId != 0 && value.DeliveryId != 0 &&
            value.RecipientSequence != 0 &&
            value.Kind is >= ClientChatOccurrenceKind.SpatialSpeech and <= ClientChatOccurrenceKind.PrivateError &&
            (spatial
                ? value.SpeechFactId != 0 && value.SpeechMode is >= ClientChatSpeechMode.Whisper and <= ClientChatSpeechMode.Yell
                : value.SpeechFactId == 0 && value.SpeechMode == ClientChatSpeechMode.None &&
                    !value.PublicAnchor.IsValid) &&
            IsBoundedText(value.DisplayLabel, _limits.MaximumDisplayLabelUtf8Bytes) &&
            IsBoundedText(value.Text, _limits.MaximumTextUtf8Bytes);
    }

    private static bool IsBoundedText(string? value, int maximumBytes)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try { return s_strictUtf8.GetByteCount(value) <= maximumBytes; }
        catch (EncoderFallbackException) { return false; }
    }

    private static void SaturatingIncrement(ref ulong value)
    {
        if (value != ulong.MaxValue) value++;
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Client Chat state is owner-thread-bound.");
    }
}

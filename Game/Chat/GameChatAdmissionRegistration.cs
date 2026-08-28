using UltraSim.Game.Admission;

namespace UltraSim.Game.Chat;

/// <summary>Cold lifetime lease for one concrete Admission source in Chat's bounded census.</summary>
public sealed class GameChatAdmissionRegistration : IDisposable
{
    private GameChatService? _chat;
    private AdmissionService? _admission;

    internal GameChatAdmissionRegistration(GameChatService chat, AdmissionService admission) =>
        (_chat, _admission) = (chat, admission);

    public void Dispose()
    {
        GameChatService? chat = _chat;
        AdmissionService? admission = _admission;
        _chat = null;
        _admission = null;
        if (chat is not null && admission is not null)
            chat.ReleaseAdmission(admission);
    }
}

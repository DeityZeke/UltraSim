#if ULTRASIM_ONLINE || ULTRASIM_COMPLETE
using Godot;
using UltraSim.Client.Core;

namespace UltraSim.Client.Godot;

public sealed partial class RemoteConceptDemoClient
{
    /// <summary>Cold R1 control: refresh an expiring retirement surface through real remote Chat.</summary>
    public async Task RefreshF112RetirementSpeech()
    {
        EnsureOperational();
        if (_session?.Chat.TrySubmit("R1 rendered retirement canary", ClientChatSpeechMode.Say) != true)
            throw new InvalidOperationException("Rendered retirement speech was refused.");
        ulong deadline = Time.GetTicksMsec() + 5_000;
        while (_actorPresenter!.GetSnapshot().VisibleOverheadLabels == 0)
        {
            if (Time.GetTicksMsec() >= deadline)
                throw new TimeoutException("Rendered retirement speech did not present within five seconds.");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
}
#endif

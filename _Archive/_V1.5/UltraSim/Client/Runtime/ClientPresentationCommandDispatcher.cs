using System;
using UltraSim.UI.Contracts;

namespace UltraSim.Client.Runtime
{
    public sealed class ClientPresentationCommandDispatcher
    {
        private readonly IPresentationSink _presentationSink;
        private readonly IUiCommandSink? _uiCommandSink;

        public ClientPresentationCommandDispatcher(IPresentationSink presentationSink, IUiCommandSink? uiCommandSink = null)
        {
            _presentationSink = presentationSink ?? throw new ArgumentNullException(nameof(presentationSink));
            _uiCommandSink = uiCommandSink;
        }

        public void Drain(ClientPresentationCommandBuffer buffer)
        {
            ArgumentNullException.ThrowIfNull(buffer);

            var presentation = buffer.PresentationCommands;
            for (int i = 0; i < presentation.Count; i++)
            {
                PresentationCommand cmd = presentation[i];
                switch (cmd.Kind)
                {
                    case PresentationCommandKind.SetActorDefinition:
                        if (cmd.DefinitionId.HasValue)
                            _presentationSink.SetActorDefinition(cmd.EntityId, cmd.DefinitionId.Value);
                        break;
                    case PresentationCommandKind.SetActorPose:
                        ClientWorldPose pose = cmd.Pose;
                        _presentationSink.SetActorPose(cmd.EntityId, in pose);
                        break;
                    case PresentationCommandKind.PlayAnimation:
                        if (cmd.Animation.HasValue)
                            _presentationSink.PlayAnimation(cmd.EntityId, cmd.Animation.Value);
                        break;
                    case PresentationCommandKind.SpawnVfx:
                        if (cmd.Vfx.HasValue)
                            _presentationSink.SpawnVfx(cmd.Vfx.Value);
                        break;
                    case PresentationCommandKind.PlaySfx:
                        if (cmd.Sfx.HasValue)
                            _presentationSink.PlaySfx(cmd.Sfx.Value);
                        break;
                    case PresentationCommandKind.ShowChatBubble:
                        _presentationSink.ShowChatBubble(cmd.EntityId, cmd.ChatText ?? string.Empty, cmd.ChatTtlSeconds);
                        break;
                    case PresentationCommandKind.UpdateNameplate:
                        if (cmd.Nameplate.HasValue)
                            _presentationSink.UpdateNameplate(cmd.EntityId, cmd.Nameplate.Value);
                        break;
                    case PresentationCommandKind.DespawnActor:
                        _presentationSink.DespawnActor(cmd.EntityId);
                        break;
                }
            }

            if (_uiCommandSink != null)
            {
                var ui = buffer.UiCommands;
                for (int i = 0; i < ui.Count; i++)
                {
                    UiCommand cmd = ui[i];
                    switch (cmd.Kind)
                    {
                        case UiCommandKind.Open when cmd.Open.HasValue:
                            _uiCommandSink.OpenPanel(cmd.Open.Value);
                            break;
                        case UiCommandKind.Update when cmd.Update.HasValue:
                            _uiCommandSink.UpdatePanel(cmd.Update.Value);
                            break;
                        case UiCommandKind.Close when cmd.Close.HasValue:
                            _uiCommandSink.ClosePanel(cmd.Close.Value);
                            break;
                    }
                }
            }

            buffer.Clear();
        }
    }
}

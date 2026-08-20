using System;
using UltraSim.UI.Contracts;

namespace Client.Godot.UI.ServerDriven
{
    public interface IGodotUiPanel
    {
        void Initialize(UiPanelOpenCommand command, Action<UiPanelResponse>? responseHandler);

        void ApplyUpdate(UiPanelUpdateCommand command);

        void PrepareClose(UiPanelCloseCommand command);
    }
}

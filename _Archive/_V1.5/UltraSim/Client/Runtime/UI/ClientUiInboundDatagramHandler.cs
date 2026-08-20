using System;
using UltraSim.UI.Contracts;

namespace UltraSim.Client.Runtime.UI
{
    public sealed class ClientUiInboundDatagramHandler
    {
        private readonly ClientUiPanelRuntime _panelRuntime;

        public ClientUiInboundDatagramHandler(ClientUiPanelRuntime panelRuntime)
        {
            _panelRuntime = panelRuntime ?? throw new ArgumentNullException(nameof(panelRuntime));
        }

        public bool TryHandle(ReadOnlySpan<byte> payload)
        {
            if (UiPanelNetworkCodec.TryDecodePanelOpen(payload, out UiPanelOpenCommand open))
            {
                _panelRuntime.OpenPanel(open);
                return true;
            }

            if (UiPanelNetworkCodec.TryDecodePanelUpdate(payload, out UiPanelUpdateCommand update))
            {
                _panelRuntime.UpdatePanel(update);
                return true;
            }

            if (UiPanelNetworkCodec.TryDecodePanelClose(payload, out UiPanelCloseCommand close))
            {
                _panelRuntime.ClosePanel(close);
                return true;
            }

            if (UiPanelNetworkCodec.TryDecodePanelDefinition(payload, out UiPanelDefinition definition))
            {
                _panelRuntime.AcceptDefinition(definition);
                return true;
            }

            return false;
        }
    }
}

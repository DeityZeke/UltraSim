using System;
using UltraSim.UI.Contracts;

namespace UltraSim.UI.Authoring
{
    /// <summary>
    /// Passed to <see cref="UiPanel.OnResponse"/> when the client fires an action.
    /// Provides the operations a handler needs: close, update, and instance identity.
    ///
    /// Services (stat service, inventory, etc.) should be captured in the panel
    /// constructor rather than accessed through the context.
    /// </summary>
    public interface IUiPanelContext
    {
        /// <summary>The live instance this response came from.</summary>
        UiPanelInstanceId InstanceId { get; }

        /// <summary>Raw response payload submitted by the client for this action.</summary>
        ReadOnlyMemory<byte> ResponsePayload { get; }

        /// <summary>Closes the panel on the client.</summary>
        void Close(UiPanelCloseReason reason = UiPanelCloseReason.ClosedByServer);

        /// <summary>
        /// Sends an updated panel to the client.
        /// Construct a new instance of the same panel type with fresh data.
        /// </summary>
        void Update(UiPanel panel);

        /// <summary>
        /// Returns the text entered by the user in the field with the given index.
        /// Field indices correspond to the order TextInput() was called during authoring (0-based).
        /// Returns null if the field was not submitted or the payload could not be decoded.
        /// </summary>
        string? GetTextField(byte fieldIndex);

        /// <summary>
        /// Returns the row key associated with a repeater action, when the click came from a repeater row button.
        /// </summary>
        bool TryGetActionRowKey(out ulong rowKey);
    }
}

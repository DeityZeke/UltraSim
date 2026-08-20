using System;

namespace UltraSim.UI.Contracts
{
    public interface IUiCommandSink
    {
        void OpenPanel(UiPanelOpenCommand command);

        void UpdatePanel(UiPanelUpdateCommand command);

        void ClosePanel(UiPanelCloseCommand command);
    }

    public readonly record struct UiPanelInstanceId(uint Value);

    public readonly record struct UiPanelTypeId(ushort Value)
    {
        public override string ToString() => $"0x{Value:X4}";
    }

    public enum UiPanelCategory : byte
    {
        World = 0,
        Hud = 1,
        Menu = 2,
        Debug = 3,
    }

    public enum UiPanelPresentationMode : byte
    {
        Shared = 0,
        ExclusiveMenuOnly = 1,
        ExclusiveAll = 2,
    }

    [Flags]
    public enum UiPanelFlags : ushort
    {
        None = 0,
        BringToFront = 1 << 0,
        HasCancelAction = 1 << 1,
        /// <summary>
        /// Panel survives session loss and stays visible until the player dismisses it.
        /// Use for rejection/ban messages where the reason must be readable before navigating away.
        /// All other panels are force-closed immediately on disconnect or crash.
        /// </summary>
        PersistOnSessionLoss = 1 << 2,
        /// <summary>
        /// While this panel is open the client keeps gameplay input blocked regardless of app state.
        /// Use for blocking panels opened before the player is fully in-game (e.g. login, character select).
        /// </summary>
        BlocksGameplayInput = 1 << 3,
    }

    public enum UiPanelUpdateMode : byte
    {
        Full = 0,
        Delta = 1,
    }

    public enum UiPanelCloseReason : byte
    {
        None = 0,
        ClosedByServer = 1,
        Cancelled = 2,
        SessionLost = 3,
        Replaced = 4,
        Error = 255,
    }

    public enum UiAckStatus : byte
    {
        Ok = 0,
        NeedDefinition = 1,
        Error = 255,
    }

    public readonly record struct UiPanelOpenCommand(
        UiPanelInstanceId PanelInstanceId,
        UiPanelTypeId PanelTypeId,
        UiPanelCategory Category,
        UiPanelPresentationMode PresentationMode,
        ushort DefinitionRevision,
        UiPanelFlags Flags,
        ReadOnlyMemory<byte> Payload)
    {
        public bool BringToFront => (Flags & UiPanelFlags.BringToFront) != 0;
        public bool HasCancelAction => (Flags & UiPanelFlags.HasCancelAction) != 0;
        public bool IsModal => PresentationMode != UiPanelPresentationMode.Shared;
    }

    public readonly record struct UiPanelUpdateCommand(
        UiPanelInstanceId PanelInstanceId,
        ReadOnlyMemory<byte> Payload,
        UiPanelUpdateMode UpdateMode = UiPanelUpdateMode.Full);

    public readonly record struct UiPanelCloseCommand(
        UiPanelInstanceId PanelInstanceId,
        UiPanelCloseReason CloseReason = UiPanelCloseReason.None);

    public readonly record struct UiPanelDefinition(
        UiPanelTypeId PanelTypeId,
        ushort DefinitionRevision,
        ReadOnlyMemory<byte> DefinitionPayload);

    public readonly record struct UiPanelAck(
        UiPanelInstanceId PanelInstanceId,
        UiPanelTypeId PanelTypeId,
        ushort DefinitionRevision,
        UiAckStatus Status);

    public readonly record struct UiPanelResponse(
        UiPanelInstanceId PanelInstanceId,
        uint ActionId,
        ReadOnlyMemory<byte> Payload = default);

    public static class SharedUiPanelIds
    {
        public static readonly UiPanelTypeId MessageBox      = new(0x1001);
        public static readonly UiPanelTypeId SimpleListMenu  = new(0x1002);
        public static readonly UiPanelTypeId TextPrompt      = new(0x1003);

        // Server-implementation panels — bundled on both sides, type ID shared for registration
        public static readonly UiPanelTypeId LoginPanel       = new(0xFF02);
        public static readonly UiPanelTypeId CharacterSelect  = new(0xFF03);
        public static readonly UiPanelTypeId CharacterCreate  = new(0xFF04);
        public static readonly UiPanelTypeId CharacterDeleteConfirm = new(0xFF05);
    }

    public static class SharedUiActionIds
    {
        public const uint QuitToMainMenu = 0xF0010001;
        public const uint DismissLocalMessageBox = 0xF0010002;
    }
}

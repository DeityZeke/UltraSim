using System;
using UltraSim.UI.Authoring;
using UltraSim.UI.Contracts;

namespace Server.Godot.UI
{
    public sealed class CharacterDeleteConfirmSchemaPanel : UiPanel
    {
        public new static readonly UiPanelTypeId TypeId = SharedUiPanelIds.CharacterDeleteConfirm;
        public static readonly uint ConfirmDelete = ActionId("confirm_delete");
        public static readonly uint ToggleSkipWarning = ActionId("toggle_skip_warning");
        public static readonly uint CancelButton = ActionId("cancel");

        private readonly string _characterName;
        private readonly string _errorMessage;
        private readonly bool _skipFutureWarning;
        private readonly Action<bool> _confirmHandler;
        private readonly Action _cancelHandler;

        public CharacterDeleteConfirmSchemaPanel(
            string characterName,
            bool skipFutureWarning,
            string? errorMessage,
            Action<bool> confirmHandler,
            Action cancelHandler)
            : base(TypeId, revision: 2)
        {
            _characterName = characterName ?? string.Empty;
            _errorMessage = errorMessage ?? string.Empty;
            _skipFutureWarning = skipFutureWarning;
            _confirmHandler = confirmHandler;
            _cancelHandler = cancelHandler;

            Size(520, 240);
            Header().Text("Delete Character", style: "text.title.centered");
            Row().Dynamic($"Delete '{_characterName}'?");
            Row().Text("Type DELETE and press Delete to continue.", style: "text.warning");
            TextInput(placeholder: "Type DELETE", maxLength: 16);
            Row().Dynamic(_errorMessage, style: "text.warning");
            Separator();
            Row()
                .Dynamic(skipFutureWarning ? "[x] Do not show this again" : "[ ] Do not show this again")
                .Button(ToggleSkipWarning, label: "Toggle");
            Footer()
                .Button(ConfirmDelete, label: "Delete")
                .Button(CancelButton, label: "Cancel");
        }

        public override void OnResponse(IUiPanelContext ctx, uint actionId)
        {
            if (actionId == ToggleSkipWarning)
            {
                ctx.Update(new CharacterDeleteConfirmSchemaPanel(
                    _characterName,
                    !_skipFutureWarning,
                    _errorMessage,
                    _confirmHandler,
                    _cancelHandler));
                return;
            }

            if (actionId == CancelButton)
            {
                _cancelHandler();
                return;
            }

            if (actionId != ConfirmDelete)
                return;

            string typed = ctx.GetTextField(0)?.Trim() ?? string.Empty;
            if (!string.Equals(typed, "DELETE", StringComparison.Ordinal))
            {
                ctx.Update(new CharacterDeleteConfirmSchemaPanel(
                    _characterName,
                    _skipFutureWarning,
                    "Type DELETE exactly to confirm character deletion.",
                    _confirmHandler,
                    _cancelHandler));
                return;
            }

            _confirmHandler(_skipFutureWarning);
        }
    }
}

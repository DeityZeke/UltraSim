using System;
using UltraSim.Shared.Gameplay;
using UltraSim.UI.Authoring;
using UltraSim.UI.Contracts;

namespace Server.Godot.UI
{
    public sealed class CharacterCreateSchemaPanel : UiPanel
    {
        public new static readonly UiPanelTypeId TypeId = SharedUiPanelIds.CharacterCreate;
        public static readonly uint CreateMaleButton = ActionId("create_male");
        public static readonly uint CreateFemaleButton = ActionId("create_female");
        public static readonly uint CancelButton = ActionId("cancel");

        private readonly Action<string, ushort> _createHandler;
        private readonly Action _cancelHandler;

        public CharacterCreateSchemaPanel(Action<string, ushort> createHandler, Action cancelHandler)
            : base(TypeId, revision: 1)
        {
            _createHandler = createHandler;
            _cancelHandler = cancelHandler;

            Size(420, 280);
            Header().Text("Create Character", style: "text.title.centered");
            Row().Text("Name");
            TextInput(placeholder: "Character name...", maxLength: 32);
            Separator();
            Row().Text("Choose archetype");
            Footer()
                .Button(CreateMaleButton, label: "Create Male")
                .Button(CreateFemaleButton, label: "Create Female")
                .Button(CancelButton, label: "Cancel");
        }

        public override void OnResponse(IUiPanelContext ctx, uint actionId)
        {
            if (actionId == CancelButton)
            {
                _cancelHandler();
                return;
            }

            string name = ctx.GetTextField(0)?.Trim() ?? string.Empty;
            if (actionId == CreateFemaleButton)
            {
                _createHandler(name, PlayerArchetypeIds.DefaultFemale);
                return;
            }

            if (actionId == CreateMaleButton)
                _createHandler(name, PlayerArchetypeIds.DefaultMale);
        }
    }
}

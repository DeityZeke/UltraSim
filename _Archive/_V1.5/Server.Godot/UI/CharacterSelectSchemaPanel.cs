using System;
using UltraSim.Server.Engines.Accounting;
using UltraSim.Shared.Gameplay;
using UltraSim.UI.Authoring;
using UltraSim.UI.Contracts;

namespace Server.Godot.UI
{
    public sealed class CharacterSelectSchemaPanel : UiPanel
    {
        public new static readonly UiPanelTypeId TypeId = SharedUiPanelIds.CharacterSelect;
        public static readonly uint SelectCharacter = ActionId("select");
        public static readonly uint CreateCharacter = ActionId("create");
        public static readonly uint DeleteCharacter = ActionId("delete");
        public static readonly uint CancelButton = ActionId("cancel");

        private readonly Action<ulong> _selectHandler;
        private readonly Action<ulong> _deleteHandler;
        private readonly Action _createHandler;
        private readonly Action _cancelHandler;

        public CharacterSelectSchemaPanel(
            string username,
            int usedCharacters,
            byte maxCharacters,
            CharacterModel[] characters,
            Action<ulong> selectHandler,
            Action<ulong> deleteHandler,
            Action createHandler,
            Action cancelHandler)
            : base(TypeId, revision: 2)
        {
            _selectHandler = selectHandler;
            _deleteHandler = deleteHandler;
            _createHandler = createHandler;
            _cancelHandler = cancelHandler;

            Size(520, 360);
            Header().Dynamic($"Characters - {username}", style: "text.title.centered");
            Row().Dynamic($"Slots used: {usedCharacters}/{maxCharacters}");
            Separator();

            UiRepeaterBuilder repeater = Repeater("characters", rowStyle: "row.character");
            repeater
                .Dynamic<string>(style: "text.title")
                .Dynamic<string>(style: "text.subtitle")
                .Button(SelectCharacter, label: "Play")
                .Button(DeleteCharacter, label: "Delete");

            for (int i = 0; i < characters.Length; i++)
            {
                CharacterModel character = characters[i];
                repeater.AddItem(character.CharacterId, character.Name, DescribeArchetype(character.ArchetypeId));
            }

            Separator();
            Footer()
                .Button(CreateCharacter, label: "New Character")
                .Button(CancelButton, label: "Cancel");
        }

        public override void OnResponse(IUiPanelContext ctx, uint actionId)
        {
            if (actionId == SelectCharacter && ctx.TryGetActionRowKey(out ulong selectedCharacterId))
            {
                _selectHandler(selectedCharacterId);
                return;
            }

            if (actionId == DeleteCharacter && ctx.TryGetActionRowKey(out ulong deletedCharacterId))
            {
                _deleteHandler(deletedCharacterId);
                return;
            }

            if (actionId == CreateCharacter)
            {
                _createHandler();
                return;
            }

            if (actionId == CancelButton)
                _cancelHandler();
        }

        private static string DescribeArchetype(ushort archetypeId) =>
            archetypeId switch
            {
                PlayerArchetypeIds.DefaultFemale => "Archetype: Female",
                _ => "Archetype: Male",
            };
    }
}

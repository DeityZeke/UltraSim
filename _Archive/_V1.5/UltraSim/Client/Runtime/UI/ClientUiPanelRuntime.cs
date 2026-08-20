using System;
using System.Collections.Generic;
using UltraSim.UI.Contracts;

namespace UltraSim.Client.Runtime.UI
{
    public sealed class ClientUiPanelRuntime : IUiCommandSink
    {
        private readonly Action<UiPanelOpenCommand> _openRenderer;
        private readonly Action<UiPanelUpdateCommand> _updateRenderer;
        private readonly Action<UiPanelCloseCommand> _closeRenderer;
        private readonly Action<UiPanelAck>? _ackSink;

        private readonly Dictionary<(ushort TypeId, ushort Revision), UiPanelDefinition> _definitions = new();
        private readonly Dictionary<uint, UiPanelOpenCommand> _activeInstances = new();
        private readonly Dictionary<uint, PendingPanelState> _pendingInstances = new();

        public ClientUiPanelRuntime(
            Action<UiPanelOpenCommand> openRenderer,
            Action<UiPanelUpdateCommand> updateRenderer,
            Action<UiPanelCloseCommand> closeRenderer,
            Action<UiPanelAck>? ackSink = null)
        {
            _openRenderer = openRenderer ?? throw new ArgumentNullException(nameof(openRenderer));
            _updateRenderer = updateRenderer ?? throw new ArgumentNullException(nameof(updateRenderer));
            _closeRenderer = closeRenderer ?? throw new ArgumentNullException(nameof(closeRenderer));
            _ackSink = ackSink;
        }

        public void RegisterBundledDefinition(UiPanelDefinition definition, bool warmCache)
        {
            if (warmCache)
                _definitions[(definition.PanelTypeId.Value, definition.DefinitionRevision)] = definition;
        }

        /// <summary>
        /// Returns the raw definition wire record for a given (typeId, revision) pair.
        /// Used by the Godot rendering layer to decode the AST and build controls.
        /// Returns false if the definition is not yet cached.
        /// </summary>
        public bool TryGetDefinition(UiPanelTypeId typeId, ushort revision, out UiPanelDefinition definition)
            => _definitions.TryGetValue((typeId.Value, revision), out definition);

        public void AcceptDefinition(UiPanelDefinition definition)
        {
            _definitions[(definition.PanelTypeId.Value, definition.DefinitionRevision)] = definition;

            // Collect all pending instances waiting for this (typeId, revision) — there may be
            // more than one if multiple opens arrived before the definition was received.
            List<uint>? matchingKeys = null;
            foreach (KeyValuePair<uint, PendingPanelState> kvp in _pendingInstances)
            {
                UiPanelOpenCommand open = kvp.Value.Open;
                if (open.PanelTypeId.Value == definition.PanelTypeId.Value &&
                    open.DefinitionRevision == definition.DefinitionRevision)
                {
                    matchingKeys ??= new List<uint>();
                    matchingKeys.Add(kvp.Key);
                }
            }

            if (matchingKeys == null)
                return;

            for (int i = 0; i < matchingKeys.Count; i++)
            {
                uint instanceId = matchingKeys[i];
                PendingPanelState pending = _pendingInstances[instanceId];
                _pendingInstances.Remove(instanceId);

                ReplaceActiveIfNeeded(pending.Open.PanelInstanceId, UiPanelCloseReason.Replaced);
                _openRenderer(pending.Open);
                _activeInstances[pending.Open.PanelInstanceId.Value] = pending.Open;

                for (int j = 0; j < pending.Updates.Count; j++)
                    _updateRenderer(pending.Updates[j]);

                EmitAck(new UiPanelAck(
                    pending.Open.PanelInstanceId,
                    pending.Open.PanelTypeId,
                    pending.Open.DefinitionRevision,
                    UiAckStatus.Ok));
            }
        }

        public void OpenPanel(UiPanelOpenCommand command)
        {
            ReplacePendingIfNeeded(command.PanelInstanceId);
            ReplaceActiveIfNeeded(command.PanelInstanceId, UiPanelCloseReason.Replaced);

            if (_definitions.ContainsKey((command.PanelTypeId.Value, command.DefinitionRevision)))
            {
                _openRenderer(command);
                _activeInstances[command.PanelInstanceId.Value] = command;
                EmitAck(new UiPanelAck(
                    command.PanelInstanceId,
                    command.PanelTypeId,
                    command.DefinitionRevision,
                    UiAckStatus.Ok));
                return;
            }

            _pendingInstances[command.PanelInstanceId.Value] = new PendingPanelState(command);
            EmitAck(new UiPanelAck(
                command.PanelInstanceId,
                command.PanelTypeId,
                command.DefinitionRevision,
                UiAckStatus.NeedDefinition));
        }

        public void UpdatePanel(UiPanelUpdateCommand command)
        {
            if (_activeInstances.ContainsKey(command.PanelInstanceId.Value))
            {
                _updateRenderer(command);
                return;
            }

            if (_pendingInstances.TryGetValue(command.PanelInstanceId.Value, out PendingPanelState? pending))
                pending.Updates.Add(command);
        }

        public void ClosePanel(UiPanelCloseCommand command)
        {
            _pendingInstances.Remove(command.PanelInstanceId.Value);

            if (_activeInstances.Remove(command.PanelInstanceId.Value))
                _closeRenderer(command);
        }

        public void CloseAllForSessionLoss()
        {
            _pendingInstances.Clear();

            if (_activeInstances.Count == 0)
                return;

            uint[] activeKeys = new uint[_activeInstances.Count];
            _activeInstances.Keys.CopyTo(activeKeys, 0);
            for (int i = 0; i < activeKeys.Length; i++)
                _closeRenderer(new UiPanelCloseCommand(new UiPanelInstanceId(activeKeys[i]), UiPanelCloseReason.SessionLost));

            _activeInstances.Clear();
        }

        private void ReplacePendingIfNeeded(UiPanelInstanceId panelInstanceId)
        {
            _pendingInstances.Remove(panelInstanceId.Value);
        }

        private void ReplaceActiveIfNeeded(UiPanelInstanceId panelInstanceId, UiPanelCloseReason closeReason)
        {
            if (_activeInstances.Remove(panelInstanceId.Value))
                _closeRenderer(new UiPanelCloseCommand(panelInstanceId, closeReason));
        }

        private void EmitAck(UiPanelAck ack)
        {
            _ackSink?.Invoke(ack);
        }

        private sealed class PendingPanelState
        {
            public PendingPanelState(UiPanelOpenCommand open)
            {
                Open = open;
            }

            public UiPanelOpenCommand Open { get; }
            public List<UiPanelUpdateCommand> Updates { get; } = new();
        }
    }
}

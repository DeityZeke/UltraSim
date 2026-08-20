using System;
using System.Collections.Generic;
using System.Text;
using UltraSim.UI.Codec;
using UltraSim.UI.Contracts;
using UltraSim.UI.Schema;

namespace UltraSim.UI.Authoring
{
    /// <summary>
    /// Base class for all server-driven UI panels.
    ///
    /// Subclass this to define a panel. The constructor IS the authoring surface:
    /// call builder methods to declare layout and dynamic slots. Pass live data
    /// directly to Dynamic() — the framework splits definition (cached) from
    /// payload (sent each open/update) transparently.
    ///
    /// Example:
    /// <code>
    ///   public sealed class VendorBuyPanel : UiPanel
    ///   {
    ///       public static readonly UiPanelTypeId TypeId   = new(0x2001);
    ///       public static readonly uint          BuyBtn   = ActionId("buy");
    ///       public static readonly uint          CancelBtn = ActionId("cancel");
    ///
    ///       public VendorBuyPanel(Item item, Mobile buyer) : base(TypeId, revision: 1)
    ///       {
    ///           Style("panel.stone");
    ///           Size(340, 200);
    ///
    ///           Header("header").Text("Purchase Item");
    ///
    ///           Row("item_row").Text("Item:").Dynamic(item.DisplayName);
    ///           Row("gold_row").Text("Your Gold:").Dynamic(buyer.TotalGold, format: "N0 gp");
    ///
    ///           Footer("footer")
    ///               .Button("buy",    label: "Buy",    style: "button.confirm")
    ///               .Button("cancel", label: "Cancel", style: "button.cancel");
    ///       }
    ///   }
    /// </code>
    ///
    /// Usage:
    /// <code>
    ///   panelService.Open(session, new VendorBuyPanel(item, mobile));
    ///   panelService.Update(session, instanceId, new VendorBuyPanel(updatedItem, mobile));
    /// </code>
    /// </summary>
    public abstract class UiPanel
    {
        // ---- Identity --------------------------------------------------------

        public UiPanelTypeId TypeId   { get; }
        public ushort        Revision { get; }

        // ---- Root state (set by builder methods) -----------------------------

        private ushort          _width;
        private ushort          _height;
        private UiPanelPosition _position = UiPanelPosition.Center;
        private string?         _style;
        private byte            _panelFlags; // bit0=Draggable, bit1=Closable

        // ---- AST + slot tracking --------------------------------------------

        private readonly List<UiSchemaNode>     _nodes = new();
        private readonly List<object?>          _slotValues = new();
        private readonly List<UiSlotDescriptor> _slotDescriptors = new();
        private readonly List<UiRepeaterNode>   _repeaters = new();
        private readonly Dictionary<byte, List<UiRepeaterItemData>> _repeaterItems = new();
        private          int                    _nodeIdCounter;
        private          int                    _textFieldCounter;
        private          int                    _repeaterIdCounter;

        // ---- Cached definition ----------------------------------------------

        private UiSchemaDefinition? _cachedDefinition;

        // ---- Constructor -----------------------------------------------------

        protected UiPanel(UiPanelTypeId typeId, ushort revision)
        {
            TypeId   = typeId;
            Revision = revision;
        }

        /// <summary>Convenience overload — pass the type ID as a raw ushort literal.</summary>
        protected UiPanel(ushort typeId, ushort revision)
            : this(new UiPanelTypeId(typeId), revision) { }

        // =========================================================================
        // Builder methods — called from subclass constructors
        // =========================================================================

        // ---- Panel root properties -------------------------------------------

        /// <summary>Visual style reference for the panel background.</summary>
        protected void Style(string style) => _style = style;

        /// <summary>Panel dimensions in virtual pixels.</summary>
        protected void Size(ushort width, ushort height) { _width = width; _height = height; }

        /// <summary>Where the panel appears on screen when opened.</summary>
        protected void Position(UiPanelPosition position) => _position = position;

        /// <summary>Allow the player to drag the panel.</summary>
        protected void Draggable() => _panelFlags |= 0x01;

        /// <summary>Show a close button on the panel chrome.</summary>
        protected void Closable() => _panelFlags |= 0x02;

        // ---- Background layers -----------------------------------------------

        /// <summary>
        /// Adds a background decoration layer rendered behind all content.
        /// Layers are rendered back-to-front in declaration order.
        /// Chain .Style() and .Inset() on the returned builder.
        /// </summary>
        protected UiLayerBuilder Layer(string id)
        {
            var node = new UiLayerNode(id);
            _nodes.Add(node);
            return new UiLayerBuilder(node);
        }

        // ---- Top-level structural nodes -------------------------------------

        /// <summary>
        /// Adds a header bar at the top of the panel.
        /// Chain .Text() / .Dynamic() on the returned builder.
        /// Provide <paramref name="id"/> only if you need to identify this node (e.g. multiple headers).
        /// </summary>
        protected UiHeaderBuilder Header(string? id = null, string? style = null)
        {
            var node = new UiHeaderNode(id ?? $"header{_nodeIdCounter++}", new List<UiSchemaNode>(), style);
            _nodes.Add(node);
            return new UiHeaderBuilder(node, this);
        }

        /// <summary>
        /// Adds a horizontal row. Children are laid out left-to-right.
        /// Chain .Text() / .Dynamic() / .Button() on the returned builder.
        /// Provide <paramref name="id"/> only if you need to identify this node.
        /// </summary>
        protected UiRowBuilder Row(string? id = null, string? style = null)
        {
            var node = new UiRowNode(id ?? $"row{_nodeIdCounter++}", new List<UiSchemaNode>(), style);
            _nodes.Add(node);
            return new UiRowBuilder(node, this);
        }

        /// <summary>
        /// Adds a footer bar at the bottom of the panel.
        /// Typically contains action buttons.
        /// Provide <paramref name="id"/> only if you need to identify this node.
        /// </summary>
        protected UiFooterBuilder Footer(string? id = null, string? style = null)
        {
            var node = new UiFooterNode(id ?? $"footer{_nodeIdCounter++}", new List<UiSchemaNode>(), style);
            _nodes.Add(node);
            return new UiFooterBuilder(node, this);
        }

        protected UiRepeaterBuilder Repeater(string? id = null, string? style = null, string? rowStyle = null)
        {
            if (_repeaterIdCounter >= 255)
                throw new InvalidOperationException("UiPanel: maximum of 255 repeaters exceeded.");

            var node = new UiRepeaterNode(
                id ?? $"repeater{_nodeIdCounter++}",
                (byte)_repeaterIdCounter++,
                new List<UiSchemaNode>(),
                new List<UiSlotDescriptor>(),
                style,
                rowStyle);
            _nodes.Add(node);
            _repeaters.Add(node);
            return new UiRepeaterBuilder(node, this);
        }

        /// <summary>Adds a visual separator between sections.</summary>
        protected void Separator(string? style = null)
        {
            _nodes.Add(new UiSeparatorNode($"sep{_nodeIdCounter++}", style));
        }

        // ---- Top-level primitive shortcuts (for flat panels) ----------------

        /// <summary>Adds a static text label directly at the panel root level.</summary>
        protected void Text(string text, string? style = null)
        {
            _nodes.Add(new UiTextNode($"txt{_nodeIdCounter++}", text, style));
        }

        /// <summary>Adds a dynamic value slot directly at the panel root level.</summary>
        protected void Dynamic<T>(T value, string? style = null, string? format = null)
        {
            byte slotIndex = AllocateSlot(value);
            _nodes.Add(new UiDynamicNode($"dyn{_nodeIdCounter++}", slotIndex, SlotTypeOf<T>(), style, format));
        }

        /// <summary>
        /// Adds a text input field directly at the panel root level.
        /// The entered text is packed into the response payload at field index <see cref="AllocateTextField"/>.
        /// </summary>
        protected void TextInput(string? placeholder = null, bool isPassword = false, ushort maxLength = 0)
        {
            byte fieldIndex = AllocateTextField();
            _nodes.Add(new UiTextInputNode($"input{_nodeIdCounter++}", fieldIndex, isPassword, placeholder, maxLength));
        }

        // =========================================================================
        // Internal slot allocation (used by builders + base)
        // =========================================================================

        /// <summary>
        /// Allocates the next text input field index (0-based, separate from display slots).
        /// Called by TextInput() methods on both the panel and container builders.
        /// </summary>
        internal byte AllocateTextField()
        {
            if (_textFieldCounter >= 255)
                throw new InvalidOperationException("UiPanel: maximum of 255 text input fields exceeded.");
            return (byte)_textFieldCounter++;
        }

        /// <summary>
        /// Allocates the next payload slot, records the current value, and returns the slot index.
        /// Called by Dynamic() methods on both the panel and container builders.
        /// </summary>
        internal byte AllocateSlot<T>(T value)
        {
            if (_slotValues.Count >= 255)
                throw new InvalidOperationException("UiPanel: maximum of 255 dynamic slots exceeded.");

            byte index = (byte)_slotValues.Count;
            _slotValues.Add(value);
            _slotDescriptors.Add(new UiSlotDescriptor(index, SlotTypeOf<T>()));
            return index;
        }

        internal void AddRepeaterItem(byte repeaterId, UiRepeaterItemData item)
        {
            if (!_repeaterItems.TryGetValue(repeaterId, out List<UiRepeaterItemData>? items))
            {
                items = new List<UiRepeaterItemData>();
                _repeaterItems[repeaterId] = items;
            }

            items.Add(item);
        }

        // =========================================================================
        // Definition + payload extraction (called by service layer)
        // =========================================================================

        /// <summary>
        /// Returns the compiled <see cref="UiSchemaDefinition"/> for this panel class.
        /// The result is cached after the first call — definition bytes are identical
        /// across all instances with the same TypeId + Revision.
        ///
        /// Note: the cache lives on THIS instance. For production use, the service
        /// layer caches by (TypeId, Revision) across instances.
        /// </summary>
        public UiSchemaDefinition ExtractDefinition()
        {
            if (_cachedDefinition is not null)
                return _cachedDefinition;

            byte[] bytes = UiSchemaCodec.Write(
                _width, _height, _position, _style, _panelFlags,
                _nodes, _slotDescriptors);

            _cachedDefinition = new UiSchemaDefinition(
                TypeId, Revision,
                _width, _height, _position, _style, _panelFlags,
                _nodes, _slotDescriptors,
                bytes);

            return _cachedDefinition;
        }

        /// <summary>
        /// Serializes the current dynamic slot values into a payload byte array.
        /// Always writes all slots (full payload). Delta is a future optimization.
        /// </summary>
        public byte[] ExtractPayload()
        {
            if (_slotDescriptors.Count == 0 && _repeaters.Count == 0)
                return Array.Empty<byte>();

            var buf = new List<byte>(_slotDescriptors.Count * 8);
            for (int i = 0; i < _slotDescriptors.Count; i++)
            {
                var slot  = _slotDescriptors[i];
                var value = _slotValues[i];

                WriteSlotValue(buf, slot.SlotType, value);
            }

            if (_repeaters.Count > 0)
            {
                buf.Add((byte)_repeaters.Count);
                for (int i = 0; i < _repeaters.Count; i++)
                {
                    UiRepeaterNode repeater = _repeaters[i];
                    buf.Add(repeater.RepeaterId);
                    _repeaterItems.TryGetValue(repeater.RepeaterId, out List<UiRepeaterItemData>? items);
                    items ??= [];
                    int itemCount = Math.Min(items.Count, ushort.MaxValue);
                    buf.Add((byte)itemCount);
                    buf.Add((byte)(itemCount >> 8));

                    for (int itemIndex = 0; itemIndex < itemCount; itemIndex++)
                    {
                        UiRepeaterItemData item = items[itemIndex];
                        WriteUInt64(buf, item.RowKey);
                        for (int slotIndex = 0; slotIndex < repeater.Slots.Count; slotIndex++)
                        {
                            object? value = slotIndex < item.Values.Length ? item.Values[slotIndex] : null;
                            WriteSlotValue(buf, repeater.Slots[slotIndex].SlotType, value);
                        }
                    }
                }
            }

            return buf.ToArray();
        }

        // =========================================================================
        // Response handler — override in subclass (RunUO-style all-in-one)
        // =========================================================================

        /// <summary>
        /// Called by the service layer when the client fires an action on this panel instance.
        /// Override to handle button presses and other interactions.
        ///
        /// Use <paramref name="ctx"/> to close or update the panel.
        /// Use the static action ID constants declared on the panel class to identify actions:
        /// <code>
        ///   public static readonly uint OkBtn = ActionId("ok");
        ///
        ///   protected override void OnResponse(IUiPanelContext ctx, uint actionId)
        ///   {
        ///       if (actionId == OkBtn)
        ///           ctx.Close();
        ///   }
        /// </code>
        /// </summary>
        public virtual void OnResponse(IUiPanelContext ctx, uint actionId) { }

        // =========================================================================
        // Static helpers
        // =========================================================================

        /// <summary>
        /// Computes the stable action ID for a button node key using FNV-1a 32-bit hash.
        /// Use this to declare action ID constants on your panel class:
        /// <code>public static readonly uint BuyBtn = ActionId("buy");</code>
        /// </summary>
        public static uint ActionId(string key)
        {
            uint hash = 2166136261u;
            foreach (char c in key)
            {
                hash ^= (byte)c;
                hash *= 16777619u;
            }
            return hash == 0 ? 1u : hash; // 0 reserved as "no action"
        }

        /// <summary>Maps a CLR type to the corresponding <see cref="UiSlotType"/> wire code.</summary>
        public static UiSlotType SlotTypeOf<T>()
        {
            if (typeof(T) == typeof(string))  return UiSlotType.String;
            if (typeof(T) == typeof(int))     return UiSlotType.Int32;
            if (typeof(T) == typeof(float))   return UiSlotType.Float32;
            if (typeof(T) == typeof(bool))    return UiSlotType.Bool;
            throw new NotSupportedException(
                $"UiPanel: unsupported Dynamic<T> type '{typeof(T).Name}'. " +
                $"Supported: string, int, float, bool.");
        }

        private static void WriteSlotValue(List<byte> buf, UiSlotType slotType, object? value)
        {
            switch (slotType)
            {
                case UiSlotType.String:
                {
                    string s = value as string ?? string.Empty;
                    byte[] utf8 = Encoding.UTF8.GetBytes(s);
                    int len = Math.Min(utf8.Length, ushort.MaxValue);
                    buf.Add((byte)len);
                    buf.Add((byte)(len >> 8));
                    if (len > 0)
                        buf.AddRange(new ReadOnlySpan<byte>(utf8, 0, len).ToArray());
                    break;
                }
                case UiSlotType.Int32:
                {
                    int v = value is int i32 ? i32 : 0;
                    buf.Add((byte)v);
                    buf.Add((byte)(v >> 8));
                    buf.Add((byte)(v >> 16));
                    buf.Add((byte)(v >> 24));
                    break;
                }
                case UiSlotType.Float32:
                {
                    float f = value is float f32 ? f32 : 0f;
                    int bits = BitConverter.SingleToInt32Bits(f);
                    buf.Add((byte)bits);
                    buf.Add((byte)(bits >> 8));
                    buf.Add((byte)(bits >> 16));
                    buf.Add((byte)(bits >> 24));
                    break;
                }
                case UiSlotType.Bool:
                    buf.Add(value is bool b && b ? (byte)1 : (byte)0);
                    break;
            }
        }

        private static void WriteUInt64(List<byte> buf, ulong value)
        {
            buf.Add((byte)value);
            buf.Add((byte)(value >> 8));
            buf.Add((byte)(value >> 16));
            buf.Add((byte)(value >> 24));
            buf.Add((byte)(value >> 32));
            buf.Add((byte)(value >> 40));
            buf.Add((byte)(value >> 48));
            buf.Add((byte)(value >> 56));
        }
    }
}

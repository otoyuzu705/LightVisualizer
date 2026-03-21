namespace Core.GDTF
{
    /// <summary>
    /// GDTF の ChannelFunction 1エントリ。
    /// DMX値のレンジとその機能（Attribute）を保持する。
    /// </summary>
    public class ChannelFunctionEntry
    {
        /// <summary>機能名（"Pan", "Dimmer", "ColorRGB_Red" 等）</summary>
        public string Attribute { get; set; } = "";

        /// <summary>ChannelFunction の Name 属性（人間可読なラベル）</summary>
        public string Name { get; set; } = "";

        /// <summary>この ChannelFunction が有効になる DMX 開始値（0-255）</summary>
        public byte DmxFrom { get; set; } = 0;

        /// <summary>この ChannelFunction のデフォルト DMX 値（0-255）</summary>
        public byte DmxDefault { get; set; } = 0;
    }
}
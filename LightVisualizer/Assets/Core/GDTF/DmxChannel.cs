using System;
using System.Collections.Generic;

namespace Core.GDTF
{
    /// <summary>
    /// GDTF の DmxChannel 1本分のデータ。
    /// Offsets はGDTF仕様の1始まり絶対アドレスをそのまま保持する。
    /// DMXアドレスへの変換は FixtureController 側で StartAddress - 1 + Offset[0] - 1 として行う。
    /// </summary>
    public class DmxChannel
    {
        /// <summary>
        /// LogicalChannel の Attribute 属性値（"Pan", "Tilt", "Dimmer", "ColorRGB_Red" 等）。
        /// どの照明パラメータに対応するかを示す。
        /// </summary>
        public string AttributeName { get; set; } = "";

        /// <summary>
        /// DMXチャンネルの絶対オフセット（1始まり）。
        /// 複数要素の場合は 16bit チャンネル（Offset[0]=粗調整, Offset[1]=細調整）。
        /// </summary>
        public int[] Offsets { get; set; } = Array.Empty<int>();

        /// <summary>
        /// デフォルトDMX値（0-255）。
        /// GDTF仕様の "分子/分母" 形式はパース時点で変換済み。
        /// </summary>
        public byte DefaultValue { get; set; } = 0;

        /// <summary>
        /// このチャンネルが持つ ChannelFunction の一覧。
        /// DMX値レンジごとに異なる機能が割り当てられる（例: カラーマクロ、ゴボ等）。
        /// </summary>
        public List<ChannelFunctionEntry> ChannelFunctions { get; set; } = new List<ChannelFunctionEntry>();

        /// <summary>16bit チャンネルかどうか（Offsets が2要素以上）</summary>
        public bool Is16Bit => Offsets.Length >= 2;
    }
}
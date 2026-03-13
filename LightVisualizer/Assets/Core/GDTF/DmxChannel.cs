using UnityEngine;

namespace Core.GDTF
{
    public class DmxChannel
    {
        public string LogicalChannelId { get; set; } = "";
        // TODO: プロパティ名Offetsはスペルミス。互換性を考慮しつつOffsetsへのリネームを検討する。
        public int[] Offets { get; set; }
        public string DefaultValue { get; set; } = "0/1";
    }
}

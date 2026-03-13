using UnityEngine;

namespace Core.GDTF
{
    public class DmxChannel
    {
        public string LogicalChannelId { get; set; } = "";
        public int[] Offets { get; set; }
        public string DefaultValue { get; set; } = "0/1";
    }
}

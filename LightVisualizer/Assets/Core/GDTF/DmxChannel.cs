using System;

namespace Core.GDTF
{
    public class DmxChannel
    {
        public string LogicalChannelId { get; set; } = "";
        public int[] Offsets { get; set; } = Array.Empty<int>();
        public string DefaultValue { get; set; } = "0/1";
    }
}

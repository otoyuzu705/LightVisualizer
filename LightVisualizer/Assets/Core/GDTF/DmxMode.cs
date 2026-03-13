using System.Collections.Generic;

namespace Core.GDTF
{
    public class DmxMode
    {
        public string Name { get; set; } = "";
        public List<DmxChannel> Channels { get; set; } = new List<DmxChannel>();
    }    
}


using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Core.GDTF
{
    public class GdtfData
    {
           public string Manufacturer { get; set; } = "Unknown";
           public string FixtureName { get; set; } = "Unknown";
           public string FixtureTypeId { get; set; } = "";
           
           public List<DmxMode> DmxModes { get; set; } = new List<DmxMode>();
    }    
}


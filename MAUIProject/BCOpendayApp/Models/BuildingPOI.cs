using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BCOpendayApp.Models
{
    public class BuildingPOI
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string CloudAnchorId { get; set; }
    }
    public static class CampusDatabase
    {
        // Your main centralized data repository
        public static List<BuildingPOI> OpenDayTargets = new List<BuildingPOI>
    {
        
    };
    }
}

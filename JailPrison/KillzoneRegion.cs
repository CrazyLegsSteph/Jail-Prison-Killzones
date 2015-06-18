using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TShockAPI.DB;

namespace JailPrison
{
    public class KillzoneRegion : Region
    {
        public List<int> AllowedIds = new List<int>();

        public KillzoneRegion(string name, List<int> allowedids, int x, int y, int x2, int y2)
        {
            this.Name = name;
            AllowedIds = allowedids;
            this.Area = new Rectangle(x, y, x2 - x, y2 - y);
        }
    }
}

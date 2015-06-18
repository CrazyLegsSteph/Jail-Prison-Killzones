using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TShockAPI.DB;

namespace JailPrison
{
    public class JailRegion : Region
    {
        public JailRegion(string name, int x, int y, int x2, int y2)
        {
            this.Name = name;
            this.Area = new Rectangle(x, y, x2 - x, y2 - y);
        }
    }
}

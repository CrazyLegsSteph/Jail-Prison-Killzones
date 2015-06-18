using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TShockAPI;

namespace JailPrison
{
    public class JPPlayer
    {
        public int Index;
        public TSPlayer TSPlayer { get { return TShock.Players[Index]; } }
        public bool InRegion;
        public string CurrentRegion;
        public Vector2 LastPosition = Vector2.Zero;

        public JPPlayer(int index)
        {
            Index = index;
        }
    }
}

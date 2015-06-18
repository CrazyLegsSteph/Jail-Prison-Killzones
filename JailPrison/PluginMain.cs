using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Data;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.DB;
using Mono.Data.Sqlite;
using MySql.Data.MySqlClient;

namespace JailPrison
{
    [ApiVersion(1, 17)]
    public class PluginMain : TerrariaPlugin
    {
        public static string configPath = Path.Combine(TShock.SavePath, "JailPrison.json");
        private Config config = new Config();

        private IDbConnection db;

        private System.Timers.Timer jailTimer;
        private System.Timers.Timer killzoneTimer;

        public static List<string> Prisoners = new List<string>();
        public static List<JPPlayer> JailPlayers = new List<JPPlayer>();
        public static List<JailRegion> JailRegions = new List<JailRegion>();
        public static List<KillzoneRegion> KillRegs = new List<KillzoneRegion>();

        public override string Name
        {
            get { return "Jail / Prison & Killzones"; }
        }

        public override string Author
        {
            get { return "DarkunderdoG"; }
        }

        public override string Description
        {
            get { return "Jail & Prison plugin"; }
        }

        public override Version Version
        {
            get { return new Version(1, 0, 0, 0); }
        }

        public PluginMain(Main game) : base(game)
        {
            base.Order = 1;
        }

        #region Initialize/Dispose
        public override void Initialize()
        {
            ServerApi.Hooks.GameInitialize.Register(this, OnInitialize);
            ServerApi.Hooks.NetGreetPlayer.Register(this, OnGreet);
            ServerApi.Hooks.ServerLeave.Register(this, OnLeave);
            ServerApi.Hooks.ServerChat.Register(this, OnChat);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ServerApi.Hooks.GameInitialize.Deregister(this, OnInitialize);
                ServerApi.Hooks.NetGreetPlayer.Deregister(this, OnGreet);
                ServerApi.Hooks.ServerLeave.Deregister(this, OnLeave);
                ServerApi.Hooks.ServerChat.Deregister(this, OnChat);
            }
            base.Dispose(disposing);
        }
        #endregion

        #region Hooks
        private void OnInitialize(EventArgs args)
        {
            SetupDb();
            loadJails();
            (config = config.Read(configPath)).Write(configPath);
            jailTimer = new System.Timers.Timer { AutoReset = true, Enabled = true, Interval = 1000 };
            jailTimer.Elapsed += jailTimer_Elapsed;
            killzoneTimer = new System.Timers.Timer { AutoReset = true, Enabled = true, Interval = 1000 };
            killzoneTimer.Elapsed += killzoneTimer_Elapsed;

            Commands.ChatCommands.Add(new Command("jp.imprison", Imprison, "imprison"));
            Commands.ChatCommands.Add(new Command("jp.imprison", SetFree, "setfree"));
            Commands.ChatCommands.Add(new Command("jp.reload", JPReload, "jpreload"));
            Commands.ChatCommands.Add(new Command("jp.region.set", JPRegion, "jpreg"));
            Commands.ChatCommands.Add(new Command("jp.killzones.set", KillzoneReg, "killreg"));
        }

        private void OnGreet(GreetPlayerEventArgs args)
        {
            JPPlayer player = new JPPlayer(args.Who);

            JailPlayers.Add(player);
        }

        private void OnLeave(LeaveEventArgs args)
        {
            JPPlayer player = new JPPlayer(args.Who);

            JailPlayers.Remove(player);
        }

        private void OnChat(ServerChatEventArgs args)
        {
            List<string> unallowedCmds = new List<string> { "tp", "home", "swap", "spawn", "warp" };

            foreach (string cmd in unallowedCmds)
            {
                if (args.Text.StartsWith("/{0}".SFormat(cmd)) && Prisoners.Contains(TShock.Players[args.Who].IP))
                {
                    TShock.Players[args.Who].SendErrorMessage("You cannot teleport out of jail.");
                    args.Handled = true;
                }
            }
        }

        void jailTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            var reg = GetRegionByName(config.jailRegion);

            foreach (JPPlayer player in JailPlayers)
            {
                if (Prisoners.Contains(player.TSPlayer.IP))
                {
                    if (player.LastPosition != new Vector2(player.TSPlayer.TileX, player.TSPlayer.TileY))
                    {
                        foreach (JailRegion region in JailRegions)
                        {
                            if (reg.Area.Intersects(new Rectangle(player.TSPlayer.TileX, player.TSPlayer.TileY, 1, 1)))
                            {
                                if (player.CurrentRegion != reg.Name)
                                {
                                    player.CurrentRegion = reg.Name;
                                    player.InRegion = true;
                                }
                            }
                            else
                            {
                                var warp = TShock.Warps.Find(config.jailWarp);
                                if (warp.Position != Point.Zero)
                                {
                                    if (player.TSPlayer.Teleport(warp.Position.X * 16, warp.Position.Y * 16))
                                    {
                                        player.TSPlayer.SendErrorMessage("You are stuck in prison - An Admin or Mod Will Need To Let You Out...");
                                        player.CurrentRegion = reg.Name;
                                        player.InRegion = true;
                                    }
                                }
                            }

                            player.LastPosition = new Vector2(player.TSPlayer.TileX, player.TSPlayer.TileY);
                        }
                    }
                }
            }
        }

        void killzoneTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            throw new NotImplementedException();
        }
        #endregion

        #region JPCommands
        private void Imprison(CommandArgs args)
        {
            if (args.Parameters.Count != 1)
            {
                args.Player.SendErrorMessage("Invalid syntax! Proper syntax: {0}imprison <player name>", TShock.Config.CommandSpecifier);
                return;
            }

            var warp = TShock.Warps.Find(config.jailWarp);
            var region = GetRegionByName(config.jailRegion);

            string player = String.Join(" ", args.Parameters[0]);
            var players = TShock.Utils.FindPlayer(player);
            if (players.Count == 0)
                args.Player.SendErrorMessage("Invalid player.");
            else if (players.Count > 1)
                TShock.Utils.SendMultipleMatchError(args.Player, players.Select(p => p.Name));
            else if (players[0].Group.HasPermission("jp.imprison.bypass"))
                args.Player.SendErrorMessage("You cannot put this player in prison.");
            else if (Prisoners.Contains(players[0].IP))
                args.Player.SendErrorMessage("This player is already imprisoned.");
            else if (warp == null)
                args.Player.SendErrorMessage("Prison warp is not set.");
            else if (region == null)
                args.Player.SendErrorMessage("Prison region is not set.");
            else
            {
                if (warp.Position != Point.Zero)
                {
                    Prisoners.Add(players[0].IP);
                    jailTimer.Enabled = true;
                    players[0].Teleport(warp.Position.X * 16, warp.Position.Y * 16);
                    args.Player.SendInfoMessage("You warped {0} to prison!", players[0].Name);
                    players[0].SendInfoMessage("{0} Warped you to the Prison! You Cannot Get Out Until An Admin Releases You", args.Player.UserAccountName);
                }
            }
        }

        private void SetFree(CommandArgs args)
        {
            if (args.Parameters.Count != 1)
            {
                args.Player.SendErrorMessage("Invalid syntax! Proper syntax: {0}setfree <player name>", TShock.Config.CommandSpecifier);
                return;
            }

            string player = String.Join(" ", args.Parameters[0]);
            var players = TShock.Utils.FindPlayer(player);
            if (players.Count == 0)
                args.Player.SendErrorMessage("Invalid player.");
            else if (players.Count > 1)
                TShock.Utils.SendMultipleMatchError(args.Player, players.Select(p => p.Name));
            else if (!Prisoners.Contains(players[0].IP))
                args.Player.SendErrorMessage("This player is not imprisoned.");
            else
            {
                Prisoners.Remove(players[0].IP);
                jailTimer.Enabled = false;
                players[0].Spawn();
                args.Player.SendInfoMessage("You warped {0} to Spawn from Prison!", players[0].Name);
                players[0].SendSuccessMessage("{0} Warped You To Spawn From Prison! Now Behave!!!!!", args.Player.UserAccountName);
            }
        }

        private void JPReload(CommandArgs args)
        {
            (config = config.Read(configPath)).Write(configPath);
            args.Player.SendSuccessMessage("Reloaded Jail/Prison config.");
        }

        private void JPRegion(CommandArgs args)
        {
             if (args.Parameters.Count == 2 && args.Parameters[0].ToLower() == "add")
             {
                 if (!args.Player.TempPoints.Any(p => p == Point.Zero))
                 {
                     string regionName = String.Join(" ", args.Parameters.GetRange(1, args.Parameters.Count - 1));
                     var X = Math.Min(args.Player.TempPoints[0].X, args.Player.TempPoints[1].X);
                     var Y = Math.Min(args.Player.TempPoints[0].Y, args.Player.TempPoints[1].Y);
                     var Width = Math.Abs(args.Player.TempPoints[0].X - args.Player.TempPoints[1].X);
                     var Height = Math.Abs(args.Player.TempPoints[0].Y - args.Player.TempPoints[1].Y);
                     JailRegions.Add(new JailRegion(regionName, X, Y, (Width + X), (Height + Y)));
                     addJail(regionName);
                     args.Player.SendSuccessMessage("Successfully set new JailRegion: \"{0}\"", regionName);
                 }
                 else
                     args.Player.SendErrorMessage("Points are not set.");
             }

             if (args.Parameters.Count == 2 && args.Parameters[0].ToLower() == "delete")
             {
                 string regionName = String.Join(" ", args.Parameters.GetRange(1, args.Parameters.Count - 1));
                 foreach (JailRegion region in JailRegions)
                 {
                     if (region.Name == regionName)
                     {
                         JailRegions.Remove(region);
                     }
                 }
                 deleteJail(regionName);
                 args.Player.SendSuccessMessage("Successfully deleted JailRegion: \"{0}\"", regionName);
             }
        }

        private void KillzoneReg(CommandArgs args)
        {
            if (args.Parameters.Count == 2 && args.Parameters[0].ToLower() == "add")
            {
                if (!args.Player.TempPoints.Any(p => p == Point.Zero))
                {
                    string regionName = String.Join(" ", args.Parameters.GetRange(1, args.Parameters.Count - 1));
                    var X = Math.Min(args.Player.TempPoints[0].X, args.Player.TempPoints[1].X);
                    var Y = Math.Min(args.Player.TempPoints[0].Y, args.Player.TempPoints[1].Y);
                    var Width = Math.Abs(args.Player.TempPoints[0].X - args.Player.TempPoints[1].X);
                    var Height = Math.Abs(args.Player.TempPoints[0].Y - args.Player.TempPoints[1].Y);
                    KillRegs.Add(new KillzoneRegion(regionName, new List<int>(), X, Y, (Width + X), (Height + Y)));
                    addKillzone(regionName);
                    args.Player.SendSuccessMessage("Successfully set new Killzone: \"{0}\"", regionName);
                }
                else
                    args.Player.SendErrorMessage("Points are not set.");
            }

            if (args.Parameters.Count == 2 && args.Parameters[0].ToLower() == "delete")
            {

            }
        }
        #endregion

        #region Database Methods
        private void SetupDb()
        {
            switch (TShock.Config.StorageType.ToLower())
            {
                case "mysql":
                    string[] dbHost = TShock.Config.MySqlHost.Split(':');
                    db = new MySqlConnection()
                    {
                        ConnectionString = string.Format("Server={0}; Port={1}; Database={2}; Uid={3}; Pwd={4};",
                            dbHost[0],
                            dbHost.Length == 1 ? "3306" : dbHost[1],
                            TShock.Config.MySqlDbName,
                            TShock.Config.MySqlUsername,
                            TShock.Config.MySqlPassword)

                    };
                    break;

                case "sqlite":
                    string sql = Path.Combine(TShock.SavePath, "tshock.sqlite");
                    db = new SqliteConnection(string.Format("uri=file://{0},Version=3", sql));
                    break;

            }

            SqlTableCreator sqlcreator = new SqlTableCreator(db, db.GetSqlType() == SqlType.Sqlite ? (IQueryBuilder)new SqliteQueryCreator() : new MysqlQueryCreator());

            sqlcreator.EnsureTableStructure(new SqlTable("Killzones",
                new SqlColumn("RegionName", MySqlDbType.Text) { Primary = true },
                new SqlColumn("AllowedIDs", MySqlDbType.Text),
                new SqlColumn("X1", MySqlDbType.Int32),
                new SqlColumn("Y1", MySqlDbType.Int32),
                new SqlColumn("X2", MySqlDbType.Int32),
                new SqlColumn("Y2", MySqlDbType.Int32)));

            sqlcreator.EnsureTableStructure(new SqlTable("Jails",
                new SqlColumn("RegionName", MySqlDbType.Text) { Primary = true },
                new SqlColumn("X1", MySqlDbType.Int32),
                new SqlColumn("Y1", MySqlDbType.Int32),
                new SqlColumn("X2", MySqlDbType.Int32),
                new SqlColumn("Y2", MySqlDbType.Int32)));
        }

        private void loadJails()
        {
            using (QueryResult reader = db.QueryReader("SELECT * FROM Jails"))
            {
                while (reader.Read())
                {
                    string name = reader.Get<string>("RegionName");
                    JailRegion region = GetRegionByName(name);
                    int x1 = reader.Get<int>("X1");
                    int y1 = reader.Get<int>("Y1");
                    int x2 = reader.Get<int>("X2");
                    int y2 = reader.Get<int>("Y2");
                    JailRegions.Add(new JailRegion(name, x1, y1, x2, y2));
                }
            }
        }

        private void loadKillzones()
        {
            using (QueryResult reader = db.QueryReader("SELECT * FROM Killzones"))
            {
                while (reader.Read())
                {
                    string name = reader.Get<string>("RegionName");
                    List<int> Ids = new List<int>();
                    Ids = GetAllowedIDs(name);
                    int x1 = reader.Get<int>("X1");
                    int y1 = reader.Get<int>("Y1");
                    int x2 = reader.Get<int>("X2");
                    int y2 = reader.Get<int>("Y2");
                    KillRegs.Add(new KillzoneRegion(name, Ids, x1, y1, x2, y2));
                }
            }
        }

        private List<int> GetAllowedIDs(string name)
        {
            List<int> Ids = new List<int>();

            using (QueryResult reader = db.QueryReader("SELECT * FROM Killzones WHERE RegionName=@0;", name))
            {
                while (reader.Read())
                {
                    string[] ids = reader.Get<string>("AllowedIDs").Split(',');
                    int IDs = Convert.ToInt32(ids);

                    for (int i = 0; i < ids.Length; i++)
                    {
                        if (ids[i].Length > 0)
                        {
                            Ids.Add(IDs);
                        }
                    }
                }
            }
            return Ids;
        }

        private void addJail(string name)
        {
            foreach (JailRegion region in JailRegions)
            {
                db.Query("INSERT INTO Jails (RegionName, X1, Y1, X2, Y2) VALUES (@0, @1, @2, @3, @4);", region.Name, region.Area.X, region.Area.Y, (region.Area.X + region.Area.Width), (region.Area.Y + region.Area.Height));
            }
        }

        private void deleteJail(string name)
        {
            foreach (JailRegion region in JailRegions)
            {
                db.Query("DELETE FROM Jails WHERE RegionName=@0;", region.Name);
            }
        }

        private void addKillzone(string name)
        {
            foreach (KillzoneRegion reg in KillRegs)
            {
                db.Query("INSERT INTO Killzones (RegionName, AllowedIDs, X1, Y1, X2, Y2); VALUES (@0, @1, @2, @3, @4, @5, @6);", reg.Name, reg.AllowedIDs, reg.Area.X, reg.Area.Y, (reg.Area.X + reg.Area.Width), (reg.Area.Y + reg.Area.Height));
            }
        }

        private void deleteKillzone(string name)
        {
            foreach (KillzoneRegion reg in KillRegs)
            {
                db.Query("DELETE FROM Killzones WHERE RegionName=@0;", reg.Name);
            }
        }
        #endregion

        #region GetRegionMethods
        private JailRegion GetRegionByName(string name)
        {
            foreach (JailRegion reg in JailRegions)
            {
                if (reg.Name == name)
                {
                    return reg;
                }
            }
            return null;
        }

        private KillzoneRegion GetKillzoneByName(string name)
        {
            foreach (KillzoneRegion reg in KillRegs)
            {
                if (reg.Name == name)
                {
                    return reg;
                }
            }
            return null;
        }
        #endregion
    }
}

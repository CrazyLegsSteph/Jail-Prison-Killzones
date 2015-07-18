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
    [ApiVersion(1, 19)]
    public class PluginMain : TerrariaPlugin
    {
        public static string configPath = Path.Combine(TShock.SavePath, "JailPrison.json");
        private Config config = new Config();

        private IDbConnection db;

        public static List<string> Prisoners = new List<string>();

        private List<Region> Killzones = new List<Region>();

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
            get { return new Version(1, 1, 0, 0); }
        }

        public PluginMain(Main game) : base(game)
        {
            base.Order = 1;
        }

        #region Initialize/Dispose
        public override void Initialize()
        {
            ServerApi.Hooks.GameInitialize.Register(this, OnInitialize);
            ServerApi.Hooks.GamePostInitialize.Register(this, OnPostInit, -1);
            ServerApi.Hooks.ServerChat.Register(this, OnChat);
            TShockAPI.Hooks.RegionHooks.RegionEntered += OnRegionEnter;
            TShockAPI.Hooks.RegionHooks.RegionLeft += OnRegionLeft;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ServerApi.Hooks.GameInitialize.Deregister(this, OnInitialize);
                ServerApi.Hooks.GamePostInitialize.Deregister(this, OnPostInit);
                ServerApi.Hooks.ServerChat.Deregister(this, OnChat);
                TShockAPI.Hooks.RegionHooks.RegionEntered -= OnRegionEnter;
                TShockAPI.Hooks.RegionHooks.RegionLeft -= OnRegionLeft;
            }
            base.Dispose(disposing);
        }
        #endregion

        #region Hooks
        private void OnInitialize(EventArgs args)
        {
            SetupDb();
            (config = config.Read(configPath)).Write(configPath);

            Commands.ChatCommands.Add(new Command("jp.imprison", Imprison, "imprison"));
            Commands.ChatCommands.Add(new Command("jp.imprison", SetFree, "setfree"));
            Commands.ChatCommands.Add(new Command("jp.reload", JPReload, "jpreload"));
            Commands.ChatCommands.Add(new Command("jp.killzones.set", KillzoneReg, "killzone", "kz", "killreg", "kr"));
        }

        private void OnPostInit(EventArgs args)
        {
            loadKillzones();
        }

        private void OnChat(ServerChatEventArgs args)
        {
            foreach (string cmd in config.unAllowedCommandsWhileImprisoned)
            {
                if (args.Text.StartsWith("/{0}".SFormat(cmd)) && Prisoners.Contains(TShock.Players[args.Who].IP))
                {
                    TShock.Players[args.Who].SendErrorMessage("You cannot use this command while imprisoned.");
                    args.Handled = true;
                }
            }
        }

        private void OnRegionEnter(TShockAPI.Hooks.RegionHooks.RegionEnteredEventArgs args)
        {
            if (Killzones.Contains(args.Region) && !args.Player.Group.HasPermission("jp.killzones.bypass"))
            {
                args.Player.DamagePlayer(15000);
                args.Player.SendErrorMessage("You stumbled into a trap...");
                TSPlayer.All.SendErrorMessage("{0} entered the killzone.", args.Player.Name);
            }
        }

        private void OnRegionLeft(TShockAPI.Hooks.RegionHooks.RegionLeftEventArgs args)
        {
            foreach (TSPlayer tsplr in TShock.Players)
            {
                if (tsplr == null)
                    continue;

                if (Prisoners.Contains(tsplr.IP))
                {
                    var warp = TShock.Warps.Find(config.jailWarp);
                    if (warp != null)
                    {
                        tsplr.Teleport(warp.Position.X * 16, warp.Position.Y * 16);
                        tsplr.SendErrorMessage("You Are Stuck In Prison... An Admin Or Mod Will Need To Let You Out...");
                    }
                }
            }
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
            var region = TShock.Regions.GetRegionByName(config.jailRegion);

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
                    players[0].Teleport(warp.Position.X * 16, warp.Position.Y * 16);
                    args.Player.SendInfoMessage("You warped {0} to prison!", players[0].Name);
                    players[0].SendInfoMessage("{0} Warped you to the Prison! You Cannot Get Out Until An Admin Releases You", args.Player.User.Name);
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
                players[0].Spawn();
                args.Player.SendInfoMessage("You warped {0} to Spawn from Prison!", players[0].Name);
                players[0].SendSuccessMessage("{0} Warped You To Spawn From Prison! Now Behave!!!!!", args.Player.User.Name);
            }
        }

        private void JPReload(CommandArgs args)
        {
            (config = config.Read(configPath)).Write(configPath);
            args.Player.SendSuccessMessage("Reloaded Jail/Prison config.");
        }

        private void KillzoneReg(CommandArgs args)
        {
            if (args.Parameters.Count < 1)
            {
                args.Player.SendErrorMessage("Invalid syntax! Proper syntax:");
                args.Player.SendErrorMessage("{0}killzone add|remove|reload|list [region]", TShock.Config.CommandSpecifier);
                return;
            }

            switch (args.Parameters[0].ToLower())
            {
                case "add":
                    {
                        var region = TShock.Regions.GetRegionByName(args.Parameters[1]);

                        if (region == null)
                            args.Player.SendErrorMessage("No such region defined.");
                        else
                        {
                            addKillzone(region.Name);
                            args.Player.SendSuccessMessage("Successfully set killzone '{0}'", region.Name);
                        }
                    }
                    return;
                case "rem":
                case "del":
                case "remove":
                case "delete":
                    {
                        var region = TShock.Regions.GetRegionByName(args.Parameters[1]);

                        if (region == null || !Killzones.Contains(region))
                            args.Player.SendErrorMessage("Region doesn't exist / Not a killzone.");
                        else
                        {
                            deleteKillzone(region.Name);
                            args.Player.SendSuccessMessage("Successfully removed killzone '{0}'", region.Name);
                        }
                    }
                    return;
                case "reload":
                    {
                        loadKillzones();
                        args.Player.SendSuccessMessage("Killzones loaded.");
                    }
                    return;
                case "list":
                    {
                        var list = from k in listKillzones() select k;

                        int pageNum;

                        if (!PaginationTools.TryParsePageNumber(args.Parameters, 1, args.Player, out pageNum))
                            return;

                        PaginationTools.SendPage(args.Player, pageNum, PaginationTools.BuildLinesFromTerms(list),
                            new PaginationTools.Settings 
                            {
                                HeaderFormat = "Killzones ({0}/{1})",
                                FooterFormat = "Type {0}killzone list {{0}} for more".SFormat(TShock.Config.CommandSpecifier),
                                NothingToDisplayString = "There are no killzones defined."
                            });
                    }
                    return;
                case "help":
                    {
                        args.Player.SendInfoMessage("Available subcommands:");
                        args.Player.SendInfoMessage("Add <region>: Makes an existing region a killzone.");
                        args.Player.SendInfoMessage("Remove <region>: Removes a killzone.");
                        args.Player.SendInfoMessage("Reload: Reloads all killzones.");
                        args.Player.SendInfoMessage("List: Lists all killzones.");
                    }
                    return;
                default:
                    {
                        args.Player.SendErrorMessage("Invalid subcommand. Type {0}killzone help for a list of valid commands.", TShock.Config.CommandSpecifier);
                    }
                    return;
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
                new SqlColumn("ID", MySqlDbType.Int32) { Primary = true, AutoIncrement = true },
                new SqlColumn("RegionName", MySqlDbType.Text) { Unique = true }));
        }

        private void loadKillzones()
        {
            Killzones.Clear();

            using (QueryResult reader = db.QueryReader("SELECT * FROM Killzones"))
            {
                while (reader.Read())
                {
                    var region = TShock.Regions.GetRegionByName(reader.Get<string>("RegionName"));
                    Killzones.Add(region);
                }
            }
        }

        private void addKillzone(string name)
        {
            var region = TShock.Regions.GetRegionByName(name);
            Killzones.Add(region);
            db.Query("INSERT INTO Killzones (RegionName) VALUES (@0);", name);
        }

        private void deleteKillzone(string name)
        {
            var region = TShock.Regions.GetRegionByName(name);
            Killzones.Remove(region);
            db.Query("DELETE FROM Killzones WHERE RegionName=@0;", name);
        }


        private List<string> listKillzones()
        {
            List<string> killzones = new List<string>();

            using (QueryResult reader = db.QueryReader("SELECT * FROM Killzones"))
            {
                while (reader.Read())
                {
                    var region = TShock.Regions.GetRegionByName(reader.Get<string>("RegionName"));
                    killzones.Add(region.Name);
                }
            }
            return killzones;
        }
        #endregion
    }
}

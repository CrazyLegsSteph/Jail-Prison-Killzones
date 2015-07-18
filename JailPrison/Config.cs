using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Newtonsoft.Json;

namespace JailPrison
{
    public class Config
    {
        public string jailWarp = "prison";
        public string jailRegion = "prison";
        public List<string> unAllowedCommandsWhileImprisoned = new List<string>() { "tp", "home", "swap", "spawn", "warp" };

        public void Write(string path)
        {
            File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
        }

        public Config Read(string path)
        {
            return !File.Exists(path)
                ? new Config()
                : JsonConvert.DeserializeObject<Config>(File.ReadAllText(path));
        }
    }
}

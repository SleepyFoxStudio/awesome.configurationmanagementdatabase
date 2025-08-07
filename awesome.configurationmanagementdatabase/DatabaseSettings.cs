using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace awesome.configurationmanagementdatabase
{
    public class DatabaseSettings
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public DatabaseType DatabaseType { get; set; }
        public string DatabaseUsername { get; set; }
        public string DatabasePassword { get; set; }
        public string DatabaseHost { get; set; }
        public string DatabaseName { get; set; }
        public string DatabasePort { get; set; }
        public string ConnectionTimeout { get; set; } = "120";
        public string CommandTimeout { get; set; } = "480";
        public string WhatsMyIpWebUrl { get; set; } = "https://ipinfo.io/ip";

    }

    public enum DatabaseType { MySql };

}

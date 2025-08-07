using System.Collections.Generic;

namespace awesome.configurationmanagementdatabase
{
    public class DatacentreSettings
    {
        public string Type { get; set; }

        public string DatacentreName { get; set; }

        public Dictionary<string, string> Credentials { get; } = new Dictionary<string, string>();
    }
}
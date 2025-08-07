using System;
using System.Collections.Generic;
using Amazon.Runtime.Internal;

namespace awesome.configurationmanagementdatabase
{
    public class ServerDetails : AccountSummary
    {
        public string Name { get; set; }
        public string Id { get; set; }
        public string Flavour { get; set; }
        public double Ram { get; set; }
        public int Cpu { get; set; }
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();
        public DateTime? Updated { get; set; } = null;
        public DateTime? Created { get; set; } = null;
        public DateTime? Terminated { get; set; } = null;
        public DateTime? Deleted { get; set; } = null;
        public DateTime? StoppedDate { get; set; }
        public bool IsDirty { get; set; }
        public string CreatorEmail { get; set; }
        public string ImageName { get; set; }
        public string ImageId { get; set; }
        public string PlatformName { get; set; } = "NA";
        public string PlatformType { get; set; } = "NA";
        public string PlatformVersion { get; set; } = "NA";
        public string PlatformLookupMethod { get; set; }
        public string Status { get; set; }
        public List<IpV4Network> Ipv4Networks { get; set; } = new List<IpV4Network>();
        public List<VolumeDetail> Volumes { get; set; } = new List<VolumeDetail>();
        public string AvailabilityZone { get; set; }

        // Set by core code, not returned from GetServers
        public string PageName { get; set; }
        public string DataCentreType { get; set; }
        public bool? StoppedFor30Days { get; set; } = null;
        public bool? StoppedFor90Days { get; set; } = null;
    }

    public class VolumeDetail
    {
        public string Id { get; set; }
        public string Label { get; set; }
        public int? Size { get; set; }
        public string Type { get; set; }
        public DateTime? Created { get; set; }
        public int? Iops { get; set; }
        public Dictionary<string, string> Tags { get; set; }
    }

    public class IpV4Network
    {
        public string Name { get; set; }
        public string IpAddress { get; set; }
    }
}
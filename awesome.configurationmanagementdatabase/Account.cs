using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace awesome.configurationmanagementdatabase
{
    public class Account
    {
        public string DataCentreType { get; set; }
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();
        public string AccountName { get; set; }
        public string AccountId { get; set; }
        public List<ServerGroup> ServerGroups { get; set; } = new List<ServerGroup>();
        public List<CloudUser> Users { get; set; } = new List<CloudUser>();
        public List<CloudVolume> Volumes { get; set; } = new List<CloudVolume>();
        public List<CloudDatabase> Databases { get; set; } = new List<CloudDatabase>();
        public List<ApiGatewayV2Api> ApiGatewayV2Apis { get; set; } = new List<ApiGatewayV2Api>();
        public List<ApiGatewayRestApi> ApiGatewayRestApis { get; set; } = new List<ApiGatewayRestApi>();
        public List<LambdaFunction> LambdaFunctions { get; set; } = new List<LambdaFunction>();
        public List<DynamoDatabase> DynamoDatabases { get; set; } = new List<DynamoDatabase>();
        public List<EcsContainerInstance> EcsContainerInstances { get; set; } = new List<EcsContainerInstance>();
    }

    public class DynamoDatabase : AccountSummary
    {
        public string TableName { get; set; }
    }

    public class LambdaFunction : AccountSummary
    {
        public string FunctionName { get; set; }
    }

    public class ApiGatewayRestApi : AccountSummary
    {
        public string ApiName { get; set; }
    }

    public class ApiGatewayV2Api : AccountSummary
    {
        public string ApiName { get; set; }
    }

    public class CloudDatabase : AccountSummary
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Engine { get; set; }
        public string Version { get; set; }
        public string AvailabilityZone { get; set; }
        public string InstanceType { get; set; }
        public DateTime? CertificateAuthorityExpirationDate { get; set; } = null;
        public DateTime? CertificateExpirationDate { get; set; } = null;
        public string CertificateAuthority { get; set; }
        public bool? CertificateExpiration90DayWarning { get; set; }
        public bool? Encrypted { get; set; } = null;
        public Dictionary<string, string> Tags { get;  set; } = new Dictionary<string, string>();
        public int? MaxAllocatedStorage { get; set; }
        public int? AllocatedStorage { get; set; }
        public double? FreeStorageSpace { get; set; }
    }

    public class ServerGroup
    {
        public string AccountId { get; set; }
        public string GroupName { get; set; }
        public string GroupId { get; set; }
        public string Region { get; set; }
        public List<ServerDetails> Servers { get; set; } = new List<ServerDetails>();
    }

    public class CloudUser : AccountSummary
    {
        public string Id { get; set; }
        public string User { get; set; }
        public string Email { get; set; }
        public bool? ConsoleAccess { get; set; } = null;
        public DateTime? CreateDate { get; set; } = null;
        public DateTime? UpdateDate { get; set; } = null;
        public DateTime? PasswordLastUsed { get; set; } = null;
    }
    public class CloudVolume : AccountSummary
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public bool? Encrypted { get; set; } = null;
    }


}
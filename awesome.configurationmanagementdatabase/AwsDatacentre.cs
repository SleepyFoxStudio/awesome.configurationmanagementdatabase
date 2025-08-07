using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.EC2;
using Amazon.EC2.Model;
using Amazon.IdentityManagement;
using Amazon.IdentityManagement.Model;
using Amazon.Organizations;
using Amazon.Pricing;
using Amazon.Pricing.Model;
using Amazon.RDS;
using Amazon.RDS.Model;
using Amazon.Runtime;
using Amazon.SecurityToken;
using Amazon.SecurityToken.Model;
using Newtonsoft.Json.Linq;
using Filter = Amazon.Pricing.Model.Filter;
using Amazon.APIGateway;
using Amazon.APIGateway.Model;
using Amazon.ApiGatewayV2;
using Amazon.ApiGatewayV2.Model;
using Amazon.CloudWatch;
using Amazon.CloudWatch.Model;
using Amazon.Lambda;
using Amazon.Lambda.Model;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.ECS;
using Amazon.ECS.Model;
using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using ConsoleDump;
using Task = System.Threading.Tasks.Task;
using Volume = Amazon.EC2.Model.Volume;
using Org.BouncyCastle.Utilities;


namespace awesome.configurationmanagementdatabase
{
    public class AwsDatacentre : IDatacentre
    {
        private readonly AWSCredentials _awsCreds;
        private readonly AWSCredentials _awsOrgCreds;
        private readonly string _cloudType = "AWS";

        public AwsDatacentre(string accessKeyId, string secretKey, string sessionToken = null, string orgAccessKeyId = null, string orgSecretKey = null)
        {
            if (sessionToken != null)
            {
                _awsCreds = new SessionAWSCredentials(accessKeyId, secretKey, sessionToken);
            }
            else
            {
                _awsCreds = new BasicAWSCredentials(accessKeyId, secretKey);
            }
            if (orgAccessKeyId != null && orgSecretKey != null)
            {
                _awsOrgCreds = new BasicAWSCredentials(orgAccessKeyId, orgSecretKey); ;
            }
            else
            {
                _awsOrgCreds = _awsCreds;
            }
        }

        public async Task<Account> GetAccountAsync()
        {

            var client = new AmazonEC2Client(_awsCreds, RegionEndpoint.EUWest1);
            var orgClient = new AmazonOrganizationsClient(_awsOrgCreds, RegionEndpoint.EUWest1);
            var account = new Account();

            var regionRequest = new DescribeRegionsRequest();
            var regionresponse = await client.DescribeRegionsAsync(regionRequest, CancellationToken.None);


            var stsClient = new AmazonSecurityTokenServiceClient(_awsCreds, RegionEndpoint.EUWest1);
            var getCallerIdentityResponse = await stsClient.GetCallerIdentityAsync(new GetCallerIdentityRequest());


            var iamClient = new AmazonIdentityManagementServiceClient(_awsCreds, RegionEndpoint.EUWest1);
            var accountAliases = await iamClient.ListAccountAliasesAsync(new ListAccountAliasesRequest());


            var accountName = accountAliases.AccountAliases.SingleOrDefault();
            if (string.IsNullOrEmpty(accountName))
            {
                accountName = getCallerIdentityResponse.Account;
            }

            if (!accountName.EndsWith($"-{getCallerIdentityResponse.Account}"))
            {
                accountName = accountName + "-" + getCallerIdentityResponse.Account;
            }
            account = (new Account
            {
                AccountName = accountName,
                AccountId = getCallerIdentityResponse.Account,
                DataCentreType = "Aws",
                ServerGroups = new List<ServerGroup>(),
                EcsContainerInstances = await GetEcsContainerInstancesAsync(regionresponse.Regions, getCallerIdentityResponse.Account).ConfigureAwait(false),
                Users = await GetUsers(getCallerIdentityResponse.Account).ConfigureAwait(false),
                Databases = await GetDatabasesAsync(regionresponse.Regions, getCallerIdentityResponse.Account).ConfigureAwait(false),
                ApiGatewayRestApis = await GetApiGatewayRestApisAsync(regionresponse.Regions, getCallerIdentityResponse.Account).ConfigureAwait(false),
                ApiGatewayV2Apis = await GetApiGatewayV2ApisAsync(regionresponse.Regions, getCallerIdentityResponse.Account).ConfigureAwait(false),
                LambdaFunctions = await GetLambdaFunctionsAsync(regionresponse.Regions, getCallerIdentityResponse.Account).ConfigureAwait(false),
                DynamoDatabases = await GetDynamoDatabasesAsync(regionresponse.Regions, getCallerIdentityResponse.Account).ConfigureAwait(false),
                Tags = await GetTagsForAccount(getCallerIdentityResponse.Account, orgClient),
                Volumes = await GetCloudVolumes(regionresponse.Regions, getCallerIdentityResponse.Account),
            });




            foreach (var region in regionresponse.Regions)
            {
                var servers = new List<ServerDetails>();
                string nextToken = null;
                while (true)
                {
                    List<Volume> allVolumes = new List<Volume>();
                    List<Image> allImages = new List<Image>();
                    var request = new DescribeInstancesRequest
                    {
                        NextToken = nextToken
                    };
                    var instanceClient = new AmazonEC2Client(_awsCreds, RegionEndpoint.GetBySystemName(region.RegionName));
                    var ssmClient = new AmazonSimpleSystemsManagementClient(_awsCreds, RegionEndpoint.GetBySystemName(region.RegionName));
                    var instanceInfoRequest = new DescribeInstanceInformationRequest
                    {
                        MaxResults = 10
                    };
                    var allInstanceInfo = new List<InstanceInformation>();
                    while (true)
                    {
                        var instanceInfoResponse = await ssmClient.DescribeInstanceInformationAsync(instanceInfoRequest);
                        allInstanceInfo.AddRange(instanceInfoResponse.InstanceInformationList);
                        if (instanceInfoResponse.NextToken == null)
                        {
                            break;
                        }
                        instanceInfoRequest.NextToken = instanceInfoResponse.NextToken;
                    }


                    var response = await instanceClient.DescribeInstancesAsync(request, CancellationToken.None).ConfigureAwait(false);

                    if (response.Reservations == null)
                    {
                        break;
                    }
                    if (response.Reservations.Count > 0)
                    {
                        allVolumes = await GetAllVolumesAsync(region).ConfigureAwait(false);
                        allImages = await GetAllImagesAsync(region, response.Reservations.SelectMany(r => r.Instances).Select(i => i.ImageId).ToList()).ConfigureAwait(false);
                    }

                    foreach (var item in response.Reservations)
                    {
                        foreach (var server in item.Instances)
                        {
                            var metadata = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);
                            if (item.Instances[0].Tags.Count > 0)
                            {
                                foreach (var tag in item.Instances[0].Tags)
                                {
                                    metadata[tag.Key] = tag.Value;
                                }
                            }

                            bool? stoppedFor30Days = false;
                            bool? stoppedFor90Days = false;

                            if (server.State.Name == InstanceStateName.Stopped)
                            {
                                stoppedFor30Days = null;
                                stoppedFor90Days = null;
                            }

                            DateTime? stoppedSince = null;

                            var stateReason = server.StateTransitionReason;
                            if (stateReason != null)
                            {

                                // Regular expression to match date and time pattern
                                var regex = new Regex(@"\((\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}) GMT\)");
                                var match = regex.Match(stateReason);

                                if (match.Success && server.State.Name == InstanceStateName.Stopped)
                                {
                                    var dateTimeString = match.Groups[1].Value;
                                    if (DateTime.TryParseExact(dateTimeString, "yyyy-MM-dd HH:mm:ss",
                                            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal,
                                            out DateTime transitionDateTime))
                                    {
                                        Console.WriteLine($"Parsed DateTime: {transitionDateTime}");
                                        stoppedFor30Days = transitionDateTime < DateTime.UtcNow.AddDays(-30);
                                        stoppedFor90Days = transitionDateTime < DateTime.UtcNow.AddDays(-90);
                                        stoppedSince = transitionDateTime;
                                    }
                                }
                            }


                            //if (server.State.Name == InstanceStateName.Stopped && stoppedSince == null)
                            //{
                            //    Console.WriteLine("Failed to check how long server was stopped, based on state change reason text. Time to check Cloud Trail");
                            //    var cloudTrailClient = new AmazonCloudTrailClient(_awsCreds, RegionEndpoint.GetBySystemName(region.RegionName));
                            //    var cloudEvents = await cloudTrailClient.LookupEventsAsync(new LookupEventsRequest
                            //    {
                            //        LookupAttributes = new List<LookupAttribute>
                            //        {
                            //            new LookupAttribute
                            //            {
                            //                AttributeKey = "EventName",
                            //                AttributeValue = "StopInstances"
                            //            }
                            //        }
                            //    });
                            //    cloudEvents.Dump();
                            //}
                            var platformName = "";
                            var platformVersion = "";
                            var platformType = "";
                            var platformLookupMethod = "";
                            var ssmInfo = allInstanceInfo.SingleOrDefault(s => s.InstanceId == server.InstanceId);
                            if (ssmInfo != null)
                            {
                                platformName = ssmInfo.PlatformName;
                                platformVersion = ssmInfo.PlatformVersion;
                                platformType = ssmInfo.PlatformType.Value;
                                platformLookupMethod = "SSM";
                            }
                            //else
                            //{
                            //    var ssmNoSet = server.Tags.Any(s =>
                            //        s.Key.Equals("SSM", StringComparison.InvariantCultureIgnoreCase) &&
                            //        s.Value.Equals("No", StringComparison.InvariantCultureIgnoreCase));
                            //    Console.WriteLine($"No SSM Info for {server.InstanceId}, SSM No set {ssmNoSet}");

                            //    if (!ssmNoSet && server.State.Name.Value.Equals("running"))
                            //    {
                            //        //foreach (var VARIABLE in allInstanceInfo)
                            //        //{
                            //        //    Console.WriteLine(
                            //        //        $"{VARIABLE.InstanceId} {VARIABLE.PlatformName} {VARIABLE.ActivationId}");
                            //        //}


                            //        //Console.WriteLine("============================================================");
                            //        Console.WriteLine($"No SSM association for {server.InstanceId}, SSM No set {ssmNoSet}");

                            //    }
                            //}

                            servers.Add(new ServerDetails
                            {
                                Name = server.Tags.FirstOrDefault(k => k.Key.Equals("name", StringComparison.InvariantCultureIgnoreCase))?.Value,
                                Id = server.InstanceId,
                                Tags = metadata,
                                Updated = null,
                                Created = server.LaunchTime,
                                Flavour = server.InstanceType.Value,
                                Cpu = server.CpuOptions.CoreCount ?? 0 * server.CpuOptions.ThreadsPerCore ?? 0,
                                Volumes = GetVolumes(server.BlockDeviceMappings, allVolumes),
                                Status = server.State.Name,
                                Ipv4Networks = GetNetworks(server),
                                ImageName = allImages.SingleOrDefault(i => i.ImageId == server.ImageId)?.Description ?? "Removed Image",
                                ImageId = server.ImageId,
                                PlatformName = platformName,
                                PlatformVersion = platformVersion,
                                PlatformType = platformType,
                                PlatformLookupMethod = platformLookupMethod,
                                AvailabilityZone = server.Placement.AvailabilityZone,
                                DataCentreType = account.DataCentreType,
                                StoppedFor30Days = stoppedFor30Days,
                                StoppedFor90Days = stoppedFor90Days,
                                StoppedDate = stoppedSince,
                                AccountId = account.AccountId,
                                Region = region.RegionName,
                                CloudType = _cloudType
                            });
                        }


                    }
                    if (response.NextToken == null)
                    {
                        break;
                    }
                    nextToken = response.NextToken;
                }



                Console.WriteLine($"{region.RegionName} contains {servers.Count} servers");

                if (servers.Count > 0)
                {
                    account.ServerGroups.Add(new ServerGroup()
                    {
                        GroupName = $"{accountName} {region.RegionName}",
                        Region = region.RegionName,
                        Servers = servers,
                        GroupId = region.RegionName,
                        AccountId = getCallerIdentityResponse.Account
                    });
                }
            }


            var allInstanceTypes = account.ServerGroups.SelectMany(a => a.Servers).Select(s => s.Flavour).Distinct();

            var priceListClient = new AmazonPricingClient(_awsCreds, RegionEndpoint.USEast1);
            var getInstanceTypeTasks = allInstanceTypes.Select(t => priceListClient.GetProductsAsync(new GetProductsRequest
            {
                ServiceCode = "AmazonEC2",
                Filters = new List<Filter>
                {
                    new Filter
                    {
                        Type = FilterType.TERM_MATCH,
                        Field = "instanceType",
                        Value = t
                    }
                },
                // We only want the first result, as there are many many pricing options for a given instanceType, 
                // and we only want memory and vCPUs, which are the same for all options.
                MaxResults = 1
            }));

            var instanceTypeResponses = await Task.WhenAll(getInstanceTypeTasks).ConfigureAwait(false);

            var instanceTypeLookup = instanceTypeResponses
                .Select(r => JObject.Parse(r.PriceList[0])["product"]["attributes"])
                .Select(j => (memory: j["memory"].Value<string>(), vcpu: j["vcpu"].Value<string>(), instanceType: j["instanceType"].Value<string>()))
                .ToDictionary(t => t.instanceType);


            foreach (var server in account.ServerGroups.SelectMany(a => a.Servers))
            {

                if (instanceTypeLookup.TryGetValue(server.Flavour, out var t))
                {
                    server.Cpu = int.Parse(t.vcpu);
                    server.Ram = ByteSize.Parse(t.memory).GigaBytes;
                }
            }


            return account;
        }

        private async Task<List<CloudVolume>> GetCloudVolumes(List<Region> regions, string account)
        {
            var volumes = new List<CloudVolume>();
            foreach (var region in regions)
            {
                var regionVolumes = await GetAllVolumesAsync(region);
                foreach (var regionVolume in regionVolumes)
                {
                    volumes.Add(new CloudVolume
                    {
                        Id = regionVolume.VolumeId,
                        Name = regionVolume.Tags.FirstOrDefault(s => s.Key.Equals("name", StringComparison.InvariantCultureIgnoreCase))?.Value,
                        Type = regionVolume.VolumeType,
                        Encrypted = regionVolume.Encrypted,
                        AccountId = account,
                        Region = region.RegionName,
                        CloudType = _cloudType
                    });
                }
            }

            return volumes;
        }

        private async Task<Dictionary<string, string>> GetTagsForAccount(string accountId, AmazonOrganizationsClient client)
        {
            var output = new Dictionary<string, string>();

            var request = new Amazon.Organizations.Model.ListTagsForResourceRequest
            {
                ResourceId = accountId
            };
            try
            {
                var accountDetails = await client.ListTagsForResourceAsync(request);
                foreach (var tag in accountDetails.Tags)
                {
                    output.Add(tag.Key, tag.Value);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"There was an error getting tags for account {accountId} '{e.Message}'");
            }

            return output;
        }

        private async Task<List<EcsContainerInstance>> GetEcsContainerInstancesAsync(List<Region> regions,
            string account)
        {
            var result = new List<EcsContainerInstance>();

            foreach (var region in regions)
            {
                var itemRegionCount = 0;
                List<string> clusterArns = await GetEcsClustersAsync(region);
                foreach (var cluster in clusterArns)
                {

                    string nextToken = null;
                    while (true)
                    {
                        var request = new ListContainerInstancesRequest
                        {
                            NextToken = nextToken,
                            Cluster = cluster
                        };
                        var amazonEcsClient =
                            new AmazonECSClient(_awsCreds, RegionEndpoint.GetBySystemName(region.RegionName));
                        var response = await amazonEcsClient
                            .ListContainerInstancesAsync(request, CancellationToken.None).ConfigureAwait(false);


                        foreach (var item in response.ContainerInstanceArns)
                        {
                            result.Add(new EcsContainerInstance
                            {
                                ContainerInstanceArn = item,
                                AccountId = account,
                                Region = region.RegionName,
                                CloudType = _cloudType
                            });
                            itemRegionCount++;
                        }

                        if (response.NextToken == null)
                        {
                            break;
                        }

                        nextToken = response.NextToken;
                    }



                }
                Console.WriteLine($"{region.RegionName} contains {itemRegionCount} Ecs Container Instances");

            }

            return result;


        }

        private async Task<List<string>> GetEcsClustersAsync(Region region)
        {
            var result = new List<string>();


            var itemRegionCount = 0;

            string nextToken = null;
            while (true)
            {
                var request = new ListClustersRequest
                {
                    NextToken = nextToken
                };
                var amazonEcsClient = new AmazonECSClient(_awsCreds, RegionEndpoint.GetBySystemName(region.RegionName));
                var response = await amazonEcsClient.ListClustersAsync(request, CancellationToken.None).ConfigureAwait(false);


                result.AddRange(response.ClusterArns);

                itemRegionCount += response.ClusterArns.Count;
                if (response.NextToken == null)
                {
                    break;
                }
                nextToken = response.NextToken;
            }

            Console.WriteLine($"{region.RegionName} contains {itemRegionCount} Ecs Clusters");

            return result;
        }

        private async Task<List<DynamoDatabase>> GetDynamoDatabasesAsync(List<Region> regions, string account)
        {
            var result = new List<DynamoDatabase>();


            foreach (var region in regions)
            {
                int itemRegionCount = 0;

                string nextToken = null;
                while (true)
                {
                    var request = new ListTablesRequest
                    {
                        ExclusiveStartTableName = nextToken
                    };
                    var dynamoDbClient = new AmazonDynamoDBClient(_awsCreds, RegionEndpoint.GetBySystemName(region.RegionName));
                    var response = await dynamoDbClient.ListTablesAsync(request, CancellationToken.None).ConfigureAwait(false);


                    foreach (var item in response.TableNames)
                    {
                        result.Add(new DynamoDatabase
                        {
                            TableName = item,
                            AccountId = account,
                            Region = region.RegionName,
                            CloudType = _cloudType
                        });
                        itemRegionCount++;
                    }
                    if (response.LastEvaluatedTableName == null)
                    {
                        break;
                    }
                    nextToken = response.LastEvaluatedTableName;
                }



                Console.WriteLine($"{region.RegionName} contains {itemRegionCount} dynamo DBs");
            }
            return result;

        }

        private async Task<List<LambdaFunction>> GetLambdaFunctionsAsync(List<Region> regions, string account)
        {

            var result = new List<LambdaFunction>();


            foreach (var region in regions)
            {
                int itemRegionCount = 0;

                string nextToken = null;
                while (true)
                {
                    var request = new ListFunctionsRequest
                    {
                        Marker = nextToken
                    };
                    var lambdaClient = new AmazonLambdaClient(_awsCreds, RegionEndpoint.GetBySystemName(region.RegionName));
                    var response = await lambdaClient.ListFunctionsAsync(request, CancellationToken.None).ConfigureAwait(false);


                    foreach (var item in response.Functions)
                    {
                        result.Add(new LambdaFunction
                        {
                            FunctionName = item.FunctionName,
                            AccountId = account,
                            Region = region.RegionName,
                            CloudType = _cloudType
                        });
                        itemRegionCount++;
                    }
                    if (response.NextMarker == null)
                    {
                        break;
                    }
                    nextToken = response.NextMarker;
                }



                Console.WriteLine($"{region.RegionName} contains {itemRegionCount} lambdas");
            }
            return result;

        }

        private async Task<List<ApiGatewayV2Api>> GetApiGatewayV2ApisAsync(List<Region> regions, string account)
        {
            var result = new List<ApiGatewayV2Api>();


            foreach (var region in regions)
            {
                int itemRegionCount = 0;
                string nextToken = null;
                try
                {

                    while (true)
                    {
                        var request = new GetApisRequest
                        {
                            NextToken = nextToken
                        };
                        var apiGatewayV2Client = new AmazonApiGatewayV2Client(_awsCreds, RegionEndpoint.GetBySystemName(region.RegionName));
                        var response = await apiGatewayV2Client.GetApisAsync(request, CancellationToken.None).ConfigureAwait(false);


                        foreach (var item in response.Items)
                        {
                            result.Add(new ApiGatewayV2Api
                            {
                                ApiName = item.Name,
                                AccountId = account,
                                Region = region.RegionName,
                                CloudType = _cloudType
                            });
                            itemRegionCount++;
                        }
                        if (response.NextToken == null)
                        {
                            break;
                        }
                        nextToken = response.NextToken;

                    }

                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    var message = e.Message;
                    CreateErrorFlag(account, message);
                }

                Console.WriteLine($"{region.RegionName} contains {itemRegionCount} ApiGatewayV2Apis");
            }
            return result;

        }

        private void CreateErrorFlag(string accountId, string message)
        {

            var filePath = $"error_report.txt";
            if (!System.IO.File.Exists(filePath))
            {
                System.IO.File.Create(filePath);
            }

            var statusFilePath = $"{accountId}_error_report.txt";


            using var sw = File.AppendText(statusFilePath);
            sw.WriteLine($"{message}");
        }

        private async Task<List<ApiGatewayRestApi>> GetApiGatewayRestApisAsync(List<Region> regions, string account)
        {
            var result = new List<ApiGatewayRestApi>();


            foreach (var region in regions)
            {
                int itemRegionCount = 0;
                string nextToken = null;
                while (true)
                {
                    var request = new GetRestApisRequest
                    {
                        Position = nextToken
                    };
                    var amazonApiGatewayClient = new AmazonAPIGatewayClient(_awsCreds, RegionEndpoint.GetBySystemName(region.RegionName));
                    var response = await amazonApiGatewayClient.GetRestApisAsync(request, CancellationToken.None).ConfigureAwait(false);


                    foreach (var item in response.Items)
                    {
                        result.Add(new ApiGatewayRestApi
                        {
                            ApiName = item.Name,
                            AccountId = account,
                            Region = region.RegionName,
                            CloudType = _cloudType
                        });
                        itemRegionCount++;
                    }
                    if (response.Position == null)
                    {
                        break;
                    }
                    nextToken = response.Position;
                }



                Console.WriteLine($"{region.RegionName} contains {itemRegionCount} ApiGatewayRestApis");
            }
            return result;

        }

        private async Task<List<CloudDatabase>> GetDatabasesAsync(List<Region> regions, string account)
        {
            var databases = new List<CloudDatabase>();
            foreach (var region in regions)
            {
                int itemRegionCount = 0;

                var rdsClient = new AmazonRDSClient(_awsCreds, RegionEndpoint.GetBySystemName(region.RegionName));
                var cloudwatchClient = new AmazonCloudWatchClient(_awsCreds, RegionEndpoint.GetBySystemName(region.RegionName));

                var rdsRequest = new DescribeDBInstancesRequest
                {
                    MaxRecords = 100
                };
                while (true)
                {
                    var rdsInstances = await rdsClient.DescribeDBInstancesAsync(rdsRequest).ConfigureAwait(false);
                    if (rdsInstances.DBInstances == null)
                    {
                        break;
                    }
                    foreach (var instance in rdsInstances.DBInstances)
                    {
                        var certInfo = await rdsClient.DescribeCertificatesAsync(new DescribeCertificatesRequest
                        {
                            CertificateIdentifier = instance.CACertificateIdentifier
                        });

                        var metadata = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);
                        if (instance.TagList != null)
                        {
                            foreach (var tag in instance.TagList)
                            {
                                metadata[tag.Key] = tag.Value;
                            }
                        }

                        bool? cert90DayWarning = null;
                        var cert = certInfo.Certificates.Single();
                        if (cert.ValidTill.HasValue)
                        {
                            cert90DayWarning = cert.ValidTill.Value.AddDays(-90) < DateTime.UtcNow;
                        }
                        databases.Add(new CloudDatabase
                        {
                            Id = instance.DbiResourceId,
                            Engine = instance.Engine,
                            Name = instance.DBInstanceIdentifier,
                            Version = instance.EngineVersion,
                            AvailabilityZone = instance.AvailabilityZone,
                            InstanceType = instance.DBInstanceClass,
                            CertificateAuthorityExpirationDate = certInfo.Certificates.Single().ValidTill,
                            CertificateExpirationDate = instance.CertificateDetails.ValidTill,
                            CertificateAuthority = instance.CertificateDetails.CAIdentifier,
                            CertificateExpiration90DayWarning = cert90DayWarning,
                            Encrypted = instance.StorageEncrypted,
                            AccountId = account,
                            Region = region.RegionName,
                            CloudType = _cloudType,
                            Tags = metadata,
                            MaxAllocatedStorage = instance.MaxAllocatedStorage,
                            AllocatedStorage = instance.AllocatedStorage,
                            FreeStorageSpace = await GetDbFreeStorage(cloudwatchClient, instance.DbiResourceId)
                        });

                        itemRegionCount++;
                    }

                    if (rdsInstances.Marker == null)
                    {
                        break;
                    }

                    rdsRequest.Marker = rdsInstances.Marker;
                }

                Console.WriteLine($"{region.RegionName} contains {itemRegionCount} RDSDatabases");
            }

            return databases;
        }


        public static async Task<double?> GetDbFreeStorage(AmazonCloudWatchClient cloudwatchClient, string instanceDbiResourceId)
        {

            var request = new GetMetricDataRequest
            {
                StartTime = DateTime.UtcNow.AddSeconds(-300),
                EndTime = DateTime.UtcNow,
                ScanBy = ScanBy.TimestampDescending,
                MetricDataQueries = new List<MetricDataQuery>
                    {
                        new MetricDataQuery
                        {
                            Id = "fetching_FreeStorageSpace",
                            MetricStat = new MetricStat
                            {
                                Metric = new Amazon.CloudWatch.Model.Metric
                                {
                                    Namespace = "AWS/RDS",
                                    MetricName = "FreeStorageSpace",
                                    Dimensions = new List<Dimension>
                                    {
                                        new Dimension
                                        {
                                            Name = "DBInstanceIdentifier",
                                            Value = "atlassianproddb"
                                        }
                                    }
                                },
                                Period = 300,
                                Stat = "Minimum"
                            }
                        }
                    }
            };

            var response = await cloudwatchClient.GetMetricDataAsync(request);
            if (response.MetricDataResults != null)
            {
                foreach (var result in response.MetricDataResults)
                {
                    Console.WriteLine($"ID: {result.Id}");
                    if (result.Timestamps != null)
                    {
                        var cloudwatchValue = result.Values.LastOrDefault();
                        double gigabytes = cloudwatchValue / 1_000_000_000.0;
                        gigabytes.Dump();
                        return Math.Round(gigabytes, 1);
                    }
                }
            }

            return null;
        }

        private async Task<List<CloudUser>> GetUsers(string accountId)
        {
            var users = new List<CloudUser>();

            try
            {
                var iamClient = new AmazonIdentityManagementServiceClient(_awsCreds, RegionEndpoint.EUWest1);
                var listUsersRequest = new ListUsersRequest
                {
                    MaxItems = 100
                };


                while (true)
                {
                    var iamUsers = await iamClient.ListUsersAsync(listUsersRequest).ConfigureAwait(false);
                    if (iamUsers.Users == null)
                    {
                        return users;
                    }


                    foreach (var user in iamUsers.Users)
                    {
                        DateTime? passwordLastUsed = null;
                        if (user.PasswordLastUsed != DateTime.MinValue)
                        {
                            passwordLastUsed = user.PasswordLastUsed;
                        }

                        GetLoginProfileResponse userDetails = null;
                        try
                        {
                            userDetails = await iamClient.GetLoginProfileAsync(new GetLoginProfileRequest
                            {
                                UserName = user.UserName
                            });
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine($"no login profile for {user.UserName}");
                        }

                        users.Add(new CloudUser
                        {
                            Id = user.UserId,
                            User = user.UserName,
                            CreateDate = user.CreateDate,
                            PasswordLastUsed = passwordLastUsed,
                            AccountId = accountId,
                            ConsoleAccess = userDetails != null,
                            Region = "",
                            CloudType = _cloudType
                        });
                    }

                    if (iamUsers.IsTruncated != true)
                    {
                        break;
                    }

                    listUsersRequest.Marker = iamUsers.Marker;
                }

            }
            catch (Exception e)
            {
                Console.WriteLine("We can`t get users as we don`t have access");
                Console.WriteLine(e);
            }
            return users;
        }


        private async Task<List<Volume>> GetAllVolumesAsync(Region region)
        {
            var client = new AmazonEC2Client(_awsCreds, RegionEndpoint.GetBySystemName(region.RegionName));

            List<Volume> volumes = new List<Volume>();
            string nextToken = null;

            while (true)
            {
                var request = new DescribeVolumesRequest
                {
                    NextToken = nextToken
                };


                var response = await client.DescribeVolumesAsync(request).ConfigureAwait(false);
                if (response.Volumes == null)
                {
                    return volumes;
                }
                volumes.AddRange(response.Volumes);
                if (response.NextToken == null)
                {
                    break;
                }
                nextToken = response.NextToken;
            }
            return volumes;
        }

        private async Task<List<Image>> GetAllImagesAsync(Region region, List<string> imageIds)
        {
            var client = new AmazonEC2Client(_awsCreds, RegionEndpoint.GetBySystemName(region.RegionName));

            List<Image> images = new List<Image>();


            var request = new DescribeImagesRequest
            {
                ImageIds = imageIds
            };


            try
            {
                while (true)
                {

                    var response = await client.DescribeImagesAsync(request).ConfigureAwait(false);
                    if (response.Images == null)
                    {
                        return images;
                    }

                    images.AddRange(response.Images);

                    if (response.NextToken == null)
                    {
                        break;
                    }
                    request.NextToken = response.NextToken;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(
                    $"Failed to get imageIds {string.Join(",", imageIds)} in {region.RegionName} '{e.Message}'");
            }
            return images;
        }

        private List<IpV4Network> GetNetworks(Instance server)
        {
            var networks = new List<IpV4Network>();
            if (server.NetworkInterfaces == null)
            {
                return networks;
            }
            foreach (var networkInterface in server.NetworkInterfaces)
            {
                var interfaceDescription = networkInterface.Description != null ? $"{networkInterface.Description} ({networkInterface.NetworkInterfaceId})" : networkInterface.NetworkInterfaceId;
                foreach (var privateIpAddress in networkInterface.PrivateIpAddresses)
                {
                    if (privateIpAddress.PrivateIpAddress != server.PrivateIpAddress)
                    {
                        networks.Add(
                            new IpV4Network
                            {
                                Name = interfaceDescription,
                                IpAddress = privateIpAddress.PrivateIpAddress
                            });
                    }
                }

                if (networkInterface.Association?.PublicIp != null)
                {
                    if (networkInterface.Association.PublicIp != server.PublicIpAddress)
                    {
                        networks.Add(
                            new IpV4Network
                            {
                                Name = interfaceDescription,
                                IpAddress = networkInterface.Association.PublicIp
                            });
                    }
                }
            }

            if (server.PrivateIpAddress != null)
            {
                networks.Add(
                    new IpV4Network
                    {
                        Name = "PrivateIpAddress",
                        IpAddress = server.PrivateIpAddress
                    });
            }
            if (server.PublicIpAddress != null)
            {
                networks.Add(
                    new IpV4Network
                    {
                        Name = "PublicIpAddress",
                        IpAddress = server.PublicIpAddress
                    });
            }

            return networks;
        }


        private List<VolumeDetail> GetVolumes(List<InstanceBlockDeviceMapping> serverVolumes, List<Volume> allVolumes)
        {
            var volumes = new List<VolumeDetail>();
            if (serverVolumes == null)
            {
                return volumes;
            }
            foreach (var volume in serverVolumes)
            {
                var volDetails = allVolumes.Single(v => v.VolumeId == volume.Ebs.VolumeId);
                var tags = new Dictionary<string, string>();
                foreach (var tag in volDetails.Tags)
                {
                    tags.Add(tag.Key, tag.Value);
                }
                volumes.Add(new VolumeDetail
                {
                    Id = volume.Ebs.VolumeId,
                    Label = volume.DeviceName,
                    Size = volDetails.Size,
                    Type = volDetails.VolumeType.ToString(),
                    Created = volDetails.CreateTime,
                    Iops = volDetails.Iops,
                    Tags = tags
                });
            }

            return volumes;
        }



    }
    public class AccountSummary
    {
        public string AccountId { get; set; }
        public string Region { get; set; }
        public string CloudType { get; set; }
    }
    public class EcsContainerInstance : AccountSummary
    {
        public string ContainerInstanceArn { get; set; }
    }
}

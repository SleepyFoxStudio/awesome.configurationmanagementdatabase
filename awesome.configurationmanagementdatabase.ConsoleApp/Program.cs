using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using ConsoleDump;
using Figgle;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace awesome.configurationmanagementdatabase.ConsoleApp
{
    class MainService : IHostedService
    {
        private readonly IHostApplicationLifetime _appLifetime;
        private readonly DataAccess _dataAccess;
        private readonly ProviderData _providerData;
        private readonly ConnectionKeySettings _connectionKeySettings;


        public MainService(IHostApplicationLifetime appLifetime, DataAccess dataAccess, IOptions<ConnectionKeySettings> connectionKeySettings, ProviderData providerData)
        {
            _appLifetime = appLifetime;
            _dataAccess = dataAccess;
            _providerData = providerData;
            _connectionKeySettings = connectionKeySettings.Value;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {

            await Console.Out.WriteLineAsync(FiggleFonts.Slant.Render("Awesome CMDB Sample App")).ConfigureAwait(false);
            await Console.Out.WriteLineAsync($"Awesome Content Management Database {Assembly.GetExecutingAssembly().GetName().Version}").ConfigureAwait(false);

            try
            {




                var awsDc = new AwsDatacentre(_connectionKeySettings.AwsAccessKeyId, _connectionKeySettings.AwsSecretKey, _connectionKeySettings.AwsSessionToken, _connectionKeySettings.OrgAwsAccessKeyId, _connectionKeySettings.OrgAwsSecretKey);
                //var alibabaDc = new AlibabaDatacentre(_connectionKeySettings.AlibabaAccessKeyId, _connectionKeySettings.AlibabaSecretKey);

                var accounts = new List<Account>();
                var existingItems = await _dataAccess.LoadExistingServerSummaries().ConfigureAwait(false);
                accounts.Add(await awsDc.GetAccountAsync().ConfigureAwait(false));
                //accounts.Add(await alibabaDc.GetAccountAsync().ConfigureAwait(false));



                foreach (var account in accounts)
                {
                    foreach (var serverGroup in account.ServerGroups)
                    {

                        foreach (var server in serverGroup.Servers)
                        {
                            server.Dump();
                            var isNew = !existingItems.ContainsKey(server.Id);
                            var isUpdated = false;
                            if (!isNew)
                            {
                                isUpdated = existingItems[server.Id].LastUpdated < server.Updated || existingItems[server.Id].LastUpdated == null;
                            }

                            if (isUpdated || isNew)
                            {
                                server.IsDirty = true;
                            }
                        }

                    }

                }



            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }

            if (Debugger.IsAttached)
            {
                await Console.Out.WriteLineAsync("press any key to exit.");
                Console.ReadKey();
            }
            _appLifetime.StopApplication();
        }


        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    class Program
    {
        private const string AppSettings = "appsettings.json";
        private const string HostSettings = "hostsettings.json";

        static async Task Main(string[] args)
        {
            //AWSConfigs.LoggingConfig.LogTo = LoggingOptions.Console;
            //AWSConfigs.LoggingConfig.LogResponses = ResponseLoggingOption.Always;
            //AWSConfigs.LoggingConfig.LogResponsesSizeLimit = 4 * 1024;

            var builder = new HostBuilder()
                .ConfigureHostConfiguration(configHost =>
                {
                    configHost.SetBasePath(Directory.GetCurrentDirectory());
                    configHost.AddJsonFile(HostSettings, optional: true);
                    configHost.AddCommandLine(args);
                })
                .ConfigureAppConfiguration((hostContext, configApp) =>
                {
                    configApp.SetBasePath(Directory.GetCurrentDirectory());
                    configApp.AddJsonFile(AppSettings, optional: true);
                    configApp.AddJsonFile($"appsettings.{hostContext.HostingEnvironment.EnvironmentName}.json", optional: true);
                    configApp.AddEnvironmentVariables(prefix: "Awesome_");
                    configApp.AddCommandLine(args);
                })
                .ConfigureServices((hostContext, services) =>
                {
                    services.Configure<ConsoleLifetimeOptions>(options =>
                    {
                        options.SuppressStatusMessages = true;
                    });

                    services.AddHttpClient("JsonClient", client =>
                    {
                        client.DefaultRequestHeaders.Add("Accept", "application/json");
                    });

                    services.AddSingleton<DataAccess>();
                    services.AddSingleton<ProviderData>();
                    services.AddSingleton<IHostedService, MainService>();

                    services.Configure<DatabaseSettings>(hostContext.Configuration.GetSection("database"));
                    services.Configure<ConnectionKeySettings>(hostContext.Configuration.GetSection("connectionKeySettings"));

                });

            await builder.RunConsoleAsync();
        }
    }

}

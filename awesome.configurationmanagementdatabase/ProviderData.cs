using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace awesome.configurationmanagementdatabase
{
    public class ProviderData
    {
        private readonly Lazy<Task<ProvidersDict>> _providers;
        private readonly DataAccess _dataAccess;
        private readonly Lazy<Task<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>> _lazyAllProviderDataItems;

        public ProviderData(IOptions<DatabaseSettings> databaseSettings)
        {
            _dataAccess = new DataAccess(databaseSettings);
            _providers = new Lazy<Task<ProvidersDict>>(_dataAccess.LoadAllProviders);
            _lazyAllProviderDataItems = new Lazy<Task<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>>(_dataAccess.LoadAllProviderData);
        }


        public async Task StoreItemsAsync(ProviderDataRequestByName providerDataRequestByName)
        {
            int providerId = await GetProviderIdAsync(providerDataRequestByName.ProviderName).ConfigureAwait(false);
            await _dataAccess.StoreProviderData(new ProviderDataRequestById
            {
                ProviderId = providerId,
                DataItems = providerDataRequestByName.DataItems
            }).ConfigureAwait(false);
        }

        public Task<Dictionary<string, Dictionary<string, Dictionary<string, string>>>> GetAllItems()
        {
            return _lazyAllProviderDataItems.Value;
        }


        public async Task<string> GetItemAsync(string providerName, string itemId, string propertyName)
        {
            var items = await _lazyAllProviderDataItems.Value.ConfigureAwait(false);
            try
            {
                return items[providerName][propertyName][itemId];
            }
            catch (KeyNotFoundException)
            {
                return null;
            }
        }

        public async Task RemoveItemsAsync(List<string> itemsToDelete)
        {
            await _dataAccess.RemoveProviderDataAsync(itemsToDelete).ConfigureAwait(false);
        }

        public async Task<int> GetProviderIdAsync(string providerName)
        {
            var loadedProviders = await _providers.Value.ConfigureAwait(false);
            if (!loadedProviders.ContainsKey(providerName))
            {
                var checkCurrentProviders = await _dataAccess.LoadAllProviders().ConfigureAwait(false);
                if (checkCurrentProviders.ContainsKey(providerName))
                {
                    loadedProviders = checkCurrentProviders;
                }
                else
                {
                    await _dataAccess.UpsertProvider(providerName).ConfigureAwait(false);
                    loadedProviders = await _dataAccess.LoadAllProviders().ConfigureAwait(false);
                }
            }
            return loadedProviders[providerName];
        }

        public static string StandardizePropName(string channelToStore)
        {
            Regex rgx = new Regex("[^a-zA-Z0-9-]");
            var output = rgx.Replace(channelToStore, "");
            output = output.ToUpperInvariant();
            return output;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
using Newtonsoft.Json;

namespace awesome.configurationmanagementdatabase
{
    public class DataAccess
    {
        private readonly DatabaseSettings _databaseSettings;
        private string _connectionString { get; }
        public DataAccess(IOptions<DatabaseSettings> databaseSettings)
        {
            _databaseSettings = databaseSettings.Value;
            _connectionString = $"server={_databaseSettings.DatabaseHost};user={_databaseSettings.DatabaseUsername};database={_databaseSettings.DatabaseName};port={_databaseSettings.DatabasePort};password={_databaseSettings.DatabasePassword};default command timeout={_databaseSettings.CommandTimeout};Connection Timeout={_databaseSettings.ConnectionTimeout}";
            Console.WriteLine("Initializing DataAccess class");
            Console.WriteLine($"Database host is {_databaseSettings.DatabaseHost}");
        }

        public async Task<Dictionary<string, ItemSummary>> LoadExistingServerSummaries(bool includeDeleted = false)
        {
            var existingItems = new Dictionary<string, ItemSummary>();
            using (var con = new MySqlConnection(_connectionString))
            {
                await con.OpenAsync();

                var sql = "SELECT idServers,updated from Servers";
                if (!includeDeleted)
                {
                    sql = $"{sql} where isnull(Deleted)";
                }

                Console.WriteLine("*************************************************************");
                Console.WriteLine(sql);
                Console.WriteLine("*************************************************************");
                var cmd = new MySqlCommand(sql, con);

                var rdr = await cmd.ExecuteReaderAsync();

                while (rdr.Read())
                {
                    //Console.WriteLine(rdr[0] + " -- " + rdr[1]);
                    DateTime? updateDate = null;
                    if (!rdr.IsDBNull(1))
                    {
                        updateDate = rdr.GetDateTime(1);
                    }
                    existingItems.Add(rdr.GetString(0), new ItemSummary
                    {
                        LastUpdated = updateDate
                    });

                }
                rdr.Close();
                await con.CloseAsync();
            }

            return existingItems;
        }

        public async Task<List<ServerDetails>> LoadExistingServersFull(bool includeDeleted = false)
        {
            var existingItems = new List<ServerDetails>();
            using (var con = new MySqlConnection(_connectionString))
            {
                await con.OpenAsync().ConfigureAwait(false);

                var sql = "SELECT Json from Servers";
                if (!includeDeleted)
                {
                    sql = $"{sql} where isnull(Deleted)";
                }

                Console.WriteLine("*************************************************************");
                Console.WriteLine(sql);
                Console.WriteLine("*************************************************************");
                var cmd = new MySqlCommand(sql, con);

                var rdr = await cmd.ExecuteReaderAsync().ConfigureAwait(false);

                while (await rdr.ReadAsync().ConfigureAwait(false))
                {
                    if (!await rdr.IsDBNullAsync(0).ConfigureAwait(false))
                    {
                        var json = rdr.GetString(0);
                        existingItems.Add(JsonConvert.DeserializeObject<ServerDetails>(json));
                    }
                }
                await rdr.CloseAsync().ConfigureAwait(false);
                await con.CloseAsync().ConfigureAwait(false);
            }

            return existingItems;
        }

        internal async Task<Dictionary<string, Dictionary<string, Dictionary<string, string>>>> LoadAllProviderData()
        {
            var providerData = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();
            using (var con = new MySqlConnection(_connectionString))
            {

                var sql = "SELECT idProvider, Name from Provider";
                var cmd = new MySqlCommand(sql, con);
                await con.OpenAsync();
                var rdr = await cmd.ExecuteReaderAsync();

                while (await rdr.ReadAsync())
                {
                    Console.WriteLine($"Loading all Provider data for {rdr.GetString(1)} ({rdr.GetInt32(0)})");
                    providerData.Add(rdr.GetString(1), await LoadProviderDataItems(rdr.GetInt32(0)));
                }
                rdr.Close();
                await con.CloseAsync();
            }
            return providerData;
        }

        internal async Task<ProvidersDict> LoadAllProviders()
        {
            var providers = new ProvidersDict();
            using (var con = new MySqlConnection(_connectionString))
            {

                var sql = "SELECT idProvider, Name from Provider";
                await con.OpenAsync();

                var cmd = new MySqlCommand(sql, con);
                var rdr = await cmd.ExecuteReaderAsync();

                while (await rdr.ReadAsync())
                {
                    Console.WriteLine($"Storing Provider Id Lookup for {rdr.GetString(1)} ({rdr.GetInt32(0)})");
                    providers.Add(rdr.GetString(1), rdr.GetInt32(0));
                }

                rdr.Close();
                await con.CloseAsync();

            }
            return providers;
        }

        internal async Task RemoveProviderDataAsync(List<string> itemsToDelete)
        {
            foreach (var item in itemsToDelete)
            {

                await Console.Out.WriteLineAsync($"Removing Data for {item}").ConfigureAwait(false);
                using (var con = new MySqlConnection(_connectionString))
                {
                    await con.OpenAsync().ConfigureAwait(false);

                    var sql = "Delete from ProviderData where ItemId in ({ItemsToDelete})";
                    var cmd = new MySqlCommand(sql, con);
                    cmd.AddArrayParameters("ItemsToDelete", itemsToDelete);
                    await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                    await con.CloseAsync().ConfigureAwait(false);
                }
            }
        }


        internal async Task StoreProviderData(ProviderDataRequestById providerDataRequest)
        {

            var currentValues = await LoadProviderDataItems(providerDataRequest.ProviderId);


            foreach (var propertyName in providerDataRequest.DataItems.Select(o => o.PropertyName).Distinct())
            {

                await Console.Out.WriteLineAsync($"Storing {propertyName}");
                var providerKeyId = await UpsertProviderKey(propertyName, providerDataRequest.ProviderId);
                var totalStopwatch = Stopwatch.StartNew();
                var queryStopwatch = new Stopwatch();
                foreach (var dataItem in providerDataRequest.DataItems.Where(k => k.PropertyName == propertyName))
                {
                    if (dataItem.PropertyValue == null)
                    {
                        continue;
                    }

                    if (currentValues.ContainsKey(propertyName))
                    {
                        if (currentValues[propertyName].ContainsKey(dataItem.ItemId))
                        {
                            if (currentValues[propertyName][dataItem.ItemId].Equals(dataItem.PropertyValue, StringComparison.InvariantCultureIgnoreCase))
                            {
                                await Console.Out.WriteLineAsync($"No need to store.. already set {dataItem.ItemId} named {dataItem.PropertyName} with value length {dataItem.PropertyValue?.Length}");
                                continue;
                            }
                        }
                    }

                    await Console.Out.WriteLineAsync($"Storing {dataItem.ItemId} named {dataItem.PropertyName} with value length {dataItem.PropertyValue?.Length}");

                    var sql = "REPLACE INTO ProviderData (ItemId,ProviderId, ProviderKeyId, PropertyValue) values (@ItemId,@ProviderId, @ProviderKeyId, @PropertyValue)";
                    var con = new MySqlConnection(_connectionString);
                    var cmd = new MySqlCommand(sql, con);
                    await con.OpenAsync();
                    cmd.Parameters.AddWithValue("@ItemId", dataItem.ItemId);
                    cmd.Parameters.AddWithValue("@ProviderId", providerDataRequest.ProviderId);
                    cmd.Parameters.AddWithValue("@ProviderKeyId", providerKeyId);
                    cmd.Parameters.AddWithValue("@PropertyValue", dataItem.PropertyValue);
                    await Console.Out.WriteLineAsync($"EXEC : Storing {dataItem.ItemId} named {dataItem.PropertyName} with value length {dataItem.PropertyValue?.Length}");
                    await cmd.ExecuteNonQueryAsync();
                    await Console.Out.WriteLineAsync($"CLOSE : Storing {dataItem.ItemId} named {dataItem.PropertyName} with value length {dataItem.PropertyValue?.Length}");
                    await con.CloseAsync();
                }
                queryStopwatch.Start();
                queryStopwatch.Stop();

                totalStopwatch.Stop();

                Console.WriteLine($"Loop took {totalStopwatch.Elapsed.TotalMinutes}, spent {queryStopwatch.Elapsed.TotalMinutes} running queries");
            }


        }

        internal async Task UpsertProvider(string providerName)
        {
            using (var con = new MySqlConnection(_connectionString))
            {

                await con.OpenAsync();

                const string sql = "Insert ignore INTO Provider (Name) values (@Name)";
                var cmd = new MySqlCommand(sql, con);
                cmd.Parameters.AddWithValue("@Name", providerName);
                await cmd.ExecuteNonQueryAsync();
                await con.CloseAsync();
            }
        }
        public async Task MarkServerDeleted(string itemId)
        {
            using (var con = new MySqlConnection(_connectionString))
            {

                await con.OpenAsync();

                const string sql = "Update Servers set Deleted = @Deleted where idservers = @serverId and deleted is null";
                var cmd = new MySqlCommand(sql, con);
                cmd.Parameters.AddWithValue("@Deleted", DateTime.Now);
                cmd.Parameters.AddWithValue("@serverId", itemId);
                await cmd.ExecuteNonQueryAsync();
                await con.CloseAsync();
            }
        }

        public async Task MarkServersDeleted(List<string> itemIds)
        {
            using (var con = new MySqlConnection(_connectionString))
            {

                await con.OpenAsync();

                const string sql = "Update Servers set Deleted = @Deleted where idservers in ({LockNumbers}) and deleted is null";
                var cmd = new MySqlCommand(sql, con);
                cmd.Parameters.AddWithValue("@Deleted", DateTime.Now);
                cmd.AddArrayParameters("LockNumbers", itemIds);
                await cmd.ExecuteNonQueryAsync();
                await con.CloseAsync();
            }
        }

        internal async Task<long> UpsertProviderKey(string providerKey, int providerId)
        {
            long newId = 0;
            await using var con = new MySqlConnection(_connectionString);
            await con.OpenAsync();
            Console.WriteLine($"Inserting providerKey {providerKey}, with providerId {providerId}");
            string sql = "Insert ignore INTO ProviderKey (ProviderKey, IdProvider) values (@ProviderKey, @IdProvider)";
            var cmd = new MySqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@ProviderKey", providerKey);
            cmd.Parameters.AddWithValue("@IdProvider", providerId);
            await cmd.ExecuteNonQueryAsync();



            Console.WriteLine($"Selecting providerKey {providerKey}, with providerId {providerId}");
            sql = "SELECT IdProviderKey from ProviderKey where ProviderKey = @ProviderKey AND IdProvider = @IdProvider limit 1";
            cmd = new MySqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@ProviderKey", providerKey);
            cmd.Parameters.AddWithValue("@IdProvider", providerId);
            var rdr = await cmd.ExecuteReaderAsync();

            Console.WriteLine($"Performing read to get ID");

            while (await rdr.ReadAsync())
            {
                newId = rdr.GetInt64(0);
            }
            await rdr.CloseAsync();
            Console.WriteLine($"Id is {newId}");

            await con.CloseAsync();

            return newId;
        }
        private async Task<string> GetProviderKey(int idProviderKey, int providerId)
        {
            string providerKey = "";
            using (var con = new MySqlConnection(_connectionString))
            {

                await con.OpenAsync();

                var sql = "SELECT ProviderKey from ProviderKey where IdProvider = @IdProvider AND idProviderKey = @idProviderKey limit 1";
                var cmd = new MySqlCommand(sql, con);
                cmd.Parameters.AddWithValue("@idProviderKey", idProviderKey);
                cmd.Parameters.AddWithValue("@IdProvider", providerId);
                var rdr = await cmd.ExecuteReaderAsync();

                while (rdr.Read())
                {
                    providerKey = rdr.GetString(0);
                }
                rdr.Close();

                await con.CloseAsync();
            }
            await Console.Out.WriteLineAsync($"Loading {providerKey}");

            return providerKey;
        }

        private async Task<Dictionary<string, string>> LoadAllProviderKeysForProvider(int providerKeyId)
        {
            var response = new Dictionary<string, string>();
            using (var con = new MySqlConnection(_connectionString))
            {

                await con.OpenAsync().ConfigureAwait(false);


                var sql = "SELECT ItemId, PropertyValue from ProviderData where ProviderKeyId = @ProviderKeyId";
                var cmd = new MySqlCommand(sql, con);
                cmd.Parameters.AddWithValue("@ProviderKeyId", providerKeyId);

                var rdr = await cmd.ExecuteReaderAsync().ConfigureAwait(false);

                while (await rdr.ReadAsync().ConfigureAwait(false))
                {
                    response.Add(rdr.GetString(0), rdr.GetString(1));
                }
                await rdr.CloseAsync().ConfigureAwait(false);

                await con.CloseAsync().ConfigureAwait(false);
            }

            return response;
        }



        public async Task<Dictionary<string, Dictionary<string, string>>> LoadProviderDataItems(int id)
        {

            var providerData = new Dictionary<string, Dictionary<string, string>>();
            using (var con = new MySqlConnection(_connectionString))
            {
                await con.OpenAsync();

                var sql = "SELECT ProviderKey, idProviderKey from ProviderKey where idProvider = @idProvider";
                var cmd = new MySqlCommand(sql, con);
                cmd.Parameters.AddWithValue("@idProvider", id);
                var rdr = await cmd.ExecuteReaderAsync();

                Console.WriteLine($"Loading all Provider data for items for Provider id:{id}");
                while (rdr.Read())
                {
                    providerData.Add(rdr.GetString(0), await LoadAllProviderKeysForProvider(rdr.GetInt32(1)));

                }
                rdr.Close();
                await con.CloseAsync();

            }

            return providerData;
        }



        public async Task StoreAccounts(Account[] accounts)
        {


            using (var con = new MySqlConnection(_connectionString))
            {

                await con.OpenAsync().ConfigureAwait(false);
                foreach (var account in accounts)
                {
                    await UpsertProject(account.AccountId, account.AccountName, account.DataCentreType).ConfigureAwait(false);
                    foreach (var server in account.ServerGroups.SelectMany(a => a.Servers))
                    {
                        //TODO: add missing Datacentres
                        try
                        {
                            var sql = "REPLACE INTO Servers (name,flavour,Created,Updated,idServers, Cpu, Ram,AccountId, Json, deleted) values (@name,@flavour,@Created,@Updated,@idServers, @Cpu, @Ram,@AccountId, @Json, null)";
                            var cmd = new MySqlCommand(sql, con);
                            cmd.Parameters.AddWithValue("@idServers", server.Id);
                            cmd.Parameters.AddWithValue("@name", server.Name);
                            cmd.Parameters.AddWithValue("@flavour", server.Flavour);
                            cmd.Parameters.AddWithValue("@Created", server.Created);
                            cmd.Parameters.AddWithValue("@Updated", server.Updated);
                            cmd.Parameters.AddWithValue("@Cpu", server.Cpu);
                            cmd.Parameters.AddWithValue("@AccountId", account.AccountId);
                            cmd.Parameters.AddWithValue("@Ram", server.Ram);
                            cmd.Parameters.AddWithValue("@Json", JsonConvert.SerializeObject(server));
                            await cmd.ExecuteNonQueryAsync();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex.ToString());
                        }

                    }

                }
                await con.CloseAsync();

            }

        }





        public async Task UpsertProject(string accountId, string accountName, string dataCentreType)
        {
            using (var con = new MySqlConnection(_connectionString))
            {

                await con.OpenAsync();

                const string sql = "Insert ignore INTO Account (idAccount,DataCentreType,AccountName) values (@idAccount, @DataCentreType,@AccountName)";
                var cmd = new MySqlCommand(sql, con);
                if (accountName == null)
                {
                    accountName = accountId;
                }
                cmd.Parameters.AddWithValue("@idAccount", accountId);
                cmd.Parameters.AddWithValue("@DataCentreType", dataCentreType);
                cmd.Parameters.AddWithValue("@AccountName", accountName);
                await cmd.ExecuteNonQueryAsync();
                await con.CloseAsync();
            }
        }
    }

    internal class ProvidersDict : Dictionary<string, int>
    {
    }

    internal class ProviderItemIdAndPropertyName
    {
        public string ItemId { get; internal set; }
        public string PropertyName { get; internal set; }
    }

    internal class ProviderDataRequestById
    {
        public IEnumerable<ProviderDataItem> DataItems { get; internal set; }
        public int ProviderId { get; set; }
    }

    public class ProviderDataRequestByName
    {
        public IEnumerable<ProviderDataItem> DataItems { get; set; }
        public string ProviderName { get; set; }
    }

    public class ProviderDataItem
    {
        public string ItemId { get; set; }
        public string PropertyName { get; set; }
        public string PropertyValue { get; set; }
    }

    public class ItemSummary
    {
        public DateTime? LastUpdated { get; set; }
    }


    public static class SqlCommandExt
    {

        /// <summary>
        /// This will add an array of parameters to a SqlCommand. This is used for an IN statement.
        /// Use the returned value for the IN part of your SQL call. (i.e. SELECT * FROM table WHERE field IN ({paramNameRoot}))
        /// </summary>
        /// <param name="cmd">The SqlCommand object to add parameters to.</param>
        /// <param name="paramNameRoot">What the parameter should be named followed by a unique value for each value. This value surrounded by {} in the CommandText will be replaced.</param>
        /// <param name="values">The array of strings that need to be added as parameters.</param>
        /// <param name="dbType">One of the System.Data.SqlDbType values. If null, determines type based on T.</param>
        /// <param name="size">The maximum size, in bytes, of the data within the column. The default value is inferred from the parameter value.</param>
        public static MySqlParameter[] AddArrayParameters<T>(this MySqlCommand cmd, string paramNameRoot, IEnumerable<T> values, MySqlDbType? dbType = null, int? size = null)
        {
            /* An array cannot be simply added as a parameter to a SqlCommand so we need to loop through things and add it manually. 
             * Each item in the array will end up being it's own SqlParameter so the return value for this must be used as part of the
             * IN statement in the CommandText.
             */
            var parameters = new List<MySqlParameter>();
            var parameterNames = new List<string>();
            var paramNbr = 1;
            foreach (var value in values)
            {
                var paramName = string.Format("@{0}{1}", paramNameRoot, paramNbr++);
                parameterNames.Add(paramName);
                MySqlParameter p = new MySqlParameter(paramName, value);
                if (dbType.HasValue)
                    p.MySqlDbType = dbType.Value;
                if (size.HasValue)
                    p.Size = size.Value;
                cmd.Parameters.Add(p);
                parameters.Add(p);
            }

            cmd.CommandText = cmd.CommandText.Replace("{" + paramNameRoot + "}", string.Join(",", parameterNames));

            return parameters.ToArray();
        }

    }
}

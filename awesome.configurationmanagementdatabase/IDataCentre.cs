using System.Collections.Generic;
using System.Threading.Tasks;

namespace awesome.configurationmanagementdatabase
{
   public interface IDatacentre
    {
        Task<Account> GetAccountAsync();
    }
}
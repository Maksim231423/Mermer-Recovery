using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mermer.FundsManagement.Models;
using Mermer.Http;

namespace Mermer.Ui.Pc.Services;

public class ApiCurrenciesRepository : BaseApiCacheRepository<Currency>
{
    public ApiCurrenciesRepository(RestClient restClient) : base(restClient, "Currency", "currencies") { }

    public override async Task<IEnumerable<Currency>> GetAllAsync()
    {
        var all = await base.GetAllAsync();
        return all?.Where(c => !c.IsDisabled).ToList() ?? Enumerable.Empty<Currency>();
    }
}
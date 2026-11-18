using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mermer.Http;
using Mermer.StockManagement.Models;
using Mermer.StockManagement.Services;

namespace Mermer.Ui.Pc.Services
{
    public class ApiStockRepriceEffectsRepository : IStockRepriceEffectsRepository
    {
        private readonly RestClient _restClient;

        public ApiStockRepriceEffectsRepository(RestClient restClient)
        {
            _restClient = restClient;
        }

        public async Task<int> CountAsync(DateTime from, DateTime till, string[] warehouseIds)
        {
            try
            {
                // СТАЛО:
                var query = $"?from={from:yyyy-MM-ddTHH:mm:ss}&till={till:yyyy-MM-ddTHH:mm:ss}";
                if (warehouseIds != null)
                    foreach (var w in warehouseIds.Where(x => !string.IsNullOrEmpty(x)))
                        query += $"&warehouseId={w}";

                return await _restClient.GetAsync<int>($"/api/stock-reprice-effects/count{query}");
            }
            catch { return 0; }
        }

        public async Task<IEnumerable<DateTime>> GetChangeDatesAsync(DateTime from, DateTime till)
        {
            try
            {
                // СТАЛО:
                var query = $"?from={from:yyyy-MM-ddTHH:mm:ss}&till={till:yyyy-MM-ddTHH:mm:ss}";
                var res = await _restClient.GetAsync<List<DateTime>>($"/api/stock-reprice-effects/dates{query}");
                return res ?? new List<DateTime>();
            }
            catch { return Enumerable.Empty<DateTime>(); }
        }

        public async Task<IEnumerable<StockRepriceEffect>> GetAsync(DateTime from, DateTime till, params string[] warehouses)
        {
            try
            {
                // СТАЛО:
                var query = $"?from={from:yyyy-MM-ddTHH:mm:ss}&till={till:yyyy-MM-ddTHH:mm:ss}";
                if (warehouses != null)
                    foreach (var w in warehouses.Where(x => !string.IsNullOrEmpty(x)))
                        query += $"&warehouseId={w}";

                var res = await _restClient.GetAsync<List<StockRepriceEffect>>($"/api/stock-reprice-effects{query}");
                return res ?? new List<StockRepriceEffect>();
            }
            catch { return Enumerable.Empty<StockRepriceEffect>(); }
        }
    }
}
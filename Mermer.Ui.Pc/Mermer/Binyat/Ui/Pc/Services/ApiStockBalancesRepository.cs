using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mermer.Http;
using Mermer.StockManagement.Models;
using Mermer.StockManagement.Services;

namespace Mermer.Ui.Pc.Services
{
    public class ApiStockBalancesRepository : IStockBalancesRepository
    {
        private readonly RestClient _restClient;

        public ApiStockBalancesRepository(RestClient restClient)
        {
            _restClient = restClient ?? throw new ArgumentNullException(nameof(restClient));
        }

        public async Task<IEnumerable<StockBalance>> GetAsync(string stockId, DateTime date, params string[] warehouses)
        {
            try
            {
                var queryParams = new List<string>();
                if (!string.IsNullOrEmpty(stockId)) queryParams.Add($"stockId={stockId}");
                if (warehouses != null)
                {
                    foreach (var w in warehouses.Where(x => !string.IsNullOrEmpty(x)))
                        queryParams.Add($"warehouseId={w}");
                }

                var query = queryParams.Any() ? "?" + string.Join("&", queryParams) : "";
                var remote = await _restClient.GetAsync<List<StockBalance>>($"/api/stock-balances{query}");
                return remote ?? new List<StockBalance>();
            }
            catch { return new List<StockBalance>(); }
        }

        public Task<IEnumerable<StockBalance>> GetAsync(string warehouseId, string[] stockIds, DateTime? date = null)
        {
            var wh = string.IsNullOrEmpty(warehouseId) ? null : new[] { warehouseId };
            return GetAsync(wh, stockIds, date);
        }

        public async Task<IEnumerable<StockBalance>> GetAsync(string[] warehouseIds, string[] stockIds, DateTime? date = null)
        {
            try
            {
                var queryParams = new List<string>();
                if (warehouseIds != null)
                {
                    foreach (var w in warehouseIds.Where(x => !string.IsNullOrEmpty(x)))
                        queryParams.Add($"warehouseId={w}");
                }
                if (stockIds != null)
                {
                    foreach (var s in stockIds.Where(x => !string.IsNullOrEmpty(x)))
                        queryParams.Add($"stockId={s}");
                }

                var query = queryParams.Any() ? "?" + string.Join("&", queryParams) : "";
                var remote = await _restClient.GetAsync<List<StockBalance>>($"/api/stock-balances{query}");
                return remote ?? new List<StockBalance>();
            }
            catch { return new List<StockBalance>(); }
        }

        public Task<IEnumerable<StockBalance>> GetAsync(string warehouseId, (string stockId, DateTime? balanceDate)[] stockBalanceDates)
        {
            var stockIds = stockBalanceDates?.Select(x => x.stockId).ToArray() ?? Array.Empty<string>();
            return GetAsync(warehouseId, stockIds);
        }

        public Task<IEnumerable<StockBalance>> GetAsync(string[] warehouseIds, (string stockId, DateTime? balanceDate)[] stockBalanceDates)
        {
            var stockIds = stockBalanceDates?.Select(x => x.stockId).ToArray() ?? Array.Empty<string>();
            return GetAsync(warehouseIds, stockIds);
        }

        public Task<IEnumerable<StockBalanceWithCodeAndName>> GetAsync(string warehouseId, string[] stockIds, string excludedTransactionId)
            => Task.FromResult(Enumerable.Empty<StockBalanceWithCodeAndName>());

        public async Task<IEnumerable<StockBalanceByTypeWithBalanceAndData>> GetByTypeAsync(string[] warehouseIds, string stockId, DateTime dateFrom, DateTime dateTill, bool aggregate)
        {
            try
            {
                var query = $"?dateFrom={dateFrom.ToUniversalTime():yyyy-MM-ddTHH:mm:ssZ}&dateTill={dateTill.ToUniversalTime():yyyy-MM-ddTHH:mm:ssZ}&aggregate={aggregate}";
                if (!string.IsNullOrEmpty(stockId)) query += $"&stockId={stockId}";
                if (warehouseIds != null) foreach (var w in warehouseIds) query += $"&warehouseId={w}";

                var remote = await _restClient.GetAsync<List<StockBalanceByTypeWithBalanceAndData>>($"/api/stock-balances/by-type{query}");
                return remote ?? new List<StockBalanceByTypeWithBalanceAndData>();
            }
            catch { return new List<StockBalanceByTypeWithBalanceAndData>(); }
        }

        public async Task<IEnumerable<StockBalanceByWarehouses>> GetByDateAndWarehousesAsync(DateTime date, IEnumerable<string> warehouseIds, string displayCurrencyId, IEnumerable<string> stockIds = null)
        {
            var whList = warehouseIds?.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            if (whList == null || !whList.Any())
            {
                return Enumerable.Empty<StockBalanceByWarehouses>();
            }

            try
            {
                var query = $"?date={date.ToUniversalTime():yyyy-MM-ddTHH:mm:ssZ}&displayCurrencyId={displayCurrencyId}";
                foreach (var w in whList) query += $"&warehouseId={w}";
                if (stockIds != null) foreach (var s in stockIds.Where(x => !string.IsNullOrWhiteSpace(x))) query += $"&stockId={s}";

                var remote = await _restClient.GetAsync<List<StockBalanceByWarehouses>>($"/api/stock-balances/by-date-warehouses{query}");
                return remote ?? new List<StockBalanceByWarehouses>();
            }
            catch { return new List<StockBalanceByWarehouses>(); }
        }
    }

    public class ApiStockBalancesAggregatedRepository : IStockBalancesAggregatedRepository
    {
        private readonly RestClient _restClient;

        public ApiStockBalancesAggregatedRepository(RestClient restClient)
        {
            _restClient = restClient;
        }

        public async Task<StockBalanceAggregated> GetByTypeAggregatedAsync(string[] warehouseIds, DateTime dateFrom, DateTime dateTill)
        {
            try
            {
                var query = $"?dateFrom={dateFrom.ToUniversalTime():yyyy-MM-ddTHH:mm:ssZ}&dateTill={dateTill.ToUniversalTime():yyyy-MM-ddTHH:mm:ssZ}";
                if (warehouseIds != null) foreach (var w in warehouseIds) query += $"&warehouseId={w}";
                var remote = await _restClient.GetAsync<StockBalanceAggregated>($"/api/stock-balances/aggregated{query}");
                return remote ?? new StockBalanceAggregated { Lines = Array.Empty<StockBalanceAggregatedLine>() };
            }
            catch { return new StockBalanceAggregated { Lines = Array.Empty<StockBalanceAggregatedLine>() }; }
        }
    }
}
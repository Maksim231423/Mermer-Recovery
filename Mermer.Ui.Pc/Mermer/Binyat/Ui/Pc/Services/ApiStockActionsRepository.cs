using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mermer.Http;
using Mermer.StockManagement.Models;
using Mermer.StockManagement.Services;

namespace Mermer.Ui.Pc.Services
{
    public class ApiStockActionsRepository : IStockActionsRepository
    {
        private readonly RestClient _restClient;

        public ApiStockActionsRepository(RestClient restClient)
        {
            _restClient = restClient ?? throw new ArgumentNullException(nameof(restClient));
        }

        public async Task<int> CountAsync(DateTime? startDate, DateTime? endDate, string stockId, params string[] warehouseIds)
        {
            try
            {
                var query = BuildQueryString(startDate, endDate, stockId, warehouseIds);
                var res = await _restClient.GetAsync<CountResponse>($"/api/stocks/actions/count{query}");
                if (res != null) return res.Count;
            }
            catch { }

            var results = await GetAsync(startDate, endDate, stockId, warehouseIds);
            return results.Count();
        }

        public async Task<IEnumerable<StockActionWithData>> GetAsync(DateTime? startDate, DateTime? endDate, string stockId, params string[] warehouseIds)
        {
            try
            {
                string url = "/api/stocks/actions" + BuildQueryString(startDate, endDate, stockId, warehouseIds);
                var remote = await _restClient.GetAsync<List<StockActionWithData>>(url);
                return remote ?? Enumerable.Empty<StockActionWithData>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[STOCK ACTIONS FETCH ERROR]: {ex.Message}");
                return Enumerable.Empty<StockActionWithData>();
            }
        }

        public Task<StockTracking> TrackByLineIdAsync(string lineId)
        {
            return Task.FromResult(new StockTracking());
        }

        public Task<IEnumerable<StockTracking>> TrackByTransactionIdAsync(string transactionId)
        {
            return Task.FromResult(Enumerable.Empty<StockTracking>());
        }

        private static string BuildQueryString(DateTime? startDate, DateTime? endDate, string stockId, string[] warehouseIds)
        {
            var queryParams = new List<string>();

            if (startDate.HasValue) queryParams.Add($"from={startDate.Value.ToUniversalTime():yyyy-MM-ddTHH:mm:ssZ}");
            if (endDate.HasValue) queryParams.Add($"till={endDate.Value.ToUniversalTime():yyyy-MM-ddTHH:mm:ssZ}");
            if (!string.IsNullOrEmpty(stockId)) queryParams.Add($"stockId={stockId}");

            if (warehouseIds != null && warehouseIds.Any())
            {
                foreach (var wh in warehouseIds.Where(w => !string.IsNullOrEmpty(w)))
                {
                    queryParams.Add($"warehouseId={wh}");
                }
            }

            return queryParams.Any() ? "?" + string.Join("&", queryParams) : "";
        }

        private class CountResponse
        {
            public int Count { get; set; }
        }
    }
}
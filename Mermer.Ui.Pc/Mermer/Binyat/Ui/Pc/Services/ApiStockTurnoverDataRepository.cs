using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Mermer.Http;
using Mermer.StockManagement.Models;
using Mermer.StockManagement.Services;

namespace Mermer.Ui.Pc.Services
{
    public class ApiStockTurnoverDataRepository : IStockTurnoverDataRepository
    {
        private readonly RestClient _restClient;

        public ApiStockTurnoverDataRepository(RestClient restClient)
        {
            _restClient = restClient;
        }

        public async Task<IEnumerable<StockTurnoverData>> GetAsync(string warehouseId)
        {
            try
            {
                // Запрашиваем без лимитов, только с фильтром по складу (если он задан)
                string query = string.Empty;
                if (!string.IsNullOrEmpty(warehouseId))
                {
                    query = $"?warehouseId={warehouseId}";
                }

                var result = await _restClient.GetAsync<List<StockTurnoverData>>($"/api/stock-turnovers{query}");
                return result ?? new List<StockTurnoverData>();
            }
            catch
            {
                return new List<StockTurnoverData>();
            }
        }
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mermer.Http;
using Mermer.Reporting.Models;
using Mermer.Reporting.Services;

namespace Mermer.Ui.Pc.Services
{
    public class ApiRevenueReportsRepository : IRevenueReportsRepository
    {
        private readonly RestClient _restClient;

        public ApiRevenueReportsRepository(RestClient restClient)
        {
            _restClient = restClient;
        }

        public async Task<IEnumerable<RevenueReport>> GetAsync(string[] warehouseIds, DateTime dateFrom, DateTime dateTill)
        {
            try
            {
                // СТАЛО:
                var query = $"?dateFrom={dateFrom:yyyy-MM-ddTHH:mm:ss}&dateTill={dateTill:yyyy-MM-ddTHH:mm:ss}";

                if (warehouseIds != null)
                {
                    foreach (var id in warehouseIds.Where(x => !string.IsNullOrEmpty(x)))
                    {
                        query += $"&warehouseId={id}";
                    }
                }

                var report = await _restClient.GetAsync<List<RevenueReport>>($"/api/revenue-reports{query}");
                return report ?? new List<RevenueReport>();
            }
            catch
            {
                return new List<RevenueReport>();
            }
        }
    }
}
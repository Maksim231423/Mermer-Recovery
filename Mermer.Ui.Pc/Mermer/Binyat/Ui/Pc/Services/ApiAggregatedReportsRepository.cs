using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mermer.Http;
using Mermer.Reporting.Models;
using Mermer.Reporting.Services;

namespace Mermer.Ui.Pc.Services
{
    public class ApiAggregatedReportsRepository : IAggregatedReportsRepository
    {
        private readonly RestClient _restClient;

        public ApiAggregatedReportsRepository(RestClient restClient)
        {
            _restClient = restClient;
        }

        public async Task<AggregatedReport> GetAsync(string[] officeIds, DateTime dateFrom, DateTime dateTill)
        {
            try
            {
                var query = $"?dateFrom={dateFrom:yyyy-MM-ddTHH:mm:ss}&dateTill={dateTill:yyyy-MM-ddTHH:mm:ss}";
                if (officeIds != null)
                {
                    foreach (var id in officeIds.Where(x => !string.IsNullOrEmpty(x)))
                    {
                        query += $"&officeId={id}";
                    }
                }

                var report = await _restClient.GetAsync<AggregatedReport>($"/api/aggregated-reports{query}");
                return report ?? new AggregatedReport();
            }
            catch
            {
                return new AggregatedReport();
            }
        }
    }
}
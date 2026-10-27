using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mermer.CRM.Models;
using Mermer.CRM.Services;
using Mermer.Http;

namespace Mermer.Ui.Pc.Services
{
    public class ApiPartnerBalancesRepository : IPartnerBalancesRepository
    {
        private readonly RestClient _restClient;

        public ApiPartnerBalancesRepository(RestClient restClient)
        {
            _restClient = restClient ?? throw new ArgumentNullException(nameof(restClient));
        }

        public async Task<IEnumerable<PartnerBalanceByTypeWithBalance>> GetByTypeAsync(DateTime dateFrom, DateTime dateTill, string partnerId, params string[] officeIds)
        {
            try
            {
                var queryParams = new List<string>
                {
                    $"from={dateFrom:yyyy-MM-ddTHH:mm:ssZ}",
                    $"till={dateTill:yyyy-MM-ddTHH:mm:ssZ}"
                };

                if (!string.IsNullOrEmpty(partnerId))
                    queryParams.Add($"partnerId={partnerId}");

                if (officeIds != null && officeIds.Length > 0)
                {
                    foreach (var offId in officeIds.Where(x => !string.IsNullOrWhiteSpace(x)))
                    {
                        queryParams.Add($"officeIds={offId}");
                    }
                }

                string url = "/api/partners/balances/by-type?" + string.Join("&", queryParams);

                var remote = await _restClient.GetAsync<List<PartnerBalanceByTypeWithBalance>>(url);
                if (remote != null)
                {
                    return remote;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Partner Balances Error]: {ex.Message}");
            }

            return Enumerable.Empty<PartnerBalanceByTypeWithBalance>();
        }

        public async Task<PartnerBalanceResult> GetBalanceToDateAsync(string officeId, string partnerId, DateTime date, string excludeTransactionId = null)
        {
            if (string.IsNullOrEmpty(officeId) || string.IsNullOrEmpty(partnerId))
                return new PartnerBalanceResult { Balance = 0m };

            try
            {
                var queryParams = new List<string>
        {
            $"officeId={officeId}",
            $"partnerId={partnerId}",
            $"date={date.ToUniversalTime():yyyy-MM-ddTHH:mm:ssZ}"
        };

                if (!string.IsNullOrEmpty(excludeTransactionId))
                    queryParams.Add($"excludeId={excludeTransactionId}");

                string url = "/api/partners/balance-to-date?" + string.Join("&", queryParams);

                var result = await _restClient.GetAsync<PartnerBalanceResult>(url);
                if (result != null)
                    return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GetBalanceToDate Error]: {ex.Message}");
            }

            return new PartnerBalanceResult { Balance = 0m };
        }

        public Task<PartnerBalanceAggregated> GetByTypeAggregatedAsync(string[] officeIds, DateTime dateFrom, DateTime dateTill)
        {
            return Task.FromResult(new PartnerBalanceAggregated());
        }
    }
}
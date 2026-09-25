using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mermer.CRM.Models;
using Mermer.CRM.Services;
using Mermer.Http;

namespace Mermer.Ui.Pc.Services
{
    public class ApiPartnerActionsRepository : IPartnerActionsRepository
    {
        private readonly RestClient _restClient;

        public ApiPartnerActionsRepository(RestClient restClient)
        {
            _restClient = restClient ?? throw new ArgumentNullException(nameof(restClient));
        }

        public async Task<int> CountAsync(DateTime? startDate, DateTime? endDate, string partnerId, params string[] officeIds)
        {
            try
            {
                var queryParams = BuildQuery(startDate, endDate, partnerId, officeIds);
                string url = "/api/partners/actions/count" + (queryParams.Any() ? "?" + string.Join("&", queryParams) : "");
                var res = await _restClient.GetAsync<CountResponse>(url);
                if (res != null) return res.Count;
            }
            catch { }

            var items = await GetAsync(startDate, endDate, partnerId, officeIds);
            return items.Count();
        }

        public async Task<IEnumerable<PartnerAction>> GetAsync(DateTime? startDate, DateTime? endDate, string partnerId, params string[] officeIds)
        {
            try
            {
                var queryParams = BuildQuery(startDate, endDate, partnerId, officeIds);
                string url = "/api/partners/actions" + (queryParams.Any() ? "?" + string.Join("&", queryParams) : "");

                var remote = await _restClient.GetAsync<List<PartnerAction>>(url);
                if (remote != null)
                {
                    return remote;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Partner Actions Error]: {ex.Message}");
            }

            return Enumerable.Empty<PartnerAction>();
        }

        public Task<Dictionary<string, PartnerActionInfo[]>> GetByPartnersAsync(string officeId, params string[] partners)
        {
            return Task.FromResult(new Dictionary<string, PartnerActionInfo[]>());
        }

        private static List<string> BuildQuery(DateTime? startDate, DateTime? endDate, string partnerId, string[] officeIds)
        {
            var queryParams = new List<string>();
            if (startDate.HasValue) queryParams.Add($"from={startDate.Value.ToUniversalTime():yyyy-MM-ddTHH:mm:ssZ}");
            if (endDate.HasValue) queryParams.Add($"till={endDate.Value.ToUniversalTime():yyyy-MM-ddTHH:mm:ssZ}");
            if (!string.IsNullOrEmpty(partnerId)) queryParams.Add($"partnerId={partnerId}");

            if (officeIds != null && officeIds.Any())
            {
                foreach (var offId in officeIds.Where(o => !string.IsNullOrEmpty(o)))
                {
                    queryParams.Add($"officeId={offId}");
                }
            }

            return queryParams;
        }

        private class CountResponse
        {
            public int Count { get; set; }
        }
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mermer.FundsManagement.Models;
using Mermer.FundsManagement.Services;
using Mermer.Http;

namespace Mermer.Ui.Pc.Services;

public class ApiFundsActionRepository : IFundsActionsRepository
{
    private readonly RestClient _restClient;

    public ApiFundsActionRepository(RestClient restClient)
    {
        _restClient = restClient ?? throw new ArgumentNullException(nameof(restClient));
    }

    // Реализация оригинального метода интерфейса IFundsActionsRepository
    public Task<int> CountAsync(DateTime? startDate, DateTime? endDate, string currencyId, params string[] depositoryIds)
    {
        return CountAsync(startDate, endDate, currencyId, null, depositoryIds);
    }

    // Расширенный метод с поддержкой isCompleted
    public async Task<int> CountAsync(DateTime? startDate, DateTime? endDate, string currencyId, bool? isCompleted, params string[] depositoryIds)
    {
        try
        {
            var query = BuildQueryString(startDate, endDate, currencyId, isCompleted, depositoryIds);
            var res = await _restClient.GetAsync<CountResponse>($"/api/finance/actions/count{query}");
            if (res != null) return res.Count;
        }
        catch { }

        var list = await GetAsync(startDate, endDate, currencyId, isCompleted, depositoryIds);
        return list.Count();
    }

    // Реализация оригинального метода интерфейса IFundsActionsRepository
    public Task<IEnumerable<FundsAction>> GetAsync(DateTime? startDate, DateTime? endDate, string currencyId, params string[] depositoryIds)
    {
        return GetAsync(startDate, endDate, currencyId, null, depositoryIds);
    }

    // Расширенный метод с поддержкой isCompleted
    public async Task<IEnumerable<FundsAction>> GetAsync(DateTime? startDate, DateTime? endDate, string currencyId, bool? isCompleted, params string[] depositoryIds)
    {
        try
        {
            string url = "/api/finance/actions" + BuildQueryString(startDate, endDate, currencyId, isCompleted, depositoryIds);
            var remote = await _restClient.GetAsync<List<FundsAction>>(url);
            return remote ?? Enumerable.Empty<FundsAction>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FUNDS ACTIONS FETCH ERROR]: {ex.Message}");
            return Enumerable.Empty<FundsAction>();
        }
    }

    private static string BuildQueryString(DateTime? startDate, DateTime? endDate, string currencyId, bool? isCompleted, string[] depositoryIds)
    {
        var queryParams = new List<string>();

        if (startDate.HasValue) queryParams.Add($"from={startDate.Value.ToUniversalTime():yyyy-MM-ddTHH:mm:ssZ}");
        if (endDate.HasValue) queryParams.Add($"till={endDate.Value.ToUniversalTime():yyyy-MM-ddTHH:mm:ssZ}");
        if (!string.IsNullOrEmpty(currencyId)) queryParams.Add($"currencyId={currencyId}");
        if (isCompleted.HasValue) queryParams.Add($"isCompleted={isCompleted.Value.ToString().ToLower()}");

        if (depositoryIds != null && depositoryIds.Any())
        {
            foreach (var depId in depositoryIds.Where(d => !string.IsNullOrEmpty(d)))
            {
                queryParams.Add($"depositoryId={depId.Trim()}");
            }
        }

        return queryParams.Any() ? "?" + string.Join("&", queryParams) : "";
    }

    private class CountResponse
    {
        public int Count { get; set; }
    }
}
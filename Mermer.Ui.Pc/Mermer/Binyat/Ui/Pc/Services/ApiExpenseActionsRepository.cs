using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mermer.Finance.Spending.Models;
using Mermer.Finance.Spending.Services;
using Mermer.Http;

namespace Mermer.Ui.Pc.Services;

public class ApiExpenseActionsRepository : IExpenseActionsRepository
{
    private readonly RestClient _restClient;

    public ApiExpenseActionsRepository(RestClient restClient)
    {
        _restClient = restClient ?? throw new ArgumentNullException(nameof(restClient));
    }

    public async Task<int> CountAsync(DateTime? startDate, DateTime? endDate, string[] depositoryIds, string expenseId)
    {
        try
        {
            var query = BuildQueryString(startDate, endDate, depositoryIds, expenseId);
            var res = await _restClient.GetAsync<CountResponse>($"/api/spending/actions/count{query}");
            if (res != null) return res.Count;
        }
        catch { }

        var result = await GetAsync(startDate, endDate, depositoryIds, expenseId);
        return result.Count();
    }

    public async Task<IEnumerable<ExpenseAction>> GetAsync(DateTime? startDate, DateTime? endDate, string[] depositoryIds, string expenseId)
    {
        try
        {
            string url = "/api/spending/actions" + BuildQueryString(startDate, endDate, depositoryIds, expenseId);
            var remote = await _restClient.GetAsync<List<ExpenseAction>>(url);
            return remote ?? Enumerable.Empty<ExpenseAction>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[EXPENSE ACTIONS FETCH ERROR]: {ex.Message}");
            return Enumerable.Empty<ExpenseAction>();
        }
    }

    private static string BuildQueryString(DateTime? startDate, DateTime? endDate, string[] depositoryIds, string expenseId)
    {
        var queryParams = new List<string>();

        if (startDate.HasValue) queryParams.Add($"from={startDate.Value.ToUniversalTime():yyyy-MM-ddTHH:mm:ssZ}");
        if (endDate.HasValue) queryParams.Add($"till={endDate.Value.ToUniversalTime():yyyy-MM-ddTHH:mm:ssZ}");
        if (!string.IsNullOrEmpty(expenseId) && expenseId != "null") queryParams.Add($"expenseId={expenseId}");

        if (depositoryIds != null && depositoryIds.Any())
        {
            foreach (var depId in depositoryIds.Where(d => !string.IsNullOrEmpty(d)))
            {
                queryParams.Add($"depositoryId={depId}");
            }
        }

        return queryParams.Any() ? "?" + string.Join("&", queryParams) : "";
    }

    private class CountResponse
    {
        public int Count { get; set; }
    }
}

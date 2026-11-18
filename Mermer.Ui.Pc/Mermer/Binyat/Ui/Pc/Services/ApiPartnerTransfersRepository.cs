using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Mermer.CRM.Models;
using Mermer.Data.Storage;
using Mermer.Http;

namespace Mermer.Ui.Pc.Services;

public class ApiPartnerTransfersRepository : IRepositoryWithFacets<PartnerTransfer>, IRepository<PartnerTransfer>, IReadOnlyRepository<PartnerTransfer>
{
    private readonly RestClient _restClient;
    private static readonly ConcurrentDictionary<string, PartnerTransfer> _cardCache = new(StringComparer.OrdinalIgnoreCase);

    public ApiPartnerTransfersRepository(RestClient restClient)
    {
        _restClient = restClient ?? throw new Exception("Http RestClient cannot be null.");
    }

    public async Task<PartnerTransfer> GetAsync(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        if (_cardCache.TryGetValue(id, out var cached))
            return cached;

        await GetAllAsync();
        if (_cardCache.TryGetValue(id, out cached))
            return cached;

        try
        {
            var remote = await _restClient.GetAsync<PartnerTransfer>($"/api/partners/transfers/{id}");
            if (remote != null)
            {
                _cardCache[remote.Id] = remote;
                return remote;
            }
        }
        catch { }

        return null;
    }

    public async Task<IEnumerable<PartnerTransfer>> GetAsync(string[] ids)
    {
        if (ids == null || !ids.Any()) return Enumerable.Empty<PartnerTransfer>();
        var result = new List<PartnerTransfer>();
        foreach (var id in ids)
        {
            var item = await GetAsync(id);
            if (item != null) result.Add(item);
        }
        return result;
    }

    public async Task<IEnumerable<PartnerTransfer>> GetAsync(params Expression<Func<PartnerTransfer, bool>>[] predicates)
    {
        var (hasDates, from, till) = TryExtractDateRange(predicates);
        bool isConflictedFilter = HasConflictedPredicate(predicates);

        try
        {
            string url = "/api/partners/transfers";
            var queryParams = new List<string>();

            if (hasDates)
            {// СТАЛО:
                var fromStr = from.ToString("yyyy-MM-ddTHH:mm:ss");
                var tillStr = till.ToString("yyyy-MM-ddTHH:mm:ss");
                queryParams.Add($"from={fromStr}&till={tillStr}");
            }

            if (isConflictedFilter)
            {
                queryParams.Add("conflicted=true");
            }

            queryParams.Add("limit=5000");
            url += "?" + string.Join("&", queryParams);

            var remoteSlice = await _restClient.GetAsync<List<PartnerTransfer>>(url);
            if (remoteSlice != null)
            {
                var query = remoteSlice.AsQueryable();
                if (predicates != null)
                {
                    foreach (var p in predicates.Where(x => x != null)) query = query.Where(p);
                }
                return query.ToList();
            }
        }
        catch { }

        var all = await GetAllAsync();
        var fallbackQuery = all.AsQueryable();
        if (predicates != null)
        {
            foreach (var p in predicates.Where(x => x != null)) fallbackQuery = fallbackQuery.Where(p);
        }
        return fallbackQuery.ToList();
    }

    public async Task<int> CountAsync(params Expression<Func<PartnerTransfer, bool>>[] predicates)
    {
        var (hasDates, from, till) = TryExtractDateRange(predicates);
        bool isConflictedFilter = HasConflictedPredicate(predicates);

        try
        {
            string url = "/api/partners/transfers/count";
            var queryParams = new List<string>();

            if (hasDates)
            {
                // СТАЛО:
                var fromStr = from.ToString("yyyy-MM-ddTHH:mm:ss");
                var tillStr = till.ToString("yyyy-MM-ddTHH:mm:ss");
                queryParams.Add($"from={fromStr}&till={tillStr}");
            }

            if (isConflictedFilter)
            {
                queryParams.Add("conflicted=true");
            }

            if (queryParams.Any())
            {
                url += "?" + string.Join("&", queryParams);
            }

            var res = await _restClient.GetAsync<CountResponse>(url);
            if (res != null) return res.Count;
        }
        catch { }

        var items = await GetAsync(predicates);
        return items.Count();
    }

    private static bool HasConflictedPredicate(Expression<Func<PartnerTransfer, bool>>[] predicates)
    {
        if (predicates == null || predicates.Length == 0) return false;

        foreach (var pred in predicates.Where(p => p != null))
        {
            var text = pred.ToString();
            if (text.IndexOf("IsConflicted", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    public async Task<IEnumerable<PartnerTransfer>> GetAllAsync()
    {
        if (_cardCache.Count > 0)
            return _cardCache.Values.ToList();

        try
        {
            var remote = await _restClient.GetAsync<List<PartnerTransfer>>("/api/partners/transfers?limit=5000");
            if (remote != null)
            {
                foreach (var item in remote)
                {
                    if (!string.IsNullOrEmpty(item?.Id))
                    {
                        _cardCache[item.Id] = item;
                    }
                }
            }
        }
        catch { }

        return _cardCache.Values.ToList();
    }

    public Task CreateAsync(PartnerTransfer model) => SaveAsync(model);
    public Task UpdateAsync(PartnerTransfer model) => SaveAsync(model);

    public async Task SaveAsync(PartnerTransfer model)
    {
        if (model == null)
        {
            throw new Exception("Cannot save an empty PartnerTransfer entity.");
        }

        bool isNew = string.IsNullOrEmpty(model.Id) || model.Id == Guid.Empty.ToString();
        if (isNew) model.Id = Guid.NewGuid().ToString();

        _cardCache[model.Id] = model;

        try
        {
            if (isNew) await _restClient.PostAsync("/api/partners/transfers", model);
            else await _restClient.PutAsync($"/api/partners/transfers/{model.Id}", model);
        }
        catch (Exception ex)
        {
            var detail = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            throw new Exception(string.Format("Failed to save PartnerTransfer '{0}'. Details: {1}", model.Id, detail), ex);
        }

        _cardCache.Clear();
        UiEventBus.NotifyDocumentModified<PartnerTransfer>(model.Id);
    }

    public async Task DeleteAsync(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            throw new Exception("PartnerTransfer identifier cannot be empty for delete operation.");
        }

        _cardCache.TryRemove(id, out _);

        try
        {
            await _restClient.DeleteAsync($"/api/partners/transfers/{id}");
        }
        catch (Exception ex)
        {
            var detail = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            throw new Exception(string.Format("Failed to delete PartnerTransfer '{0}'. Details: {1}", id, detail), ex);
        }

        UiEventBus.NotifyDocumentModified<PartnerTransfer>(id);
    }

    public async Task<Dictionary<string, Dictionary<string, int>>> GetFacets(params string[] fields)
    {
        var dict = new Dictionary<string, Dictionary<string, int>>();
        if (fields != null)
        {
            foreach (var f in fields) dict[f] = new Dictionary<string, int>();
        }

        try
        {
            var fieldsParam = fields != null && fields.Length > 0 ? string.Join(",", fields) : "Date";
            var apiResult = await _restClient.GetAsync<Dictionary<string, Dictionary<string, int>>>($"/api/partners/transfers/facets?fields={fieldsParam}");
            if (apiResult != null)
            {
                foreach (var kvp in apiResult) dict[kvp.Key] = kvp.Value;
            }
        }
        catch { }

        return dict;
    }

    private static (bool HasDates, DateTime From, DateTime Till) TryExtractDateRange(Expression<Func<PartnerTransfer, bool>>[] predicates)
    {
        if (predicates == null || predicates.Length == 0)
            return (false, default, default);

        DateTime from = DateTime.MinValue;
        DateTime till = DateTime.MaxValue;
        bool found = false;

        foreach (var pred in predicates.Where(p => p != null))
        {
            try
            {
                ExtractDatesFromExpression(pred.Body, ref from, ref till, ref found);
            }
            catch { }
        }

        return (found, from, till);
    }

    private static void ExtractDatesFromExpression(Expression expr, ref DateTime from, ref DateTime till, ref bool found)
    {
        if (expr is BinaryExpression bin)
        {
            if (bin.NodeType == ExpressionType.AndAlso || bin.NodeType == ExpressionType.And)
            {
                ExtractDatesFromExpression(bin.Left, ref from, ref till, ref found);
                ExtractDatesFromExpression(bin.Right, ref from, ref till, ref found);
                return;
            }

            if (bin.NodeType == ExpressionType.GreaterThanOrEqual || bin.NodeType == ExpressionType.GreaterThan)
            {
                var val = EvaluateExpression(bin.Right);
                if (val is DateTime dt) { from = dt; found = true; }
            }
            else if (bin.NodeType == ExpressionType.LessThanOrEqual || bin.NodeType == ExpressionType.LessThan)
            {
                var val = EvaluateExpression(bin.Right);
                if (val is DateTime dt) { till = dt; found = true; }
            }
        }
    }

    private static object EvaluateExpression(Expression expr)
    {
        if (expr is ConstantExpression ce) return ce.Value;
        if (expr is MemberExpression me)
        {
            var target = EvaluateExpression(me.Expression);
            if (me.Member is System.Reflection.FieldInfo fi) return fi.GetValue(target);
            if (me.Member is System.Reflection.PropertyInfo pi) return pi.GetValue(target);
        }
        var lambda = Expression.Lambda(expr);
        return lambda.Compile().DynamicInvoke();
    }

    private class CountResponse
    {
        public int Count { get; set; }
    }
}
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Mermer.Data.Storage;
using Mermer.Finance.Models;
using Mermer.Http;

namespace Mermer.Ui.Pc.Services;

public class ApiFundsSlipsRepository : IRepositoryWithFacets<FundsSlip>, IRepository<FundsSlip>, IReadOnlyRepository<FundsSlip>
{
    private readonly RestClient _restClient;
    private static readonly ConcurrentDictionary<string, FundsSlip> _cardCache = new(StringComparer.OrdinalIgnoreCase);

    public ApiFundsSlipsRepository(RestClient restClient)
    {
        _restClient = restClient ?? throw new Exception("Http RestClient cannot be null.");
    }

    public async Task<FundsSlip> GetAsync(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        if (_cardCache.TryGetValue(id, out var cached))
            return cached;

        try
        {
            var remote = await _restClient.GetAsync<FundsSlip>($"/api/finance/slips/{id}");
            if (remote != null)
            {
                _cardCache[remote.Id] = remote;
                return remote;
            }
        }
        catch { }

        return null;
    }

    public async Task<IEnumerable<FundsSlip>> GetAsync(string[] ids)
    {
        if (ids == null || !ids.Any()) return Enumerable.Empty<FundsSlip>();
        var result = new List<FundsSlip>();
        foreach (var id in ids)
        {
            var item = await GetAsync(id);
            if (item != null) result.Add(item);
        }
        return result;
    }

    public async Task<IEnumerable<FundsSlip>> GetAsync(params Expression<Func<FundsSlip, bool>>[] predicates)
    {
        var (hasDates, from, till) = TryExtractDateRange(predicates);

        try
        {
            string url = "/api/finance/slips";
            if (hasDates)
            {
                // СТАЛО:
                var fromStr = from.ToString("yyyy-MM-ddTHH:mm:ss");
                var tillStr = till.ToString("yyyy-MM-ddTHH:mm:ss");
                url += $"?from={fromStr}&till={tillStr}";
            }

            var remoteSlice = await _restClient.GetAsync<List<FundsSlip>>(url);
            if (remoteSlice != null)
            {
                var query = remoteSlice.AsQueryable();
                if (predicates != null)
                {
                    foreach (var p in predicates.Where(x => x != null))
                        query = query.Where(p);
                }
                return query.ToList();
            }
        }
        catch { }

        var all = await GetAllAsync();
        var fallbackQuery = all.AsQueryable();
        if (predicates != null)
        {
            foreach (var p in predicates.Where(x => x != null))
                fallbackQuery = fallbackQuery.Where(p);
        }
        return fallbackQuery.ToList();
    }

    public async Task<int> CountAsync(params Expression<Func<FundsSlip, bool>>[] predicates)
    {
        var (hasDates, from, till) = TryExtractDateRange(predicates);

        try
        {
            string url = "/api/finance/slips/count";
            if (hasDates)
            {
                // СТАЛО:
                var fromStr = from.ToString("yyyy-MM-ddTHH:mm:ss");
                var tillStr = till.ToString("yyyy-MM-ddTHH:mm:ss");
                url += $"?from={fromStr}&till={tillStr}";
            }

            var res = await _restClient.GetAsync<CountResponse>(url);
            if (res != null) return res.Count;
        }
        catch { }

        var items = await GetAsync(predicates);
        return items.Count();
    }

    public async Task<IEnumerable<FundsSlip>> GetAllAsync()
    {
        if (_cardCache.Count > 0)
            return _cardCache.Values.ToList();

        try
        {
            var remote = await _restClient.GetAsync<List<FundsSlip>>("/api/finance/slips");
            if (remote != null)
            {
                foreach (var item in remote)
                {
                    if (!string.IsNullOrEmpty(item?.Id))
                        _cardCache[item.Id] = item;
                }
            }
        }
        catch { }

        return _cardCache.Values.ToList();
    }

    public Task CreateAsync(FundsSlip model) => SaveAsync(model);
    public Task UpdateAsync(FundsSlip model) => SaveAsync(model);

    public async Task SaveAsync(FundsSlip model)
    {
        if (model == null)
        {
            throw new Exception("Cannot save an empty FundsSlip entity.");
        }

        bool isNew = string.IsNullOrEmpty(model.Id) || model.Id == Guid.Empty.ToString();
        if (isNew) model.Id = Guid.NewGuid().ToString();

        _cardCache[model.Id] = model;

        try
        {
            if (isNew)
                await _restClient.PostAsync("/api/finance/slips", model);
            else
                await _restClient.PutAsync($"/api/finance/slips/{model.Id}", model);
        }
        catch (Exception ex)
        {
            var detail = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            throw new Exception(string.Format("Failed to save FundsSlip '{0}'. Details: {1}", model.Id, detail), ex);
        }

        UiEventBus.NotifyDocumentModified<FundsSlip>(model.Id);
    }

    public async Task DeleteAsync(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            throw new Exception("FundsSlip identifier cannot be empty for delete operation.");
        }

        _cardCache.TryRemove(id, out _);

        try
        {
            await _restClient.DeleteAsync($"/api/finance/slips/{id}");
        }
        catch (Exception ex)
        {
            var detail = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            throw new Exception(string.Format("Failed to delete FundsSlip '{0}'. Details: {1}", id, detail), ex);
        }

        UiEventBus.NotifyDocumentModified<FundsSlip>(id);
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
            var apiResult = await _restClient.GetAsync<Dictionary<string, Dictionary<string, int>>>($"/api/finance/slips/facets?fields={fieldsParam}");
            if (apiResult != null)
            {
                foreach (var kvp in apiResult) dict[kvp.Key] = kvp.Value;
            }
        }
        catch { }

        return dict;
    }

    private static (bool HasDates, DateTime From, DateTime Till) TryExtractDateRange(Expression<Func<FundsSlip, bool>>[] predicates)
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
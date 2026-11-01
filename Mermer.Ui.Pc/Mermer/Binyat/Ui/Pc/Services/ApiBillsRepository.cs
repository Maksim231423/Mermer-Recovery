using DevExpress.Xpf.Bars;
using Mermer.Commerce.Models;
using Mermer.Data.Storage;
using Mermer.Http;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace Mermer.Ui.Pc.Services;

public class ApiBillsRepository : IRepositoryWithFacets<Bill>, IRepository<Bill>, IReadOnlyRepository<Bill>
{
    private readonly RestClient _restClient;

    // Оперативный кэш открытых карточек в памяти
    private static readonly ConcurrentDictionary<string, Bill> _cardCache = new(StringComparer.OrdinalIgnoreCase);

    public ApiBillsRepository(RestClient restClient)
    {
        _restClient = restClient ?? throw new Exception("Http RestClient cannot be null.");
    }

    public async Task<Bill> GetAsync(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        if (_cardCache.TryGetValue(id, out var cached))
            return cached;

        try
        {
            var remote = await _restClient.GetAsync<Bill>($"/api/bills/{id}");
            if (remote != null)
            {
                _cardCache[remote.Id] = remote;
                return remote;
            }
        }
        catch { }

        return null;
    }

    public async Task<IEnumerable<Bill>> GetAsync(string[] ids)
    {
        if (ids == null || !ids.Any()) return Enumerable.Empty<Bill>();
        var result = new List<Bill>();
        foreach (var id in ids)
        {
            var item = await GetAsync(id);
            if (item != null) result.Add(item);
        }
        return result;
    }

    public async Task<IEnumerable<Bill>> GetAsync(params Expression<Func<Bill, bool>>[] predicates)
    {
        var (hasDates, from, till) = TryExtractDateRange(predicates);

        if (hasDates)
        {
            try
            {
                var fromStr = from.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
                var tillStr = till.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");

                var remoteSlice = await _restClient.GetAsync<List<Bill>>($"/api/bills?from={fromStr}&till={tillStr}&limit=50000");
                if (remoteSlice != null)
                {
                    foreach (var b in remoteSlice)
                    {
                        if (!string.IsNullOrEmpty(b?.Id)) _cardCache[b.Id] = b;
                    }

                    var query = remoteSlice.AsQueryable();
                    if (predicates != null)
                    {
                        foreach (var p in predicates.Where(x => x != null)) query = query.Where(p);
                    }
                    return query.OrderByDescending(b => b.Date).ToList();
                }
            }
            catch { }
        }

        var all = await GetAllAsync();
        var fallbackQuery = all.AsQueryable();
        if (predicates != null)
        {
            foreach (var p in predicates.Where(x => x != null)) fallbackQuery = fallbackQuery.Where(p);
        }
        return fallbackQuery.OrderByDescending(b => b.Date).ToList();
    }

    public async Task<int> CountAsync(params Expression<Func<Bill, bool>>[] predicates)
    {
        var (hasDates, from, till) = TryExtractDateRange(predicates);

        if (hasDates)
        {
            try
            {
                var fromStr = from.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
                var tillStr = till.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
                var res = await _restClient.GetAsync<CountResponse>($"/api/bills/count?from={fromStr}&till={tillStr}");
                if (res != null) return res.Count;
            }
            catch { }
        }
        else
        {
            try
            {
                var res = await _restClient.GetAsync<CountResponse>("/api/bills/count");
                if (res != null && res.Count > 0) return res.Count;
            }
            catch { }
        }

        var result = await GetAsync(predicates);
        return result.Count();
    }

    public async Task<IEnumerable<Bill>> GetAllAsync()
    {
        if (_cardCache.Count > 0)
        {
            return _cardCache.Values.OrderByDescending(b => b.Date).ToList();
        }

        try
        {
            var remote = await _restClient.GetAsync<List<Bill>>("/api/bills?limit=50000");
            if (remote != null && remote.Any())
            {
                foreach (var item in remote)
                {
                    if (!string.IsNullOrEmpty(item?.Id))
                    {
                        _cardCache[item.Id] = item;
                    }
                }
                return _cardCache.Values.OrderByDescending(b => b.Date).ToList();
            }
        }
        catch { }

        return _cardCache.Values.OrderByDescending(b => b.Date).ToList();
    }

    public Task CreateAsync(Bill model) => SaveAsync(model);
    public Task UpdateAsync(Bill model) => SaveAsync(model);

    public async Task<Bill> SaveAsync(Bill entity)
    {
        if (entity == null)
        {
            throw new Exception("Cannot save an empty Bill entity.");
        }

        bool isNew = string.IsNullOrEmpty(entity.Id) || entity.Id == Guid.Empty.ToString();
        if (isNew) entity.Id = Guid.NewGuid().ToString();

        _cardCache[entity.Id] = entity;

        try
        {
            if (isNew)
            {
                await _restClient.PostAsync("/api/bills", entity);
            }
            else
            {
                await _restClient.PutAsync($"/api/bills/{entity.Id}", entity);
            }
        }
        catch (Exception ex)
        {
            var detail = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            throw new Exception(string.Format("Failed to save Bill '{0}'. Details: {1}", entity.Id, detail), ex);
        }

        UiEventBus.NotifyDocumentModified<Bill>(entity.Id);
        return entity;
    }

    public async Task DeleteAsync(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            throw new Exception("Bill identifier cannot be empty for delete operation.");
        }

        _cardCache.TryRemove(id, out _);

        try
        {
            await _restClient.DeleteAsync($"/api/bills/{id}");
        }
        catch (Exception ex)
        {
            var detail = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            throw new Exception(string.Format("Failed to delete Bill '{0}'. Details: {1}", id, detail), ex);
        }

        UiEventBus.NotifyDocumentModified<Bill>(id);
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
            var apiResult = await _restClient.GetAsync<Dictionary<string, Dictionary<string, int>>>($"/api/bills/facets?fields={fieldsParam}");
            if (apiResult != null)
            {
                foreach (var kvp in apiResult) dict[kvp.Key] = kvp.Value;
            }
        }
        catch { }

        return dict;
    }

    private static (bool HasDates, DateTime From, DateTime Till) TryExtractDateRange(Expression<Func<Bill, bool>>[] predicates)
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
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Mermer.Data.Storage;
using Mermer.Http;
using Mermer.Warehousing.Models;

namespace Mermer.Ui.Pc.Services;

public class ApiStockSlipsRepository : IRepository<StockSlip>, IReadOnlyRepository<StockSlip>, IRepositoryWithFacets<StockSlip>
{
    private readonly RestClient _restClient;
    private const string DocType = "StockSlip";

    private static readonly ConcurrentDictionary<string, StockSlip> _cardCache = new(StringComparer.OrdinalIgnoreCase);

    public ApiStockSlipsRepository(RestClient restClient)
    {
        _restClient = restClient ?? throw new ArgumentNullException(nameof(restClient));
    }

    public async Task<StockSlip> GetAsync(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        if (_cardCache.TryGetValue(id, out var cached) && cached.Lines != null && cached.Lines.Any())
            return cached;

        try
        {
            var remote = await _restClient.GetAsync<StockSlip>($"/api/catalog/slips/{id}");
            if (remote != null)
            {
                _cardCache[remote.Id] = remote;
                LocalSqliteCache.SaveDocument(DocType, remote.Id, remote, isSynced: true);
                return remote;
            }
        }
        catch { }

        var local = LocalSqliteCache.GetAllDocuments<StockSlip>(DocType)?
            .FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

        return local;
    }

    public async Task<IEnumerable<StockSlip>> GetAsync(string[] ids)
    {
        if (ids == null || !ids.Any()) return Enumerable.Empty<StockSlip>();
        var result = new List<StockSlip>();
        foreach (var id in ids)
        {
            var item = await GetAsync(id);
            if (item != null) result.Add(item);
        }
        return result;
    }

    // --- БЫСТРАЯ ВЫБОРКА ПО ДАТАМ (ПРЯМОЙ СРЕЗ С СЕРВЕРА) ---
    public async Task<IEnumerable<StockSlip>> GetAsync(params Expression<Func<StockSlip, bool>>[] predicates)
    {
        var (hasDates, from, till) = TryExtractDateRange(predicates);

        if (hasDates)
        {
            try
            {
                var fromStr = from.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
                var tillStr = till.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");

                var remoteSlice = await _restClient.GetAsync<List<StockSlip>>($"/api/catalog/slips?from={fromStr}&till={tillStr}");
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
        }

        var all = await GetAllAsync();
        var fallbackQuery = all.AsQueryable();
        if (predicates != null)
        {
            foreach (var p in predicates.Where(x => x != null)) fallbackQuery = fallbackQuery.Where(p);
        }
        return fallbackQuery.ToList();
    }

    public async Task<int> CountAsync(params Expression<Func<StockSlip, bool>>[] predicates)
    {
        var (hasDates, from, till) = TryExtractDateRange(predicates);

        if (hasDates)
        {
            try
            {
                var fromStr = from.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
                var tillStr = till.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");

                var res = await _restClient.GetAsync<CountResponse>($"/api/catalog/slips/count?from={fromStr}&till={tillStr}");
                if (res != null) return res.Count;
            }
            catch { }
        }

        var items = await GetAsync(predicates);
        return items.Count();
    }

    public async Task<IEnumerable<StockSlip>> GetAllAsync()
    {
        if (_cardCache.Count > 0)
            return _cardCache.Values.ToList();

        var local = LocalSqliteCache.GetAllDocuments<StockSlip>(DocType);
        if (local != null)
        {
            foreach (var item in local)
            {
                if (!string.IsNullOrEmpty(item?.Id)) _cardCache[item.Id] = item;
            }
        }

        return _cardCache.Values.ToList();
    }

    public Task CreateAsync(StockSlip entity) => SaveAsync(entity);
    public Task UpdateAsync(StockSlip entity) => SaveAsync(entity);

    public async Task SaveAsync(StockSlip entity)
    {
        if (entity == null) return;
        if (string.IsNullOrEmpty(entity.Id)) entity.Id = Guid.NewGuid().ToString();

        _cardCache[entity.Id] = entity;
        LocalSqliteCache.SaveDocument(DocType, entity.Id, entity, isSynced: false);

        _ = Task.Run(async () =>
        {
            try
            {
                await _restClient.PostAsync("/api/catalog/slips", entity);
                LocalSqliteCache.SaveDocument(DocType, entity.Id, entity, isSynced: true);
            }
            catch { }
        });
    }

    public async Task DeleteAsync(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        _cardCache.TryRemove(id, out _);

        _ = Task.Run(async () =>
        {
            try { await _restClient.DeleteAsync($"/api/catalog/slips/{id}"); } catch { }
        });
    }

    public async Task<Dictionary<string, Dictionary<string, int>>> GetFacets(params string[] fields)
    {
        var dict = new Dictionary<string, Dictionary<string, int>>();
        if (fields != null)
            foreach (var f in fields) dict[f] = new Dictionary<string, int>();

        try
        {
            var fieldsParam = fields != null && fields.Length > 0 ? string.Join(",", fields) : "Date";
            var apiResult = await _restClient.GetAsync<Dictionary<string, Dictionary<string, int>>>($"/api/catalog/slips/facets?fields={fieldsParam}");
            if (apiResult != null)
            {
                foreach (var kvp in apiResult) dict[kvp.Key] = kvp.Value;
            }
        }
        catch { }

        return dict;
    }

    private static (bool HasDates, DateTime From, DateTime Till) TryExtractDateRange(Expression<Func<StockSlip, bool>>[] predicates)
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
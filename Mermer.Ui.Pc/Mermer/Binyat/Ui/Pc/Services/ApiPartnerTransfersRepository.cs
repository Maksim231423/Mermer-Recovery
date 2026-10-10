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
    private const string DocType = "PartnerTransfer";

    private static readonly ConcurrentDictionary<string, PartnerTransfer> _cardCache = new(StringComparer.OrdinalIgnoreCase);

    public ApiPartnerTransfersRepository(RestClient restClient)
    {
        _restClient = restClient ?? throw new ArgumentNullException(nameof(restClient));
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
                LocalSqliteCache.SaveDocument(DocType, remote.Id, remote, isSynced: true);
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

    // --- БЫСТРАЯ ВЫБОРКА ПО ДАТАМ (ПРЯМОЙ ЗАПРОС СРЕЗА) ---
    public async Task<IEnumerable<PartnerTransfer>> GetAsync(params Expression<Func<PartnerTransfer, bool>>[] predicates)
    {
        var (hasDates, from, till) = TryExtractDateRange(predicates);

        if (hasDates)
        {
            try
            {
                var fromStr = from.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
                var tillStr = till.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");

                var remoteSlice = await _restClient.GetAsync<List<PartnerTransfer>>($"/api/partners/transfers?from={fromStr}&till={tillStr}&limit=5000");
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

        // Для фильтра All Time запрашиваем список через GetAllAsync (который гарантированно подгрузит данные с бэкенда)
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

        try
        {
            string url = "/api/partners/transfers/count";
            if (hasDates)
            {
                var fromStr = from.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
                var tillStr = till.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
                url += $"?from={fromStr}&till={tillStr}";
            }

            var res = await _restClient.GetAsync<CountResponse>(url);
            if (res != null) return res.Count;
        }
        catch { }

        var items = await GetAsync(predicates);
        return items.Count();
    }

    public async Task<IEnumerable<PartnerTransfer>> GetAllAsync()
    {
        if (_cardCache.Count > 0)
            return _cardCache.Values.ToList();

        // 1. Пытаемся взять из локального кэша
        var local = LocalSqliteCache.GetAllDocuments<PartnerTransfer>(DocType);
        if (local != null && local.Any())
        {
            foreach (var item in local)
            {
                if (!string.IsNullOrEmpty(item?.Id)) _cardCache[item.Id] = item;
            }
            return _cardCache.Values.ToList();
        }

        // 2. Если в кэше пусто — подгружаем с сервера
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
                        LocalSqliteCache.SaveDocument(DocType, item.Id, item, isSynced: true);
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
        if (model == null) return;
        if (string.IsNullOrEmpty(model.Id)) model.Id = Guid.NewGuid().ToString();

        _cardCache[model.Id] = model;
        LocalSqliteCache.SaveDocument(DocType, model.Id, model, isSynced: false);

        // Мгновенно обновляем список взаиморасчетов/переводов контрагентов
        UiEventBus.NotifyDocumentModified<PartnerTransfer>(model.Id);

        _ = Task.Run(async () =>
        {
            try
            {
                await _restClient.PostAsync("/api/partners/transfers", model);
                LocalSqliteCache.SaveDocument(DocType, model.Id, model, isSynced: true);
                UiEventBus.NotifyDocumentModified<PartnerTransfer>(model.Id);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PARTNER TRANSFER SYNC ERROR]: {ex.Message}");
            }
        });
    }

    public async Task DeleteAsync(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        _cardCache.TryRemove(id, out _);

        // Мгновенно удаляем из открытого списка в UI
        UiEventBus.NotifyDocumentModified<PartnerTransfer>(id);

        _ = Task.Run(async () =>
        {
            try { await _restClient.DeleteAsync($"/api/partners/transfers/{id}"); } catch { }
        });
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

    // --- ПАРСЕР ДАТ ИЗ EXPRESSION-ДЕРЕВА MVVMCROSS ---
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
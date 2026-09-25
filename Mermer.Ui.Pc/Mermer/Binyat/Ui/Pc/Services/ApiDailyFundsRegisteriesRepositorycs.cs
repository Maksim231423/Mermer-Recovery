using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Mermer.Data.Storage;
using Mermer.Finance.DailyRegistery.Models;
using Mermer.Finance.DailyRegistery.Services;
using Mermer.Http;

namespace Mermer.Ui.Pc.Services;

public class ApiDailyFundsRegisteriesRepository :
    IRepositoryWithFacets<DailyFundsRegistery>,
    IRepository<DailyFundsRegistery>,
    IReadOnlyRepository<DailyFundsRegistery>,
    IDailyFundsRegisteriesRepository
{
    private readonly RestClient _restClient;
    private const string DocType = "DailyFundsRegistery";

    // In-memory кэш открытых карточек для моментального O(1) доступа
    private static readonly ConcurrentDictionary<string, DailyFundsRegistery> _cardCache = new(StringComparer.OrdinalIgnoreCase);

    public ApiDailyFundsRegisteriesRepository(RestClient restClient)
    {
        _restClient = restClient ?? throw new ArgumentNullException(nameof(restClient));
    }

    public async Task<DailyFundsRegistery> GetAsync(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        if (_cardCache.TryGetValue(id, out var cached))
            return cached;

        try
        {
            var remote = await _restClient.GetAsync<DailyFundsRegistery>($"/api/finance/registeries/{id}");
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

    public async Task<IEnumerable<DailyFundsRegistery>> GetAsync(string[] ids)
    {
        if (ids == null || !ids.Any()) return Enumerable.Empty<DailyFundsRegistery>();
        var result = new List<DailyFundsRegistery>();
        foreach (var id in ids)
        {
            var item = await GetAsync(id);
            if (item != null) result.Add(item);
        }
        return result;
    }

    // --- БЫСТРАЯ ВЫБОРКА ПО ДАТАМ (БЕЗ ВЫГРУЗКИ ВСЕЙ БАЗЫ ИЗ SQLITE) ---
    public async Task<IEnumerable<DailyFundsRegistery>> GetAsync(params Expression<Func<DailyFundsRegistery, bool>>[] predicates)
    {
        var (hasDates, from, till) = TryExtractDateRange(predicates);

        // Если есть диапазон дат — берем срез напрямую с бэкенда
        if (hasDates)
        {
            try
            {
                var fromStr = from.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
                var tillStr = till.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");

                var remoteSlice = await _restClient.GetAsync<List<DailyFundsRegistery>>($"/api/finance/registeries?from={fromStr}&till={tillStr}");
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

        // Иначе отдаем из кэша памяти
        var all = await GetAllAsync();
        var fallbackQuery = all.AsQueryable();
        if (predicates != null)
        {
            foreach (var p in predicates.Where(x => x != null)) fallbackQuery = fallbackQuery.Where(p);
        }
        return fallbackQuery.ToList();
    }

    // Реализация для интерфейса IDailyFundsRegisteriesRepository (маппинг в Info)
    async Task<IEnumerable<DailyFundsRegisteryInfo>> IDailyFundsRegisteriesRepository.GetAsync(params Expression<Func<DailyFundsRegistery, bool>>[] predicates)
    {
        var items = await GetAsync(predicates);

        return items.Select(x => new DailyFundsRegisteryInfo
        {
            Id = x.Id,
            Code = x.Code,
            Date = x.Date,
            DepositoryId = x.DepositoryId,
            IsCompleted = x.IsCompleted,
            IsDisabled = x.IsDisabled,
            UserName = x.UserName,
            Group = x.Group,
            Tags = x.Tags,
            Description = x.Description,
            Lines = x.Lines,
            CurrencyConvertions = x.CurrencyConvertions,
            Computed = null
        }).ToList();
    }

    // Мгновенный подсчет для боковых плиток дат (Today, This Week, This Month и т.д.)
    public async Task<int> CountAsync(params Expression<Func<DailyFundsRegistery, bool>>[] predicates)
    {
        var (hasDates, from, till) = TryExtractDateRange(predicates);

        if (hasDates)
        {
            try
            {
                var fromStr = from.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
                var tillStr = till.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");

                var res = await _restClient.GetAsync<CountResponse>($"/api/finance/registeries/count?from={fromStr}&till={tillStr}");
                if (res != null) return res.Count;
            }
            catch { }
        }

        var items = await GetAsync(predicates);
        return items.Count();
    }

    public async Task<IEnumerable<DailyFundsRegistery>> GetAllAsync()
    {
        if (_cardCache.Count > 0)
            return _cardCache.Values.ToList();

        var local = LocalSqliteCache.GetAllDocuments<DailyFundsRegistery>(DocType);
        if (local != null)
        {
            foreach (var item in local)
            {
                if (!string.IsNullOrEmpty(item?.Id)) _cardCache[item.Id] = item;
            }
        }

        return _cardCache.Values.ToList();
    }

    public Task CreateAsync(DailyFundsRegistery model) => SaveAsync(model);
    public Task UpdateAsync(DailyFundsRegistery model) => SaveAsync(model);

    public async Task SaveAsync(DailyFundsRegistery model)
    {
        if (model == null) return;

        bool isNew = string.IsNullOrEmpty(model.Id) || model.Id == Guid.Empty.ToString();
        if (isNew) model.Id = Guid.NewGuid().ToString();

        _cardCache[model.Id] = model;
        LocalSqliteCache.SaveDocument(DocType, model.Id, model, isSynced: false);

        _ = Task.Run(async () =>
        {
            try
            {
                if (isNew)
                    await _restClient.PostAsync("/api/finance/registeries", model);
                else
                    await _restClient.PutAsync($"/api/finance/registeries/{model.Id}", model);

                LocalSqliteCache.SaveDocument(DocType, model.Id, model, isSynced: true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DAILY REGISTERY SYNC ERROR]: {ex.Message}");
            }
        });
    }

    public async Task DeleteAsync(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        _cardCache.TryRemove(id, out _);

        _ = Task.Run(async () =>
        {
            try { await _restClient.DeleteAsync($"/api/finance/registeries/{id}"); } catch { }
        });
    }

    public async Task<Dictionary<string, Dictionary<string, int>>> GetFacets(params string[] fields)
    {
        var dict = new Dictionary<string, Dictionary<string, int>>();
        if (fields != null) foreach (var f in fields) dict[f] = new Dictionary<string, int>();

        try
        {
            var fieldsParam = fields != null && fields.Length > 0 ? string.Join(",", fields) : "Date";
            var apiResult = await _restClient.GetAsync<Dictionary<string, Dictionary<string, int>>>($"/api/finance/registeries/facets?fields={fieldsParam}");
            if (apiResult != null)
            {
                foreach (var kvp in apiResult) dict[kvp.Key] = kvp.Value;
            }
        }
        catch { }

        return dict;
    }

    // --- ПАРСЕР ДАТ ИЗ ДЕРЕВА EXPRESSION (MvvmCross) ---
    private static (bool HasDates, DateTime From, DateTime Till) TryExtractDateRange(Expression<Func<DailyFundsRegistery, bool>>[] predicates)
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
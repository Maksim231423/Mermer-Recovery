using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Mermer.Data.Storage;
using Mermer.Finance.Spending.Models;
using Mermer.Http;

namespace Mermer.Ui.Pc.Services;

public class ApiExpenseSlipsRepository : IRepositoryWithFacets<ExpenseSlip>, IRepository<ExpenseSlip>, IReadOnlyRepository<ExpenseSlip>
{
    private readonly RestClient _restClient;
    private const string DocType = "ExpenseSlip";

    private static readonly ConcurrentDictionary<string, ExpenseSlip> _cardCache = new(StringComparer.OrdinalIgnoreCase);

    public ApiExpenseSlipsRepository(RestClient restClient)
    {
        _restClient = restClient ?? throw new ArgumentNullException(nameof(restClient));
        SyncPendingSlips();
    }

    public async Task<ExpenseSlip> GetAsync(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        if (_cardCache.TryGetValue(id, out var cached))
            return cached;

        await GetAllAsync();
        if (_cardCache.TryGetValue(id, out cached))
            return cached;

        try
        {
            var remote = await _restClient.GetAsync<ExpenseSlip>($"/api/spending/slips/{id}");
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

    public async Task<IEnumerable<ExpenseSlip>> GetAsync(string[] ids)
    {
        if (ids == null || !ids.Any()) return Enumerable.Empty<ExpenseSlip>();
        var result = new List<ExpenseSlip>();
        foreach (var id in ids)
        {
            var item = await GetAsync(id);
            if (item != null) result.Add(item);
        }
        return result;
    }

    // --- ПОЛУЧЕНИЕ СПИСКА (С ФИЛЬТРОМ ИЛИ ЗА ВСЁ ВРЕМЯ) ---
    public async Task<IEnumerable<ExpenseSlip>> GetAsync(params Expression<Func<ExpenseSlip, bool>>[] predicates)
    {
        var (hasDates, from, till) = TryExtractDateRange(predicates);

        try
        {
            string url = "/api/spending/slips";
            if (hasDates)
            {
                var fromStr = from.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
                var tillStr = till.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
                url += $"?from={fromStr}&till={tillStr}";
            }

            var remoteSlice = await _restClient.GetAsync<List<ExpenseSlip>>(url);
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
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ExpenseSlips GetAsync Error]: {ex.Message}");
        }

        var all = await GetAllAsync();
        var fallbackQuery = all.AsQueryable();
        if (predicates != null)
        {
            foreach (var p in predicates.Where(x => x != null))
                fallbackQuery = fallbackQuery.Where(p);
        }
        return fallbackQuery.ToList();
    }

    // --- ПОДСЧЕТ ДЛЯ ПЛИТОК ДАТ (ЛОГИКА СЧЕТЧИКОВ СОХРАНЕНА) ---
    public async Task<int> CountAsync(params Expression<Func<ExpenseSlip, bool>>[] predicates)
    {
        var (hasDates, from, till) = TryExtractDateRange(predicates);

        try
        {
            string url = "/api/spending/slips/count";
            if (hasDates)
            {
                var fromStr = from.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
                var tillStr = till.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
                url += $"?from={fromStr}&till={tillStr}";
            }

            var res = await _restClient.GetAsync<CountResponse>(url);
            if (res != null) return res.Count;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ExpenseSlips CountAsync Error]: {ex.Message}");
        }

        var items = await GetAsync(predicates);
        return items.Count();
    }

    public async Task<IEnumerable<ExpenseSlip>> GetAllAsync()
    {
        if (_cardCache.Count > 0)
            return _cardCache.Values.ToList();

        var local = LocalSqliteCache.GetAllDocuments<ExpenseSlip>(DocType);
        if (local != null)
        {
            foreach (var item in local)
            {
                if (!string.IsNullOrEmpty(item?.Id)) _cardCache[item.Id] = item;
            }
        }

        return _cardCache.Values.ToList();
    }

    public Task CreateAsync(ExpenseSlip model) => SaveAsync(model);
    public Task UpdateAsync(ExpenseSlip model) => SaveAsync(model);

    public async Task<ExpenseSlip> SaveAsync(ExpenseSlip entity)
    {
        if (entity == null) return null;
        if (string.IsNullOrEmpty(entity.Id) || entity.Id == Guid.Empty.ToString())
            entity.Id = Guid.NewGuid().ToString();

        _cardCache[entity.Id] = entity;
        LocalSqliteCache.SaveDocument(DocType, entity.Id, entity, isSynced: false);

        var payload = BuildPayload(entity);

        _ = Task.Run(async () =>
        {
            try
            {
                await _restClient.PostAsync("/api/spending/slips", payload);
                LocalSqliteCache.SaveDocument(DocType, entity.Id, entity, isSynced: true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EXPENSE SLIP SYNC ERROR]: {ex.Message}");
            }
        });

        return entity;
    }

    public async Task DeleteAsync(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        _cardCache.TryRemove(id, out _);

        _ = Task.Run(async () =>
        {
            try { await _restClient.DeleteAsync($"/api/spending/slips/{id}"); } catch { }
        });
    }

    public async Task<Dictionary<string, Dictionary<string, int>>> GetFacets(params string[] fields)
    {
        var dict = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
        if (fields != null)
        {
            foreach (var f in fields)
                dict[f] = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var fieldsParam = fields != null && fields.Length > 0 ? string.Join(",", fields) : "Group,Tags,Date";
            var apiResult = await _restClient.GetAsync<Dictionary<string, Dictionary<string, int>>>($"/api/spending/slips/facets?fields={fieldsParam}");
            if (apiResult != null)
            {
                foreach (var kvp in apiResult)
                {
                    dict[kvp.Key] = new Dictionary<string, int>(kvp.Value, StringComparer.OrdinalIgnoreCase);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ExpenseSlips GetFacets Error]: {ex.Message}");
        }

        return dict;
    }

    // --- ФОНОВАЯ ДОСЫЛКА НЕОТПРАВЛЕННЫХ ЗАПИСЕЙ ИЗ SQLITE ---
    private void SyncPendingSlips()
    {
        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(2500);
                var allLocal = LocalSqliteCache.GetAllDocuments<ExpenseSlip>(DocType);
                if (allLocal == null) return;

                foreach (var slip in allLocal)
                {
                    try
                    {
                        var payload = BuildPayload(slip);
                        await _restClient.PostAsync("/api/spending/slips", payload);
                        LocalSqliteCache.SaveDocument(DocType, slip.Id, slip, isSynced: true);
                    }
                    catch { }
                }
            }
            catch { }
        });
    }

    private static object BuildPayload(ExpenseSlip entity)
    {
        var linesList = new List<object>();
        if (entity.Lines != null)
        {
            foreach (var l in entity.Lines)
            {
                linesList.Add(new
                {
                    Id = string.IsNullOrEmpty(l.Id) ? Guid.NewGuid().ToString() : l.Id,
                    ExpenseId = l.ExpenseId,
                    Amount = l.Amount,
                    CurrencyId = string.IsNullOrEmpty(l.CurrencyId) ? entity.DisplayCurrencyId : l.CurrencyId
                });
            }
        }

        return new
        {
            Id = entity.Id,
            Code = entity.Code ?? "",
            Date = entity.Date.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
            DepositoryId = entity.DepositoryId,
            DisplayCurrencyId = entity.DisplayCurrencyId,
            UserId = entity.UserId,
            UserName = entity.UserName ?? "admin",
            IsCompleted = entity.IsCompleted,
            IsDisabled = entity.IsDisabled,
            Group = entity.Group ?? "",
            Tags = entity.Tags != null ? entity.Tags.ToList() : new List<string>(),
            Description = entity.Description ?? "",
            Lines = linesList
        };
    }

    // --- ПАРСЕР ДАТ ИЗ ВЫРАЖЕНИЙ MVVMCROSS ---
    private static (bool HasDates, DateTime From, DateTime Till) TryExtractDateRange(Expression<Func<ExpenseSlip, bool>>[] predicates)
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
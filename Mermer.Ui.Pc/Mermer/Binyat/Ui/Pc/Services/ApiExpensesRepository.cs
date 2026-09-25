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

public class ApiExpensesRepository : IRepositoryWithFacets<Expense>, IRepository<Expense>, IReadOnlyRepository<Expense>
{
    private readonly RestClient _restClient;
    private const string DocType = "Expense";

    private static readonly ConcurrentDictionary<string, Expense> _cardCache = new(StringComparer.OrdinalIgnoreCase);
    private static List<Expense> _memoryCache;
    private static readonly object _syncLock = new();

    public ApiExpensesRepository(RestClient restClient)
    {
        _restClient = restClient ?? throw new ArgumentNullException(nameof(restClient));
    }

    public async Task<Expense> GetAsync(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        if (_cardCache.TryGetValue(id, out var cached))
            return cached;

        await GetAllAsync();
        if (_cardCache.TryGetValue(id, out cached))
            return cached;

        try
        {
            var remote = await _restClient.GetAsync<Expense>($"/api/expenses/{id}");
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

    public async Task<IEnumerable<Expense>> GetAsync(string[] ids)
    {
        if (ids == null || !ids.Any()) return Enumerable.Empty<Expense>();
        var all = await GetAllAsync();
        var idSet = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        return all.Where(e => idSet.Contains(e.Id)).ToList();
    }

    public async Task<IEnumerable<Expense>> GetAsync(params Expression<Func<Expense, bool>>[] predicates)
    {
        var all = await GetAllAsync();
        var query = all.AsQueryable();

        if (predicates != null && predicates.Any())
        {
            foreach (var p in predicates.Where(x => x != null))
            {
                query = query.Where(p);
            }
        }

        return query.ToList();
    }

    public async Task<IEnumerable<Expense>> GetAllAsync()
    {
        lock (_syncLock)
        {
            if (_memoryCache != null && _memoryCache.Count > 0)
                return _memoryCache;
        }

        // 1. Читаем из локального SQLite
        var local = LocalSqliteCache.GetAllDocuments<Expense>(DocType)?.ToList() ?? new List<Expense>();
        if (local.Any())
        {
            lock (_syncLock)
            {
                _memoryCache = local;
                foreach (var exp in local)
                {
                    if (!string.IsNullOrEmpty(exp?.Id)) _cardCache[exp.Id] = exp;
                }
            }
        }

        // 2. Если локально пусто — запрашиваем API
        if (_memoryCache == null || _memoryCache.Count == 0)
        {
            try
            {
                var remote = await _restClient.GetAsync<List<Expense>>("/api/expenses");
                if (remote != null && remote.Any())
                {
                    lock (_syncLock)
                    {
                        _memoryCache = remote;
                        foreach (var exp in remote)
                        {
                            if (!string.IsNullOrEmpty(exp?.Id))
                            {
                                _cardCache[exp.Id] = exp;
                                LocalSqliteCache.SaveDocument(DocType, exp.Id, exp, isSynced: true);
                            }
                        }
                    }
                }
            }
            catch { }
        }

        return _memoryCache ?? Enumerable.Empty<Expense>();
    }

    public async Task<int> CountAsync(params Expression<Func<Expense, bool>>[] predicates)
    {
        return (await GetAsync(predicates)).Count();
    }

    public Task CreateAsync(Expense model) => SaveAsync(model);
    public Task UpdateAsync(Expense model) => SaveAsync(model);

    public async Task SaveAsync(Expense model)
    {
        if (model == null) return;
        if (string.IsNullOrEmpty(model.Id)) model.Id = Guid.NewGuid().ToString();

        _cardCache[model.Id] = model;
        lock (_syncLock)
        {
            if (_memoryCache != null)
            {
                int idx = _memoryCache.FindIndex(x => x.Id == model.Id);
                if (idx >= 0) _memoryCache[idx] = model;
                else _memoryCache.Add(model);
            }
        }

        LocalSqliteCache.SaveDocument(DocType, model.Id, model, isSynced: false);

        _ = Task.Run(async () =>
        {
            try
            {
                await _restClient.PostAsync("/api/expenses", model);
                LocalSqliteCache.SaveDocument(DocType, model.Id, model, isSynced: true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EXPENSE SYNC ERROR]: {ex.Message}");
            }
        });
    }

    public async Task DeleteAsync(string id)
    {
        if (string.IsNullOrEmpty(id)) return;

        _cardCache.TryRemove(id, out _);
        lock (_syncLock)
        {
            _memoryCache?.RemoveAll(x => x.Id == id);
        }

        _ = Task.Run(async () =>
        {
            try { await _restClient.DeleteAsync($"/api/expenses/{id}"); } catch { }
        });
    }

    public async Task<Dictionary<string, Dictionary<string, int>>> GetFacets(params string[] fields)
    {
        var dict = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase)
        {
            ["TypeNames"] = new Dictionary<string, int>(),
            ["GroupNames"] = new Dictionary<string, int>(),
            ["TagNames"] = new Dictionary<string, int>()
        };

        if (fields != null)
        {
            foreach (var f in fields.Where(x => !dict.ContainsKey(x)))
            {
                dict[f] = new Dictionary<string, int>();
            }
        }

        try
        {
            var fieldsParam = fields != null && fields.Length > 0 ? string.Join(",", fields) : "";
            var apiResult = await _restClient.GetAsync<Dictionary<string, Dictionary<string, int>>>($"/api/expenses/facets?fields={fieldsParam}");
            if (apiResult != null)
            {
                foreach (var kvp in apiResult) dict[kvp.Key] = kvp.Value;
            }
        }
        catch { }

        return dict;
    }
}
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Mermer.Data.Storage;
using Mermer.Http;
using Mermer.StockManagement.Models;
using Mermer.StockManagement.Services;

namespace Mermer.Ui.Pc.Services;

public class ApiStocksRepository : IRepository<Stock>, IReadOnlyRepository<Stock>, IRepositoryWithFacets<Stock>, IStocksRepository
{
    private readonly RestClient _restClient;
    private const string DocType = "Stock";

    private static readonly ConcurrentDictionary<string, Stock> _cardCache = new(StringComparer.OrdinalIgnoreCase);
    private static List<Stock> _memoryCache;
    private static readonly object _syncLock = new();

    public ApiStocksRepository(RestClient restClient)
    {
        _restClient = restClient ?? throw new ArgumentNullException(nameof(restClient));
    }

    public async Task<IEnumerable<Stock>> GetAllAsync()
    {
        lock (_syncLock)
        {
            if (_memoryCache != null && _memoryCache.Count > 0)
                return _memoryCache;
        }

        // 1. Быстрое чтение из локального SQLite
        var local = LocalSqliteCache.GetAllDocuments<Stock>(DocType)?.ToList() ?? new List<Stock>();
        if (local.Any())
        {
            lock (_syncLock)
            {
                _memoryCache = local.OrderBy(s => s.Name).ToList();
                foreach (var s in local)
                {
                    if (!string.IsNullOrEmpty(s?.Id)) _cardCache[s.Id] = s;
                }
            }
        }

        // 2. Если локально пусто — запрашиваем API
        if (_memoryCache == null || _memoryCache.Count == 0)
        {
            try
            {
                var remote = await _restClient.GetAsync<List<Stock>>("/api/stocks");
                if (remote != null && remote.Any())
                {
                    lock (_syncLock)
                    {
                        _memoryCache = remote.OrderBy(s => s.Name).ToList();
                        foreach (var s in remote)
                        {
                            if (!string.IsNullOrEmpty(s?.Id))
                            {
                                _cardCache[s.Id] = s;
                                LocalSqliteCache.SaveDocument(DocType, s.Id, s, isSynced: true);
                            }
                        }
                    }
                }
            }
            catch { }
        }

        return _memoryCache ?? Enumerable.Empty<Stock>();
    }

    public async Task<Stock> GetAsync(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        if (_cardCache.TryGetValue(id, out var cached))
            return cached;

        await GetAllAsync();
        if (_cardCache.TryGetValue(id, out cached))
            return cached;

        try
        {
            var remote = await _restClient.GetAsync<Stock>($"/api/stocks/{id}");
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

    public async Task<IEnumerable<Stock>> GetAsync(string[] ids)
    {
        if (ids == null || !ids.Any()) return Enumerable.Empty<Stock>();
        var all = await GetAllAsync();
        var idSet = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        return all.Where(s => idSet.Contains(s.Id)).OrderBy(s => s.Name).ToList();
    }

    public async Task<IEnumerable<Stock>> GetListAsync(params string[] ids) => await GetAsync(ids);

    public async Task<IEnumerable<Stock>> GetAsync(params Expression<Func<Stock, bool>>[] predicates)
    {
        var all = await GetAllAsync();
        var query = all.AsQueryable();
        if (predicates != null)
        {
            foreach (var p in predicates.Where(x => x != null))
            {
                query = query.Where(p);
            }
        }
        return query.OrderBy(s => s.Name).ToList();
    }

    public async Task<int> CountAsync(params Expression<Func<Stock, bool>>[] predicates)
    {
        return (await GetAsync(predicates)).Count();
    }

    public Task CreateAsync(Stock entity) => SaveAsync(entity);
    public Task UpdateAsync(Stock entity) => SaveAsync(entity);

    public async Task SaveAsync(Stock entity)
    {
        if (entity == null) return;
        bool isNew = string.IsNullOrEmpty(entity.Id) || entity.Id == Guid.Empty.ToString();
        if (isNew) entity.Id = Guid.NewGuid().ToString();

        _cardCache[entity.Id] = entity;
        lock (_syncLock)
        {
            if (_memoryCache != null)
            {
                int idx = _memoryCache.FindIndex(x => x.Id == entity.Id);
                if (idx >= 0) _memoryCache[idx] = entity;
                else _memoryCache.Add(entity);
                _memoryCache = _memoryCache.OrderBy(s => s.Name).ToList();
            }
        }

        LocalSqliteCache.SaveDocument(DocType, entity.Id, entity, isSynced: false);

        _ = Task.Run(async () =>
        {
            try
            {
                if (isNew) await _restClient.PostAsync("/api/stocks", entity);
                else await _restClient.PutAsync($"/api/stocks/{entity.Id}", entity);
                LocalSqliteCache.SaveDocument(DocType, entity.Id, entity, isSynced: true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[STOCK SYNC ERROR]: {ex.Message}");
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
            try { await _restClient.DeleteAsync($"/api/stocks/{id}"); } catch { }
        });
    }

    public async Task<Dictionary<string, Dictionary<string, int>>> GetFacets(params string[] fields)
    {
        var result = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
        if (fields != null)
        {
            foreach (var field in fields) result[field] = new Dictionary<string, int>();
        }

        try
        {
            var fieldsParam = fields != null && fields.Length > 0 ? string.Join(",", fields) : "";
            var apiResult = await _restClient.GetAsync<Dictionary<string, Dictionary<string, int>>>($"/api/stocks/facets?fields={fieldsParam}");
            if (apiResult != null)
            {
                foreach (var kvp in apiResult) result[kvp.Key] = kvp.Value;
            }
        }
        catch { }

        return result;
    }

    public async Task<IEnumerable<StockInfo>> GetInfoAsync(params string[] stockIds)
    {
        if (stockIds == null || !stockIds.Any()) return Enumerable.Empty<StockInfo>();
        var stocks = await GetAsync(stockIds);

        return stocks.Select(stock => new StockInfo
        {
            Id = stock.Id,
            Code = stock.Code,
            Name = stock.Name,
            ShortName = stock.ShortName,
            Unit = stock.Unit,
            Price = stock.Price,
            CurrencyId = stock.CurrencyId,
            Type = stock.Type,
            Group = stock.Group,
            Tags = stock.Tags?.ToList(),
            Barcodes = stock.Barcodes?.ToList(),
            IsDisabled = stock.IsDisabled
        }).ToList();
    }

    public async Task<IEnumerable<StockInfo>> GetInfoAsync(string additionalPriceCurrencyId, string additionalPriceGroup)
    {
        var all = await GetAllAsync();

        return all.Select(stock => new StockInfo
        {
            Id = stock.Id,
            Code = stock.Code,
            Name = stock.Name,
            ShortName = stock.ShortName,
            Unit = stock.Unit,
            Price = stock.Price,
            CurrencyId = stock.CurrencyId,
            Type = stock.Type,
            Group = stock.Group,
            Tags = stock.Tags?.ToList(),
            Barcodes = stock.Barcodes?.ToList(),
            IsDisabled = stock.IsDisabled
        }).ToList();
    }

    public async Task MergeAsync(string mainStockId, string[] mergeStockIds, bool disableMergedItems)
    {
        if (string.IsNullOrEmpty(mainStockId) || mergeStockIds == null || !mergeStockIds.Any()) return;

        try
        {
            await _restClient.PostAsync("/api/stocks/merge", new
            {
                MainStockId = mainStockId,
                MergeStockIds = mergeStockIds,
                DisableMergedItems = disableMergedItems
            });

            lock (_syncLock)
            {
                _memoryCache = null;
            }
            _cardCache.Clear();
            await GetAllAsync();
        }
        catch { }
    }
}
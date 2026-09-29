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

        // 1. Быстрое чтение из локального SQLite (не отсекаем IsDisabled, чтобы работали стили зачеркивания)
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

        // 2. Если кэш пуст — подгружаем с бэкенда
        if (_memoryCache == null || _memoryCache.Count == 0)
        {
            try
            {
                var remote = await _restClient.GetAsync<List<Stock>>("/api/stocks?limit=10000");
                if (remote != null && remote.Any())
                {
                    lock (_syncLock)
                    {
                        _memoryCache = remote.OrderBy(s => s.Name).ToList();
                        foreach (var s in _memoryCache)
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
            foreach (var field in fields) result[field] = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var fieldsParam = fields != null && fields.Length > 0 ? string.Join(",", fields) : "";
            var apiResult = await _restClient.GetAsync<Dictionary<string, Dictionary<string, int>>>($"/api/stocks/facets?fields={fieldsParam}");
            if (apiResult != null)
            {
                foreach (var kvp in apiResult) result[kvp.Key] = new Dictionary<string, int>(kvp.Value, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch { }

        // Fallback для PriceGroupNames
        if (result.ContainsKey("PriceGroupNames") && result["PriceGroupNames"].Count == 0)
        {
            var all = await GetAllAsync();
            var priceGroups = all
                .Where(s => s.AdditionalPrices != null)
                .SelectMany(s => s.AdditionalPrices)
                .Where(p => !string.IsNullOrWhiteSpace(p?.Group))
                .GroupBy(p => p.Group.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            result["PriceGroupNames"] = priceGroups;
        }

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

        return all.Select(stock =>
        {
            decimal addPrice = 0m;
            string addPriceCurrency = null;

            if (!string.IsNullOrEmpty(additionalPriceGroup) && stock.AdditionalPrices != null && stock.AdditionalPrices.Any())
            {
                var matched = stock.AdditionalPrices
                    .Where(p => string.Equals(p.Group, additionalPriceGroup, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(p => p.ValidFrom)
                    .FirstOrDefault();

                if (matched != null)
                {
                    addPrice = matched.Price;
                    addPriceCurrency = matched.CurrencyId ?? stock.CurrencyId;
                }
            }

            if (addPrice == 0m && !string.IsNullOrEmpty(additionalPriceCurrencyId))
            {
                addPrice = stock.Price;
                addPriceCurrency = additionalPriceCurrencyId;
            }

            return new StockInfo
            {
                Id = stock.Id,
                Code = stock.Code,
                Name = stock.Name,
                ShortName = stock.ShortName,
                Unit = stock.Unit,
                Price = stock.Price,
                CurrencyId = stock.CurrencyId,
                AdditionalPrice = addPrice,
                AdditionalPriceCurrencyId = addPriceCurrency,
                Type = stock.Type,
                Group = stock.Group,
                Tags = stock.Tags?.ToList(),
                Barcodes = stock.Barcodes?.ToList(),
                IsDisabled = stock.IsDisabled
            };
        }).ToList();
    }

    public async Task MergeAsync(string mainStockId, string[] mergeStockIds, bool disableMergedItems)
    {
        if (string.IsNullOrEmpty(mainStockId) || mergeStockIds == null || !mergeStockIds.Any()) return;

        try
        {
            var payload = new
            {
                MainStockId = mainStockId,
                MergeStockIds = mergeStockIds,
                DisableMergedItems = disableMergedItems
            };

            // 1. Отправляем запрос на сервер
            await _restClient.PostAsync("/api/stocks/merge", payload);

            var mergeSet = new HashSet<string>(mergeStockIds, StringComparer.OrdinalIgnoreCase);

            // 2. Мгновенно обновляем флаг в оперативной памяти и локальном SQLite
            lock (_syncLock)
            {
                if (_memoryCache != null)
                {
                    foreach (var s in _memoryCache.Where(x => mergeSet.Contains(x.Id)))
                    {
                        s.IsDisabled = disableMergedItems;
                        LocalSqliteCache.SaveDocument(DocType, s.Id, s, isSynced: true);
                    }
                }
            }

            foreach (var id in mergeStockIds)
            {
                if (_cardCache.TryGetValue(id, out var s))
                {
                    s.IsDisabled = disableMergedItems;
                    LocalSqliteCache.SaveDocument(DocType, id, s, isSynced: true);
                }
            }

            // 3. Сбрасываем кэш в фоне, не блокируя UI
            _ = Task.Run(async () =>
            {
                try
                {
                    var remote = await _restClient.GetAsync<List<Stock>>("/api/stocks?limit=10000");
                    if (remote != null && remote.Any())
                    {
                        lock (_syncLock)
                        {
                            _memoryCache = remote.OrderBy(x => x.Name).ToList();
                            foreach (var s in _memoryCache)
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
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[STOCK MERGE ERROR]: {ex.Message}");
            throw;
        }
    }
}
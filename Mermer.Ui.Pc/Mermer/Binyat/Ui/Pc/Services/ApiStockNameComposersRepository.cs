using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Mermer.Data.Storage;
using Mermer.Http;
using Mermer.StockManagement.Models;

namespace Mermer.Ui.Pc.Services;

public class ApiStockNameComposersRepository :
    IRepository<StockNameComposer>,
    IReadOnlyRepository<StockNameComposer>
{
    private readonly RestClient _restClient;

    private static readonly ConcurrentDictionary<string, StockNameComposer> _cardCache = new(StringComparer.OrdinalIgnoreCase);
    private static List<StockNameComposer> _memoryCache;
    private static readonly object _syncLock = new();

    public ApiStockNameComposersRepository(RestClient restClient)
    {
        _restClient = restClient ?? throw new Exception("Http RestClient cannot be null.");
    }

    public async Task<IEnumerable<StockNameComposer>> GetAllAsync()
    {
        lock (_syncLock)
        {
            if (_memoryCache != null && _memoryCache.Count > 0)
                return _memoryCache;
        }

        try
        {
            var remote = await _restClient.GetAsync<List<StockNameComposer>>("/api/stock-management/name-composers");
            if (remote != null && remote.Any())
            {
                lock (_syncLock)
                {
                    _memoryCache = remote.OrderBy(x => x.Order).ToList();
                    foreach (var item in remote)
                    {
                        if (!string.IsNullOrEmpty(item?.Id))
                        {
                            _cardCache[item.Id] = item;
                        }
                    }
                }
            }
        }
        catch { }

        return _memoryCache ?? Enumerable.Empty<StockNameComposer>();
    }

    public async Task<StockNameComposer> GetAsync(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        if (_cardCache.TryGetValue(id, out var cached))
            return cached;

        await GetAllAsync();
        if (_cardCache.TryGetValue(id, out cached))
            return cached;

        return null;
    }

    public async Task<IEnumerable<StockNameComposer>> GetAsync(string[] ids)
    {
        if (ids == null || !ids.Any()) return Enumerable.Empty<StockNameComposer>();
        var all = await GetAllAsync();
        var idSet = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        return all.Where(x => idSet.Contains(x.Id)).ToList();
    }

    public async Task<IEnumerable<StockNameComposer>> GetAsync(params Expression<Func<StockNameComposer, bool>>[] predicates)
    {
        var all = await GetAllAsync();
        var query = all.AsQueryable();
        if (predicates != null)
        {
            foreach (var p in predicates.Where(x => x != null)) query = query.Where(p);
        }
        return query.ToList();
    }

    public async Task<int> CountAsync(params Expression<Func<StockNameComposer, bool>>[] predicates)
    {
        return (await GetAsync(predicates)).Count();
    }

    public Task CreateAsync(StockNameComposer entity) => SaveAsync(entity);
    public Task UpdateAsync(StockNameComposer entity) => SaveAsync(entity);

    public async Task SaveAsync(StockNameComposer entity)
    {
        if (entity == null)
        {
            throw new Exception("Cannot save an empty StockNameComposer entity.");
        }

        if (string.IsNullOrEmpty(entity.Id)) entity.Id = Guid.NewGuid().ToString();

        _cardCache[entity.Id] = entity;
        lock (_syncLock)
        {
            if (_memoryCache != null)
            {
                int idx = _memoryCache.FindIndex(x => x.Id == entity.Id);
                if (idx >= 0) _memoryCache[idx] = entity;
                else _memoryCache.Add(entity);
                _memoryCache = _memoryCache.OrderBy(x => x.Order).ToList();
            }
        }

        try
        {
            await _restClient.PostAsync("/api/stock-management/name-composers", entity);
        }
        catch (Exception ex)
        {
            var detail = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            throw new Exception(string.Format("Failed to save StockNameComposer '{0}'. Details: {1}", entity.Id, detail), ex);
        }

        UiEventBus.NotifyDocumentModified<StockNameComposer>(entity.Id);
    }

    public async Task DeleteAsync(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            throw new Exception("StockNameComposer identifier cannot be empty for delete operation.");
        }

        _cardCache.TryRemove(id, out _);
        lock (_syncLock)
        {
            _memoryCache?.RemoveAll(x => x.Id == id);
        }

        try
        {
            await _restClient.DeleteAsync($"/api/stock-management/name-composers/{id}");
        }
        catch (Exception ex)
        {
            var detail = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            throw new Exception(string.Format("Failed to delete StockNameComposer '{0}'. Details: {1}", id, detail), ex);
        }

        UiEventBus.NotifyDocumentModified<StockNameComposer>(id);
    }
}
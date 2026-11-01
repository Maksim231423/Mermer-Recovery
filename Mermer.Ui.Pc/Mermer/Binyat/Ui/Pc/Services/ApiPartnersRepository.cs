using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Mermer.CRM.Models;
using Mermer.CRM.Services;
using Mermer.Data.Storage;
using Mermer.Http;

namespace Mermer.Ui.Pc.Services;

public class ApiPartnersRepository :
    IPartnersRepository,
    IRepository<Partner>,
    IReadOnlyRepository<Partner>,
    IRepositoryWithFacets<Partner>
{
    private readonly RestClient _restClient;

    private static readonly ConcurrentDictionary<string, Partner> _cardCache = new(StringComparer.OrdinalIgnoreCase);
    private static List<Partner> _memoryCache;
    private static readonly object _syncLock = new();

    public ApiPartnersRepository(RestClient restClient)
    {
        _restClient = restClient ?? throw new Exception("Http RestClient cannot be null.");
    }

    public async Task<IEnumerable<Partner>> GetAllAsync()
    {
        lock (_syncLock)
        {
            if (_memoryCache != null && _memoryCache.Count > 0)
                return _memoryCache;
        }

        try
        {
            var remote = await _restClient.GetAsync<List<Partner>>("/api/partners");
            if (remote != null && remote.Any())
            {
                lock (_syncLock)
                {
                    _memoryCache = remote.OrderBy(p => p.Name).ToList();
                    foreach (var p in remote)
                    {
                        if (!string.IsNullOrEmpty(p?.Id))
                            _cardCache[p.Id] = p;
                    }
                }
            }
        }
        catch { }

        return _memoryCache ?? Enumerable.Empty<Partner>();
    }

    public async Task ReloadAsync()
    {
        lock (_syncLock)
        {
            _memoryCache = null;
            _cardCache.Clear();
        }

        try
        {
            var remote = await _restClient.GetAsync<List<Partner>>("/api/partners");
            if (remote != null)
            {
                lock (_syncLock)
                {
                    _memoryCache = remote.OrderBy(p => p.Name).ToList();
                    foreach (var p in remote)
                    {
                        if (!string.IsNullOrEmpty(p?.Id))
                            _cardCache[p.Id] = p;
                    }
                }
            }
        }
        catch { }
    }

    public async Task<Partner> GetAsync(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        if (_cardCache.TryGetValue(id, out var cached))
            return cached;

        await GetAllAsync();
        if (_cardCache.TryGetValue(id, out cached))
            return cached;

        try
        {
            var remote = await _restClient.GetAsync<Partner>($"/api/partners/{id}");
            if (remote != null)
            {
                _cardCache[remote.Id] = remote;
                return remote;
            }
        }
        catch { }

        return null;
    }

    public async Task<IEnumerable<Partner>> GetAsync(string[] ids)
    {
        if (ids == null || !ids.Any()) return Enumerable.Empty<Partner>();
        var all = await GetAllAsync();
        var idSet = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        return all.Where(p => idSet.Contains(p.Id)).OrderBy(p => p.Name).ToList();
    }

    public async Task<IEnumerable<Partner>> GetAsync(params Expression<Func<Partner, bool>>[] predicates)
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
        return query.OrderBy(p => p.Name).ToList();
    }

    public async Task<int> CountAsync(params Expression<Func<Partner, bool>>[] predicates)
    {
        return (await GetAsync(predicates)).Count();
    }

    public Task CreateAsync(Partner entity) => SaveAsync(entity);
    public Task UpdateAsync(Partner entity) => SaveAsync(entity);

    public async Task SaveAsync(Partner entity)
    {
        if (entity == null)
        {
            throw new Exception("Cannot save an empty Partner entity.");
        }

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
                _memoryCache = _memoryCache.OrderBy(p => p.Name).ToList();
            }
        }

        try
        {
            if (isNew) await _restClient.PostAsync("/api/partners", entity);
            else await _restClient.PutAsync($"/api/partners/{entity.Id}", entity);
        }
        catch (Exception ex)
        {
            var detail = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            throw new Exception(string.Format("Failed to save Partner '{0}'. Details: {1}", entity.Id, detail), ex);
        }

        UiEventBus.NotifyDocumentModified<Partner>(entity.Id);
    }

    public async Task DeleteAsync(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            throw new Exception("Partner identifier cannot be empty for delete operation.");
        }

        _cardCache.TryRemove(id, out _);
        lock (_syncLock)
        {
            _memoryCache?.RemoveAll(x => x.Id == id);
        }

        try
        {
            await _restClient.DeleteAsync($"/api/partners/{id}");
        }
        catch (Exception ex)
        {
            var detail = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            throw new Exception(string.Format("Failed to delete Partner '{0}'. Details: {1}", id, detail), ex);
        }

        UiEventBus.NotifyDocumentModified<Partner>(id);
    }

    public async Task MergeAsync(string mainItemId, string[] mergeItemIds, bool disableMergedItems)
    {
        if (string.IsNullOrEmpty(mainItemId) || mergeItemIds == null || !mergeItemIds.Any())
        {
            throw new Exception("Invalid parameters for merging partners.");
        }

        try
        {
            var payload = new
            {
                MainItemId = mainItemId,
                MergeItemIds = mergeItemIds,
                DisableMergedItems = disableMergedItems
            };

            await _restClient.PostAsync("/api/partners/merge", payload);

            if (disableMergedItems)
            {
                var mergeSet = new HashSet<string>(mergeItemIds, StringComparer.OrdinalIgnoreCase);

                lock (_syncLock)
                {
                    if (_memoryCache != null)
                    {
                        foreach (var item in _memoryCache.Where(p => mergeSet.Contains(p.Id)))
                        {
                            item.IsDisabled = true;
                        }
                    }
                }

                foreach (var id in mergeItemIds)
                {
                    if (_cardCache.TryGetValue(id, out var cachedPartner))
                    {
                        cachedPartner.IsDisabled = true;
                    }
                }
            }

            await ReloadAsync();
        }
        catch (Exception ex)
        {
            var detail = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            throw new Exception(string.Format("Failed to merge partners into '{0}'. Details: {1}", mainItemId, detail), ex);
        }
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
            var apiResult = await _restClient.GetAsync<Dictionary<string, Dictionary<string, int>>>($"/api/partners/facets?fields={fieldsParam}");
            if (apiResult != null)
            {
                foreach (var kvp in apiResult) result[kvp.Key] = kvp.Value;
            }
        }
        catch { }

        return result;
    }
}
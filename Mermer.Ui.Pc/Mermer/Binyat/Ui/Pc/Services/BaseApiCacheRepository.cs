using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Mermer.Data.Models;
using Mermer.Data.Storage;
using Mermer.Http;

namespace Mermer.Ui.Pc.Services;

public class BaseApiCacheRepository<T> : IRepository<T>, IReadOnlyRepository<T>, IRepositoryWithFacets<T> where T : class, IModel
{
    protected readonly RestClient _restClient;
    protected readonly string DocType;
    protected readonly string ApiRoute;

    protected List<T>? _ramCache;
    protected readonly object _syncLock = new();

    public BaseApiCacheRepository(RestClient restClient, string docType, string apiRoute)
    {
        _restClient = restClient ?? throw new ArgumentNullException(nameof(restClient));
        DocType = docType;
        ApiRoute = apiRoute;
    }

    public virtual async Task<IEnumerable<T>> GetAllAsync()
    {
        lock (_syncLock)
        {
            if (_ramCache != null && _ramCache.Count > 0)
                return _ramCache.ToList();
        }

        try
        {
            var remote = await _restClient.GetAsync<List<T>>($"/api/{ApiRoute}");
            if (remote != null)
            {
                lock (_syncLock)
                {
                    _ramCache = remote;
                }
                return remote;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BaseApiCacheRepository<{typeof(T).Name}>] Ошибка загрузки: {ex.Message}");
        }

        lock (_syncLock)
        {
            return _ramCache?.ToList() ?? new List<T>();
        }
    }

    public async Task<T> GetAsync(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        lock (_syncLock)
        {
            var existing = _ramCache?.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            if (existing != null) return existing;
        }

        try
        {
            var remote = await _restClient.GetAsync<T>($"/api/{ApiRoute}/{id}");
            if (remote != null)
            {
                lock (_syncLock)
                {
                    if (_ramCache != null)
                    {
                        var idx = _ramCache.FindIndex(x => string.Equals(x.Id, remote.Id, StringComparison.OrdinalIgnoreCase));
                        if (idx >= 0) _ramCache[idx] = remote;
                        else _ramCache.Add(remote);
                    }
                }
                return remote;
            }
        }
        catch { }

        var all = await GetAllAsync();
        return all.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IEnumerable<T>> GetAsync(string[] ids)
    {
        if (ids == null || !ids.Any()) return Enumerable.Empty<T>();
        var all = await GetAllAsync();
        var idSet = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        return all.Where(x => idSet.Contains(x.Id)).ToList();
    }

    public async Task<IEnumerable<T>> GetListAsync(params string[] ids) => await GetAsync(ids);

    public virtual async Task<IEnumerable<T>> GetAsync(params Expression<Func<T, bool>>[] predicates)
    {
        var all = await GetAllAsync();
        var query = all.AsQueryable();
        if (predicates != null)
        {
            foreach (var p in predicates.Where(x => x != null)) query = query.Where(p);
        }
        return query.ToList();
    }

    public async Task<int> CountAsync(params Expression<Func<T, bool>>[] predicates) => (await GetAsync(predicates)).Count();

    public async Task SaveAsync(T entity)
    {
        if (entity == null) return;
        bool isNew = string.IsNullOrEmpty(entity.Id) || entity.Id == Guid.Empty.ToString();
        if (isNew) entity.Id = Guid.NewGuid().ToString();

        // 1. Обновляем оперативный кэш
        lock (_syncLock)
        {
            if (_ramCache != null)
            {
                var idx = _ramCache.FindIndex(x => string.Equals(x.Id, entity.Id, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0) _ramCache[idx] = entity;
                else _ramCache.Add(entity);
            }
        }

        // 2. Мгновенное сохранение в локальный API (1-2 мс)
        try
        {
            if (isNew) await _restClient.PostAsync($"/api/{ApiRoute}", entity);
            else await _restClient.PutAsync($"/api/{ApiRoute}/{entity.Id}", entity);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BaseApiCacheRepository<{typeof(T).Name}>] Ошибка сохранения: {ex.Message}");
            throw;
        }

        // 3. Уведомляем UI об обновлении сущности
        UiEventBus.NotifyDocumentModified<T>(entity.Id);
    }

    public async Task CreateAsync(T entity) => await SaveAsync(entity);
    public async Task UpdateAsync(T entity) => await SaveAsync(entity);

    public async Task DeleteAsync(string id)
    {
        if (string.IsNullOrEmpty(id)) return;

        lock (_syncLock)
        {
            if (_ramCache != null)
            {
                _ramCache.RemoveAll(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            }
        }

        try
        {
            await _restClient.DeleteAsync($"/api/{ApiRoute}/{id}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BaseApiCacheRepository<{typeof(T).Name}>] Ошибка удаления: {ex.Message}");
            throw;
        }

        UiEventBus.NotifyDocumentModified<T>(id);
    }

    public async Task<Dictionary<string, Dictionary<string, int>>> GetFacets(params string[] fields)
    {
        var result = new Dictionary<string, Dictionary<string, int>>();
        if (fields != null)
        {
            foreach (var field in fields) result[field] = new Dictionary<string, int>();
        }
        return await Task.FromResult(result);
    }
}
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
    protected bool _isSyncing = false;

    public BaseApiCacheRepository(RestClient restClient, string docType, string apiRoute)
    {
        _restClient = restClient ?? throw new ArgumentNullException(nameof(restClient));
        DocType = docType;
        ApiRoute = apiRoute;
    }

    public virtual async Task<IEnumerable<T>> GetAllAsync()
    {
        // 1. Мгновенная отдача (0 миллисекунд)
        if (_ramCache == null)
        {
            _ramCache = LocalSqliteCache.GetAllDocuments<T>(DocType)?.ToList() ?? new List<T>();
        }

        // 2. Фоновая отправка и загрузка данных (не тормозит UI)
        if (!_isSyncing)
        {
            _isSyncing = true;
            _ = Task.Run(async () =>
            {
                try
                {
                    var unsynced = LocalSqliteCache.GetUnsyncedDocuments<T>(DocType);
                    if (unsynced != null)
                    {
                        foreach (var item in unsynced)
                        {
                            await _restClient.PutAsync($"/api/{ApiRoute}/{item.id}", item.entity);
                            LocalSqliteCache.SaveDocument(DocType, item.id, item.entity, isSynced: true);
                        }
                    }

                    var remote = await _restClient.GetAsync<List<T>>($"/api/{ApiRoute}");
                    if (remote != null)
                    {
                        foreach (var entity in remote)
                        {
                            LocalSqliteCache.SaveDocument(DocType, entity.Id, entity, isSynced: true);
                        }
                        _ramCache = remote;
                    }
                }
                catch { }
                finally { _isSyncing = false; }
            });
        }

        return _ramCache;
    }

    public async Task<T> GetAsync(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
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

        LocalSqliteCache.SaveDocument(DocType, entity.Id, entity, isSynced: false);

        if (_ramCache != null)
        {
            var existing = _ramCache.FirstOrDefault(x => x.Id == entity.Id);
            if (existing != null) _ramCache.Remove(existing);
            _ramCache.Add(entity);
        }

        // Мгновенно уведомляем открытые списки данного типа T (Depository, Warehouse, Office и т.д.)
        UiEventBus.NotifyDocumentModified<T>(entity.Id);

        try
        {
            if (isNew) await _restClient.PostAsync($"/api/{ApiRoute}", entity);
            else await _restClient.PutAsync($"/api/{ApiRoute}/{entity.Id}", entity);
            LocalSqliteCache.SaveDocument(DocType, entity.Id, entity, isSynced: true);

            // Подтверждаем обновление после ответа сервера
            UiEventBus.NotifyDocumentModified<T>(entity.Id);
        }
        catch { }
    }

    public async Task CreateAsync(T entity) => await SaveAsync(entity);
    public async Task UpdateAsync(T entity) => await SaveAsync(entity);

    public async Task DeleteAsync(string id)
    {
        if (string.IsNullOrEmpty(id)) return;

        if (_ramCache != null)
        {
            var existing = _ramCache.FirstOrDefault(x => x.Id == id);
            if (existing != null) _ramCache.Remove(existing);
        }

        // Мгновенно убираем удаленный элемент из открытого списка в UI
        UiEventBus.NotifyDocumentModified<T>(id);

        try { await _restClient.DeleteAsync($"/api/{ApiRoute}/{id}"); } catch { }
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
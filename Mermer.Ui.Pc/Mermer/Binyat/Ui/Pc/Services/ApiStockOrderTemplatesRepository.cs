using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Mermer.Data.Storage;
using Mermer.Http;
using Mermer.Warehousing.Ordering.Models;

namespace Mermer.Ui.Pc.Services
{
    public class ApiStockOrderTemplatesRepository :
        IRepository<StockOrderTemplate>,
        IReadOnlyRepository<StockOrderTemplate>,
        IRepositoryWithFacets<StockOrderTemplate>
    {
        private readonly RestClient _restClient;
        private const string DocType = "StockOrderTemplate";

        private static readonly ConcurrentDictionary<string, StockOrderTemplate> _cardCache = new(StringComparer.OrdinalIgnoreCase);
        private static List<StockOrderTemplate> _memoryCache;
        private static readonly object _syncLock = new();

        public ApiStockOrderTemplatesRepository(RestClient restClient)
        {
            _restClient = restClient ?? throw new ArgumentNullException(nameof(restClient));
        }

        public async Task<IEnumerable<StockOrderTemplate>> GetAllAsync()
        {
            lock (_syncLock)
            {
                if (_memoryCache != null && _memoryCache.Count > 0)
                    return _memoryCache;
            }

            // 1. Быстрое чтение из локального SQLite
            var local = LocalSqliteCache.GetAllDocuments<StockOrderTemplate>(DocType)?.ToList() ?? new List<StockOrderTemplate>();
            if (local.Any())
            {
                lock (_syncLock)
                {
                    _memoryCache = local;
                    foreach (var item in local)
                    {
                        if (!string.IsNullOrEmpty(item?.Id)) _cardCache[item.Id] = item;
                    }
                }
            }

            // 2. Если в локальной БД пусто — подгружаем с бэкенда
            if (_memoryCache == null || _memoryCache.Count == 0)
            {
                try
                {
                    var remote = await _restClient.GetAsync<List<StockOrderTemplate>>("/api/warehousing/order-templates");
                    if (remote != null && remote.Any())
                    {
                        lock (_syncLock)
                        {
                            _memoryCache = remote;
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
                }
                catch { }
            }

            return _memoryCache ?? Enumerable.Empty<StockOrderTemplate>();
        }

        public async Task<StockOrderTemplate> GetAsync(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            if (_cardCache.TryGetValue(id, out var cached))
                return cached;

            await GetAllAsync();
            if (_cardCache.TryGetValue(id, out cached))
                return cached;

            return null;
        }

        public async Task<IEnumerable<StockOrderTemplate>> GetAsync(string[] ids)
        {
            if (ids == null || !ids.Any()) return Enumerable.Empty<StockOrderTemplate>();
            var all = await GetAllAsync();
            var idSet = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
            return all.Where(x => idSet.Contains(x.Id)).ToList();
        }

        public async Task<IEnumerable<StockOrderTemplate>> GetAsync(params Expression<Func<StockOrderTemplate, bool>>[] predicates)
        {
            var all = await GetAllAsync();
            var query = all.AsQueryable();
            if (predicates != null)
            {
                foreach (var p in predicates.Where(x => x != null)) query = query.Where(p);
            }
            return query.ToList();
        }

        public async Task<int> CountAsync(params Expression<Func<StockOrderTemplate, bool>>[] predicates)
        {
            return (await GetAsync(predicates)).Count();
        }

        public Task CreateAsync(StockOrderTemplate entity) => SaveAsync(entity);
        public Task UpdateAsync(StockOrderTemplate entity) => SaveAsync(entity);

        public async Task SaveAsync(StockOrderTemplate entity)
        {
            if (entity == null) return;
            if (string.IsNullOrEmpty(entity.Id)) entity.Id = Guid.NewGuid().ToString();

            _cardCache[entity.Id] = entity;
            lock (_syncLock)
            {
                if (_memoryCache != null)
                {
                    int idx = _memoryCache.FindIndex(x => x.Id == entity.Id);
                    if (idx >= 0) _memoryCache[idx] = entity;
                    else _memoryCache.Add(entity);
                }
            }

            LocalSqliteCache.SaveDocument(DocType, entity.Id, entity, isSynced: false);

            _ = Task.Run(async () =>
            {
                try
                {
                    await _restClient.PostAsync("/api/warehousing/order-templates", entity);
                    LocalSqliteCache.SaveDocument(DocType, entity.Id, entity, isSynced: true);
                }
                catch { }
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
                try { await _restClient.DeleteAsync($"/api/warehousing/order-templates/{id}"); } catch { }
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
                var query = fields != null && fields.Any() ? "?fields=" + string.Join(",", fields) : "";
                var result = await _restClient.GetAsync<Dictionary<string, Dictionary<string, int>>>($"/api/warehousing/order-templates/facets{query}");
                if (result != null)
                {
                    foreach (var kvp in result) dict[kvp.Key] = kvp.Value;
                }
            }
            catch { }

            return dict;
        }
    }
}
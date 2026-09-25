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

namespace Mermer.Ui.Pc.Services
{
    public class ApiStockAlternativesRepository : IStockAlternativesRepository
    {
        private readonly RestClient _restClient;
        private const string DocType = "StockAlternative";

        private static readonly ConcurrentDictionary<string, StockAlternative> _cardCache = new(StringComparer.OrdinalIgnoreCase);
        private static List<StockAlternative> _memoryCache;
        private static readonly object _syncLock = new();

        public ApiStockAlternativesRepository(RestClient restClient)
        {
            _restClient = restClient ?? throw new ArgumentNullException(nameof(restClient));
        }

        public async Task<IEnumerable<StockAlternative>> GetAllAsync()
        {
            lock (_syncLock)
            {
                if (_memoryCache != null && _memoryCache.Count > 0)
                    return _memoryCache;
            }

            // 1. Читаем из локального SQLite
            var local = LocalSqliteCache.GetAllDocuments<StockAlternative>(DocType)?.ToList() ?? new List<StockAlternative>();
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
                    var remote = await _restClient.GetAsync<List<StockAlternative>>("/api/stock-management/alternatives");
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

            return _memoryCache ?? Enumerable.Empty<StockAlternative>();
        }

        public async Task<StockAlternative> GetAsync(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            if (_cardCache.TryGetValue(id, out var cached))
                return cached;

            await GetAllAsync();
            if (_cardCache.TryGetValue(id, out cached))
                return cached;

            return null;
        }

        public async Task<IEnumerable<StockAlternative>> GetAsync(string[] ids)
        {
            if (ids == null || !ids.Any()) return Enumerable.Empty<StockAlternative>();
            var all = await GetAllAsync();
            var idSet = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
            return all.Where(x => idSet.Contains(x.Id)).ToList();
        }

        public async Task<IEnumerable<StockAlternative>> GetAsync(params Expression<Func<StockAlternative, bool>>[] predicates)
        {
            var all = await GetAllAsync();
            var query = all.AsQueryable();
            if (predicates != null)
            {
                foreach (var p in predicates.Where(x => x != null)) query = query.Where(p);
            }
            return query.ToList();
        }

        public async Task<int> CountAsync(params Expression<Func<StockAlternative, bool>>[] predicates)
        {
            return (await GetAsync(predicates)).Count();
        }

        public Task CreateAsync(StockAlternative entity) => SaveAsync(entity);
        public Task UpdateAsync(StockAlternative entity) => SaveAsync(entity);

        public async Task SaveAsync(StockAlternative entity)
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
                    await _restClient.PostAsync("/api/stock-management/alternatives", entity);
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
                try
                {
                    await _restClient.DeleteAsync($"/api/stock-management/alternatives/{id}");
                }
                catch { }
            });
        }

        public async Task<SingleStockAlternative> GetAlternativesAsync(string stockId)
        {
            if (string.IsNullOrEmpty(stockId))
                return new SingleStockAlternative { StockId = stockId, Alternatives = Array.Empty<string>() };

            try
            {
                var result = await _restClient.GetAsync<SingleStockAlternative>($"/api/stock-management/alternatives/for-stock/{stockId}");
                return result ?? new SingleStockAlternative { StockId = stockId, Alternatives = Array.Empty<string>() };
            }
            catch
            {
                return new SingleStockAlternative { StockId = stockId, Alternatives = Array.Empty<string>() };
            }
        }
    }
}
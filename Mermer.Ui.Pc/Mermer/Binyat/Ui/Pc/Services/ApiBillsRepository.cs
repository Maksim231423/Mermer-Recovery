using Mermer.Commerce.Models;
using Mermer.Data.Storage;
using Mermer.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Mermer.Ui.Pc.Services;

public class ApiBillsRepository : IRepositoryWithFacets<Bill>, IRepository<Bill>, IReadOnlyRepository<Bill>
{
    private readonly RestClient _restClient;
    private const string DocType = "Bill";

    public ApiBillsRepository(RestClient restClient)
    {
        _restClient = restClient ?? throw new ArgumentNullException(nameof(restClient));
    }

    public async Task<Bill> GetAsync(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        var allLocal = LocalSqliteCache.GetAllDocuments<Bill>(DocType);
        var local = allLocal?.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        if (local != null) return local;

        try
        {
            var remote = await _restClient.GetAsync<Bill>($"/api/bills/{id}");
            if (remote != null)
            {
                LocalSqliteCache.SaveDocument(DocType, remote.Id, remote, isSynced: true);
                return remote;
            }
        }
        catch { }

        return null;
    }

    public async Task<IEnumerable<Bill>> GetAsync(string[] ids)
    {
        if (ids == null || !ids.Any()) return Enumerable.Empty<Bill>();
        var all = await GetAllAsync();
        var idSet = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        return all.Where(b => idSet.Contains(b.Id)).ToList();
    }

    public async Task<IEnumerable<Bill>> GetAsync(params Expression<Func<Bill, bool>>[] predicates)
    {
        var all = await GetAllAsync();
        var query = all.AsQueryable();

        if (predicates != null && predicates.Any())
        {
            foreach (var predicate in predicates.Where(p => p != null))
            {
                query = query.Where(predicate);
            }
        }

        return query.ToList();
    }

    private static List<Bill> _memoryCache;
    private static DateTime _lastFetchTime = DateTime.MinValue;
    private static readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

    private async Task<IEnumerable<Bill>> GetAllAsync()
    {
        // Отдаем кэш из памяти, если прошло меньше 5 секунд (защита от 11 параллельных вызовов грида)
        if (_memoryCache != null && (DateTime.UtcNow - _lastFetchTime).TotalSeconds < 5)
        {
            return _memoryCache;
        }

        await _lock.WaitAsync();
        try
        {
            if (_memoryCache != null && (DateTime.UtcNow - _lastFetchTime).TotalSeconds < 5)
            {
                return _memoryCache;
            }

            // 1. Быстро забираем с бэкенда
            try
            {
                var remote = await _restClient.GetAsync<IEnumerable<Bill>>("/api/bills");
                if (remote != null && remote.Any())
                {
                    var list = remote.ToList();
                    _memoryCache = list;
                    _lastFetchTime = DateTime.UtcNow;

                    // Сохраняем в локальный SQLite асинхронно одной пачкой в фоне, не блокируя UI
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            foreach (var bill in list)
                            {
                                LocalSqliteCache.SaveDocument(DocType, bill.Id, bill, isSynced: true);
                            }
                        }
                        catch { }
                    });

                    return list;
                }
            }
            catch { }

            // 2. Фолбэк на локальный SQLite, если сервер недоступен
            if (_memoryCache == null)
            {
                _memoryCache = LocalSqliteCache.GetAllDocuments<Bill>(DocType)?.ToList() ?? new List<Bill>();
                _lastFetchTime = DateTime.UtcNow;
            }

            return _memoryCache;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<int> CountAsync(params Expression<Func<Bill, bool>>[] predicates)
    {
        return (await GetAsync(predicates)).Count();
    }

    public async Task CreateAsync(Bill model) => await SaveAsync(model);

    public async Task UpdateAsync(Bill model) => await SaveAsync(model);

    public async Task<Bill> SaveAsync(Bill entity)
    {
        if (entity == null) return null;
        if (string.IsNullOrEmpty(entity.Id)) entity.Id = Guid.NewGuid().ToString();

        // Мгновенно сохраняем в локальный SQLite
        LocalSqliteCache.SaveDocument(DocType, entity.Id, entity, isSynced: false);

        try
        {
            await _restClient.PostAsync("/api/bills", entity);
            LocalSqliteCache.SaveDocument(DocType, entity.Id, entity, isSynced: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BILL SYNC WARNING]: {ex.Message}");
        }

        return entity;
    }

    public async Task DeleteAsync(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        try
        {
            await _restClient.DeleteAsync($"/api/bills/{id}");
        }
        catch { }
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
            var fieldsParam = fields != null && fields.Length > 0 ? string.Join(",", fields) : "Date";
            var apiResult = await _restClient.GetAsync<Dictionary<string, Dictionary<string, int>>>($"/api/bills/facets?fields={fieldsParam}");
            if (apiResult != null)
            {
                foreach (var kvp in apiResult) dict[kvp.Key] = kvp.Value;
            }
        }
        catch { }

        return dict;
    }
}
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Mermer.Authorization.Models;
using Mermer.Data.Storage;
using Mermer.Http;

namespace Mermer.Ui.Pc.Services;

public class ApiRolesRepository : IRepository<Role>
{
    private readonly RestClient _restClient;
    private const string DocType = "Role";

    private static readonly ConcurrentDictionary<string, Role> _cardCache = new(StringComparer.OrdinalIgnoreCase);
    private static List<Role> _rolesCache;
    private static readonly object _syncLock = new();

    public ApiRolesRepository(RestClient restClient)
    {
        _restClient = restClient ?? throw new ArgumentNullException(nameof(restClient));
    }

    public async Task<Role> GetAsync(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        if (_cardCache.TryGetValue(id, out var cached))
            return cached;

        await GetAllAsync();
        if (_cardCache.TryGetValue(id, out cached))
            return cached;

        try
        {
            var remote = await _restClient.GetAsync<Role>($"/api/roles/{id}");
            if (remote != null)
            {
                var normalized = NormalizeAuthorizations(remote);
                _cardCache[normalized.Id] = normalized;
                LocalSqliteCache.SaveDocument(DocType, normalized.Id, normalized, isSynced: true);
                return normalized;
            }
        }
        catch { }

        return null;
    }

    public async Task<IEnumerable<Role>> GetAsync(string[] ids)
    {
        if (ids == null || !ids.Any()) return Enumerable.Empty<Role>();
        var all = await GetAllAsync();
        var idSet = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        return all.Where(r => idSet.Contains(r.Id)).ToList();
    }

    public async Task<IEnumerable<Role>> GetAsync(params Expression<Func<Role, bool>>[] predicates)
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

    public async Task<IEnumerable<Role>> GetAllAsync()
    {
        lock (_syncLock)
        {
            if (_rolesCache != null && _rolesCache.Count > 0)
                return _rolesCache;
        }

        // 1. Читаем из локального SQLite
        var local = LocalSqliteCache.GetAllDocuments<Role>(DocType)?.ToList() ?? new List<Role>();
        if (local.Any())
        {
            lock (_syncLock)
            {
                _rolesCache = local.Select(NormalizeAuthorizations).ToList();
                foreach (var r in _rolesCache)
                {
                    if (!string.IsNullOrEmpty(r?.Id)) _cardCache[r.Id] = r;
                }
            }
        }

        // 2. Если в локальной БД пусто — подгружаем с API
        if (_rolesCache == null || _rolesCache.Count == 0)
        {
            try
            {
                var remote = await _restClient.GetAsync<List<Role>>("/api/roles");
                if (remote != null && remote.Any())
                {
                    lock (_syncLock)
                    {
                        _rolesCache = remote.Select(NormalizeAuthorizations).ToList();
                        foreach (var role in _rolesCache)
                        {
                            if (!string.IsNullOrEmpty(role?.Id))
                            {
                                _cardCache[role.Id] = role;
                                LocalSqliteCache.SaveDocument(DocType, role.Id, role, isSynced: true);
                            }
                        }
                    }
                }
            }
            catch { }
        }

        return _rolesCache ?? Enumerable.Empty<Role>();
    }

    public async Task<int> CountAsync(params Expression<Func<Role, bool>>[] predicates)
    {
        return (await GetAsync(predicates)).Count();
    }

    public Task CreateAsync(Role model) => SaveAsync(model, isNew: true);
    public Task UpdateAsync(Role model) => SaveAsync(model, isNew: false);

    private async Task SaveAsync(Role model, bool isNew)
    {
        if (model == null) return;
        if (string.IsNullOrEmpty(model.Id)) model.Id = Guid.NewGuid().ToString();

        var normalized = NormalizeAuthorizations(model);
        _cardCache[normalized.Id] = normalized;

        lock (_syncLock)
        {
            if (_rolesCache != null)
            {
                int idx = _rolesCache.FindIndex(x => x.Id == normalized.Id);
                if (idx >= 0) _rolesCache[idx] = normalized;
                else _rolesCache.Add(normalized);
            }
        }

        LocalSqliteCache.SaveDocument(DocType, normalized.Id, normalized, isSynced: false);

        _ = Task.Run(async () =>
        {
            try
            {
                var payload = CreatePayload(normalized);
                if (isNew) await _restClient.PostAsync("/api/roles", payload);
                else await _restClient.PutAsync($"/api/roles/{normalized.Id}", payload);

                LocalSqliteCache.SaveDocument(DocType, normalized.Id, normalized, isSynced: true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ROLE SYNC WARNING]: {ex.Message}");
            }
        });
    }

    public async Task DeleteAsync(string id)
    {
        if (string.IsNullOrEmpty(id)) return;

        _cardCache.TryRemove(id, out _);
        lock (_syncLock)
        {
            _rolesCache?.RemoveAll(x => x.Id == id);
        }

        _ = Task.Run(async () =>
        {
            try { await _restClient.DeleteAsync($"/api/roles/{id}"); } catch { }
        });
    }

    private static Role NormalizeAuthorizations(Role role)
    {
        if (role?.Authorizations != null && role.Authorizations.Any())
        {
            role.Authorizations = role.Authorizations.ToDictionary(
                x => x.Key.First().ToString().ToUpper() + x.Key.Substring(1),
                x => x.Value);
        }
        return role;
    }

    private static object CreatePayload(Role model)
    {
        return new
        {
            Id = model.Id,
            Name = model.Name,
            Description = model.Description,
            IsDisabled = model.IsDisabled,
            Authorizations = model.Authorizations?.ToDictionary(x => x.Key, x => x.Value) ?? new Dictionary<string, int>()
        };
    }
}
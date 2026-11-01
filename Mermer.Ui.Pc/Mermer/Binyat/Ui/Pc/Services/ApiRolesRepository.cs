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

    private static readonly ConcurrentDictionary<string, Role> _cardCache = new(StringComparer.OrdinalIgnoreCase);
    private static List<Role> _rolesCache;
    private static readonly object _syncLock = new();

    public ApiRolesRepository(RestClient restClient)
    {
        _restClient = restClient ?? throw new Exception("Http RestClient cannot be null.");
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
                        }
                    }
                }
            }
        }
        catch { }

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
        if (model == null)
        {
            throw new Exception("Cannot save an empty Role entity.");
        }

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

        try
        {
            var payload = CreatePayload(normalized);
            if (isNew) await _restClient.PostAsync("/api/roles", payload);
            else await _restClient.PutAsync($"/api/roles/{normalized.Id}", payload);
        }
        catch (Exception ex)
        {
            var detail = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            throw new Exception(string.Format("Failed to save Role '{0}'. Details: {1}", model.Id, detail), ex);
        }

        UiEventBus.NotifyDocumentModified<Role>(normalized.Id);
    }

    public async Task DeleteAsync(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            throw new Exception("Role identifier cannot be empty for delete operation.");
        }

        _cardCache.TryRemove(id, out _);
        lock (_syncLock)
        {
            _rolesCache?.RemoveAll(x => x.Id == id);
        }

        try
        {
            await _restClient.DeleteAsync($"/api/roles/{id}");
        }
        catch (Exception ex)
        {
            var detail = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            throw new Exception(string.Format("Failed to delete Role '{0}'. Details: {1}", id, detail), ex);
        }

        UiEventBus.NotifyDocumentModified<Role>(id);
    }

    private static Role NormalizeAuthorizations(Role role)
    {
        if (role?.Authorizations != null && role.Authorizations.Any())
        {
            role.Authorizations = new Dictionary<string, int>(role.Authorizations, StringComparer.OrdinalIgnoreCase);
        }
        else if (role != null)
        {
            role.Authorizations = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }
        return role;
    }

    private static object CreatePayload(Role model)
    {
        var authDict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (model.Authorizations != null)
        {
            foreach (var kvp in model.Authorizations)
            {
                authDict[kvp.Key] = kvp.Value;
            }
        }

        return new
        {
            Id = model.Id,
            Name = model.Name ?? string.Empty,
            Description = model.Description,
            IsDisabled = model.IsDisabled,
            Authorizations = authDict
        };
    }
}
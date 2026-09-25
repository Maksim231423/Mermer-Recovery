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

public class ApiUsersRepository : IRepository<User>
{
    private readonly RestClient _restClient;
    private const string DocType = "User";

    private static readonly ConcurrentDictionary<string, User> _cardCache = new(StringComparer.OrdinalIgnoreCase);
    private static List<User> _usersCache;
    private static readonly object _syncLock = new();

    public ApiUsersRepository(RestClient restClient)
    {
        _restClient = restClient ?? throw new ArgumentNullException(nameof(restClient));
    }

    public async Task<User> GetAsync(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        if (_cardCache.TryGetValue(id, out var cached))
            return SanitizeUser(cached);

        await GetAllAsync();
        if (_cardCache.TryGetValue(id, out cached))
            return SanitizeUser(cached);

        try
        {
            var remote = await _restClient.GetAsync<User>($"/api/users/{id}");
            if (remote != null)
            {
                _cardCache[remote.Id] = remote;
                LocalSqliteCache.SaveDocument(DocType, remote.Id, remote, isSynced: true);
                return SanitizeUser(remote);
            }
        }
        catch { }

        return null;
    }

    public async Task<IEnumerable<User>> GetAsync(string[] ids)
    {
        if (ids == null || !ids.Any()) return Enumerable.Empty<User>();
        var all = await GetAllAsync();
        var idSet = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        return all.Where(u => idSet.Contains(u.Id)).Select(SanitizeUser).ToList();
    }

    public async Task<IEnumerable<User>> GetAsync(params Expression<Func<User, bool>>[] predicates)
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

        return query.Select(SanitizeUser).ToList();
    }

    public async Task<IEnumerable<User>> GetAllAsync()
    {
        lock (_syncLock)
        {
            if (_usersCache != null && _usersCache.Count > 0)
                return _usersCache.Select(SanitizeUser).ToList();
        }

        // 1. Читаем из локального SQLite
        var local = LocalSqliteCache.GetAllDocuments<User>(DocType)?.ToList() ?? new List<User>();
        if (local.Any())
        {
            lock (_syncLock)
            {
                _usersCache = local;
                foreach (var u in local)
                {
                    if (!string.IsNullOrEmpty(u?.Id)) _cardCache[u.Id] = u;
                }
            }
        }

        // 2. Если локально пусто — запрашиваем API
        if (_usersCache == null || _usersCache.Count == 0)
        {
            try
            {
                var remote = await _restClient.GetAsync<List<User>>("/api/users");
                if (remote != null && remote.Any())
                {
                    lock (_syncLock)
                    {
                        _usersCache = remote;
                        foreach (var user in remote)
                        {
                            if (!string.IsNullOrEmpty(user?.Id))
                            {
                                _cardCache[user.Id] = user;
                                LocalSqliteCache.SaveDocument(DocType, user.Id, user, isSynced: true);
                            }
                        }
                    }
                }
            }
            catch { }
        }

        return (_usersCache ?? new List<User>()).Select(SanitizeUser).ToList();
    }

    public async Task<int> CountAsync(params Expression<Func<User, bool>>[] predicates)
    {
        return (await GetAsync(predicates)).Count();
    }

    public Task CreateAsync(User model) => SaveAsync(model, isNew: true);
    public Task UpdateAsync(User model) => SaveAsync(model, isNew: false);

    private async Task SaveAsync(User model, bool isNew)
    {
        if (model == null) return;
        if (string.IsNullOrEmpty(model.Id)) model.Id = Guid.NewGuid().ToString();

        _cardCache[model.Id] = model;
        lock (_syncLock)
        {
            if (_usersCache != null)
            {
                int idx = _usersCache.FindIndex(x => x.Id == model.Id);
                if (idx >= 0) _usersCache[idx] = model;
                else _usersCache.Add(model);
            }
        }

        LocalSqliteCache.SaveDocument(DocType, model.Id, model, isSynced: false);

        _ = Task.Run(async () =>
        {
            try
            {
                var payload = CreatePayload(model);
                if (isNew) await _restClient.PostAsync("/api/users", payload);
                else await _restClient.PutAsync($"/api/users/{model.Id}", payload);

                LocalSqliteCache.SaveDocument(DocType, model.Id, model, isSynced: true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[USER SYNC WARNING]: {ex.Message}");
            }
        });
    }

    public async Task DeleteAsync(string id)
    {
        if (string.IsNullOrEmpty(id)) return;

        _cardCache.TryRemove(id, out _);
        lock (_syncLock)
        {
            _usersCache?.RemoveAll(x => x.Id == id);
        }

        _ = Task.Run(async () =>
        {
            try { await _restClient.DeleteAsync($"/api/users/{id}"); } catch { }
        });
    }

    private static User SanitizeUser(User user)
    {
        if (user == null) return null;
        return new User
        {
            Id = user.Id,
            Username = user.Username,
            Password = null, // Сохраняем поведение Couchbase: пароль никогда не отдается в UI
            IsAdmin = user.IsAdmin,
            IsDisabled = user.IsDisabled,
            Description = user.Description,
            Roles = user.Roles?.ToList() ?? new List<string>(),
            AccountPrivileges = user.AccountPrivileges?.ToDictionary(x => x.Key, x => x.Value) ?? new Dictionary<string, Mermer.Authorization.Enums.AccountAccessLevel>()
        };
    }

    private static object CreatePayload(User model)
    {
        return new
        {
            Id = model.Id,
            Username = model.Username,
            Password = model.Password,
            IsAdmin = model.IsAdmin,
            IsDisabled = model.IsDisabled,
            Description = model.Description,
            Roles = model.Roles?.ToList() ?? new List<string>(),
            AccountPrivileges = model.AccountPrivileges?.ToDictionary(x => x.Key, x => (int)x.Value) ?? new Dictionary<string, int>()
        };
    }
}
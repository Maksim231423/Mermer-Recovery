using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mermer.Authorization.Enums;
using Mermer.Authorization.Models;
using Mermer.Core.Authorization.Services;
using Mermer.Data.Storage;
using Mermer.Enterprise.Models;
using Mermer.FundsManagement.Models;
using Mermer.Http;
using Mermer.Ui.Core.Helpers;
using Mermer.Ui.Pc.DTOs;

namespace Mermer.Ui.Pc.Services
{
    public class ApiLoginService : LoginService
    {
        private readonly RestClient _restClient;
        private readonly IRepository<Role> _rolesRepository;
        private Dictionary<string, AccountAccessLevel> _cachedAccountPrivileges;

        public ApiLoginService(RestClient restClient, IRepository<Role> rolesRepository)
        {
            _restClient = restClient ?? throw new Exception("Http RestClient cannot be null.");
            _rolesRepository = rolesRepository;
        }

        protected override async Task<User> GetUser(string username, string password)
        {
            try
            {
                var apiResponse = await _restClient.PostAsync<ApiLoginResponse>("/api/auth/login", new
                {
                    Username = username,
                    Password = password
                });

                if (apiResponse == null)
                {
                    throw new Exception("Authentication API returned empty response.");
                }

                bool isAdmin = apiResponse.IsAdmin
                               || string.Equals(apiResponse.Role, "Admin", StringComparison.OrdinalIgnoreCase)
                               || string.Equals(username, "admin", StringComparison.OrdinalIgnoreCase);

                var userRoles = apiResponse.Roles ?? new List<string>();

                // Преобразуем словарь account_privileges из API в словарь клиентских Enums
                var accountPrivileges = new Dictionary<string, AccountAccessLevel>(StringComparer.OrdinalIgnoreCase);
                if (apiResponse.AccountPrivileges != null)
                {
                    foreach (var kvp in apiResponse.AccountPrivileges)
                    {
                        accountPrivileges[kvp.Key] = (AccountAccessLevel)kvp.Value;
                    }
                }

                // Сохраняем в кэш для передачи в Session.Accounts
                _cachedAccountPrivileges = accountPrivileges;

                return new User
                {
                    Id = apiResponse.Id,
                    Username = apiResponse.Username,
                    IsAdmin = isAdmin,
                    Roles = userRoles,
                    AccountPrivileges = accountPrivileges
                };
            }
            catch (Exception ex)
            {
                var detail = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                throw new Exception(string.Format("Authentication failed for user '{0}'. Details: {1}", username, detail), ex);
            }
        }

        protected override async Task<IEnumerable<Role>> GetRoles(IEnumerable<string> roleIds)
        {
            // 1. Гарантированно наполняем Session.Accounts сразу после создания сессии
            if (Session != null && _cachedAccountPrivileges != null)
            {
                Session.Accounts = new Dictionary<string, AccountAccessLevel>(_cachedAccountPrivileges, StringComparer.OrdinalIgnoreCase);
            }

            // 2. Включаем централизованную защиту на уровне Reference<T>
            ApplyGlobalSecurityFilter();

            if (roleIds == null || !roleIds.Any())
                return Enumerable.Empty<Role>();

            try
            {
                var allRoles = await _rolesRepository.GetAsync();
                if (allRoles == null) return Enumerable.Empty<Role>();

                var roleIdSet = new HashSet<string>(roleIds, StringComparer.OrdinalIgnoreCase);
                var matchedRoles = allRoles.Where(r => roleIdSet.Contains(r.Id)).ToList();

                if (Session != null)
                {
                    var mergedAuth = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    foreach (var role in matchedRoles)
                    {
                        if (role.Authorizations == null) continue;
                        foreach (var kvp in role.Authorizations)
                        {
                            if (!mergedAuth.ContainsKey(kvp.Key))
                                mergedAuth[kvp.Key] = kvp.Value;
                            else
                                mergedAuth[kvp.Key] = Math.Min(mergedAuth[kvp.Key], kvp.Value);
                        }
                    }
                    Session.Roles = mergedAuth;

                    if (_cachedAccountPrivileges != null)
                    {
                        Session.Accounts = new Dictionary<string, AccountAccessLevel>(_cachedAccountPrivileges, StringComparer.OrdinalIgnoreCase);
                    }

                    ApplyGlobalSecurityFilter();
                }

                return matchedRoles;
            }
            catch (Exception ex)
            {
                var detail = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                throw new Exception(string.Format("Failed to resolve roles. Details: {0}", detail), ex);
            }
        }

        private void ApplyGlobalSecurityFilter()
        {
            if (Session != null && !Session.IsAdmin && Session.Accounts != null)
            {
                var accounts = Session.Accounts;

                // Объект скрывается ТОЛЬКО если к нему явно закрыт доступ (уровень 0 / None)
                Func<string, bool> checkAllowed = id =>
                {
                    if (string.IsNullOrEmpty(id)) return true;

                    if (accounts.TryGetValue(id, out var level))
                    {
                        // Если в правах стоит 0 (None) — скрываем
                        return level != AccountAccessLevel.None &&
                               (level.HasFlag(AccountAccessLevel.Operate) || level.HasFlag(AccountAccessLevel.Read));
                    }

                    // Если объекта нет в списке ограничений — он доступен по умолчанию
                    return true;
                };

                Reference<Warehouse>.SecurityFilter = checkAllowed;
                Reference<Depository>.SecurityFilter = checkAllowed;
                Reference<Office>.SecurityFilter = checkAllowed;
            }
            else
            {
                Reference<Warehouse>.SecurityFilter = null;
                Reference<Depository>.SecurityFilter = null;
                Reference<Office>.SecurityFilter = null;
            }
        }

        public override async Task UpdatePassword(string currentPassword, string newPassword)
        {
            if (Session == null || string.IsNullOrEmpty(Session.UserId))
            {
                throw new Exception("Cannot update password: user is not authenticated.");
            }

            try
            {
                await _restClient.PostAsync("/api/auth/update-password", new
                {
                    UserId = Session.UserId,
                    CurrentPassword = currentPassword,
                    NewPassword = newPassword
                });
            }
            catch (Exception ex)
            {
                var detail = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                throw new Exception(string.Format("Failed to update password for user '{0}'. Details: {1}", Session.UserId, detail), ex);
            }
        }
    }
}
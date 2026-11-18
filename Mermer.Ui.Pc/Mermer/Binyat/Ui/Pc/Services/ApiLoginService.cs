using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mermer.Authorization.Enums;
using Mermer.Authorization.Models;
using Mermer.Core.Authorization.Services;
using Mermer.Data.Storage;
using Mermer.Http;
using Mermer.Ui.Pc.DTOs;

namespace Mermer.Ui.Pc.Services
{
    public class ApiLoginService : LoginService
    {
        private readonly RestClient _restClient;
        private readonly IRepository<Role> _rolesRepository;

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

                return new User
                {
                    Id = apiResponse.Id,
                    Username = apiResponse.Username,
                    IsAdmin = isAdmin,
                    Roles = userRoles,
                    AccountPrivileges = accountPrivileges // Передаем привилегии в базовый LoginService
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
                }

                return matchedRoles;
            }
            catch (Exception ex)
            {
                var detail = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                throw new Exception(string.Format("Failed to resolve roles. Details: {0}", detail), ex);
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
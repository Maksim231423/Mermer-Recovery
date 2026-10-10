using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
            _restClient = restClient ?? throw new ArgumentNullException(nameof(restClient));
            _rolesRepository = rolesRepository;
        }

        protected override async Task<User> GetUser(string username, string password)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var apiResponse = await _restClient.PostAsync<ApiLoginResponse>("/api/auth/login", new
                {
                    Username = username,
                    Password = password
                });

                if (apiResponse == null)
                {
                    throw new Exception("API вернул пустой ответ (null).");
                }

                bool isAdmin = apiResponse.IsAdmin
                               || string.Equals(apiResponse.Role, "Admin", StringComparison.OrdinalIgnoreCase)
                               || string.Equals(username, "admin", StringComparison.OrdinalIgnoreCase);

                var userRoles = apiResponse.Roles ?? new List<string>();

                return new User
                {
                    Id = apiResponse.Id,
                    Username = apiResponse.Username,
                    IsAdmin = isAdmin,
                    Roles = userRoles
                };
            }
            catch (Exception ex)
            {
                string detail = ex.InnerException != null ? $"{ex.Message} -> {ex.InnerException.Message}" : ex.Message;
                throw new Exception($"[Auth Debug] {detail}", ex);
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

                // Объединяем права всех назначенных ролей в сессию пользователя
                if (Session != null)
                {
                    var mergedAuth = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    foreach (var role in matchedRoles)
                    {
                        if (role.Authorizations == null) continue;
                        foreach (var kvp in role.Authorizations)
                        {
                            // Если в одной из ролей запрещено (0), фиксируем запрет
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
                System.Diagnostics.Debug.WriteLine($"[AUTH GetRoles Failed]: {ex.Message}");
                return Enumerable.Empty<Role>();
            }
        }


        public override async Task UpdatePassword(string currentPassword, string newPassword)
        {
            if (Session == null || string.IsNullOrEmpty(Session.UserId))
                throw new InvalidOperationException("Пользователь не авторизован.");

            await _restClient.PostAsync("/api/auth/update-password", new
            {
                UserId = Session.UserId,
                CurrentPassword = currentPassword,
                NewPassword = newPassword
            });

            var localUser = LocalSqliteCache.GetAllDocuments<User>("User")
                ?.FirstOrDefault(u => string.Equals(u.Id, Session.UserId, StringComparison.OrdinalIgnoreCase));

            if (localUser != null)
            {
                localUser.Password = newPassword;
                LocalSqliteCache.SaveDocument("User", localUser.Id, localUser, isSynced: true);
            }
        }
    }
}
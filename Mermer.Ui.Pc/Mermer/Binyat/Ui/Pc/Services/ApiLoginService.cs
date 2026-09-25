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
                System.Diagnostics.Debug.WriteLine($"[AUTH HTTP] Sending POST /api/auth/login...");

                var apiResponse = await _restClient.PostAsync<ApiLoginResponse>("/api/auth/login", new
                {
                    Username = username,
                    Password = password
                });

                System.Diagnostics.Debug.WriteLine($"[AUTH HTTP] Received response in {sw.ElapsedMilliseconds} ms");

                if (apiResponse == null)
                {
                    throw new Exception("API вернул пустой ответ (null).");
                }

                bool isAdmin = string.Equals(apiResponse.Role, "Admin", StringComparison.OrdinalIgnoreCase);

                return new User
                {
                    Id = apiResponse.Id,
                    Username = apiResponse.Username,
                    IsAdmin = isAdmin
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AUTH HTTP FAILED in {sw.ElapsedMilliseconds} ms]: {ex.Message}");
                string detail = ex.InnerException != null ? $"{ex.Message} -> {ex.InnerException.Message}" : ex.Message;
                throw new Exception($"[Auth Debug] {detail}", ex);
            }
        }

        protected override async Task<IEnumerable<Role>> GetRoles(IEnumerable<string> roleIds)
        {
            if (roleIds == null || !roleIds.Any()) return Enumerable.Empty<Role>();

            try
            {
                return await _rolesRepository.GetAsync(roleIds.ToArray());
            }
            catch
            {
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

            // Обновляем пароль в локальном кэше пользователя
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
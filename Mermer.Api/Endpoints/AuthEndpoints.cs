using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Mermer.Api.DTOs;
using Mermer.Data.Postgres;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Mermer.Api.Endpoints;

public record LoginRequestDto(string Username, string Password);
public record UpdatePasswordRequestDto(string? UserId, string? CurrentPassword, string? NewPassword);

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/auth").WithTags("Auth");

        // 1. Авторизация
        group.MapPost("/login", async (LoginRequestDto request, MermerDbContext db) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
                {
                    return Results.BadRequest(new { message = "Логин и пароль обязательны." });
                }

                var user = await db.Users
                    .FirstOrDefaultAsync(u => u.Username.ToLower() == request.Username.Trim().ToLower());

                if (user == null)
                {
                    return Results.Json(new { message = $"Пользователь '{request.Username}' не найден в БД." }, statusCode: StatusCodes.Status401Unauthorized);
                }

                if (user.IsDisabled)
                {
                    return Results.BadRequest(new { message = "Учетная запись отключена." });
                }

                if (!VerifyPassword(user.Password, request.Password))
                {
                    return Results.Json(new { message = "Неверный пароль." }, statusCode: StatusCodes.Status401Unauthorized);
                }

                string role = user.IsAdmin ? "Admin" : "User";
                string name = !string.IsNullOrEmpty(user.Description) ? user.Description : user.Username;

                var response = new UserSessionDto(
                    Id: user.Id.ToString(),
                    Username: user.Username,
                    Name: name,
                    Role: role,
                    Token: Guid.NewGuid().ToString()
                );

                return Results.Ok(response);
            }
            catch (Exception ex)
            {
                return Results.Json(new
                {
                    message = "Исключение на сервере!",
                    error = ex.Message,
                    inner = ex.InnerException?.Message,
                    stackTrace = ex.StackTrace
                }, statusCode: StatusCodes.Status500InternalServerError);
            }
        })
        .WithName("Login")
        .WithSummary("Авторизация пользователя в системе");

        // 2. Смена пароля
        group.MapPost("/update-password", async (UpdatePasswordRequestDto request, MermerDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(request.UserId) || !Guid.TryParse(request.UserId, out var userId))
            {
                return Results.BadRequest(new { message = "Некорректный ID пользователя." });
            }

            if (string.IsNullOrWhiteSpace(request.CurrentPassword) || string.IsNullOrWhiteSpace(request.NewPassword))
            {
                return Results.BadRequest(new { message = "Текущий и новый пароли обязательны." });
            }

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
            {
                return Results.NotFound(new { message = "Пользователь не найден." });
            }

            if (!VerifyPassword(user.Password, request.CurrentPassword))
            {
                return Results.BadRequest(new { message = "Неверный текущий пароль!" });
            }

            user.Password = HashPasswordSha256Base64(request.NewPassword);
            user.UpdatedAt = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync();

            return Results.Ok(new { message = "Пароль успешно обновлен." });
        })
        .WithName("UpdatePassword")
        .WithSummary("Смена пароля текущего пользователя");

        // 3. Получение ролей
        group.MapPost("/roles", async (List<string> roleIds, MermerDbContext db) =>
        {
            var guids = (roleIds ?? new List<string>())
                .Select(x => Guid.TryParse(x, out var g) ? (Guid?)g : null)
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .ToList();

            var roles = await db.Roles
                .AsNoTracking()
                .Where(r => guids.Contains(r.Id) && !r.IsDisabled)
                .ToListAsync();

            return Results.Ok(roles.Select(r => new
            {
                Id = r.Id.ToString(),
                Name = r.Name,
                Authorizations = r.Authorizations
            }));
        })
        .WithName("GetRoles")
        .WithSummary("Получение ролей пользователя");
    }

    private static bool VerifyPassword(string storedPassword, string providedPassword)
    {
        if (string.IsNullOrEmpty(storedPassword) || string.IsNullOrEmpty(providedPassword))
            return false;

        // 1. Прямое совпадение
        if (string.Equals(storedPassword, providedPassword, StringComparison.Ordinal))
            return true;

        // 2. Стандартный SHA-256 (Base64)
        if (string.Equals(storedPassword, HashPasswordSha256Base64(providedPassword), StringComparison.Ordinal))
            return true;

        // 3. SHA-256 (HEX)
        if (string.Equals(storedPassword, HashPasswordSha256Hex(providedPassword), StringComparison.OrdinalIgnoreCase))
            return true;

        // 4. Легаси Binyat / Couchbase SHA-1 (Base64)
        if (string.Equals(storedPassword, HashPasswordSha1Base64(providedPassword), StringComparison.Ordinal))
            return true;

        return false;
    }

    private static string HashPasswordSha256Base64(string password)
    {
        if (string.IsNullOrEmpty(password)) return string.Empty;
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(bytes);
    }

    private static string HashPasswordSha256Hex(string password)
    {
        if (string.IsNullOrEmpty(password)) return string.Empty;
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string HashPasswordSha1Base64(string password)
    {
        if (string.IsNullOrEmpty(password)) return string.Empty;
        using var sha1 = SHA1.Create();
        var bytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(bytes);
    }
}
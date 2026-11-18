using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Mermer.Api.Services;
using Mermer.Data.Postgres;

namespace Mermer.Api.Endpoints;

public record LoginRequestDto(string Username, string Password);
public record UpdatePasswordRequestDto(string? UserId, string? CurrentPassword, string? NewPassword);

public static class AuthEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null, // PascalCase для полного соответствия моделям клиента
        PropertyNameCaseInsensitive = true
    };

    public static void MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        // =========================================================================
        // АВТОРИЗАЦИЯ (/api/auth)
        // =========================================================================
        var authGroup = routes.MapGroup("/api/auth").WithTags("Auth");

        authGroup.MapPost("/login", async (
            LoginRequestDto request,
            MermerDbContext db,
            IServerLicenseValidationService licenseValidator,
            CancellationToken ct) =>
        {
            bool isServerValid = await licenseValidator.IsServerLicenseValidAsync();
            if (!isServerValid)
            {
                return Results.Problem(
                    detail: "Server license is missing or expired.",
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var username = (request.Username ?? string.Empty).Trim();
            var password = request.Password ?? string.Empty;

            var user = await db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower() && !u.IsDisabled, ct);

            if (user == null || !VerifyPassword(user.Password, password))
            {
                return Results.Unauthorized();
            }

            var userRoles = user.Roles ?? new List<string>();

            var response = new
            {
                Id = user.Id.ToString(),
                Username = user.Username,
                Name = !string.IsNullOrEmpty(user.Description) ? user.Description : user.Username,
                Role = user.IsAdmin ? "Admin" : (userRoles.FirstOrDefault() ?? "User"),
                Roles = userRoles,
                IsAdmin = user.IsAdmin,
                AccountPrivileges = user.AccountPrivileges,
                Token = Guid.NewGuid().ToString()
            };

            return Results.Json(response, JsonOptions);
        });

        authGroup.MapPost("/update-password", async (UpdatePasswordRequestDto request, MermerDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.UserId) || !Guid.TryParse(request.UserId, out var userId))
                return Results.BadRequest(new { message = "Некорректный ID пользователя." });

            if (string.IsNullOrWhiteSpace(request.CurrentPassword) || string.IsNullOrWhiteSpace(request.NewPassword))
                return Results.BadRequest(new { message = "Пароли не могут быть пустыми." });

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user == null) return Results.NotFound(new { message = "Пользователь не найден." });

            if (!VerifyPassword(user.Password, request.CurrentPassword))
                return Results.BadRequest(new { message = "Неверный текущий пароль." });

            user.Password = HashPasswordSha256Base64(request.NewPassword);
            user.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { message = "Пароль успешно обновлен." });
        });
    }

    #region Password Verification & Hashing
    public static bool VerifyPassword(string storedPassword, string providedPassword)
    {
        if (string.IsNullOrEmpty(storedPassword) && string.IsNullOrEmpty(providedPassword))
            return true;

        if (string.IsNullOrEmpty(storedPassword) || string.IsNullOrEmpty(providedPassword))
            return false;

        if (string.Equals(storedPassword, providedPassword, StringComparison.Ordinal))
            return true;

        if (string.Equals(storedPassword, HashPasswordSha256Base64(providedPassword), StringComparison.Ordinal))
            return true;

        if (string.Equals(storedPassword, HashPasswordSha256Hex(providedPassword), StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.Equals(storedPassword, HashPasswordSha1Base64(providedPassword), StringComparison.Ordinal))
            return true;

        return false;
    }

    public static string HashPasswordSha256Base64(string password)
    {
        if (string.IsNullOrEmpty(password)) return string.Empty;
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(bytes);
    }

    public static string HashPasswordSha256Hex(string password)
    {
        if (string.IsNullOrEmpty(password)) return string.Empty;
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string HashPasswordSha1Base64(string password)
    {
        if (string.IsNullOrEmpty(password)) return string.Empty;
        using var sha1 = SHA1.Create();
        var bytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(bytes);
    }
    #endregion
}
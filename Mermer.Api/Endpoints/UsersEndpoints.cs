using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Mermer.Data.Postgres;
using Mermer.Data.Postgres.Entities;

namespace Mermer.Api.Endpoints;

public static class UsersEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        PropertyNameCaseInsensitive = true
    };

    public static IEndpointRouteBuilder MapUsersEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users").WithTags("Users");

        // 1. GET /api/users
        group.MapGet("/", async (MermerDbContext db, CancellationToken ct) =>
        {
            var users = await db.Users
                .AsNoTracking()
                .OrderBy(u => u.Username)
                .ToListAsync(ct);

            var result = users.Select(u => new UserDto
            {
                Id = u.Id.ToString(),
                Username = u.Username,
                Password = null, // Не передаем хэш клиенту
                IsAdmin = u.IsAdmin,
                IsDisabled = u.IsDisabled,
                Description = u.Description ?? string.Empty,
                Roles = u.Roles ?? new List<string>(),
                AccountPrivileges = u.AccountPrivileges
            });

            return Results.Json(result, JsonOptions);
        });

        // 2. GET /api/users/{id}
        group.MapGet("/{id}", async (string id, MermerDbContext db, CancellationToken ct) =>
        {
            if (!Guid.TryParse(id, out var gId))
                return Results.NotFound();

            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == gId, ct);
            if (user == null) return Results.NotFound();

            return Results.Json(new UserDto
            {
                Id = user.Id.ToString(),
                Username = user.Username,
                Password = null,
                IsAdmin = user.IsAdmin,
                IsDisabled = user.IsDisabled,
                Description = user.Description ?? string.Empty,
                Roles = user.Roles ?? new List<string>(),
                AccountPrivileges = user.AccountPrivileges
            }, JsonOptions);
        });

        // 3. POST /api/users
        group.MapPost("/", async (UserDto model, MermerDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(model.Username))
                return Results.BadRequest(new { message = "Username cannot be empty." });

            var userId = (Guid.TryParse(model.Id, out var g) && g != Guid.Empty) ? g : Guid.NewGuid();

            var entity = new UserEntity
            {
                Id = userId,
                Username = model.Username.Trim(),
                Password = ProcessIncomingPassword(model.Password),
                IsAdmin = model.IsAdmin,
                IsDisabled = model.IsDisabled,
                Description = model.Description ?? string.Empty,
                Roles = model.Roles ?? new List<string>(),
                AccountPrivileges = model.AccountPrivileges ?? new Dictionary<string, int>(),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            db.Users.Add(entity);
            await db.SaveChangesAsync(ct);

            model.Id = entity.Id.ToString();
            model.Password = null;
            return Results.Json(model, JsonOptions);
        });

        // 4. PUT /api/users/{id} (Upsert)
        group.MapPut("/{id}", async (string id, UserDto model, MermerDbContext db, CancellationToken ct) =>
        {
            if (!Guid.TryParse(id, out var gId))
                return Results.NotFound();

            var entity = await db.Users.FirstOrDefaultAsync(u => u.Id == gId, ct);
            var newUsername = model.Username?.Trim() ?? string.Empty;

            var usernameTaken = await db.Users.AnyAsync(u => u.Id != gId && u.Username.ToLower() == newUsername.ToLower(), ct);
            if (usernameTaken)
            {
                return Results.BadRequest(new { message = $"Имя пользователя '{newUsername}' уже занято." });
            }

            string? passwordToSave = ProcessIncomingPassword(model.Password);

            if (entity is null)
            {
                entity = new UserEntity
                {
                    Id = gId,
                    Username = newUsername,
                    Password = passwordToSave ?? string.Empty,
                    IsAdmin = model.IsAdmin,
                    IsDisabled = model.IsDisabled,
                    Description = model.Description ?? string.Empty,
                    Roles = model.Roles ?? new List<string>(),
                    AccountPrivileges = model.AccountPrivileges ?? new Dictionary<string, int>(),
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };

                db.Users.Add(entity);
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(newUsername))
                    entity.Username = newUsername;

                entity.IsAdmin = model.IsAdmin;
                entity.IsDisabled = model.IsDisabled;
                entity.Description = model.Description ?? string.Empty;
                entity.Roles = model.Roles ?? new List<string>();
                entity.AccountPrivileges = model.AccountPrivileges ?? new Dictionary<string, int>();
                entity.UpdatedAt = DateTimeOffset.UtcNow;

                // Перезаписываем пароль, только если передан новый непустой пароль
                if (!string.IsNullOrEmpty(passwordToSave))
                {
                    entity.Password = passwordToSave;
                }
            }

            await db.SaveChangesAsync(ct);

            model.Id = entity.Id.ToString();
            model.Password = null;
            return Results.Json(model, JsonOptions);
        });

        // 5. DELETE /api/users/{id}
        group.MapDelete("/{id}", async (string id, MermerDbContext db, CancellationToken ct) =>
        {
            if (!Guid.TryParse(id, out var gId))
                return Results.NotFound();

            var entity = await db.Users.FirstOrDefaultAsync(u => u.Id == gId, ct);
            if (entity == null) return Results.NotFound();

            entity.IsDisabled = true;
            entity.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        return app;
    }

    private static string ProcessIncomingPassword(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        // Если клиент уже прислал Base64-хэш (SHA-1 = 28 символов или SHA-256 = 44 символа со знаком '=')
        if (raw.EndsWith("=") && (raw.Length == 28 || raw.Length == 44))
        {
            return raw;
        }

        // Если пароль передан открытым текстом — хэшируем в SHA-1 Base64 (родной формат Couchbase/Mermer)
        return AuthEndpoints.HashPasswordSha1Base64(raw);
    }

    public class UserDto
    {
        public string? Id { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        public bool IsAdmin { get; set; }
        public bool IsDisabled { get; set; }
        public string? Description { get; set; }
        public List<string>? Roles { get; set; } = new();
        public Dictionary<string, int>? AccountPrivileges { get; set; } = new();
    }
}
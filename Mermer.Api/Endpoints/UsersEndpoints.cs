using System;
using System.Collections.Generic;
using System.Linq;
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
    public static IEndpointRouteBuilder MapUsersEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users").WithTags("Users");

        // 1. GET /api/users
        group.MapGet("/", async (MermerDbContext db, CancellationToken ct) =>
        {
            var users = await db.Users
                .AsNoTracking()
                .Select(u => new UserDto
                {
                    Id = u.Id.ToString(),
                    Username = u.Username,
                    Password = null, // Не отдаем хэш клиенту
                    IsAdmin = u.IsAdmin,
                    IsDisabled = u.IsDisabled,
                    Description = u.Description,
                    Roles = u.Roles ?? new List<string>(),
                    AccountPrivileges = new Dictionary<string, int>()
                })
                .ToListAsync(ct);

            return Results.Ok(users);
        });

        // 2. GET /api/users/{id}
        group.MapGet("/{id}", async (string id, MermerDbContext db, CancellationToken ct) =>
        {
            if (!Guid.TryParse(id, out var gId))
                return Results.NotFound();

            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == gId, ct);
            if (user == null) return Results.NotFound();

            return Results.Ok(new UserDto
            {
                Id = user.Id.ToString(),
                Username = user.Username,
                Password = null, // Не отдаем хэш клиенту
                IsAdmin = user.IsAdmin,
                IsDisabled = user.IsDisabled,
                Description = user.Description,
                Roles = user.Roles ?? new List<string>(),
                AccountPrivileges = new Dictionary<string, int>()
            });
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
                Password = !string.IsNullOrWhiteSpace(model.Password)
                    ? AuthEndpoints.HashPasswordSha256Base64(model.Password)
                    : "0DPiKuNIrrVmD8IUCuw1hQxNqZc=", // пароль "admin" по умолчанию
                IsAdmin = model.IsAdmin,
                IsDisabled = model.IsDisabled,
                Description = model.Description,
                Roles = model.Roles ?? new List<string>(),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            db.Users.Add(entity);
            await db.SaveChangesAsync(ct);

            model.Id = entity.Id.ToString();
            model.Password = null;
            return Results.Ok(model);
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

            string passwordToSave = null;
            if (!string.IsNullOrWhiteSpace(model.Password))
            {
                // Если клиент уже прислал Base64 SHA-1/SHA-256 (обычно 28 или 44 символа с '=')
                if (model.Password.EndsWith("=") && (model.Password.Length == 28 || model.Password.Length == 44))
                {
                    passwordToSave = model.Password;
                }
                else
                {
                    // Иначе хэшируем как SHA-1 Base64 (родной формат Couchbase/Binyat)
                    passwordToSave = AuthEndpoints.HashPasswordSha1Base64(model.Password);
                }
            }

            if (entity is null)
            {
                entity = new UserEntity
                {
                    Id = gId,
                    Username = newUsername,
                    Password = passwordToSave ?? "0DPiKuNIrrVmD8IUCuw1hQxNqZc=", // "admin"
                    IsAdmin = model.IsAdmin,
                    IsDisabled = model.IsDisabled,
                    Description = model.Description,
                    Roles = model.Roles ?? new List<string>(),
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
                entity.Description = model.Description;
                entity.Roles = model.Roles ?? new List<string>();
                entity.UpdatedAt = DateTimeOffset.UtcNow;

                if (!string.IsNullOrEmpty(passwordToSave))
                {
                    entity.Password = passwordToSave;
                }
            }

            await db.SaveChangesAsync(ct);

            model.Id = entity.Id.ToString();
            model.Password = null;
            return Results.Ok(model);
        });

        // 5. DELETE /api/users/{id}
        group.MapDelete("/{id}", async (string id, MermerDbContext db, CancellationToken ct) =>
        {
            if (!Guid.TryParse(id, out var gId))
                return Results.NotFound();

            var entity = await db.Users.FirstOrDefaultAsync(u => u.Id == gId, ct);
            if (entity == null) return Results.NotFound();

            entity.IsDisabled = true;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { success = true });
        });

        return app;
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
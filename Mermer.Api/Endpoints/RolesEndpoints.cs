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

public static class RolesEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null, // PascalCase для гарантированной совместимости с WPF-клиентом
        PropertyNameCaseInsensitive = true
    };

    public static IEndpointRouteBuilder MapRolesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/roles").WithTags("Roles");

        // 1. GET /api/roles
        group.MapGet("/", async (MermerDbContext db, CancellationToken ct) =>
        {
            var roles = await db.Roles
                .AsNoTracking()
                .Where(r => !r.IsDisabled)
                .OrderBy(r => r.Name)
                .ToListAsync(ct);

            var result = roles.Select(r => new RoleDto
            {
                Id = r.Id.ToString(),
                Name = r.Name ?? string.Empty,
                Description = r.Description ?? string.Empty,
                IsDisabled = r.IsDisabled,
                Authorizations = r.Authorizations,
                AccountPrivileges = r.AccountPrivileges
            });

            return Results.Json(result, JsonOptions);
        });

        // 2. GET /api/roles/{id}
        group.MapGet("/{id}", async (string id, MermerDbContext db, CancellationToken ct) =>
        {
            if (!Guid.TryParse(id, out var gId))
                return Results.NotFound();

            var r = await db.Roles
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == gId, ct);

            if (r == null)
                return Results.NotFound();

            var result = new RoleDto
            {
                Id = r.Id.ToString(),
                Name = r.Name ?? string.Empty,
                Description = r.Description ?? string.Empty,
                IsDisabled = r.IsDisabled,
                Authorizations = r.Authorizations,
                AccountPrivileges = r.AccountPrivileges
            };

            return Results.Json(result, JsonOptions);
        });

        // 3. POST /api/roles
        group.MapPost("/", async (RoleDto model, MermerDbContext db, CancellationToken ct) =>
        {
            var roleId = (Guid.TryParse(model.Id, out var g) && g != Guid.Empty) ? g : Guid.NewGuid();

            var entity = new RoleEntity
            {
                Id = roleId,
                Name = model.Name ?? string.Empty,
                Description = model.Description ?? string.Empty,
                IsDisabled = model.IsDisabled,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            entity.Authorizations = model.Authorizations != null
                ? new Dictionary<string, int>(model.Authorizations, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            entity.AccountPrivileges = model.AccountPrivileges != null
                ? new Dictionary<string, int>(model.AccountPrivileges, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            db.Roles.Add(entity);
            await db.SaveChangesAsync(ct);

            model.Id = entity.Id.ToString();
            return Results.Json(model, JsonOptions);
        });

        // 4. PUT /api/roles/{id} (Upsert)
        group.MapPut("/{id}", async (string id, RoleDto model, MermerDbContext db, CancellationToken ct) =>
        {
            if (!Guid.TryParse(id, out var gId))
                return Results.NotFound();

            var entity = await db.Roles.FirstOrDefaultAsync(x => x.Id == gId, ct);
            if (entity == null)
            {
                entity = new RoleEntity
                {
                    Id = gId,
                    Name = model.Name ?? string.Empty,
                    Description = model.Description ?? string.Empty,
                    IsDisabled = model.IsDisabled,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };

                entity.Authorizations = model.Authorizations != null
                    ? new Dictionary<string, int>(model.Authorizations, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                entity.AccountPrivileges = model.AccountPrivileges != null
                    ? new Dictionary<string, int>(model.AccountPrivileges, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                db.Roles.Add(entity);
            }
            else
            {
                entity.Name = model.Name ?? entity.Name;
                entity.Description = model.Description ?? string.Empty;
                entity.IsDisabled = model.IsDisabled;

                if (model.Authorizations != null)
                {
                    entity.Authorizations = new Dictionary<string, int>(model.Authorizations, StringComparer.OrdinalIgnoreCase);
                }

                if (model.AccountPrivileges != null)
                {
                    entity.AccountPrivileges = new Dictionary<string, int>(model.AccountPrivileges, StringComparer.OrdinalIgnoreCase);
                }

                entity.UpdatedAt = DateTimeOffset.UtcNow;
            }

            await db.SaveChangesAsync(ct);

            model.Id = entity.Id.ToString();
            return Results.Json(model, JsonOptions);
        });

        // 5. DELETE /api/roles/{id}
        group.MapDelete("/{id}", async (string id, MermerDbContext db, CancellationToken ct) =>
        {
            if (!Guid.TryParse(id, out var gId))
                return Results.NotFound();

            var entity = await db.Roles.FirstOrDefaultAsync(x => x.Id == gId, ct);
            if (entity == null)
                return Results.NotFound();

            entity.IsDisabled = true;
            entity.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            return Results.NoContent();
        });

        return app;
    }

    public class RoleDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public bool IsDisabled { get; set; }
        public Dictionary<string, int>? Authorizations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int>? AccountPrivileges { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
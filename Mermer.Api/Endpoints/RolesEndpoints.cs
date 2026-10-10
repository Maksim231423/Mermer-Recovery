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

public static class RolesEndpoints
{
    public static IEndpointRouteBuilder MapRolesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/roles").WithTags("Roles");

        // 1. GET /api/roles
        group.MapGet("/", async (MermerDbContext db, CancellationToken ct) =>
        {
            var roles = await db.Roles.AsNoTracking().Where(r => !r.IsDisabled).ToListAsync(ct);
            return Results.Ok(roles.Select(r => new RoleDto
            {
                Id = r.Id.ToString(),
                Name = r.Name,
                Description = r.Description,
                IsDisabled = r.IsDisabled,
                Authorizations = r.Authorizations,
                AccountPrivileges = r.AccountPrivileges
            }));
        });

        // 2. GET /api/roles/{id}
        group.MapGet("/{id}", async (string id, MermerDbContext db, CancellationToken ct) =>
        {
            if (!Guid.TryParse(id, out var gId)) return Results.NotFound();

            var r = await db.Roles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == gId, ct);
            if (r == null) return Results.NotFound();

            return Results.Ok(new RoleDto
            {
                Id = r.Id.ToString(),
                Name = r.Name,
                Description = r.Description,
                IsDisabled = r.IsDisabled,
                Authorizations = r.Authorizations,
                AccountPrivileges = r.AccountPrivileges
            });
        });

        // 3. POST /api/roles
        group.MapPost("/", async (RoleDto model, MermerDbContext db, CancellationToken ct) =>
        {
            var roleId = (Guid.TryParse(model.Id, out var g) && g != Guid.Empty) ? g : Guid.NewGuid();

            var entity = new RoleEntity
            {
                Id = roleId,
                Name = model.Name ?? string.Empty,
                Description = model.Description,
                IsDisabled = model.IsDisabled,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            entity.Authorizations = model.Authorizations ?? new Dictionary<string, int>();
            entity.AccountPrivileges = model.AccountPrivileges ?? new Dictionary<string, int>();

            db.Roles.Add(entity);
            await db.SaveChangesAsync(ct);

            Console.WriteLine($"[ROLES POST] Создана роль '{entity.Name}' ({entity.Id}), прав: {entity.Authorizations.Count}");

            model.Id = entity.Id.ToString();
            return Results.Ok(model);
        });

        // 4. PUT /api/roles/{id}
        group.MapPut("/{id}", async (string id, RoleDto model, MermerDbContext db, CancellationToken ct) =>
        {
            if (!Guid.TryParse(id, out var gId)) return Results.NotFound();

            var entity = await db.Roles.FirstOrDefaultAsync(x => x.Id == gId, ct);
            if (entity == null)
            {
                entity = new RoleEntity
                {
                    Id = gId,
                    Name = model.Name ?? string.Empty,
                    Description = model.Description,
                    IsDisabled = model.IsDisabled,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                entity.Authorizations = model.Authorizations ?? new Dictionary<string, int>();
                entity.AccountPrivileges = model.AccountPrivileges ?? new Dictionary<string, int>();
                db.Roles.Add(entity);
            }
            else
            {
                entity.Name = model.Name ?? entity.Name;
                entity.Description = model.Description;
                entity.IsDisabled = model.IsDisabled;

                if (model.Authorizations != null)
                {
                    entity.Authorizations = new Dictionary<string, int>(model.Authorizations, StringComparer.OrdinalIgnoreCase);
                }

                if (model.AccountPrivileges != null)
                {
                    entity.AccountPrivileges = model.AccountPrivileges;
                }

                entity.UpdatedAt = DateTimeOffset.UtcNow;
            }

            await db.SaveChangesAsync(ct);
            Console.WriteLine($"[ROLES PUT] Сохранена роль '{entity.Name}' ({entity.Id}), прав в Authorizations: {entity.Authorizations.Count}");

            model.Id = entity.Id.ToString();
            return Results.Ok(model);
        });

        // 5. DELETE /api/roles/{id}
        group.MapDelete("/{id}", async (string id, MermerDbContext db, CancellationToken ct) =>
        {
            if (!Guid.TryParse(id, out var gId)) return Results.NotFound();

            var entity = await db.Roles.FirstOrDefaultAsync(x => x.Id == gId, ct);
            if (entity == null) return Results.NotFound();

            entity.IsDisabled = true;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { success = true });
        });

        return app;
    }

    public class RoleDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public bool IsDisabled { get; set; }
        public Dictionary<string, int>? Authorizations { get; set; } = new();
        public Dictionary<string, int>? AccountPrivileges { get; set; } = new();
    }
}
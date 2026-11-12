using Mermer.Data.Postgres;
using Mermer.Data.Postgres.Entities;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mermer.Api.Endpoints;

public static class WarehouseEndpoints
{
    public static IEndpointRouteBuilder MapWarehouseEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/warehouses").WithTags("Warehouses");

        // ФАСЕТЫ: Теги складов для выпадающих списков
        group.MapGet("/facets", async (MermerDbContext db, CancellationToken ct) =>
        {
            var warehouses = await db.Warehouses
                .AsNoTracking()
                .Where(w => !w.IsDisabled)
                .Select(w => w.Tags)
                .ToListAsync(ct);

            var tagDict = warehouses
                .Where(tags => tags != null)
                .SelectMany(tags => tags)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .GroupBy(t => t.Trim())
                .ToDictionary(g => g.Key, g => g.Count());

            var result = new Dictionary<string, Dictionary<string, int>>
            {
                ["TagNames"] = tagDict
            };

            return Results.Ok(result);
        });

        // PUT: Создание / Обновление склада (Upsert)
        group.MapPut("/{id:guid}", async (Guid id, WarehouseDto dto, MermerDbContext db, CancellationToken ct) =>
        {
            var existing = await db.Warehouses.FirstOrDefaultAsync(w => w.Id == id, ct);

            Guid? officeId = null;
            if (!string.IsNullOrWhiteSpace(dto.OfficeId) && Guid.TryParse(dto.OfficeId, out var parsedOfficeId) && parsedOfficeId != Guid.Empty)
            {
                officeId = parsedOfficeId;
            }
            else
            {
                var defOffice = await db.Offices.AsNoTracking().FirstOrDefaultAsync(o => !o.IsDisabled, ct);
                officeId = defOffice?.Id;
            }

            if (existing is null)
            {
                var newEntity = new WarehouseEntity
                {
                    Id = id,
                    Name = dto.Name ?? string.Empty,
                    OfficeId = officeId,
                    Description = dto.Description ?? string.Empty,
                    Tags = dto.Tags,
                    IsDisabled = dto.IsDisabled,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };

                db.Warehouses.Add(newEntity);
            }
            else
            {
                existing.Name = dto.Name ?? string.Empty;
                existing.OfficeId = officeId;
                existing.Description = dto.Description ?? string.Empty;
                existing.Tags = dto.Tags;
                existing.IsDisabled = dto.IsDisabled;
                existing.UpdatedAt = DateTimeOffset.UtcNow;
            }

            await db.SaveChangesAsync(ct);
            dto.Id = id.ToString();
            return Results.Ok(dto);
        });

        // DELETE: Пометка IsDisabled
        group.MapDelete("/{id:guid}", async (Guid id, MermerDbContext db, CancellationToken ct) =>
        {
            var warehouse = await db.Warehouses.FirstOrDefaultAsync(w => w.Id == id, ct);
            if (warehouse is null) return Results.NotFound();

            warehouse.IsDisabled = true;
            await db.SaveChangesAsync(ct);

            return Results.NoContent();
        });

        return app;
    }

    public class WarehouseDto
    {
        public string Id { get; set; } = string.Empty;
        public string? OfficeId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string[]? Tags { get; set; }
        public bool IsDisabled { get; set; }
    }
}
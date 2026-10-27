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
                // Подстановка дефолтного офиса предприятия
                var defOffice = await db.Offices.AsNoTracking().FirstOrDefaultAsync(o => !o.IsDisabled, ct);
                officeId = defOffice?.Id;
            }

            if (existing is null)
            {
                var newEntity = Activator.CreateInstance(db.Warehouses.EntityType.ClrType);
                var entry = db.Entry(newEntity);

                entry.Property("Id").CurrentValue = id;
                entry.Property("Name").CurrentValue = dto.Name ?? "";
                if (entry.Metadata.FindProperty("OfficeId") != null) entry.Property("OfficeId").CurrentValue = officeId;
                if (entry.Metadata.FindProperty("Description") != null) entry.Property("Description").CurrentValue = dto.Description ?? "";
                if (entry.Metadata.FindProperty("Tags") != null) entry.Property("Tags").CurrentValue = dto.Tags;
                entry.Property("IsDisabled").CurrentValue = dto.IsDisabled;

                db.Add(newEntity);
            }
            else
            {
                var entry = db.Entry(existing);

                existing.Name = dto.Name ?? "";
                if (entry.Metadata.FindProperty("OfficeId") != null) entry.Property("OfficeId").CurrentValue = officeId;
                if (entry.Metadata.FindProperty("Description") != null) entry.Property("Description").CurrentValue = dto.Description ?? "";
                if (entry.Metadata.FindProperty("Tags") != null) entry.Property("Tags").CurrentValue = dto.Tags;
                existing.IsDisabled = dto.IsDisabled;
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
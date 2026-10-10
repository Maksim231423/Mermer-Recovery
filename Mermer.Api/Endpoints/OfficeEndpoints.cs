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

public static class OfficeEndpoints
{
    public static IEndpointRouteBuilder MapOfficeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/offices").WithTags("Offices");

        // ФАСЕТЫ: Уникальные регионы и теги из базы данных
        group.MapGet("/facets", async (MermerDbContext db, CancellationToken ct) =>
        {
            var offices = await db.Offices
                .AsNoTracking()
                .Where(o => !o.IsDisabled)
                .Select(o => new { o.Region, o.Tags })
                .ToListAsync(ct);

            var regionDict = offices
                .Where(o => !string.IsNullOrWhiteSpace(o.Region))
                .GroupBy(o => o.Region.Trim())
                .ToDictionary(g => g.Key, g => g.Count());

            var tagDict = offices
                .Where(o => o.Tags != null)
                .SelectMany(o => o.Tags)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .GroupBy(t => t.Trim())
                .ToDictionary(g => g.Key, g => g.Count());

            var result = new Dictionary<string, Dictionary<string, int>>
            {
                ["RegionNames"] = regionDict,
                ["TagNames"] = tagDict
            };

            return Results.Ok(result);
        });

        // PUT: Создание / Обновление офиса (Upsert)
        group.MapPut("/{id:guid}", async (Guid id, OfficeDto dto, MermerDbContext db, CancellationToken ct) =>
        {
            var existing = await db.Offices.FirstOrDefaultAsync(o => o.Id == id, ct);

            if (existing is null)
            {
                var newEntity = Activator.CreateInstance(db.Offices.EntityType.ClrType);
                var entry = db.Entry(newEntity);

                entry.Property("Id").CurrentValue = id;
                entry.Property("Name").CurrentValue = dto.Name ?? "";
                entry.Property("Region").CurrentValue = dto.Region ?? "";
                entry.Property("Description").CurrentValue = dto.Description ?? "";
                if (entry.Metadata.FindProperty("Tags") != null) entry.Property("Tags").CurrentValue = dto.Tags;
                entry.Property("IsDisabled").CurrentValue = dto.IsDisabled;

                db.Add(newEntity);
            }
            else
            {
                existing.Name = dto.Name ?? "";
                existing.Region = dto.Region ?? "";
                existing.Description = dto.Description ?? "";
                existing.Tags = dto.Tags;
                existing.IsDisabled = dto.IsDisabled;
            }

            await db.SaveChangesAsync(ct);
            dto.Id = id.ToString();
            return Results.Ok(dto);
        });

        // DELETE: Пометка IsDisabled
        group.MapDelete("/{id:guid}", async (Guid id, MermerDbContext db, CancellationToken ct) =>
        {
            var office = await db.Offices.FirstOrDefaultAsync(o => o.Id == id, ct);
            if (office is null) return Results.NotFound();

            office.IsDisabled = true;
            await db.SaveChangesAsync(ct);

            return Results.NoContent();
        });

        return app;
    }

    public class OfficeDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string[]? Tags { get; set; }
        public bool IsDisabled { get; set; }
    }
}
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
                var newEntity = new OfficeEntity
                {
                    Id = id,
                    Name = dto.Name ?? string.Empty,
                    Region = dto.Region ?? string.Empty,
                    Description = dto.Description ?? string.Empty,
                    Tags = dto.Tags,
                    IsDisabled = dto.IsDisabled,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };

                db.Offices.Add(newEntity);
            }
            else
            {
                existing.Name = dto.Name ?? string.Empty;
                existing.Region = dto.Region ?? string.Empty;
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
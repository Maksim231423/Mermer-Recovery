using Mermer.Data.Postgres;
using Mermer.Data.Postgres.Entities;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mermer.Api.Endpoints;

public static class DepositoriesEndpoints
{
    public static void MapDepositoriesEndpoints(this IEndpointRouteBuilder routes)
    {
        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = null };

        async Task<IResult> GetAll(MermerDbContext db, CancellationToken ct)
        {
            var depositories = await db.Depositories
                .AsNoTracking()
                .Where(d => !d.IsDisabled)
                .OrderBy(d => d.Name)
                .ToListAsync(ct);

            var result = depositories.Select(d =>
            {
                var entry = db.Entry(d);
                string[]? tags = null;
                if (entry.Metadata.FindProperty("Tags") != null)
                {
                    tags = entry.Property("Tags").CurrentValue as string[];
                }

                return new DepositoryDto
                {
                    Id = d.Id.ToString(),
                    Name = d.Name ?? "",
                    OfficeId = d.OfficeId.HasValue ? d.OfficeId.Value.ToString() : null,
                    Tags = tags,
                    IsDisabled = d.IsDisabled
                };
            });

            return Results.Json(result, jsonOptions);
        }

        routes.MapGet("/api/depositories", GetAll).WithTags("Depositories");
        routes.MapGet("/api/enterprise/depositories", GetAll).WithTags("Depositories");

        // ФАСЕТЫ: Уникальные теги счетов/касс
        routes.MapGet("/api/depositories/facets", async (MermerDbContext db, CancellationToken ct) =>
        {
            var depositories = await db.Depositories
                .AsNoTracking()
                .Where(d => !d.IsDisabled)
                .ToListAsync(ct);

            var allTags = new List<string>();
            foreach (var d in depositories)
            {
                var entry = db.Entry(d);
                if (entry.Metadata.FindProperty("Tags") != null && entry.Property("Tags").CurrentValue is string[] tags)
                {
                    allTags.AddRange(tags.Where(t => !string.IsNullOrWhiteSpace(t)));
                }
            }

            var tagDict = allTags
                .GroupBy(t => t.Trim())
                .ToDictionary(g => g.Key, g => g.Count());

            var result = new Dictionary<string, Dictionary<string, int>>
            {
                ["TagNames"] = tagDict
            };

            return Results.Json(result, jsonOptions);
        }).WithTags("Depositories");

        routes.MapGet("/api/depositories/next-code", async (MermerDbContext db, CancellationToken ct) =>
        {
            var count = await db.Depositories.CountAsync(ct);
            var nextCode = $"DEP-{(count + 1):D5}";
            return Results.Ok(new { code = nextCode });
        }).WithTags("Depositories");

        // PUT: Создание / Обновление (Upsert)
        routes.MapPut("/api/depositories/{id:guid}", async (Guid id, DepositoryDto dto, MermerDbContext db, CancellationToken ct) =>
        {
            var existing = await db.Depositories.FirstOrDefaultAsync(d => d.Id == id, ct);

            Guid? officeId = null;
            if (!string.IsNullOrWhiteSpace(dto.OfficeId) && Guid.TryParse(dto.OfficeId, out var parsedOfficeId))
            {
                officeId = parsedOfficeId;
            }

            if (existing is null)
            {
                var newEntity = new DepositoryEntity
                {
                    Id = id,
                    Name = dto.Name ?? "",
                    OfficeId = officeId,
                    Description = dto.Description ?? "",
                    Tags = dto.Tags,
                    IsDisabled = dto.IsDisabled,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                db.Depositories.Add(newEntity);
            }
            else
            {
                existing.Name = dto.Name ?? "";
                existing.OfficeId = officeId;
                existing.Description = dto.Description ?? "";
                existing.Tags = dto.Tags;
                existing.IsDisabled = dto.IsDisabled;
                existing.UpdatedAt = DateTimeOffset.UtcNow;
            }

            await db.SaveChangesAsync(ct);
            dto.Id = id.ToString();
            return Results.Json(dto, jsonOptions);
        }).WithTags("Depositories");

        // DELETE: Пометка IsDisabled
        routes.MapDelete("/api/depositories/{id:guid}", async (Guid id, MermerDbContext db, CancellationToken ct) =>
        {
            var dep = await db.Depositories.FirstOrDefaultAsync(d => d.Id == id, ct);
            if (dep is null) return Results.NotFound();

            dep.IsDisabled = true;
            await db.SaveChangesAsync(ct);

            return Results.NoContent();
        }).WithTags("Depositories");
    }

    public class DepositoryDto
    {
        public string Id { get; set; } = string.Empty;
        public string? OfficeId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string[]? Tags { get; set; }
        public bool IsDisabled { get; set; }
    }
}
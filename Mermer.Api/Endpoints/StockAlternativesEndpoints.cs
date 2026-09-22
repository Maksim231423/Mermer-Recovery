using System;
using System.IO;
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

public static class StockAlternativesEndpoints
{
    public static IEndpointRouteBuilder MapStockAlternativesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/stock-management/alternatives").WithTags("StockAlternatives");

        JsonElement GetProp(JsonElement el, string name)
        {
            if (el.TryGetProperty(name, out var val)) return val;
            if (el.TryGetProperty(char.ToLower(name[0]) + name.Substring(1), out val)) return val;
            return default;
        }

        // 1. СПИСОК (С ПАГИНАЦИЕЙ)
        group.MapGet("/", async (int? limit, int? offset, MermerDbContext db, CancellationToken ct) =>
        {
            int take = limit.HasValue ? Math.Clamp(limit.Value, 1, 500) : 150;
            int skip = offset.GetValueOrDefault(0);

            var list = await db.StockAlternatives
                .AsNoTracking()
                .Where(a => !a.IsDisabled)
                .OrderBy(a => a.Name)
                .Skip(skip)
                .Take(take)
                .Include(a => a.Lines)
                .AsSplitQuery()
                .ToListAsync(ct);

            return Results.Ok(list.Select(a => new
            {
                Id = a.Id.ToString(),
                Name = a.Name,
                Description = a.Description ?? "",
                IsDisabled = a.IsDisabled,
                Lines = a.Lines.Select(l => new { Id = l.Id.ToString(), StockId = l.StockId?.ToString() })
            }));
        });

        // 2. ДЛЯ КОНКРЕТНОГО ТОВАРА (В ОДИН ЗАПРОС)
        group.MapGet("/for-stock/{stockId}", async (string stockId, MermerDbContext db, CancellationToken ct) =>
        {
            if (!Guid.TryParse(stockId, out var sG))
                return Results.Ok(new { StockId = stockId, Alternatives = Array.Empty<string>() });

            var resultIds = await (
                from l1 in db.StockAlternativeLines.AsNoTracking()
                join l2 in db.StockAlternativeLines.AsNoTracking() on l1.StockAlternativeId equals l2.StockAlternativeId
                where l1.StockId == sG && l2.StockId != sG && l2.StockId != null
                select l2.StockId!.Value.ToString()
            ).Distinct().ToListAsync(ct);

            return Results.Ok(new { StockId = stockId, Alternatives = resultIds });
        });

        // 3. СОХРАНЕНИЕ
        group.MapPost("/", async (HttpRequest req, MermerDbContext db) =>
        {
            using var reader = new StreamReader(req.Body);
            using var doc = JsonDocument.Parse(await reader.ReadToEndAsync());
            var root = doc.RootElement;

            var ip = GetProp(root, "Id");
            Guid altId = ip.ValueKind != JsonValueKind.Undefined && Guid.TryParse(ip.GetString(), out var g) && g != Guid.Empty ? g : Guid.NewGuid();

            var existing = await db.StockAlternatives.Include(a => a.Lines).AsSplitQuery().FirstOrDefaultAsync(r => r.Id == altId);
            if (existing == null)
            {
                existing = new StockAlternativeEntity { Id = altId, CreatedAt = DateTimeOffset.UtcNow };
                await db.StockAlternatives.AddAsync(existing);
            }

            var np = GetProp(root, "Name");
            if (np.ValueKind != JsonValueKind.Undefined) existing.Name = np.GetString() ?? "";

            var dp = GetProp(root, "Description");
            if (dp.ValueKind != JsonValueKind.Undefined) existing.Description = dp.GetString();

            var dis = GetProp(root, "IsDisabled");
            if (dis.ValueKind == JsonValueKind.True || dis.ValueKind == JsonValueKind.False) existing.IsDisabled = dis.GetBoolean();

            existing.UpdatedAt = DateTimeOffset.UtcNow;

            var linesElem = GetProp(root, "Lines");
            if (linesElem.ValueKind == JsonValueKind.Array)
            {
                db.StockAlternativeLines.RemoveRange(existing.Lines);
                foreach (var le in linesElem.EnumerateArray())
                {
                    var lidProp = GetProp(le, "Id");
                    var lStockProp = GetProp(le, "StockId");

                    var line = new StockAlternativeLineEntity
                    {
                        Id = lidProp.ValueKind != JsonValueKind.Undefined && Guid.TryParse(lidProp.GetString(), out var lG) ? lG : Guid.NewGuid(),
                        StockAlternativeId = altId,
                        StockId = lStockProp.ValueKind != JsonValueKind.Undefined && Guid.TryParse(lStockProp.GetString(), out var stG) ? stG : null
                    };
                    await db.StockAlternativeLines.AddAsync(line);
                }
            }

            await db.SaveChangesAsync();
            return Results.Ok(new { id = altId });
        });

        // 4. УДАЛЕНИЕ
        group.MapDelete("/{id}", async (string id, MermerDbContext db) =>
        {
            if (Guid.TryParse(id, out var guid))
            {
                var o = await db.StockAlternatives.FirstOrDefaultAsync(x => x.Id == guid);
                if (o != null) { db.StockAlternatives.Remove(o); await db.SaveChangesAsync(); }
            }
            return Results.Ok();
        });

        return app;
    }
}
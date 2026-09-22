using System;
using System.Collections.Generic;
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

public static class StockNameComposersEndpoints
{
    public static IEndpointRouteBuilder MapStockNameComposersEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/stock-management/name-composers").WithTags("StockNameComposers");

        JsonElement GetProp(JsonElement el, string name)
        {
            if (el.TryGetProperty(name, out var val)) return val;
            if (el.TryGetProperty(char.ToLower(name[0]) + name.Substring(1), out val)) return val;
            return default;
        }

        group.MapGet("/", async (MermerDbContext db, CancellationToken ct) =>
        {
            var list = await db.StockNameComposers
                .AsNoTracking()
                .Where(c => !c.IsDisabled)
                .OrderBy(c => c.Order)
                .Include(c => c.Values)
                .AsSplitQuery()
                .ToListAsync(ct);

            return Results.Ok(list.Select(c => new
            {
                Id = c.Id.ToString(),
                Order = c.Order,
                Name = c.Name,
                Description = c.Description ?? "",
                IsDisabled = c.IsDisabled,
                Values = c.Values.OrderBy(v => v.Order).Select(v => new
                {
                    Id = v.Id.ToString(),
                    Order = v.Order,
                    Name = v.Name ?? "",
                    ShortName = v.ShortName ?? ""
                })
            }));
        });

        group.MapPost("/", async (HttpRequest req, MermerDbContext db) =>
        {
            using var reader = new StreamReader(req.Body);
            var body = await reader.ReadToEndAsync();
            if (string.IsNullOrEmpty(body)) return Results.BadRequest("Empty body");

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var ip = GetProp(root, "Id");
            Guid composerId = ip.ValueKind != JsonValueKind.Undefined && Guid.TryParse(ip.GetString(), out var parsedGuid) && parsedGuid != Guid.Empty ? parsedGuid : Guid.NewGuid();

            var existing = await db.StockNameComposers.Include(c => c.Values).AsSplitQuery().FirstOrDefaultAsync(r => r.Id == composerId);
            if (existing == null)
            {
                existing = new StockNameComposerEntity { Id = composerId, CreatedAt = DateTimeOffset.UtcNow };
                await db.StockNameComposers.AddAsync(existing);
            }

            var op = GetProp(root, "Order");
            if (op.ValueKind == JsonValueKind.Number) existing.Order = op.GetInt32();

            var np = GetProp(root, "Name");
            if (np.ValueKind != JsonValueKind.Undefined) existing.Name = np.GetString() ?? "";

            var dp = GetProp(root, "Description");
            if (dp.ValueKind != JsonValueKind.Undefined) existing.Description = dp.GetString();

            var dis = GetProp(root, "IsDisabled");
            if (dis.ValueKind == JsonValueKind.True || dis.ValueKind == JsonValueKind.False) existing.IsDisabled = dis.GetBoolean();

            existing.UpdatedAt = DateTimeOffset.UtcNow;

            var valuesElem = GetProp(root, "Values");
            if (valuesElem.ValueKind == JsonValueKind.Array)
            {
                db.StockNameComposerValues.RemoveRange(existing.Values);
                foreach (var ve in valuesElem.EnumerateArray())
                {
                    var vidProp = GetProp(ve, "Id");
                    var vOrderProp = GetProp(ve, "Order");
                    var vNameProp = GetProp(ve, "Name");
                    var vShortProp = GetProp(ve, "ShortName");

                    var valueItem = new StockNameComposerValueEntity
                    {
                        Id = vidProp.ValueKind != JsonValueKind.Undefined && Guid.TryParse(vidProp.GetString(), out var vG) ? vG : Guid.NewGuid(),
                        ComposerId = composerId,
                        Order = vOrderProp.ValueKind == JsonValueKind.Number ? vOrderProp.GetInt32() : 0,
                        Name = vNameProp.ValueKind != JsonValueKind.Undefined ? vNameProp.GetString() : null,
                        ShortName = vShortProp.ValueKind != JsonValueKind.Undefined ? vShortProp.GetString() : null
                    };
                    await db.StockNameComposerValues.AddAsync(valueItem);
                }
            }

            await db.SaveChangesAsync();
            return Results.Ok(new { id = composerId });
        });

        group.MapDelete("/{id}", async (string id, MermerDbContext db) =>
        {
            if (Guid.TryParse(id, out var guid))
            {
                var o = await db.StockNameComposers.FirstOrDefaultAsync(x => x.Id == guid);
                if (o != null)
                {
                    o.IsDisabled = true;
                    o.UpdatedAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync();
                }
            }
            return Results.Ok();
        });

        return app;
    }
}
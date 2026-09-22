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

public static class AggregatedStockOrdersEndpoints
{
    public static IEndpointRouteBuilder MapAggregatedStockOrdersEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/warehousing/aggregated-orders").WithTags("AggregatedStockOrders");

        // 1. СПИСОК СВОДНЫХ ЗАКАЗОВ (С ПАГИНАЦИЕЙ)
        group.MapGet("/", async (DateTime? from, DateTime? till, string? warehouseId, int? limit, int? offset, MermerDbContext db, CancellationToken ct) =>
        {
            int take = limit.HasValue ? Math.Clamp(limit.Value, 1, 1000) : 200;
            int skip = offset.GetValueOrDefault(0);

            DateTimeOffset startDate = from.HasValue ? new DateTimeOffset(from.Value.ToUniversalTime()) : DateTimeOffset.UtcNow.AddMonths(-1);
            DateTimeOffset endDate = till.HasValue ? new DateTimeOffset(till.Value.ToUniversalTime()) : DateTimeOffset.UtcNow;

            var query = db.AggregatedStockOrders
                .AsNoTracking()
                .Where(o => o.Date >= startDate && o.Date <= endDate && !o.IsDisabled);

            if (Guid.TryParse(warehouseId, out var wG))
                query = query.Where(o => o.WarehouseId == wG);

            var list = await query
                .OrderByDescending(o => o.Date)
                .Skip(skip)
                .Take(take)
                .Include(o => o.Lines)
                .AsSplitQuery()
                .ToListAsync(ct);

            return Results.Ok(list.Select(o => new
            {
                Id = o.Id.ToString(),
                Code = o.Code ?? "",
                Date = o.Date.UtcDateTime,
                WarehouseId = o.WarehouseId?.ToString(),
                PartnerId = o.PartnerId?.ToString(),
                UserId = o.UserId?.ToString(),
                UserName = o.UserName ?? "admin",
                IsCompleted = o.IsCompleted,
                IsDisabled = o.IsDisabled,
                Group = o.GroupName ?? "",
                Tags = o.Tags != null ? o.Tags.ToList() : new List<string>(),
                Description = o.Description ?? "",
                Lines = o.Lines.Select(l => new
                {
                    Id = l.Id.ToString(),
                    AggregatedStockOrderId = o.Id.ToString(),
                    StockId = l.StockId?.ToString(),
                    UnitId = l.UnitId?.ToString(),
                    Orders = !string.IsNullOrEmpty(l.OrdersJson)
                        ? (JsonSerializer.Deserialize<Dictionary<string, decimal>>(l.OrdersJson) ?? new Dictionary<string, decimal>())
                        : new Dictionary<string, decimal>()
                })
            }));
        });

        // 2. ПОЛУЧЕНИЕ ПО ID
        group.MapGet("/{id}", async (string id, MermerDbContext db, CancellationToken ct) =>
        {
            if (!Guid.TryParse(id, out var orderId)) return Results.NotFound();

            var o = await db.AggregatedStockOrders
                .Include(x => x.Lines)
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == orderId, ct);

            if (o == null) return Results.NotFound();

            return Results.Ok(new
            {
                Id = o.Id.ToString(),
                Code = o.Code ?? "",
                Date = o.Date.UtcDateTime,
                WarehouseId = o.WarehouseId?.ToString(),
                PartnerId = o.PartnerId?.ToString(),
                UserId = o.UserId?.ToString(),
                UserName = o.UserName ?? "admin",
                IsCompleted = o.IsCompleted,
                IsDisabled = o.IsDisabled,
                Group = o.GroupName ?? "",
                Tags = o.Tags != null ? o.Tags.ToList() : new List<string>(),
                Description = o.Description ?? "",
                Lines = o.Lines.Select(l => new
                {
                    Id = l.Id.ToString(),
                    AggregatedStockOrderId = o.Id.ToString(),
                    StockId = l.StockId?.ToString(),
                    UnitId = l.UnitId?.ToString(),
                    Orders = !string.IsNullOrEmpty(l.OrdersJson)
                        ? (JsonSerializer.Deserialize<Dictionary<string, decimal>>(l.OrdersJson) ?? new Dictionary<string, decimal>())
                        : new Dictionary<string, decimal>()
                })
            });
        });

        // 3. ФАСЕТЫ (БЕЗ ВЫГРУЗКИ ВСЕЙ ТАБЛИЦЫ)
        group.MapGet("/facets", async (HttpContext ctx, MermerDbContext db, CancellationToken ct) =>
        {
            string? fields = ctx.Request.Query["fields"].ToString();
            var fieldList = string.IsNullOrEmpty(fields)
                ? new[] { "Date", "Group", "Tags", "GroupNames", "TagNames" }
                : fields.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var result = new Dictionary<string, Dictionary<string, int>>();

            foreach (var field in fieldList)
            {
                if (field.Equals("Group", StringComparison.OrdinalIgnoreCase) || field.Equals("GroupNames", StringComparison.OrdinalIgnoreCase))
                {
                    var groups = await db.AggregatedStockOrders
                        .AsNoTracking()
                        .Where(x => !string.IsNullOrEmpty(x.GroupName) && !x.IsDisabled)
                        .GroupBy(x => x.GroupName!)
                        .Select(g => new { Key = g.Key, Count = g.Count() })
                        .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

                    result[field] = groups;
                }
                else if (field.Equals("Tags", StringComparison.OrdinalIgnoreCase) || field.Equals("TagNames", StringComparison.OrdinalIgnoreCase))
                {
                    var allTags = await db.AggregatedStockOrders
                        .AsNoTracking()
                        .Where(x => x.Tags != null && x.Tags.Length > 0 && !x.IsDisabled)
                        .Select(x => x.Tags)
                        .ToListAsync(ct);

                    var tagCounts = allTags
                        .SelectMany(t => t!)
                        .GroupBy(t => t)
                        .ToDictionary(g => g.Key, g => g.Count());

                    result[field] = tagCounts;
                }
                else if (field.Equals("Date", StringComparison.OrdinalIgnoreCase))
                {
                    var todayUtc = DateTime.UtcNow.Date;
                    var weekStart = todayUtc.AddDays(-7);
                    var monthStart = new DateTime(todayUtc.Year, todayUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);

                    var countToday = await db.AggregatedStockOrders.CountAsync(r => !r.IsDisabled && r.Date >= todayUtc, ct);
                    var countWeek = await db.AggregatedStockOrders.CountAsync(r => !r.IsDisabled && r.Date >= weekStart, ct);
                    var countMonth = await db.AggregatedStockOrders.CountAsync(r => !r.IsDisabled && r.Date >= monthStart, ct);
                    var countAll = await db.AggregatedStockOrders.CountAsync(r => !r.IsDisabled, ct);

                    result[field] = new Dictionary<string, int>
                    {
                        { "#Today", countToday },
                        { "#This Week", countWeek },
                        { "#This Month", countMonth },
                        { "#All Records", countAll }
                    };
                }
                else
                {
                    result[field] = new Dictionary<string, int>();
                }
            }

            return Results.Ok(result);
        });

        // 4. СОХРАНЕНИЕ
        Func<HttpRequest, MermerDbContext, Task<IResult>> saveAggregatedOrderHandler = async (req, db) =>
        {
            using var reader = new StreamReader(req.Body);
            var body = await reader.ReadToEndAsync();
            if (string.IsNullOrEmpty(body)) return Results.BadRequest("Empty body");

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            string? idStr = GetStringProp(root, "id", "Id");
            Guid orderId = Guid.TryParse(idStr, out var parsedGuid) && parsedGuid != Guid.Empty ? parsedGuid : Guid.NewGuid();

            var existing = await db.AggregatedStockOrders.FirstOrDefaultAsync(r => r.Id == orderId);

            string code = GetStringProp(root, "code", "Code") ?? $"AGR-{DateTime.UtcNow:yyMMddHHmmss}";
            Guid? wId = Guid.TryParse(GetStringProp(root, "warehouseId", "WarehouseId"), out var wG) ? wG : null;
            Guid? pId = Guid.TryParse(GetStringProp(root, "partnerId", "PartnerId"), out var pG) ? pG : null;
            Guid? uId = Guid.TryParse(GetStringProp(root, "userId", "UserId"), out var uG) ? uG : null;
            string userName = GetStringProp(root, "userName", "UserName") ?? "admin";
            string groupName = GetStringProp(root, "group", "Group", "groupName", "GroupName") ?? string.Empty;
            string description = GetStringProp(root, "description", "Description") ?? string.Empty;
            bool isCompleted = GetBoolProp(root, "isCompleted", "IsCompleted");
            bool isDisabled = GetBoolProp(root, "isDisabled", "IsDisabled");

            DateTimeOffset date = DateTimeOffset.UtcNow;
            string? dateStr = GetStringProp(root, "date", "Date");
            if (!string.IsNullOrEmpty(dateStr) && DateTimeOffset.TryParse(dateStr, out var pDate))
                date = pDate.ToUniversalTime();

            var tagsList = ExtractTagsFromRawJson(root);

            var linesList = new List<AggregatedStockOrderLineEntity>();
            if (TryGetPropCaseInsensitive(root, "lines", out var linesElem) && linesElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var le in linesElem.EnumerateArray())
                {
                    string? lidStr = GetStringProp(le, "id", "Id");
                    string? lStockStr = GetStringProp(le, "stockId", "StockId");
                    string? lUnitStr = GetStringProp(le, "unitId", "UnitId");

                    string ordersJson = "{}";
                    if (TryGetPropCaseInsensitive(le, "orders", out var ordersProp))
                    {
                        ordersJson = ordersProp.ValueKind == JsonValueKind.Object ? ordersProp.GetRawText() : "{}";
                    }

                    linesList.Add(new AggregatedStockOrderLineEntity
                    {
                        Id = Guid.TryParse(lidStr, out var lG) && lG != Guid.Empty ? lG : Guid.NewGuid(),
                        AggregatedStockOrderId = orderId,
                        StockId = Guid.TryParse(lStockStr, out var stG) ? stG : null,
                        UnitId = Guid.TryParse(lUnitStr, out var unitG) ? unitG : null,
                        OrdersJson = ordersJson
                    });
                }
            }

            if (existing == null)
            {
                await db.AggregatedStockOrders.AddAsync(new AggregatedStockOrderEntity
                {
                    Id = orderId,
                    Code = code,
                    Date = date,
                    WarehouseId = wId,
                    PartnerId = pId,
                    UserId = uId,
                    UserName = userName,
                    IsCompleted = isCompleted,
                    IsDisabled = isDisabled,
                    GroupName = groupName,
                    Description = description,
                    Tags = tagsList.ToArray(),
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                    Lines = linesList
                });
            }
            else
            {
                existing.Code = code;
                existing.Date = date;
                existing.WarehouseId = wId;
                existing.PartnerId = pId;
                if (uId.HasValue) existing.UserId = uId;
                if (!string.IsNullOrEmpty(userName)) existing.UserName = userName;
                existing.IsCompleted = isCompleted;
                existing.IsDisabled = isDisabled;
                existing.GroupName = groupName;
                existing.Description = description;
                existing.Tags = tagsList.ToArray();
                existing.UpdatedAt = DateTimeOffset.UtcNow;

                var oldLines = await db.AggregatedStockOrderLines.Where(l => l.AggregatedStockOrderId == orderId).ToListAsync();
                if (oldLines.Any()) db.AggregatedStockOrderLines.RemoveRange(oldLines);
                if (linesList.Any()) await db.AggregatedStockOrderLines.AddRangeAsync(linesList);

                db.AggregatedStockOrders.Update(existing);
            }

            await db.SaveChangesAsync();
            return Results.Ok(new { id = orderId, code });
        };

        group.MapPost("/", saveAggregatedOrderHandler);
        group.MapPut("/{id}", saveAggregatedOrderHandler);

        // 5. УДАЛЕНИЕ
        group.MapDelete("/{id}", async (string id, MermerDbContext db) =>
        {
            if (Guid.TryParse(id, out var guid))
            {
                var o = await db.AggregatedStockOrders.FirstOrDefaultAsync(x => x.Id == guid);
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

    #region Helpers
    private static List<string> ExtractTagsFromRawJson(JsonElement root)
    {
        var list = new List<string>();

        if (!root.TryGetProperty("tags", out var tagsProp) &&
            !root.TryGetProperty("Tags", out tagsProp))
        {
            return list;
        }

        if (tagsProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in tagsProp.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var s = item.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) list.Add(s.Trim());
                }
                else if (item.ValueKind == JsonValueKind.Object)
                {
                    if (item.TryGetProperty("Text", out var t) || item.TryGetProperty("Value", out t) || item.TryGetProperty("Name", out t))
                    {
                        var s = t.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) list.Add(s.Trim());
                    }
                }
            }
        }
        else if (tagsProp.ValueKind == JsonValueKind.String)
        {
            var raw = tagsProp.GetString();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                list.AddRange(raw.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                                 .Select(x => x.Trim())
                                 .Where(x => !string.IsNullOrWhiteSpace(x)));
            }
        }

        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool TryGetPropCaseInsensitive(JsonElement el, string name, out JsonElement val)
    {
        foreach (var p in el.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                val = p.Value;
                return true;
            }
        }
        val = default;
        return false;
    }

    private static string? GetStringProp(JsonElement el, params string[] names)
    {
        foreach (var n in names)
        {
            if (TryGetPropCaseInsensitive(el, n, out var p) && p.ValueKind == JsonValueKind.String)
                return p.GetString();
        }
        return null;
    }

    private static bool GetBoolProp(JsonElement el, params string[] names)
    {
        foreach (var n in names)
        {
            if (TryGetPropCaseInsensitive(el, n, out var p))
            {
                if (p.ValueKind == JsonValueKind.True) return true;
                if (p.ValueKind == JsonValueKind.False) return false;
            }
        }
        return false;
    }
    #endregion
}
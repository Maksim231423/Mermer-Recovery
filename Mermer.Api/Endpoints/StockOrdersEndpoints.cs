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

public static class StockOrdersEndpoints
{
    public static IEndpointRouteBuilder MapStockOrdersEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/warehousing/orders").WithTags("StockOrders");
        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = null };

        // 1. СПИСОК ЗАКАЗОВ
        group.MapGet("/", async (DateTime? from, DateTime? till, string? warehouseId, string? partnerId, bool? isCompleted, bool? isDisabled, int? limit, int? offset, MermerDbContext db, CancellationToken ct) =>
        {
            int take = limit.HasValue ? Math.Clamp(limit.Value, 1, 10000) : 5000;
            int skip = offset.GetValueOrDefault(0);

            var query = db.StockOrders.AsNoTracking();

            if (isDisabled.HasValue)
            {
                query = query.Where(o => o.IsDisabled == isDisabled.Value);
            }
            else
            {
                query = query.Where(o => !o.IsDisabled);
            }

            if (isCompleted.HasValue)
            {
                query = query.Where(o => o.IsCompleted == isCompleted.Value);
            }

            if (from.HasValue && from.Value.Year > 2000)
            {
                var startDate = new DateTimeOffset(from.Value.ToUniversalTime());
                query = query.Where(o => o.Date >= startDate);
            }

            if (till.HasValue && till.Value.Year < 2099)
            {
                var endDate = new DateTimeOffset(till.Value.ToUniversalTime());
                query = query.Where(o => o.Date <= endDate);
            }

            if (Guid.TryParse(warehouseId, out var wG))
                query = query.Where(o => o.WarehouseId == wG);

            if (Guid.TryParse(partnerId, out var pG))
                query = query.Where(o => o.PartnerId == pG);

            var list = await query
                .OrderByDescending(o => o.Date)
                .Skip(skip)
                .Take(take)
                .Include(o => o.Lines)
                .Include(o => o.UnitConvertions)
                .AsSplitQuery()
                .ToListAsync(ct);

            var result = list.Select(o => new
            {
                Id = o.Id.ToString(),
                Code = o.Code ?? "",
                Date = o.Date.ToLocalTime().ToString("yyyy-MM-ddTHH:mm:ss"),
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
                    StockOrderId = o.Id.ToString(),
                    StockId = l.StockId?.ToString(),
                    Quantity = l.Quantity,
                    UnitId = l.UnitId?.ToString()
                }),
                StockUnitConvertions = o.UnitConvertions.Select(c => new
                {
                    Id = c.Id.ToString(),
                    StockOrderId = o.Id.ToString(),
                    StockId = c.StockId?.ToString(),
                    UnitId = c.UnitId?.ToString(),
                    Multiplier = c.Multiplier,
                    Divider = c.Divider
                })
            });

            return Results.Json(result, jsonOptions);
        });

        // 2. СЧЕТЧИК ЗАКАЗОВ
        group.MapGet("/count", async (DateTime? from, DateTime? till, bool? isCompleted, bool? isDisabled, string? userId, HttpRequest req, MermerDbContext db, CancellationToken ct) =>
        {
            var query = db.StockOrders.AsNoTracking();

            if (isDisabled.HasValue)
            {
                query = query.Where(o => o.IsDisabled == isDisabled.Value);
            }
            else
            {
                query = query.Where(o => !o.IsDisabled);
            }

            if (isCompleted.HasValue)
            {
                query = query.Where(o => o.IsCompleted == isCompleted.Value);
            }

            if (Guid.TryParse(userId, out var uG))
            {
                query = query.Where(o => o.UserId == uG);
            }

            if (from.HasValue && from.Value.Year > 2000)
            {
                var startDate = new DateTimeOffset(from.Value.ToUniversalTime());
                query = query.Where(o => o.Date >= startDate);
            }

            if (till.HasValue && till.Value.Year < 2099)
            {
                var endDate = new DateTimeOffset(till.Value.ToUniversalTime());
                query = query.Where(o => o.Date <= endDate);
            }

            int total = await query.CountAsync(ct);
            return Results.Json(new { Count = total }, jsonOptions);
        });

        // 3. ПОЛУЧЕНИЕ ПО ID
        group.MapGet("/{id}", async (string id, MermerDbContext db, CancellationToken ct) =>
        {
            if (!Guid.TryParse(id, out var orderId)) return Results.NotFound();

            var o = await db.StockOrders
                .Include(x => x.Lines)
                .Include(x => x.UnitConvertions)
                .AsSplitQuery()
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == orderId, ct);

            if (o == null) return Results.NotFound();

            return Results.Json(new
            {
                Id = o.Id.ToString(),
                Code = o.Code ?? "",
                Date = o.Date.ToLocalTime().ToString("yyyy-MM-ddTHH:mm:ss"),
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
                    StockOrderId = o.Id.ToString(),
                    StockId = l.StockId?.ToString(),
                    Quantity = l.Quantity,
                    UnitId = l.UnitId?.ToString()
                }),
                StockUnitConvertions = o.UnitConvertions.Select(c => new
                {
                    Id = c.Id.ToString(),
                    StockOrderId = o.Id.ToString(),
                    StockId = c.StockId?.ToString(),
                    UnitId = c.UnitId?.ToString(),
                    Multiplier = c.Multiplier,
                    Divider = c.Divider
                })
            }, jsonOptions);
        });

        // 4. ФАСЕТЫ
        group.MapGet("/facets", async (HttpContext ctx, MermerDbContext db, CancellationToken ct) =>
        {
            string? fields = ctx.Request.Query["fields"].ToString();
            var fieldList = string.IsNullOrEmpty(fields)
                ? new[] { "Date", "Group", "Tags", "GroupNames", "TagNames" }
                : fields.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var result = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);

            foreach (var field in fieldList)
            {
                if (field.Equals("Group", StringComparison.OrdinalIgnoreCase) || field.Equals("GroupNames", StringComparison.OrdinalIgnoreCase))
                {
                    var groups = await db.StockOrders
                        .AsNoTracking()
                        .Where(x => !string.IsNullOrEmpty(x.GroupName) && !x.IsDisabled)
                        .GroupBy(x => x.GroupName!)
                        .Select(g => new { Key = g.Key, Count = g.Count() })
                        .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

                    result[field] = groups;
                }
                else if (field.Equals("Tags", StringComparison.OrdinalIgnoreCase) || field.Equals("TagNames", StringComparison.OrdinalIgnoreCase))
                {
                    var allTags = await db.StockOrders
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

                    var countToday = await db.StockOrders.CountAsync(r => !r.IsDisabled && r.Date >= todayUtc, ct);
                    var countWeek = await db.StockOrders.CountAsync(r => !r.IsDisabled && r.Date >= weekStart, ct);
                    var countMonth = await db.StockOrders.CountAsync(r => !r.IsDisabled && r.Date >= monthStart, ct);
                    var countAll = await db.StockOrders.CountAsync(r => !r.IsDisabled, ct);

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

            return Results.Json(result, jsonOptions);
        });

        // 5. СОХРАНЕНИЕ (POST / PUT)
        Func<HttpRequest, MermerDbContext, Task<IResult>> saveOrderHandler = async (req, db) =>
        {
            using var reader = new StreamReader(req.Body);
            var body = await reader.ReadToEndAsync();
            if (string.IsNullOrEmpty(body)) return Results.BadRequest("Empty body");

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            string? idStr = GetStringProp(root, "id", "Id");
            Guid orderId = Guid.TryParse(idStr, out var parsedGuid) && parsedGuid != Guid.Empty ? parsedGuid : Guid.NewGuid();

            var existing = await db.StockOrders.FirstOrDefaultAsync(r => r.Id == orderId);

            string code = GetStringProp(root, "code", "Code") ?? $"ORD-{DateTime.UtcNow:yyMMddHHmmss}";
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

            var linesList = new List<StockOrderLineEntity>();
            if (TryGetPropCaseInsensitive(root, "lines", out var linesElem) && linesElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var le in linesElem.EnumerateArray())
                {
                    linesList.Add(new StockOrderLineEntity
                    {
                        Id = Guid.TryParse(GetStringProp(le, "id", "Id"), out var lG) && lG != Guid.Empty ? lG : Guid.NewGuid(),
                        StockOrderId = orderId,
                        StockId = Guid.TryParse(GetStringProp(le, "stockId", "StockId"), out var stG) ? stG : null,
                        UnitId = Guid.TryParse(GetStringProp(le, "unitId", "UnitId"), out var uGuid) ? uGuid : null,
                        Quantity = GetDecimalProp(le, "quantity", "Quantity")
                    });
                }
            }

            var convList = new List<StockOrderUnitConvertionEntity>();
            if (TryGetPropCaseInsensitive(root, "stockUnitConvertions", out var convElem) && convElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var ce in convElem.EnumerateArray())
                {
                    decimal mult = GetDecimalProp(ce, "multiplier", "Multiplier");
                    decimal div = GetDecimalProp(ce, "divider", "Divider");

                    convList.Add(new StockOrderUnitConvertionEntity
                    {
                        Id = Guid.TryParse(GetStringProp(ce, "id", "Id"), out var cG) && cG != Guid.Empty ? cG : Guid.NewGuid(),
                        StockOrderId = orderId,
                        StockId = Guid.TryParse(GetStringProp(ce, "stockId", "StockId"), out var stG) ? stG : null,
                        UnitId = Guid.TryParse(GetStringProp(ce, "unitId", "UnitId"), out var uGuid) ? uGuid : null,
                        Multiplier = mult != 0 ? mult : 1m,
                        Divider = div != 0 ? div : 1m
                    });
                }
            }

            if (existing == null)
            {
                await db.StockOrders.AddAsync(new StockOrderEntity
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
                    Lines = linesList,
                    UnitConvertions = convList
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

                var curLines = await db.StockOrderLines.Where(l => l.StockOrderId == orderId).ToListAsync();
                if (curLines.Any()) db.StockOrderLines.RemoveRange(curLines);
                if (linesList.Any()) await db.StockOrderLines.AddRangeAsync(linesList);

                var curConvs = await db.StockOrderUnitConvertions.Where(c => c.StockOrderId == orderId).ToListAsync();
                if (curConvs.Any()) db.StockOrderUnitConvertions.RemoveRange(curConvs);
                if (convList.Any()) await db.StockOrderUnitConvertions.AddRangeAsync(convList);

                db.StockOrders.Update(existing);
            }

            await db.SaveChangesAsync();
            return Results.Json(new { Id = orderId, Code = code }, jsonOptions);
        };

        group.MapPost("/", saveOrderHandler);
        group.MapPut("/{id}", saveOrderHandler);

        // 6. УДАЛЕНИЕ
        group.MapDelete("/{id}", async (string id, MermerDbContext db) =>
        {
            if (Guid.TryParse(id, out var guid))
            {
                var o = await db.StockOrders.FirstOrDefaultAsync(x => x.Id == guid);
                if (o != null)
                {
                    o.IsDisabled = true;
                    o.UpdatedAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync();
                }
            }
            return Results.NoContent();
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

    private static decimal GetDecimalProp(JsonElement el, params string[] names)
    {
        foreach (var n in names)
        {
            if (TryGetPropCaseInsensitive(el, n, out var p))
            {
                if (p.ValueKind == JsonValueKind.Number) return p.GetDecimal();
                if (p.ValueKind == JsonValueKind.String && decimal.TryParse(p.GetString(), out var v)) return v;
            }
        }
        return 0m;
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
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

public static class StockTransfersEndpoints
{
    public static void MapStockTransfersEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/warehousing/transfers").WithTags("StockTransfers");

        // 1. СПИСОК ПЕРЕМЕЩЕНИЙ
        group.MapGet("/", async (DateTime? from, DateTime? till, string? warehouseId, string? destinationWarehouseId, int? limit, int? offset, MermerDbContext db, CancellationToken ct) =>
        {
            int take = limit.HasValue ? Math.Clamp(limit.Value, 1, 1000) : 200;
            int skip = offset.GetValueOrDefault(0);

            DateTimeOffset startDate = from.HasValue ? new DateTimeOffset(from.Value.ToUniversalTime()) : DateTimeOffset.UtcNow.AddMonths(-1);
            DateTimeOffset endDate = till.HasValue ? new DateTimeOffset(till.Value.ToUniversalTime()) : DateTimeOffset.UtcNow;

            var defCur = await db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => c.IsDefault, ct)
                         ?? await db.Currencies.AsNoTracking().FirstOrDefaultAsync(ct);
            var defCurId = defCur?.Id.ToString() ?? string.Empty;
            var convertions = await GetCurrencyConvertionsAsync(db, DateTime.UtcNow, ct);

            var query = db.StockTransfers
                .AsNoTracking()
                .Where(t => t.Date >= startDate && t.Date <= endDate && !t.IsDisabled);

            if (Guid.TryParse(warehouseId, out var srcGuid))
                query = query.Where(t => t.WarehouseId == srcGuid);

            if (Guid.TryParse(destinationWarehouseId, out var dstGuid))
                query = query.Where(t => t.DestinationWarehouseId == dstGuid);

            var transfers = await query
                .OrderByDescending(t => t.Date)
                .Skip(skip)
                .Take(take)
                .Include(t => t.Lines)
                .AsSplitQuery()
                .ToListAsync(ct);

            var result = transfers.Select(t =>
            {
                decimal sentTotal = t.Lines != null && t.Lines.Any() ? t.Lines.Sum(l => l.ActionTotal) : t.ActionTotal;
                decimal receivedTotal = t.Lines != null && t.Lines.Any() ? t.Lines.Sum(l => l.ActionReceivedTotal) : t.ActionReceivedTotal;

                return new
                {
                    Id = t.Id.ToString(),
                    Code = t.Code ?? string.Empty,
                    Date = t.Date.UtcDateTime,
                    Type = "StockTransfer",
                    WarehouseId = t.WarehouseId?.ToString(),
                    DestinationWarehouseId = t.DestinationWarehouseId?.ToString(),
                    DisplayCurrencyId = t.DisplayCurrencyId?.ToString() ?? defCurId,
                    CurrencyConvertions = convertions,
                    UserName = t.UserName ?? "admin",
                    IsCompleted = t.IsCompleted,
                    IsDisabled = t.IsDisabled,
                    Group = t.GroupName ?? string.Empty,
                    Tags = t.Tags != null ? t.Tags.ToList() : new List<string>(),
                    Description = t.Description ?? string.Empty,
                    ActionTotal = sentTotal,
                    ActionReceivedTotal = receivedTotal,
                    DisplayTotal = sentTotal,
                    DisplayReceivedTotal = receivedTotal,
                    Lines = t.Lines != null ? t.Lines.Select(l => (object)new
                    {
                        Id = l.Id.ToString(),
                        StockTransferId = t.Id.ToString(),
                        StockId = l.StockId?.ToString(),
                        UnitId = l.UnitId?.ToString(),
                        ReceivedUnitId = l.ReceivedUnitId?.ToString() ?? l.UnitId?.ToString(),
                        Quantity = l.Quantity,
                        ReceivedQuantity = l.ReceivedQuantity,
                        Price = l.Price,
                        ActionTotal = l.ActionTotal,
                        ActionReceivedTotal = l.ActionReceivedTotal,
                        SortOrder = l.SortOrder
                    }).ToList() : new List<object>()
                };
            });

            return Results.Ok(result);
        });

        // 2. ПОЛУЧЕНИЕ ПО ID
        group.MapGet("/{id}", async (string id, MermerDbContext db, CancellationToken ct) =>
        {
            if (!Guid.TryParse(id, out var guid)) return Results.NotFound();
            var t = await db.StockTransfers.Include(x => x.Lines).AsNoTracking().FirstOrDefaultAsync(x => x.Id == guid, ct);
            if (t == null) return Results.NotFound();

            var defCur = await db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => c.IsDefault, ct)
                         ?? await db.Currencies.AsNoTracking().FirstOrDefaultAsync(ct);
            var defCurId = defCur?.Id.ToString() ?? string.Empty;
            var convertions = await GetCurrencyConvertionsAsync(db, t.Date.UtcDateTime, ct);

            decimal sentTotal = t.Lines != null && t.Lines.Any() ? t.Lines.Sum(l => l.ActionTotal) : t.ActionTotal;
            decimal receivedTotal = t.Lines != null && t.Lines.Any() ? t.Lines.Sum(l => l.ActionReceivedTotal) : t.ActionReceivedTotal;

            return Results.Ok(new
            {
                Id = t.Id.ToString(),
                Code = t.Code ?? string.Empty,
                Date = t.Date.UtcDateTime,
                Type = "StockTransfer",
                WarehouseId = t.WarehouseId?.ToString(),
                DestinationWarehouseId = t.DestinationWarehouseId?.ToString(),
                DisplayCurrencyId = t.DisplayCurrencyId?.ToString() ?? defCurId,
                CurrencyConvertions = convertions,
                UserName = t.UserName ?? "admin",
                IsCompleted = t.IsCompleted,
                IsDisabled = t.IsDisabled,
                Group = t.GroupName ?? string.Empty,
                Tags = t.Tags != null ? t.Tags.ToList() : new List<string>(),
                Description = t.Description ?? string.Empty,
                ActionTotal = sentTotal,
                ActionReceivedTotal = receivedTotal,
                DisplayTotal = sentTotal,
                DisplayReceivedTotal = receivedTotal,
                Lines = t.Lines != null ? t.Lines.Select(l => (object)new
                {
                    Id = l.Id.ToString(),
                    StockTransferId = t.Id.ToString(),
                    StockId = l.StockId?.ToString(),
                    UnitId = l.UnitId?.ToString(),
                    ReceivedUnitId = l.ReceivedUnitId?.ToString() ?? l.UnitId?.ToString(),
                    Quantity = l.Quantity,
                    ReceivedQuantity = l.ReceivedQuantity,
                    Price = l.Price,
                    ActionTotal = l.ActionTotal,
                    ActionReceivedTotal = l.ActionReceivedTotal,
                    SortOrder = l.SortOrder
                }).ToList() : new List<object>()
            });
        });

        // 3. СОХРАНЕНИЕ ПЕРЕМЕЩЕНИЯ (POST / PUT)
        Func<HttpRequest, MermerDbContext, Task<IResult>> saveTransferHandler = async (request, db) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync();
            if (string.IsNullOrEmpty(body)) return Results.BadRequest("Empty body");

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            string? idStr = GetStringProp(root, "id", "Id");
            Guid transferId = Guid.TryParse(idStr, out var parsedGuid) && parsedGuid != Guid.Empty ? parsedGuid : Guid.NewGuid();

            var existing = await db.StockTransfers.Include(t => t.Lines).FirstOrDefaultAsync(t => t.Id == transferId);

            string code = GetStringProp(root, "code", "Code") ?? $"STR-{DateTime.UtcNow:yyMMddHHmmss}";
            Guid? srcWhId = Guid.TryParse(GetStringProp(root, "warehouseId", "WarehouseId"), out var sW) ? sW : null;
            Guid? dstWhId = Guid.TryParse(GetStringProp(root, "destinationWarehouseId", "DestinationWarehouseId"), out var dW) ? dW : null;
            Guid? dispCurId = Guid.TryParse(GetStringProp(root, "displayCurrencyId", "DisplayCurrencyId"), out var cG) ? cG : null;

            DateTimeOffset date = DateTimeOffset.UtcNow;
            string? dateStr = GetStringProp(root, "date", "Date");
            if (!string.IsNullOrEmpty(dateStr) && DateTimeOffset.TryParse(dateStr, out var pDate))
                date = pDate.ToUniversalTime();

            var tagsList = ExtractTagsFromRawJson(root);
            string groupName = GetStringProp(root, "group", "Group", "groupName", "GroupName") ?? string.Empty;
            string description = GetStringProp(root, "description", "Description") ?? string.Empty;

            var linesList = new List<StockTransferLineEntity>();
            if (TryGetPropCaseInsensitive(root, "lines", out var linesProp) && linesProp.ValueKind == JsonValueKind.Array)
            {
                int order = 0;
                foreach (var l in linesProp.EnumerateArray())
                {
                    decimal qty = GetDecimalProp(l, "quantity", "Quantity");
                    decimal receivedQty = GetDecimalProp(l, "receivedQuantity", "ReceivedQuantity");
                    decimal price = GetDecimalProp(l, "price", "Price");

                    linesList.Add(new StockTransferLineEntity
                    {
                        Id = Guid.TryParse(GetStringProp(l, "id", "Id"), out var lId) && lId != Guid.Empty ? lId : Guid.NewGuid(),
                        StockTransferId = transferId,
                        StockId = Guid.TryParse(GetStringProp(l, "stockId", "StockId"), out var sG) ? sG : null,
                        UnitId = Guid.TryParse(GetStringProp(l, "unitId", "UnitId"), out var uG) ? uG : null,
                        ReceivedUnitId = Guid.TryParse(GetStringProp(l, "receivedUnitId", "ReceivedUnitId"), out var ruG) ? ruG : uG,
                        Quantity = qty,
                        ReceivedQuantity = receivedQty,
                        Price = price,
                        ActionTotal = qty * price,
                        ActionReceivedTotal = receivedQty * price,
                        SortOrder = order++
                    });
                }
            }

            decimal totalSent = linesList.Any() ? linesList.Sum(l => l.ActionTotal) : GetDecimalProp(root, "actionTotal", "ActionTotal", "total", "Total");
            decimal totalReceived = linesList.Any() ? linesList.Sum(l => l.ActionReceivedTotal) : GetDecimalProp(root, "actionReceivedTotal", "ActionReceivedTotal");

            if (existing == null)
            {
                await db.StockTransfers.AddAsync(new StockTransferEntity
                {
                    Id = transferId,
                    Code = code,
                    Date = date,
                    WarehouseId = srcWhId,
                    DestinationWarehouseId = dstWhId,
                    DisplayCurrencyId = dispCurId,
                    UserName = GetStringProp(root, "userName", "UserName") ?? "admin",
                    IsCompleted = GetBoolProp(root, "isCompleted", "IsCompleted"),
                    IsDisabled = GetBoolProp(root, "isDisabled", "IsDisabled"),
                    GroupName = groupName,
                    Description = description,
                    Tags = tagsList.ToArray(),
                    ActionTotal = totalSent,
                    ActionReceivedTotal = totalReceived,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                    Lines = linesList
                });
            }
            else
            {
                existing.Code = code;
                existing.Date = date;
                existing.WarehouseId = srcWhId;
                existing.DestinationWarehouseId = dstWhId;
                existing.DisplayCurrencyId = dispCurId;
                existing.IsCompleted = GetBoolProp(root, "isCompleted", "IsCompleted");
                existing.IsDisabled = GetBoolProp(root, "isDisabled", "IsDisabled");
                existing.GroupName = groupName;
                existing.Description = description;
                existing.Tags = tagsList.ToArray();
                existing.ActionTotal = totalSent;
                existing.ActionReceivedTotal = totalReceived;
                existing.UpdatedAt = DateTimeOffset.UtcNow;

                var currentDocLines = await db.StockTransferLines
                    .Where(l => l.StockTransferId == transferId)
                    .ToListAsync();

                if (currentDocLines.Any())
                {
                    db.StockTransferLines.RemoveRange(currentDocLines);
                }

                if (linesList.Any())
                {
                    await db.StockTransferLines.AddRangeAsync(linesList);
                }
            }

            await db.SaveChangesAsync();
            return Results.Ok(new { id = transferId, code });
        };

        group.MapPost("/", saveTransferHandler);
        group.MapPut("/{id}", saveTransferHandler);

        // 4. УДАЛЕНИЕ
        group.MapDelete("/{id}", async (string id, MermerDbContext db) =>
        {
            if (Guid.TryParse(id, out var guid))
            {
                var item = await db.StockTransfers.FirstOrDefaultAsync(x => x.Id == guid);
                if (item != null)
                {
                    item.IsDisabled = true;
                    item.UpdatedAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync();
                }
            }
            return Results.Ok();
        });

        // 5. ФАСЕТЫ (БЕЗ N+1 И СКАНИРОВАНИЯ ВСЕЙ ТАБЛИЦЫ)
        group.MapGet("/facets", async (HttpContext ctx, MermerDbContext db, CancellationToken ct) =>
        {
            string? fields = ctx.Request.Query["fields"].ToString();
            var fieldList = string.IsNullOrEmpty(fields)
                ? new[] { "Date", "Group", "Tags" }
                : fields.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var result = new Dictionary<string, Dictionary<string, int>>();

            foreach (var field in fieldList)
            {
                if (field.Equals("Group", StringComparison.OrdinalIgnoreCase) || field.Equals("GroupNames", StringComparison.OrdinalIgnoreCase))
                {
                    var groups = await db.StockTransfers
                        .AsNoTracking()
                        .Where(x => !string.IsNullOrEmpty(x.GroupName) && !x.IsDisabled)
                        .GroupBy(x => x.GroupName!)
                        .Select(g => new { Key = g.Key, Count = g.Count() })
                        .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

                    result[field] = groups;
                }
                else if (field.Equals("Tags", StringComparison.OrdinalIgnoreCase) || field.Equals("TagNames", StringComparison.OrdinalIgnoreCase))
                {
                    var allTags = await db.StockTransfers
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

                    var countToday = await db.StockTransfers.CountAsync(r => !r.IsDisabled && r.Date >= todayUtc, ct);
                    var countWeek = await db.StockTransfers.CountAsync(r => !r.IsDisabled && r.Date >= weekStart, ct);
                    var countMonth = await db.StockTransfers.CountAsync(r => !r.IsDisabled && r.Date >= monthStart, ct);
                    var countAll = await db.StockTransfers.CountAsync(r => !r.IsDisabled, ct);

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

    private static async Task<object[]> GetCurrencyConvertionsAsync(MermerDbContext db, DateTime docDate, CancellationToken ct)
    {
        var currencies = await db.Currencies.AsNoTracking().Where(c => !c.IsDisabled).ToListAsync(ct);
        var rates = await db.CurrencyRates
            .AsNoTracking()
            .Where(r => r.ValidFrom <= docDate.Date)
            .OrderByDescending(r => r.ValidFrom)
            .ToListAsync(ct);

        var convertions = new List<object>();
        foreach (var cur in currencies)
        {
            var rate = rates.FirstOrDefault(r => r.CurrencyId == cur.Id);
            decimal mult = rate?.Multiplier ?? 1m;
            decimal div = rate?.Divider ?? 1m;
            if (div == 0m) div = 1m;
            if (mult == 0m) mult = 1m;

            convertions.Add(new
            {
                CurrencyId = cur.Id.ToString(),
                Multiplier = mult,
                Divider = div
            });
        }
        return convertions.ToArray();
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
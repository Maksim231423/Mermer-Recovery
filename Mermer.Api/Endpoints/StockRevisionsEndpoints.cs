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

public static class StockRevisionsEndpoints
{
    public static IEndpointRouteBuilder MapStockRevisionsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/warehousing/revisions").WithTags("StockRevisions");

        // 1. СПИСОК ИНВЕНТАРИЗАЦИЙ (С ЛИМИТОМ)
        group.MapGet("/", async (int? limit, int? offset, MermerDbContext db, CancellationToken ct) =>
        {
            int take = limit.HasValue ? Math.Clamp(limit.Value, 1, 500) : 150;
            int skip = offset.GetValueOrDefault(0);

            var list = await db.StockRevisions
                .AsNoTracking()
                .Where(r => !r.IsDisabled)
                .OrderByDescending(r => r.Date)
                .Skip(skip)
                .Take(take)
                .ToListAsync(ct);

            return Results.Ok(list.Select(r => new
            {
                Id = r.Id.ToString(),
                Code = r.Code ?? "",
                Date = r.Date.UtcDateTime,
                FinishDate = r.FinishDate?.UtcDateTime,
                WarehouseId = r.WarehouseId?.ToString(),
                ExceedSlipId = r.ExceedSlipId?.ToString(),
                DeficitSlipId = r.DeficitSlipId?.ToString(),
                UserId = r.UserId?.ToString(),
                UserName = r.UserName ?? "",
                IsCompleted = r.IsCompleted,
                IsDisabled = r.IsDisabled,
                Group = r.GroupName ?? "",
                Tags = r.Tags != null ? r.Tags.ToList() : new List<string>(),
                Description = r.Description ?? ""
            }));
        });

        // 2. ПОЛУЧЕНИЕ ПО ID
        group.MapGet("/{id}", async (string id, MermerDbContext db, CancellationToken ct) =>
        {
            if (!Guid.TryParse(id, out var guid)) return Results.NotFound();
            var r = await db.StockRevisions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == guid, ct);
            if (r == null) return Results.NotFound();

            return Results.Ok(new
            {
                Id = r.Id.ToString(),
                Code = r.Code ?? "",
                Date = r.Date.UtcDateTime,
                FinishDate = r.FinishDate?.UtcDateTime,
                WarehouseId = r.WarehouseId?.ToString(),
                ExceedSlipId = r.ExceedSlipId?.ToString(),
                DeficitSlipId = r.DeficitSlipId?.ToString(),
                UserId = r.UserId?.ToString(),
                UserName = r.UserName ?? "",
                IsCompleted = r.IsCompleted,
                IsDisabled = r.IsDisabled,
                Group = r.GroupName ?? "",
                Tags = r.Tags != null ? r.Tags.ToList() : new List<string>(),
                Description = r.Description ?? ""
            });
        });

        // 3. ПОЛУЧЕНИЕ СТРОК ИНВЕНТАРИЗАЦИИ
        group.MapGet("/{id}/lines", async (string id, MermerDbContext db, CancellationToken ct) =>
        {
            if (!Guid.TryParse(id, out var guid)) return Results.Ok(Array.Empty<object>());
            var lines = await db.StockRevisionLines.AsNoTracking().Where(l => l.StockRevisionId == guid).ToListAsync(ct);

            return Results.Ok(lines.Select(l => new
            {
                Id = l.Id.ToString(),
                StockRevisionId = l.StockRevisionId.ToString(),
                StockId = l.StockId?.ToString(),
                Date = l.Date.UtcDateTime,
                Quantity = l.Quantity,
                UnitId = l.UnitId?.ToString(),
                Price = l.Price,
                CurrencyId = l.CurrencyId?.ToString(),
                UserId = l.UserId?.ToString(),
                UserName = l.UserName ?? ""
            }));
        });

        // 4. ДОБАВЛЕНИЕ/СОХРАНЕНИЕ СТРОКИ
        group.MapPost("/{id}/lines", async (string id, HttpRequest req, MermerDbContext db) =>
        {
            if (!Guid.TryParse(id, out var revGuid)) return Results.BadRequest("Invalid Revision ID");
            using var reader = new StreamReader(req.Body);
            var body = await reader.ReadToEndAsync();
            if (string.IsNullOrEmpty(body)) return Results.BadRequest("Empty body");

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var revision = await db.StockRevisions.FirstOrDefaultAsync(r => r.Id == revGuid);
            if (revision == null)
            {
                revision = new StockRevisionEntity { Id = revGuid, Code = $"REV-{DateTime.UtcNow:yyMMddHHmmss}", Date = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
                await db.StockRevisions.AddAsync(revision);
            }

            string? lineIdStr = GetStringProp(root, "id", "Id");
            Guid lineId = Guid.TryParse(lineIdStr, out var parsedLineId) && parsedLineId != Guid.Empty ? parsedLineId : Guid.NewGuid();

            var line = await db.StockRevisionLines.FirstOrDefaultAsync(l => l.Id == lineId);
            if (line == null)
            {
                line = new StockRevisionLineEntity { Id = lineId, StockRevisionId = revGuid };
                await db.StockRevisionLines.AddAsync(line);
            }

            Guid? stockId = Guid.TryParse(GetStringProp(root, "stockId", "StockId"), out var sG) ? sG : null;
            Guid? unitId = Guid.TryParse(GetStringProp(root, "unitId", "UnitId"), out var uG) ? uG : null;
            Guid? currencyId = Guid.TryParse(GetStringProp(root, "currencyId", "CurrencyId"), out var cG) ? cG : null;
            Guid? userId = Guid.TryParse(GetStringProp(root, "userId", "UserId"), out var usrG) ? usrG : null;
            string? userName = GetStringProp(root, "userName", "UserName");

            if (stockId.HasValue) line.StockId = stockId;
            if (unitId.HasValue) line.UnitId = unitId;
            if (currencyId.HasValue) line.CurrencyId = currencyId;
            if (userId.HasValue) line.UserId = userId;
            if (!string.IsNullOrEmpty(userName)) line.UserName = userName;

            line.Quantity = GetDecimalProp(root, "quantity", "Quantity");
            decimal priceVal = GetDecimalProp(root, "price", "Price");
            if (priceVal > 0) line.Price = priceVal;

            line.Date = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(new { id = lineId });
        });

        // 5. УДАЛЕНИЕ СТРОКИ
        group.MapDelete("/lines/{lineId}", async (string lineId, MermerDbContext db) =>
        {
            if (Guid.TryParse(lineId, out var g))
            {
                var l = await db.StockRevisionLines.FirstOrDefaultAsync(x => x.Id == g);
                if (l != null)
                {
                    db.StockRevisionLines.Remove(l);
                    await db.SaveChangesAsync();
                }
            }
            return Results.Ok();
        });

        // 6. СОХРАНЕНИЕ ШАПКИ ИНВЕНТАРИЗАЦИИ (POST / PUT)
        Func<HttpRequest, MermerDbContext, Task<IResult>> saveRevisionHandler = async (req, db) =>
        {
            using var reader = new StreamReader(req.Body);
            var body = await reader.ReadToEndAsync();
            if (string.IsNullOrEmpty(body)) return Results.BadRequest("Empty body");

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            string? idStr = GetStringProp(root, "id", "Id");
            Guid revId = Guid.TryParse(idStr, out var parsedGuid) && parsedGuid != Guid.Empty ? parsedGuid : Guid.NewGuid();

            var existing = await db.StockRevisions.FirstOrDefaultAsync(r => r.Id == revId);

            string code = GetStringProp(root, "code", "Code") ?? $"REV-{DateTime.UtcNow:yyMMddHHmmss}";
            Guid? wId = Guid.TryParse(GetStringProp(root, "warehouseId", "WarehouseId"), out var wG) ? wG : null;
            Guid? exceedSlipId = Guid.TryParse(GetStringProp(root, "exceedSlipId", "ExceedSlipId"), out var eG) ? eG : null;
            Guid? deficitSlipId = Guid.TryParse(GetStringProp(root, "deficitSlipId", "DeficitSlipId"), out var dG) ? dG : null;
            Guid? userId = Guid.TryParse(GetStringProp(root, "userId", "UserId"), out var uG) ? uG : null;
            string userName = GetStringProp(root, "userName", "UserName") ?? "admin";
            string groupName = GetStringProp(root, "group", "Group", "groupName", "GroupName") ?? string.Empty;
            string description = GetStringProp(root, "description", "Description") ?? string.Empty;
            bool isCompleted = GetBoolProp(root, "isCompleted", "IsCompleted");
            bool isDisabled = GetBoolProp(root, "isDisabled", "IsDisabled");

            DateTimeOffset? finishDate = null;
            string? finishDateStr = GetStringProp(root, "finishDate", "FinishDate");
            if (!string.IsNullOrEmpty(finishDateStr) && DateTimeOffset.TryParse(finishDateStr, out var parsedFinishDate))
                finishDate = parsedFinishDate.ToUniversalTime();

            DateTimeOffset date = DateTimeOffset.UtcNow;
            string? dateStr = GetStringProp(root, "date", "Date");
            if (!string.IsNullOrEmpty(dateStr) && DateTimeOffset.TryParse(dateStr, out var parsedDate))
                date = parsedDate.ToUniversalTime();

            var tagsList = ExtractTagsFromRawJson(root);

            if (existing == null)
            {
                existing = new StockRevisionEntity
                {
                    Id = revId,
                    Code = code,
                    Date = date,
                    FinishDate = finishDate,
                    WarehouseId = wId,
                    ExceedSlipId = exceedSlipId,
                    DeficitSlipId = deficitSlipId,
                    UserId = userId,
                    UserName = userName,
                    IsCompleted = isCompleted,
                    IsDisabled = isDisabled,
                    GroupName = groupName,
                    Description = description,
                    Tags = tagsList.ToArray(),
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                await db.StockRevisions.AddAsync(existing);
            }
            else
            {
                existing.Code = code;
                existing.Date = date;
                existing.FinishDate = finishDate;
                if (wId.HasValue) existing.WarehouseId = wId;
                if (exceedSlipId.HasValue) existing.ExceedSlipId = exceedSlipId;
                if (deficitSlipId.HasValue) existing.DeficitSlipId = deficitSlipId;
                if (userId.HasValue) existing.UserId = userId;
                if (!string.IsNullOrEmpty(userName)) existing.UserName = userName;
                existing.IsCompleted = isCompleted;
                existing.IsDisabled = isDisabled;
                existing.GroupName = groupName;
                existing.Description = description;
                existing.Tags = tagsList.ToArray();
                existing.UpdatedAt = DateTimeOffset.UtcNow;
                db.StockRevisions.Update(existing);
            }

            await db.SaveChangesAsync();
            return Results.Ok(new { id = revId, code = existing.Code });
        };

        group.MapPost("/", saveRevisionHandler);
        group.MapPut("/{id}", saveRevisionHandler);

        // 7. ФАСЕТЫ
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
                    var groups = await db.StockRevisions
                        .AsNoTracking()
                        .Where(x => !string.IsNullOrEmpty(x.GroupName) && !x.IsDisabled)
                        .GroupBy(x => x.GroupName!)
                        .Select(g => new { Key = g.Key, Count = g.Count() })
                        .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

                    result[field] = groups;
                }
                else if (field.Equals("Tags", StringComparison.OrdinalIgnoreCase) || field.Equals("TagNames", StringComparison.OrdinalIgnoreCase))
                {
                    var allTags = await db.StockRevisions
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

                    var countToday = await db.StockRevisions.CountAsync(r => !r.IsDisabled && r.Date >= todayUtc, ct);
                    var countWeek = await db.StockRevisions.CountAsync(r => !r.IsDisabled && r.Date >= weekStart, ct);
                    var countMonth = await db.StockRevisions.CountAsync(r => !r.IsDisabled && r.Date >= monthStart, ct);
                    var countAll = await db.StockRevisions.CountAsync(r => !r.IsDisabled, ct);

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
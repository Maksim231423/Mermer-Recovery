using System;
using System.Collections.Generic;
using System.Globalization;
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

public static class StockSlipsEndpoints
{
    public static void MapStockSlipsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/catalog").WithTags("Catalog");

        // --- СПИСОК СКЛАДСКИХ ОРДЕРОВ ---
        group.MapGet("/slips", async (HttpRequest req, MermerDbContext db, CancellationToken ct) =>
        {
            try
            {
                var defCur = await db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => c.IsDefault, ct)
                             ?? await db.Currencies.AsNoTracking().FirstOrDefaultAsync(ct);
                var defCurId = defCur?.Id.ToString() ?? "00000000-0000-0000-0000-000000000001";

                object[] convertions;
                try
                {
                    convertions = await GetCurrencyConvertionsAsync(db, DateTime.UtcNow, ct);
                }
                catch
                {
                    convertions = Array.Empty<object>();
                }

                if (convertions == null || convertions.Length == 0)
                {
                    convertions = new object[]
                    {
                        new { CurrencyId = defCurId, Multiplier = 1m, Divider = 1m }
                    };
                }

                string? fromStr = req.Query["from"].FirstOrDefault();
                string? tillStr = req.Query["till"].FirstOrDefault();
                string? warehouseId = req.Query["warehouseId"].FirstOrDefault();

                DateTimeOffset startDate = !string.IsNullOrEmpty(fromStr)
                    ? ParseSafeFilterOffset(fromStr, isTill: false)
                    : DateTimeOffset.MinValue;

                DateTimeOffset endDate = !string.IsNullOrEmpty(tillStr)
                    ? ParseSafeFilterOffset(tillStr, isTill: true, fromRaw: fromStr)
                    : DateTimeOffset.MaxValue;

                var slipsQuery = db.StockSlips
                    .AsNoTracking()
                    .Where(s => s.Date >= startDate && s.Date <= endDate);

                if (Guid.TryParse(warehouseId, out var wGuid))
                    slipsQuery = slipsQuery.Where(s => s.WarehouseId == wGuid);

                var rawSlips = await slipsQuery
                    .OrderByDescending(s => s.Date)
                    .Select(s => new
                    {
                        s.Id,
                        s.Code,
                        s.Date,
                        s.SlipType,
                        s.WarehouseId,
                        s.UserId,
                        s.IsCompleted,
                        s.DisplayTotal,
                        s.Description,
                        s.Tags
                    })
                    .ToListAsync(ct);

                if (!rawSlips.Any())
                {
                    return Results.Ok(Array.Empty<object>());
                }

                var slipIds = rawSlips.Select(x => x.Id).ToList();
                var allLines = await db.StockSlipLines
                    .AsNoTracking()
                    .Where(l => slipIds.Contains(l.StockSlipId))
                    .Select(l => new
                    {
                        l.Id,
                        l.StockSlipId,
                        l.StockId,
                        l.UnitId,
                        l.Quantity,
                        l.Price,
                        l.ActionTotal,
                        l.SortOrder
                    })
                    .ToListAsync(ct);

                var linesGrouped = allLines
                    .GroupBy(l => l.StockSlipId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                var result = rawSlips.Select(s =>
                {
                    var linesList = new List<object>();
                    var unitConvertionsDict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

                    if (linesGrouped.TryGetValue(s.Id, out var slipLines) && slipLines.Any())
                    {
                        foreach (var l in slipLines)
                        {
                            var sId = l.StockId?.ToString() ?? Guid.Empty.ToString();
                            var uId = l.UnitId?.ToString() ?? Guid.Empty.ToString();
                            decimal lineTotal = l.ActionTotal != 0m ? l.ActionTotal : (l.Quantity * l.Price);

                            linesList.Add(new
                            {
                                Id = l.Id.ToString(),
                                StockSlipId = s.Id.ToString(),
                                StockId = sId,
                                UnitId = uId,
                                CurrencyId = defCurId,
                                Quantity = l.Quantity,
                                Price = l.Price,
                                Total = lineTotal,
                                ActionTotal = lineTotal,
                                SortOrder = l.SortOrder
                            });

                            string convKey = $"{sId}_{uId}";
                            if (!unitConvertionsDict.ContainsKey(convKey))
                            {
                                unitConvertionsDict[convKey] = new
                                {
                                    StockId = sId,
                                    UnitId = uId,
                                    Multiplier = 1m,
                                    Divider = 1m
                                };
                            }
                        }
                    }
                    else if (s.DisplayTotal > 0m)
                    {
                        var dummyStockId = Guid.Empty.ToString();
                        var dummyUnitId = Guid.Empty.ToString();

                        linesList.Add(new
                        {
                            Id = Guid.NewGuid().ToString(),
                            StockSlipId = s.Id.ToString(),
                            StockId = dummyStockId,
                            UnitId = dummyUnitId,
                            CurrencyId = defCurId,
                            Quantity = 1m,
                            Price = s.DisplayTotal,
                            Total = s.DisplayTotal,
                            ActionTotal = s.DisplayTotal,
                            SortOrder = 0
                        });

                        string convKey = $"{dummyStockId}_{dummyUnitId}";
                        unitConvertionsDict[convKey] = new
                        {
                            StockId = dummyStockId,
                            UnitId = dummyUnitId,
                            Multiplier = 1m,
                            Divider = 1m
                        };
                    }

                    decimal finalTotal = s.DisplayTotal != 0m
                        ? s.DisplayTotal
                        : (linesList.Any() ? allLines.Where(x => x.StockSlipId == s.Id).Sum(x => x.Quantity * x.Price) : 0m);

                    return new
                    {
                        Id = s.Id.ToString(),
                        Code = s.Code ?? string.Empty,
                        Date = s.Date.ToString("yyyy-MM-ddTHH:mm:ss"),
                        SlipType = s.SlipType,
                        Type = s.SlipType,
                        WarehouseId = s.WarehouseId?.ToString(),
                        UserId = s.UserId?.ToString(),
                        UserName = "admin",
                        TransactionUserName = "admin",
                        IsCompleted = s.IsCompleted,
                        IsDisabled = false,
                        Group = string.Empty,
                        Tags = s.Tags != null ? s.Tags.ToList() : new List<string>(),
                        Description = s.Description ?? string.Empty,

                        Total = finalTotal,
                        DisplayTotal = finalTotal,
                        ActionTotal = finalTotal,

                        DisplayCurrencyId = defCurId,
                        Lines = linesList,
                        StockUnitConvertions = unitConvertionsDict.Values.ToList(),
                        CurrencyConvertions = convertions
                    };
                });

                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.ToString(), statusCode: 500);
            }
        });

        // --- ПОЛУЧЕНИЕ ПО ID ---
        group.MapGet("/slips/{id}", async (string id, MermerDbContext db, CancellationToken ct) =>
        {
            if (!Guid.TryParse(id, out var guid)) return Results.NotFound();
            var s = await db.StockSlips.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == guid, ct);
            if (s == null) return Results.NotFound();

            var defCur = await db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => c.IsDefault, ct)
                         ?? await db.Currencies.AsNoTracking().FirstOrDefaultAsync(ct);
            var defCurId = defCur?.Id.ToString() ?? string.Empty;
            var convertions = await GetCurrencyConvertionsAsync(db, s.Date.UtcDateTime, ct);

            var unitConvertions = s.Lines != null && s.Lines.Any()
                ? s.Lines.Where(l => l.StockId.HasValue && l.UnitId.HasValue)
                         .GroupBy(l => new { StockId = l.StockId!.Value, UnitId = l.UnitId!.Value })
                         .Select(g => (object)new
                         {
                             StockId = g.Key.StockId.ToString(),
                             UnitId = g.Key.UnitId.ToString(),
                             Multiplier = 1m,
                             Divider = 1m
                         }).ToList()
                : new List<object>();

            decimal calculatedTotal = s.DisplayTotal;
            if (calculatedTotal == 0m && s.Lines != null && s.Lines.Any())
            {
                calculatedTotal = s.Lines.Sum(l => l.ActionTotal != 0m ? l.ActionTotal : (l.Quantity * l.Price));
            }

            return Results.Ok(new
            {
                Id = s.Id.ToString(),
                Code = s.Code ?? string.Empty,
                Date = s.Date.ToString("yyyy-MM-ddTHH:mm:ss"),
                SlipType = s.SlipType,
                Type = s.SlipType,
                WarehouseId = s.WarehouseId?.ToString(),
                UserId = s.UserId?.ToString(),
                UserName = "admin",
                TransactionUserName = "admin",
                IsCompleted = s.IsCompleted,
                IsDisabled = false,
                Group = s.GroupName ?? string.Empty,
                Tags = s.Tags != null ? s.Tags.ToList() : new List<string>(),
                Description = s.Description ?? string.Empty,

                Total = calculatedTotal,
                DisplayTotal = calculatedTotal,
                ActionTotal = calculatedTotal,

                DisplayCurrencyId = defCurId,
                CurrencyConvertions = convertions,
                StockUnitConvertions = unitConvertions,

                Lines = s.Lines != null ? s.Lines.Select(l => (object)new
                {
                    Id = l.Id.ToString(),
                    StockSlipId = s.Id.ToString(),
                    StockId = l.StockId?.ToString(),
                    UnitId = l.UnitId?.ToString(),
                    CurrencyId = defCurId,
                    Quantity = l.Quantity,
                    Price = l.Price,
                    Total = l.ActionTotal != 0m ? l.ActionTotal : (l.Quantity * l.Price),
                    ActionTotal = l.ActionTotal != 0m ? l.ActionTotal : (l.Quantity * l.Price),
                    SortOrder = l.SortOrder
                }).ToList() : new List<object>()
            });
        });

        // --- СОХРАНЕНИЕ ---
        Func<HttpRequest, MermerDbContext, Task<IResult>> saveSlipHandler = async (request, db) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync();
            if (string.IsNullOrEmpty(body)) return Results.BadRequest("Empty body");

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            string? idStr = GetStringProp(root, "id", "Id");
            Guid slipId = Guid.TryParse(idStr, out var parsedGuid) && parsedGuid != Guid.Empty ? parsedGuid : Guid.NewGuid();

            var existing = await db.StockSlips.Include(s => s.Lines).FirstOrDefaultAsync(p => p.Id == slipId);

            string code = GetStringProp(root, "code", "Code") ?? $"SSLIP-{DateTime.UtcNow:yyMMddHHmmss}";
            string desc = GetStringProp(root, "description", "Description") ?? "";
            string slipType = GetStringProp(root, "slipType", "SlipType", "type", "Type") ?? "StockOpening";
            string groupName = GetStringProp(root, "group", "Group", "groupName", "GroupName") ?? "";

            string? wIdStr = GetStringProp(root, "warehouseId", "WarehouseId");
            Guid? wId = Guid.TryParse(wIdStr, out var parsedW) && parsedW != Guid.Empty ? parsedW : null;

            if (!wId.HasValue)
            {
                var defOffice = await db.Offices.AsNoTracking().FirstOrDefaultAsync(o => !o.IsDisabled);
                var defWh = (defOffice != null
                                ? await db.Warehouses.AsNoTracking().FirstOrDefaultAsync(w => w.OfficeId == defOffice.Id && !w.IsDisabled)
                                : null)
                            ?? await db.Warehouses.AsNoTracking().FirstOrDefaultAsync(w => !w.IsDisabled);

                wId = defWh?.Id;
            }

            DateTimeOffset date = new DateTimeOffset(DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Utc), TimeSpan.Zero);
            string? dateStr = GetStringProp(root, "date", "Date");
            if (!string.IsNullOrEmpty(dateStr) && DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                date = new DateTimeOffset(DateTime.SpecifyKind(parsedDate, DateTimeKind.Utc), TimeSpan.Zero);
            }

            bool isCompleted = GetBoolProp(root, "isCompleted", "IsCompleted");
            decimal displayTotal = GetDecimalProp(root, "displayTotal", "DisplayTotal", "actionTotal", "ActionTotal", "total", "Total");

            var tagsList = ExtractTagsFromRawJson(root);

            var linesList = new List<StockSlipLineEntity>();
            if (TryGetPropCaseInsensitive(root, "lines", out var linesProp) && linesProp.ValueKind == JsonValueKind.Array)
            {
                int sortOrder = 0;
                foreach (var lJson in linesProp.EnumerateArray())
                {
                    decimal qty = GetDecimalProp(lJson, "quantity", "Quantity");
                    decimal price = GetDecimalProp(lJson, "price", "Price");

                    string? lineIdStr = GetStringProp(lJson, "id", "Id");
                    Guid lineGuid = Guid.TryParse(lineIdStr, out var lG) && lG != Guid.Empty ? lG : Guid.NewGuid();

                    string? stockIdStr = GetStringProp(lJson, "stockId", "StockId");
                    Guid? stockGuid = Guid.TryParse(stockIdStr, out var sG) ? sG : null;

                    string? unitIdStr = GetStringProp(lJson, "unitId", "UnitId");
                    Guid? unitGuid = Guid.TryParse(unitIdStr, out var uG) ? uG : null;

                    decimal lineTotal = GetDecimalProp(lJson, "total", "Total", "actionTotal", "ActionTotal");
                    if (lineTotal == 0m) lineTotal = qty * price;

                    linesList.Add(new StockSlipLineEntity
                    {
                        Id = lineGuid,
                        StockSlipId = slipId,
                        StockId = stockGuid,
                        UnitId = unitGuid,
                        Quantity = qty,
                        ActionQuantity = qty,
                        Price = price,
                        ActionTotal = lineTotal,
                        SortOrder = sortOrder++
                    });
                }
            }

            if (displayTotal == 0m && linesList.Any())
            {
                displayTotal = linesList.Sum(l => l.ActionTotal);
            }

            if (existing == null)
            {
                await db.StockSlips.AddAsync(new StockSlipEntity
                {
                    Id = slipId,
                    Code = code,
                    SlipType = slipType,
                    Date = date,
                    WarehouseId = wId,
                    Description = desc,
                    GroupName = groupName,
                    Tags = tagsList.ToArray(),
                    IsCompleted = isCompleted,
                    DisplayTotal = displayTotal,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                    Lines = linesList
                });
            }
            else
            {
                existing.Code = code;
                existing.SlipType = slipType;
                existing.WarehouseId = wId;
                existing.Description = desc;
                existing.GroupName = groupName;
                existing.Tags = tagsList.ToArray();
                existing.IsCompleted = isCompleted;
                existing.DisplayTotal = displayTotal;
                existing.UpdatedAt = DateTimeOffset.UtcNow;

                // 1. Удаляем старые строки из контекста
                if (existing.Lines != null && existing.Lines.Any())
                {
                    db.StockSlipLines.RemoveRange(existing.Lines);
                }

                // 2. Добавляем новые строки в DbSet, чтобы EF пометил их как Added, а не Modified
                if (linesList.Any())
                {
                    await db.StockSlipLines.AddRangeAsync(linesList);
                }
            }

            await db.SaveChangesAsync();
            return Results.Ok(new { id = slipId, code });
        };

        group.MapPost("/slips", saveSlipHandler);
        group.MapPut("/slips/{id}", saveSlipHandler);

        group.MapDelete("/slips/{id}", async (string id, MermerDbContext db) =>
        {
            var item = await db.StockSlips.FirstOrDefaultAsync(x => x.Id == Guid.Parse(id));
            if (item != null)
            {
                db.StockSlips.Remove(item);
                await db.SaveChangesAsync();
            }
            return Results.Ok();
        });

        group.MapGet("/slips/count", async (HttpRequest req, MermerDbContext db, CancellationToken ct) =>
        {
            var query = db.StockSlips.AsNoTracking();

            string? fromStr = req.Query["from"].FirstOrDefault();
            string? tillStr = req.Query["till"].FirstOrDefault();
            string? warehouseId = req.Query["warehouseId"].FirstOrDefault();

            if (!string.IsNullOrEmpty(fromStr))
            {
                var fDate = ParseSafeFilterOffset(fromStr, isTill: false);
                query = query.Where(s => s.Date >= fDate);
            }

            if (!string.IsNullOrEmpty(tillStr))
            {
                var tDate = ParseSafeFilterOffset(tillStr, isTill: true, fromRaw: fromStr);
                query = query.Where(s => s.Date <= tDate);
            }

            if (Guid.TryParse(warehouseId, out var wGuid))
            {
                query = query.Where(s => s.WarehouseId == wGuid);
            }

            var count = await query.CountAsync(ct);
            return Results.Ok(new { count });
        });

        group.MapGet("/slips/facets", async (HttpContext ctx, MermerDbContext db, CancellationToken ct) =>
        {
            string? fields = ctx.Request.Query["fields"].ToString();
            var fieldList = string.IsNullOrEmpty(fields)
                ? new[] { "Date", "Group", "Tags" }
                : fields.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var result = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);

            foreach (var field in fieldList)
            {
                if (field.Equals("Group", StringComparison.OrdinalIgnoreCase) || field.Equals("GroupNames", StringComparison.OrdinalIgnoreCase))
                {
                    var groups = await db.StockSlips
                        .AsNoTracking()
                        .Where(x => !string.IsNullOrEmpty(x.GroupName))
                        .GroupBy(x => x.GroupName!)
                        .Select(g => new { Key = g.Key, Count = g.Count() })
                        .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

                    result[field] = groups;
                }
                else if (field.Equals("Tags", StringComparison.OrdinalIgnoreCase) || field.Equals("TagNames", StringComparison.OrdinalIgnoreCase))
                {
                    var allTags = await db.StockSlips
                        .AsNoTracking()
                        .Where(x => x.Tags != null && x.Tags.Length > 0)
                        .Select(x => x.Tags)
                        .ToListAsync(ct);

                    var tagCounts = allTags
                        .SelectMany(t => t!)
                        .Where(t => !string.IsNullOrWhiteSpace(t))
                        .GroupBy(t => t.Trim(), StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.Count());

                    result[field] = tagCounts;
                }
                else if (field.Equals("Date", StringComparison.OrdinalIgnoreCase))
                {
                    var today = DateTime.Today;
                    var todayStart = new DateTimeOffset(DateTime.SpecifyKind(today, DateTimeKind.Utc), TimeSpan.Zero);
                    var tomorrowStart = todayStart.AddDays(1);
                    var yesterdayStart = todayStart.AddDays(-1);

                    int diff = (7 + (today.DayOfWeek - DayOfWeek.Monday)) % 7;
                    var weekStart = todayStart.AddDays(-1 * diff);
                    var pastWeekStart = weekStart.AddDays(-7);

                    var monthStart = new DateTimeOffset(DateTime.SpecifyKind(new DateTime(today.Year, today.Month, 1), DateTimeKind.Utc), TimeSpan.Zero);
                    var pastMonthStart = monthStart.AddMonths(-1);

                    var countToday = await db.StockSlips.CountAsync(s => s.Date >= todayStart && s.Date < tomorrowStart, ct);
                    var countYesterday = await db.StockSlips.CountAsync(s => s.Date >= yesterdayStart && s.Date < todayStart, ct);
                    var countWeek = await db.StockSlips.CountAsync(s => s.Date >= weekStart && s.Date < tomorrowStart, ct);
                    var countPastWeek = await db.StockSlips.CountAsync(s => s.Date >= pastWeekStart && s.Date < weekStart, ct);
                    var countMonth = await db.StockSlips.CountAsync(s => s.Date >= monthStart && s.Date < tomorrowStart, ct);
                    var countPastMonth = await db.StockSlips.CountAsync(s => s.Date >= pastMonthStart && s.Date < monthStart, ct);
                    var countAll = await db.StockSlips.CountAsync(ct);

                    var dateFacets = new Dictionary<string, int>
                    {
                        { "#Today", countToday },
                        { "#Yesturday", countYesterday },
                        { "#Yesterday", countYesterday },
                        { "#This Week", countWeek },
                        { "#Past Week", countPastWeek },
                        { "#This Month", countMonth },
                        { "#Past Month", countPastMonth },
                        { "#All Records", countAll }
                    };
                    result[field] = dateFacets;
                }
                else
                {
                    result[field] = new Dictionary<string, int>();
                }
            }

            return Results.Ok(result);
        });
    }

    #region Safe Date Helpers
    private static DateTimeOffset ParseCleanDateOffset(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new DateTimeOffset(DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Utc), TimeSpan.Zero);

        raw = raw.Replace(" ", "+").Trim();
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            return new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc), TimeSpan.Zero);
        }
        return new DateTimeOffset(DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Utc), TimeSpan.Zero);
    }

    private static DateTimeOffset ParseSafeFilterOffset(string raw, bool isTill = false, string? fromRaw = null)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return isTill
                ? new DateTimeOffset(DateTime.SpecifyKind(new DateTime(2099, 12, 31, 23, 59, 59), DateTimeKind.Utc), TimeSpan.Zero)
                : new DateTimeOffset(DateTime.SpecifyKind(new DateTime(2000, 1, 1, 0, 0, 0), DateTimeKind.Utc), TimeSpan.Zero);

        raw = raw.Replace(" ", "+").Trim();
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            if (isTill && dt.TimeOfDay == TimeSpan.Zero)
            {
                if (!string.IsNullOrWhiteSpace(fromRaw) &&
                    DateTime.TryParse(fromRaw.Replace(" ", "+").Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var fDt) &&
                    dt.Date > fDt.Date)
                {
                    dt = dt.AddTicks(-1);
                }
                else
                {
                    dt = dt.Date.AddDays(1).AddTicks(-1);
                }
            }
            return new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc), TimeSpan.Zero);
        }

        return isTill
            ? new DateTimeOffset(DateTime.SpecifyKind(new DateTime(2099, 12, 31, 23, 59, 59), DateTimeKind.Utc), TimeSpan.Zero)
            : new DateTimeOffset(DateTime.SpecifyKind(new DateTime(2000, 1, 1, 0, 0, 0), DateTimeKind.Utc), TimeSpan.Zero);
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
            }
        }
        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool TryGetPropCaseInsensitive(JsonElement element, string propName, out JsonElement value)
    {
        foreach (var prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, propName, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static string? GetStringProp(JsonElement element, params string[] propNames)
    {
        foreach (var name in propNames)
        {
            if (TryGetPropCaseInsensitive(element, name, out var prop) && prop.ValueKind == JsonValueKind.String)
                return prop.GetString();
        }
        return null;
    }

    private static decimal GetDecimalProp(JsonElement element, params string[] propNames)
    {
        foreach (var name in propNames)
        {
            if (TryGetPropCaseInsensitive(element, name, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.Number) return prop.GetDecimal();
                if (prop.ValueKind == JsonValueKind.String && decimal.TryParse(prop.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
                    return val;
            }
        }
        return 0m;
    }

    private static bool GetBoolProp(JsonElement element, params string[] propNames)
    {
        foreach (var name in propNames)
        {
            if (TryGetPropCaseInsensitive(element, name, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.True) return true;
                if (prop.ValueKind == JsonValueKind.False) return false;
            }
        }
        return false;
    }
    #endregion
}
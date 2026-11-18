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

public static class StockTransfersEndpoints
{
    public static void MapStockTransfersEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/warehousing/transfers").WithTags("StockTransfers");

        // 1. СПИСОК ПЕРЕМЕЩЕНИЙ
        group.MapGet("/", async (HttpRequest req, MermerDbContext db, CancellationToken ct) =>
        {
            int take = int.TryParse(req.Query["limit"], out var lVal) ? Math.Clamp(lVal, 1, 10000) : 5000;
            int skip = int.TryParse(req.Query["offset"], out var oVal) ? Math.Max(0, oVal) : 0;

            string? fromStr = req.Query["from"].FirstOrDefault();
            string? tillStr = req.Query["till"].FirstOrDefault();
            string? warehouseId = req.Query["warehouseId"].FirstOrDefault();
            string? destinationWarehouseId = req.Query["destinationWarehouseId"].FirstOrDefault();
            bool? conflicted = bool.TryParse(req.Query["conflicted"], out var cVal) ? cVal : null;

            DateTimeOffset startDate = !string.IsNullOrEmpty(fromStr)
                ? ParseSafeFilterOffset(fromStr, isTill: false)
                : DateTimeOffset.MinValue;

            DateTimeOffset endDate = !string.IsNullOrEmpty(tillStr)
                ? ParseSafeFilterOffset(tillStr, isTill: true, fromRaw: fromStr)
                : DateTimeOffset.MaxValue;

            var defCur = await db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => c.IsDefault, ct)
                         ?? await db.Currencies.AsNoTracking().FirstOrDefaultAsync(ct);
            var defCurId = defCur?.Id.ToString() ?? "00000000-0000-0000-0000-000000000001";
            var convertions = await GetCurrencyConvertionsAsync(db, DateTime.UtcNow, ct);

            var query = db.StockTransfers
                .AsNoTracking()
                .Where(t => t.Date >= startDate && t.Date <= endDate);

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
                decimal dbSentTotal = t.ActionTotal;
                decimal dbRecTotal = t.ActionReceivedTotal != 0m ? t.ActionReceivedTotal : t.ActionTotal;

                var linesList = new List<object>();
                var unitConvertionsDict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                bool hasQuantityConflict = false;

                if (t.Lines != null && t.Lines.Any())
                {
                    foreach (var l in t.Lines)
                    {
                        var sId = l.StockId?.ToString() ?? Guid.NewGuid().ToString();
                        var uId = l.UnitId?.ToString() ?? Guid.NewGuid().ToString();
                        var ruId = l.ReceivedUnitId?.ToString() ?? uId;

                        decimal sentQty = l.Quantity;
                        decimal recQty = l.ReceivedQuantity != 0m ? l.ReceivedQuantity : 0m;

                        if (sentQty != recQty)
                        {
                            hasQuantityConflict = true;
                        }

                        linesList.Add(new
                        {
                            Id = l.Id.ToString(),
                            StockTransferId = t.Id.ToString(),
                            StockId = sId,
                            UnitId = uId,
                            ReceivedUnitId = ruId,
                            CurrencyId = defCurId,
                            Quantity = sentQty,
                            ReceivedQuantity = recQty,
                            Price = l.Price,
                            ActionTotal = l.ActionTotal != 0m ? l.ActionTotal : (sentQty * l.Price),
                            ActionReceivedTotal = l.ActionReceivedTotal != 0m ? l.ActionReceivedTotal : (recQty * l.Price),
                            SortOrder = l.SortOrder
                        });

                        string keySent = $"{sId}_{uId}";
                        if (!unitConvertionsDict.ContainsKey(keySent))
                        {
                            unitConvertionsDict[keySent] = new
                            {
                                StockId = sId,
                                UnitId = uId,
                                Multiplier = 1m,
                                Divider = 1m
                            };
                        }

                        string keyRec = $"{sId}_{ruId}";
                        if (!unitConvertionsDict.ContainsKey(keyRec))
                        {
                            unitConvertionsDict[keyRec] = new
                            {
                                StockId = sId,
                                UnitId = ruId,
                                Multiplier = 1m,
                                Divider = 1m
                            };
                        }
                    }
                }

                decimal finalSentTotal = linesList.Any() && t.Lines != null && t.Lines.Any()
                    ? t.Lines.Sum(x => x.ActionTotal != 0m ? x.ActionTotal : (x.Quantity * x.Price))
                    : dbSentTotal;

                decimal finalReceivedTotal = linesList.Any() && t.Lines != null && t.Lines.Any()
                    ? t.Lines.Sum(x => x.ActionReceivedTotal != 0m ? x.ActionReceivedTotal : (x.ReceivedQuantity * x.Price))
                    : dbRecTotal;

                bool isConflicted = hasQuantityConflict || (finalSentTotal != finalReceivedTotal);

                return new
                {
                    Id = t.Id.ToString(),
                    Code = t.Code ?? string.Empty,
                    Date = t.Date.ToString("yyyy-MM-ddTHH:mm:ss"),
                    Type = "StockTransfer",
                    WarehouseId = t.WarehouseId?.ToString(),
                    DestinationWarehouseId = t.DestinationWarehouseId?.ToString(),
                    DisplayCurrencyId = t.DisplayCurrencyId?.ToString() ?? defCurId,
                    CurrencyConvertions = convertions,
                    StockUnitConvertions = unitConvertionsDict.Values.ToList(),
                    UserName = t.UserName ?? "admin",
                    IsCompleted = t.IsCompleted,
                    IsDisabled = t.IsDisabled,
                    IsConflicted = isConflicted,
                    Group = t.GroupName ?? string.Empty,
                    Tags = t.Tags != null ? t.Tags.ToList() : new List<string>(),
                    Description = t.Description ?? string.Empty,
                    ActionTotal = finalSentTotal,
                    ActionReceivedTotal = finalReceivedTotal,
                    DisplayTotal = finalSentTotal,
                    DisplayReceivedTotal = finalReceivedTotal,
                    Total = finalSentTotal,
                    Lines = linesList
                };
            });

            if (conflicted.HasValue && conflicted.Value)
            {
                result = result.Where(x => ((dynamic)x).IsConflicted);
            }

            return Results.Ok(result);
        });

        // 1.1. БЫСТРЫЙ ПОДСЧЕТ ДЛЯ ПЛИТОК ДАТ
        group.MapGet("/count", async (HttpRequest req, MermerDbContext db, CancellationToken ct) =>
        {
            var query = db.StockTransfers.AsNoTracking();

            string? fromStr = req.Query["from"].FirstOrDefault();
            string? tillStr = req.Query["till"].FirstOrDefault();
            string? warehouseId = req.Query["warehouseId"].FirstOrDefault();
            string? destinationWarehouseId = req.Query["destinationWarehouseId"].FirstOrDefault();
            bool? conflicted = bool.TryParse(req.Query["conflicted"], out var cVal) ? cVal : null;

            if (!string.IsNullOrEmpty(fromStr))
            {
                var startDate = ParseSafeFilterOffset(fromStr, isTill: false);
                query = query.Where(t => t.Date >= startDate);
            }

            if (!string.IsNullOrEmpty(tillStr))
            {
                var endDate = ParseSafeFilterOffset(tillStr, isTill: true, fromStr);
                query = query.Where(t => t.Date <= endDate);
            }

            if (Guid.TryParse(warehouseId, out var srcGuid))
                query = query.Where(t => t.WarehouseId == srcGuid);

            if (Guid.TryParse(destinationWarehouseId, out var dstGuid))
                query = query.Where(t => t.DestinationWarehouseId == dstGuid);

            if (conflicted.HasValue && conflicted.Value)
            {
                query = query.Where(t => t.Lines.Any(l => l.Quantity != (l.ReceivedQuantity != 0m ? l.ReceivedQuantity : 0m)));
            }

            var count = await query.CountAsync(ct);
            return Results.Ok(new { count });
        });

        // 2. ПОЛУЧЕНИЕ ПО ID
        group.MapGet("/{id}", async (string id, MermerDbContext db, CancellationToken ct) =>
        {
            if (!Guid.TryParse(id, out var guid)) return Results.NotFound();
            var t = await db.StockTransfers.Include(x => x.Lines).AsNoTracking().FirstOrDefaultAsync(x => x.Id == guid, ct);
            if (t == null) return Results.NotFound();

            var defCur = await db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => c.IsDefault && !c.IsDisabled, ct)
                         ?? await db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => !c.IsDisabled, ct);
            var defCurId = defCur?.Id.ToString() ?? string.Empty;
            var docCurId = t.DisplayCurrencyId?.ToString() ?? defCurId;

            var convertions = await GetCurrencyConvertionsAsync(db, t.Date.UtcDateTime, ct);

            // Собираем конвертации единиц измерения (StockUnitConvertions)
            var unitConvertionsDict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (t.Lines != null)
            {
                foreach (var l in t.Lines)
                {
                    var sId = l.StockId?.ToString() ?? Guid.Empty.ToString();
                    var uId = l.UnitId?.ToString() ?? Guid.Empty.ToString();
                    var ruId = l.ReceivedUnitId?.ToString() ?? uId;

                    string keySent = $"{sId}_{uId}";
                    if (!unitConvertionsDict.ContainsKey(keySent))
                    {
                        unitConvertionsDict[keySent] = new
                        {
                            StockId = sId,
                            UnitId = uId,
                            Multiplier = 1m,
                            Divider = 1m
                        };
                    }

                    string keyRec = $"{sId}_{ruId}";
                    if (!unitConvertionsDict.ContainsKey(keyRec))
                    {
                        unitConvertionsDict[keyRec] = new
                        {
                            StockId = sId,
                            UnitId = ruId,
                            Multiplier = 1m,
                            Divider = 1m
                        };
                    }
                }
            }

            decimal linesSent = t.Lines != null && t.Lines.Any() ? t.Lines.Sum(l => l.ActionTotal) : 0m;
            decimal linesReceived = t.Lines != null && t.Lines.Any() ? t.Lines.Sum(l => l.ActionReceivedTotal) : 0m;

            decimal sentTotal = linesSent != 0m ? linesSent : t.ActionTotal;
            decimal receivedTotal = linesReceived != 0m ? linesReceived : t.ActionReceivedTotal;

            bool hasQuantityConflict = t.Lines != null && t.Lines.Any(l => l.Quantity != l.ReceivedQuantity);
            bool isConflicted = hasQuantityConflict || (sentTotal != receivedTotal);

            return Results.Ok(new
            {
                Id = t.Id.ToString(),
                Code = t.Code ?? string.Empty,
                Date = t.Date.ToString("yyyy-MM-ddTHH:mm:ss"),
                Type = "StockTransfer",
                WarehouseId = t.WarehouseId?.ToString(),
                DestinationWarehouseId = t.DestinationWarehouseId?.ToString(),
                DisplayCurrencyId = docCurId,
                CurrencyConvertions = convertions,
                StockUnitConvertions = unitConvertionsDict.Values.ToList(), // <-- ДОБАВЛЕНО
                UserName = t.UserName ?? "admin",
                IsCompleted = t.IsCompleted,
                IsDisabled = t.IsDisabled,
                IsConflicted = isConflicted,
                Group = t.GroupName ?? string.Empty,
                Tags = t.Tags != null ? t.Tags.ToList() : new List<string>(),
                Description = t.Description ?? string.Empty,
                ActionTotal = sentTotal,
                ActionReceivedTotal = receivedTotal,
                DisplayTotal = sentTotal,
                DisplayReceivedTotal = receivedTotal,
                Total = sentTotal,
                Lines = t.Lines != null ? t.Lines.Select(l => (object)new
                {
                    Id = l.Id.ToString(),
                    StockTransferId = t.Id.ToString(),
                    StockId = l.StockId?.ToString(),
                    UnitId = l.UnitId?.ToString(),
                    ReceivedUnitId = l.ReceivedUnitId?.ToString() ?? l.UnitId?.ToString(),
                    CurrencyId = docCurId, // <-- ВОТ ЗДЕСЬ БЫЛА ОШИБКА: CurrencyId не отдавался клиенту!
                    Quantity = l.Quantity,
                    ReceivedQuantity = l.ReceivedQuantity,
                    Price = l.Price,
                    Total = l.ActionTotal != 0m ? l.ActionTotal : (l.Quantity * l.Price),
                    ActionTotal = l.ActionTotal != 0m ? l.ActionTotal : (l.Quantity * l.Price),
                    ActionReceivedTotal = l.ActionReceivedTotal != 0m ? l.ActionReceivedTotal : (l.ReceivedQuantity * l.Price),
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
            Guid? srcWhId = Guid.TryParse(GetStringProp(root, "warehouseId", "WarehouseId"), out var sW) && sW != Guid.Empty ? sW : null;
            Guid? dstWhId = Guid.TryParse(GetStringProp(root, "destinationWarehouseId", "DestinationWarehouseId"), out var dW) && dW != Guid.Empty ? dW : null;
            Guid? dispCurId = Guid.TryParse(GetStringProp(root, "displayCurrencyId", "DisplayCurrencyId"), out var cG) && cG != Guid.Empty ? cG : null;

            if (!srcWhId.HasValue)
            {
                var defOffice = await db.Offices.AsNoTracking().FirstOrDefaultAsync(o => !o.IsDisabled);
                var defWh = (defOffice != null
                                ? await db.Warehouses.AsNoTracking().FirstOrDefaultAsync(w => w.OfficeId == defOffice.Id && !w.IsDisabled)
                                : null)
                            ?? await db.Warehouses.AsNoTracking().FirstOrDefaultAsync(w => !w.IsDisabled);

                srcWhId = defWh?.Id;
            }

            if (!dispCurId.HasValue)
            {
                var defCur = await db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => c.IsDefault && !c.IsDisabled)
                             ?? await db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => !c.IsDisabled);
                dispCurId = defCur?.Id;
            }

            DateTimeOffset date = new DateTimeOffset(DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Utc), TimeSpan.Zero);
            string? dateStr = GetStringProp(root, "date", "Date");
            if (!string.IsNullOrEmpty(dateStr) && DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                date = new DateTimeOffset(DateTime.SpecifyKind(parsedDate, DateTimeKind.Utc), TimeSpan.Zero);
            }

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
                existing.WarehouseId = srcWhId;
                existing.DestinationWarehouseId = dstWhId;

                // Если валюта в базе ранее не была сохранена — заполняем её
                if (!existing.DisplayCurrencyId.HasValue && dispCurId.HasValue)
                {
                    existing.DisplayCurrencyId = dispCurId;
                }

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

        // 5. ФАСЕТЫ
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
                    var today = DateTime.Today;
                    var todayStart = new DateTimeOffset(DateTime.SpecifyKind(today, DateTimeKind.Utc), TimeSpan.Zero);
                    var tomorrowStart = todayStart.AddDays(1);
                    var yesterdayStart = todayStart.AddDays(-1);

                    int diff = (7 + (today.DayOfWeek - DayOfWeek.Monday)) % 7;
                    var weekStart = todayStart.AddDays(-1 * diff);
                    var pastWeekStart = weekStart.AddDays(-7);

                    var monthStart = new DateTimeOffset(DateTime.SpecifyKind(new DateTime(today.Year, today.Month, 1), DateTimeKind.Utc), TimeSpan.Zero);
                    var pastMonthStart = monthStart.AddMonths(-1);

                    var countToday = await db.StockTransfers.CountAsync(r => !r.IsDisabled && r.Date >= todayStart && r.Date < tomorrowStart, ct);
                    var countYesterday = await db.StockTransfers.CountAsync(r => !r.IsDisabled && r.Date >= yesterdayStart && r.Date < todayStart, ct);
                    var countWeek = await db.StockTransfers.CountAsync(r => !r.IsDisabled && r.Date >= weekStart && r.Date < tomorrowStart, ct);
                    var countPastWeek = await db.StockTransfers.CountAsync(r => !r.IsDisabled && r.Date >= pastWeekStart && r.Date < weekStart, ct);
                    var countMonth = await db.StockTransfers.CountAsync(r => !r.IsDisabled && r.Date >= monthStart && r.Date < tomorrowStart, ct);
                    var countPastMonth = await db.StockTransfers.CountAsync(r => !r.IsDisabled && r.Date >= pastMonthStart && r.Date < monthStart, ct);
                    var countAll = await db.StockTransfers.CountAsync(r => !r.IsDisabled, ct);

                    result[field] = new Dictionary<string, int>
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
    private static DateTimeOffset ParseCleanDateOffset(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new DateTimeOffset(DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Utc), TimeSpan.Zero);
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
            return isTill ? new DateTimeOffset(new DateTime(2099, 12, 31, 23, 59, 59, DateTimeKind.Utc), TimeSpan.Zero)
                          : new DateTimeOffset(new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc), TimeSpan.Zero);

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

        return isTill ? new DateTimeOffset(new DateTime(2099, 12, 31, 23, 59, 59, DateTimeKind.Utc), TimeSpan.Zero)
                      : new DateTimeOffset(new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc), TimeSpan.Zero);
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
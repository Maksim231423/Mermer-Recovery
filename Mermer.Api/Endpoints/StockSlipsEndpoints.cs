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

public static class StockSlipsEndpoints
{
    public static void MapStockSlipsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/catalog").WithTags("Catalog");

        // --- СПИСОК СКЛАДСКИХ ОРДЕРОВ ---
        group.MapGet("/slips", async (DateTime? from, DateTime? till, string? warehouseId, MermerDbContext db, CancellationToken ct) =>
        {
            try
            {
                // 1. Валюты и конвертеры
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

                // 2. Безопасные границы дат в UTC
                DateTimeOffset startDate = from.HasValue
                    ? DateTime.SpecifyKind(from.Value, DateTimeKind.Utc)
                    : DateTimeOffset.MinValue;
                DateTimeOffset endDate = till.HasValue
                    ? DateTime.SpecifyKind(till.Value, DateTimeKind.Utc)
                    : DateTimeOffset.MaxValue;

                // 3. Выборка заголовков накладных (БЕЗ групповых полей и связей)
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

                // 4. Подгружаем строки только для найденных накладных
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

                // 5. Формируем DTO для клиента
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
                        Date = s.Date.UtcDateTime,
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
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[CRITICAL ERROR in /slips]: {ex}");
                Console.ResetColor();
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
                Date = s.Date.UtcDateTime,
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

            string? idStr = GetStringProperty(root, "id", "Id");
            Guid slipId = Guid.TryParse(idStr, out var parsedGuid) && parsedGuid != Guid.Empty ? parsedGuid : Guid.NewGuid();

            var existing = await db.StockSlips.Include(s => s.Lines).FirstOrDefaultAsync(p => p.Id == slipId);

            string code = GetStringProperty(root, "code", "Code") ?? $"SSLIP-{DateTime.UtcNow:yyMMddHHmmss}";
            string desc = GetStringProperty(root, "description", "Description") ?? "";
            string slipType = GetStringProperty(root, "slipType", "SlipType", "type", "Type") ?? "StockOpening";
            string groupName = GetStringProperty(root, "group", "Group", "groupName", "GroupName") ?? "";

            string? wIdStr = GetStringProperty(root, "warehouseId", "WarehouseId");
            Guid? wId = Guid.TryParse(wIdStr, out var parsedW) ? parsedW : null;

            DateTimeOffset date = DateTimeOffset.UtcNow;
            string? dateStr = GetStringProperty(root, "date", "Date");
            if (!string.IsNullOrEmpty(dateStr) && DateTimeOffset.TryParse(dateStr, out var pDate))
            {
                date = pDate.ToUniversalTime();
            }

            bool isCompleted = GetBoolProperty(root, "isCompleted", "IsCompleted");
            decimal displayTotal = GetDecimalProperty(root, "displayTotal", "DisplayTotal", "actionTotal", "ActionTotal", "total", "Total");

            var tagsList = ExtractTagsFromRawJson(root);

            var linesList = new List<StockSlipLineEntity>();
            if (TryGetPropertyCaseInsensitive(root, "lines", out var linesProp) && linesProp.ValueKind == JsonValueKind.Array)
            {
                int sortOrder = 0;
                foreach (var lJson in linesProp.EnumerateArray())
                {
                    decimal qty = GetDecimalProperty(lJson, "quantity", "Quantity");
                    decimal price = GetDecimalProperty(lJson, "price", "Price");

                    string? lineIdStr = GetStringProperty(lJson, "id", "Id");
                    Guid lineGuid = Guid.TryParse(lineIdStr, out var lG) && lG != Guid.Empty ? lG : Guid.NewGuid();

                    string? stockIdStr = GetStringProperty(lJson, "stockId", "StockId");
                    Guid? stockGuid = Guid.TryParse(stockIdStr, out var sG) ? sG : null;

                    string? unitIdStr = GetStringProperty(lJson, "unitId", "UnitId");
                    Guid? unitGuid = Guid.TryParse(unitIdStr, out var uG) ? uG : null;

                    decimal lineTotal = GetDecimalProperty(lJson, "total", "Total", "actionTotal", "ActionTotal");
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
                existing.Date = date;
                existing.WarehouseId = wId;
                existing.Description = desc;
                existing.GroupName = groupName;
                existing.Tags = tagsList.ToArray();
                existing.IsCompleted = isCompleted;
                existing.DisplayTotal = displayTotal;
                existing.UpdatedAt = DateTimeOffset.UtcNow;

                if (existing.Lines != null) db.StockSlipLines.RemoveRange(existing.Lines);
                existing.Lines = linesList;
                db.StockSlips.Update(existing);
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

        // --- ФАСЕТЫ ---
        group.MapGet("/slips/facets", async (HttpContext ctx, MermerDbContext db, CancellationToken ct) =>
        {
            string? fields = ctx.Request.Query["fields"].ToString();
            var fieldList = string.IsNullOrEmpty(fields)
                ? new[] { "Date", "Group", "Tags" }
                : fields.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(f => f.Trim())
                        .ToArray();

            var result = new Dictionary<string, Dictionary<string, int>>();

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
                        .GroupBy(t => t)
                        .ToDictionary(g => g.Key, g => g.Count());

                    result[field] = tagCounts;
                }
                else if (field.Equals("Date", StringComparison.OrdinalIgnoreCase))
                {
                    var now = DateTime.Now.Date;
                    var slips = await db.StockSlips.AsNoTracking().Select(s => s.Date).ToListAsync(ct);
                    var localDates = slips.Select(d => d.ToLocalTime().Date).ToList();

                    var dateFacets = new Dictionary<string, int>
                    {
                        { "#Today", localDates.Count(d => d == now) },
                        { "#This Week", localDates.Count(d => d >= now.AddDays(-7)) },
                        { "#This Month", localDates.Count(d => d.Month == now.Month && d.Year == now.Year) },
                        { "#All Records", localDates.Count }
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

    private static bool TryGetPropertyCaseInsensitive(JsonElement element, string propName, out JsonElement value)
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

    private static string? GetStringProperty(JsonElement element, params string[] propNames)
    {
        foreach (var name in propNames)
        {
            if (TryGetPropertyCaseInsensitive(element, name, out var prop) && prop.ValueKind == JsonValueKind.String) return prop.GetString();
        }
        return null;
    }

    private static decimal GetDecimalProperty(JsonElement element, params string[] propNames)
    {
        foreach (var name in propNames)
        {
            if (TryGetPropertyCaseInsensitive(element, name, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.Number) return prop.GetDecimal();
                if (prop.ValueKind == JsonValueKind.String && decimal.TryParse(prop.GetString(), out var val)) return val;
            }
        }
        return 0m;
    }

    private static bool GetBoolProperty(JsonElement element, params string[] propNames)
    {
        foreach (var name in propNames)
        {
            if (TryGetPropertyCaseInsensitive(element, name, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.True) return true;
                if (prop.ValueKind == JsonValueKind.False) return false;
            }
        }
        return false;
    }
    #endregion
}
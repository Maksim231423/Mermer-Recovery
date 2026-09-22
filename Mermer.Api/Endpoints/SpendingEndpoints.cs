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

public static class SpendingEndpoints
{
    public static void MapSpendingEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/spending/slips").WithTags("SpendingSlips");

        // 1. СПИСОК АКТОВ РАСХОДОВ (С ПАГИНАЦИЕЙ)
        group.MapGet("", async (DateTime? from, DateTime? till, string? depositoryId, int? limit, int? offset, MermerDbContext db, CancellationToken ct) =>
        {
            int take = limit.HasValue ? Math.Clamp(limit.Value, 1, 1000) : 200;
            int skip = offset.GetValueOrDefault(0);

            var startDate = from ?? DateTime.UtcNow.AddMonths(-3);
            var endDate = till ?? DateTime.UtcNow;

            var defaultCur = await db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => c.IsDefault, ct)
                             ?? await db.Currencies.AsNoTracking().FirstOrDefaultAsync(ct);
            var defaultCurrencyId = defaultCur?.Id.ToString();

            var allConvertions = await GetCurrencyConvertionsAsync(db, DateTime.UtcNow, ct);

            var query = db.ExpenseSlips
                .AsNoTracking()
                .Where(s => s.Date >= startDate && s.Date <= endDate && !s.IsDisabled);

            if (Guid.TryParse(depositoryId, out var depGuid))
                query = query.Where(s => s.DepositoryId == depGuid);

            var slips = await query
                .OrderByDescending(s => s.Date)
                .Skip(skip)
                .Take(take)
                .Include(s => s.Lines)
                .AsSplitQuery()
                .ToListAsync(ct);

            var result = slips.Select(s =>
            {
                var docCurrencyId = s.DisplayCurrencyId?.ToString() ?? defaultCurrencyId;
                decimal total = s.Lines?.Sum(l => l.Amount) ?? 0m;

                return new
                {
                    Id = s.Id.ToString(),
                    Code = s.Code ?? "",
                    Date = s.Date,
                    Type = "ExpenseSlip",
                    FundsSlipType = "ExpenseSlip",
                    DepositoryId = s.DepositoryId?.ToString(),
                    OfficeId = s.OfficeId?.ToString(),
                    DisplayCurrencyId = docCurrencyId,
                    CurrencyId = docCurrencyId,
                    CurrencyConvertions = allConvertions,
                    UserId = s.UserId?.ToString(),
                    UserName = s.UserName,
                    IsCompleted = s.IsCompleted,
                    IsDisabled = s.IsDisabled,
                    Group = s.GroupName ?? "",
                    Tags = s.Tags != null ? s.Tags.ToList() : new List<string>(),
                    Description = s.Description ?? "",
                    ActionTotal = total,
                    DisplayTotal = total,
                    Amount = total,
                    Total = total,
                    LinesCount = s.Lines?.Count ?? 0,
                    Lines = s.Lines != null
                        ? s.Lines.Select(l => (object)new
                        {
                            Id = l.Id.ToString(),
                            ExpenseSlipId = s.Id.ToString(),
                            ExpenseId = l.ExpenseId?.ToString(),
                            Amount = l.Amount,
                            Total = l.Amount,
                            CurrencyId = l.CurrencyId?.ToString() ?? docCurrencyId,
                            SortOrder = l.SortOrder
                        }).ToList()
                        : new List<object>()
                };
            });

            return Results.Ok(result);
        });

        // 2. ПОЛУЧЕНИЕ ПО ID
        group.MapGet("/{id}", async (string id, MermerDbContext db, CancellationToken ct) =>
        {
            if (!Guid.TryParse(id, out var guid)) return Results.NotFound();
            var s = await db.ExpenseSlips.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == guid, ct);
            if (s == null) return Results.NotFound();

            var docCurrencyId = s.DisplayCurrencyId?.ToString() ?? (await db.Currencies.FirstOrDefaultAsync(c => c.IsDefault))?.Id.ToString();
            var convertions = await GetCurrencyConvertionsAsync(db, s.Date, ct);
            decimal total = s.Lines?.Sum(l => l.Amount) ?? 0m;

            return Results.Ok(new
            {
                Id = s.Id.ToString(),
                Code = s.Code ?? "",
                Date = s.Date,
                Type = "ExpenseSlip",
                FundsSlipType = "ExpenseSlip",
                DepositoryId = s.DepositoryId?.ToString(),
                OfficeId = s.OfficeId?.ToString(),
                DisplayCurrencyId = docCurrencyId,
                CurrencyId = docCurrencyId,
                CurrencyConvertions = convertions,
                UserId = s.UserId?.ToString(),
                UserName = s.UserName,
                IsCompleted = s.IsCompleted,
                IsDisabled = s.IsDisabled,
                Group = s.GroupName ?? "",
                Tags = s.Tags != null ? s.Tags.ToList() : new List<string>(),
                Description = s.Description ?? "",
                ActionTotal = total,
                DisplayTotal = total,
                Amount = total,
                Total = total,
                LinesCount = s.Lines?.Count ?? 0,
                Lines = s.Lines != null
                    ? s.Lines.Select(l => (object)new
                    {
                        Id = l.Id.ToString(),
                        ExpenseSlipId = s.Id.ToString(),
                        ExpenseId = l.ExpenseId?.ToString(),
                        Amount = l.Amount,
                        Total = l.Amount,
                        CurrencyId = l.CurrencyId?.ToString() ?? docCurrencyId,
                        SortOrder = l.SortOrder
                    }).ToList()
                    : new List<object>()
            });
        });

        // 3. СОХРАНЕНИЕ (POST / PUT)
        Func<HttpRequest, MermerDbContext, Task<IResult>> saveHandler = async (request, db) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync();
            if (string.IsNullOrEmpty(body)) return Results.BadRequest("Empty body");

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            string? idStr = GetStringProp(root, "id", "Id");
            Guid id = Guid.TryParse(idStr, out var g) && g != Guid.Empty ? g : Guid.NewGuid();

            var existing = await db.ExpenseSlips.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);

            string code = GetStringProp(root, "code", "Code") ?? $"EXP-{DateTime.UtcNow:yyMMddHHmmss}";
            Guid? depId = Guid.TryParse(GetStringProp(root, "depositoryId", "DepositoryId"), out var dG) ? dG : null;
            Guid? offId = Guid.TryParse(GetStringProp(root, "officeId", "OfficeId"), out var oG) ? oG : null;
            Guid? curId = Guid.TryParse(GetStringProp(root, "displayCurrencyId", "DisplayCurrencyId", "currencyId", "CurrencyId"), out var cG) ? cG : null;
            Guid? userId = Guid.TryParse(GetStringProp(root, "userId", "UserId"), out var uG) ? uG : null;

            DateTime date = DateTime.UtcNow;
            if (DateTime.TryParse(GetStringProp(root, "date", "Date"), out var d)) date = d.ToUniversalTime();

            var tagsList = ExtractTagsFromRawJson(root);
            string groupName = GetStringProp(root, "group", "Group", "groupName", "GroupName") ?? "";
            string description = GetStringProp(root, "description", "Description") ?? "";

            var linesList = new List<ExpenseSlipLineEntity>();
            if (TryGetPropCaseInsensitive(root, "lines", out var linesProp) && linesProp.ValueKind == JsonValueKind.Array)
            {
                int order = 0;
                foreach (var l in linesProp.EnumerateArray())
                {
                    linesList.Add(new ExpenseSlipLineEntity
                    {
                        Id = Guid.TryParse(GetStringProp(l, "id", "Id"), out var lId) && lId != Guid.Empty ? lId : Guid.NewGuid(),
                        ExpenseSlipId = id,
                        ExpenseId = Guid.TryParse(GetStringProp(l, "expenseId", "ExpenseId"), out var eG) ? eG : null,
                        Amount = GetDecimalProp(l, "amount", "Amount"),
                        CurrencyId = Guid.TryParse(GetStringProp(l, "currencyId", "CurrencyId"), out var lcG) ? lcG : curId,
                        SortOrder = order++
                    });
                }
            }

            if (existing == null)
            {
                await db.ExpenseSlips.AddAsync(new ExpenseSlipEntity
                {
                    Id = id,
                    Code = code,
                    Date = date,
                    UserId = userId,
                    DepositoryId = depId,
                    OfficeId = offId,
                    DisplayCurrencyId = curId,
                    UserName = GetStringProp(root, "userName", "UserName") ?? "admin",
                    IsCompleted = GetBoolProp(root, "isCompleted", "IsCompleted"),
                    IsDisabled = GetBoolProp(root, "isDisabled", "IsDisabled"),
                    GroupName = groupName,
                    Description = description,
                    Tags = tagsList.ToArray(),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    Lines = linesList
                });
            }
            else
            {
                existing.Code = code;
                existing.Date = date;
                existing.UserId = userId;
                existing.DepositoryId = depId;
                existing.OfficeId = offId;
                existing.DisplayCurrencyId = curId;
                existing.IsCompleted = GetBoolProp(root, "isCompleted", "IsCompleted");
                existing.IsDisabled = GetBoolProp(root, "isDisabled", "IsDisabled");
                existing.GroupName = groupName;
                existing.Description = description;
                existing.Tags = tagsList.ToArray();
                existing.UpdatedAt = DateTime.UtcNow;

                db.ExpenseSlipLines.RemoveRange(existing.Lines);
                existing.Lines = linesList;
                db.ExpenseSlips.Update(existing);
            }

            await db.SaveChangesAsync();
            return Results.Content($"{{\"id\":\"{id}\",\"code\":\"{code}\"}}", "application/json");
        };

        group.MapPost("/", saveHandler);
        group.MapPut("/{routeId}", async (string routeId, HttpRequest request, MermerDbContext db) => await saveHandler(request, db));
        group.MapPost("", saveHandler);
        group.MapPut("/{id}", saveHandler);

        // 4. УДАЛЕНИЕ
        group.MapDelete("/{id}", async (string id, MermerDbContext db) =>
        {
            if (Guid.TryParse(id, out var guid))
            {
                var item = await db.ExpenseSlips.FirstOrDefaultAsync(x => x.Id == guid);
                if (item != null)
                {
                    item.IsDisabled = true;
                    item.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync();
                }
            }
            return Results.Ok();
        });

        // 5. ФАСЕТЫ (БЕЗ ВЫГРУЗКИ ВСЕЙ ТАБЛИЦЫ В ПАМЯТЬ)
        group.MapGet("/facets", async (HttpContext ctx, MermerDbContext db, CancellationToken ct) =>
        {
            var todayUtc = DateTime.UtcNow.Date;
            var weekStart = todayUtc.AddDays(-7);
            var monthStart = new DateTime(todayUtc.Year, todayUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            var countToday = await db.ExpenseSlips.CountAsync(s => !s.IsDisabled && s.Date >= todayUtc, ct);
            var countWeek = await db.ExpenseSlips.CountAsync(s => !s.IsDisabled && s.Date >= weekStart, ct);
            var countMonth = await db.ExpenseSlips.CountAsync(s => !s.IsDisabled && s.Date >= monthStart, ct);
            var countAll = await db.ExpenseSlips.CountAsync(s => !s.IsDisabled, ct);

            var groups = await db.ExpenseSlips
                .AsNoTracking()
                .Where(x => !string.IsNullOrEmpty(x.GroupName) && !x.IsDisabled)
                .GroupBy(x => x.GroupName!)
                .Select(g => new { Key = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

            return Results.Ok(new Dictionary<string, object>
            {
                ["Group"] = groups,
                ["Date"] = new Dictionary<string, int>
                {
                    { "#Today", countToday },
                    { "#This Week", countWeek },
                    { "#This Month", countMonth },
                    { "#All Records", countAll }
                }
            });
        });

        // 6. ЖУРНАЛ СТАТЕЙ РАСХОДОВ С ЛИМИТОМ
        routes.MapGet("/api/spending/actions", async (DateTime? from, DateTime? till, string? expenseId, int? limit, HttpRequest req, MermerDbContext db, CancellationToken ct) =>
        {
            int take = limit.HasValue ? Math.Clamp(limit.Value, 1, 1000) : 300;
            var startDate = from ?? DateTime.UtcNow.AddMonths(-1);
            var endDate = till ?? DateTime.UtcNow;

            var depIds = req.Query["depositoryId"]
                .Select(x => Guid.TryParse(x, out var g) ? (Guid?)g : null)
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .ToList();

            Guid? filterExpenseGuid = Guid.TryParse(expenseId, out var eGuid) ? eGuid : null;

            var query = db.ExpenseSlips
                .Include(s => s.Lines)
                .AsNoTracking()
                .Where(s => s.Date >= startDate && s.Date <= endDate && !s.IsDisabled);

            if (depIds.Any())
            {
                query = query.Where(s => s.DepositoryId.HasValue && depIds.Contains(s.DepositoryId.Value));
            }

            var slips = await query.OrderByDescending(s => s.Date).Take(take).ToListAsync(ct);
            var actions = new List<object>();

            foreach (var s in slips)
            {
                foreach (var line in s.Lines ?? Enumerable.Empty<ExpenseSlipLineEntity>())
                {
                    if (filterExpenseGuid.HasValue && line.ExpenseId != filterExpenseGuid) continue;

                    actions.Add(new
                    {
                        TransactionId = s.Id.ToString(),
                        TransactionCode = s.Code ?? "",
                        TransactionDate = s.Date,
                        TransactionType = "ExpenseSlip",
                        TransactionUserId = s.UserId?.ToString(),
                        TransactionUserName = s.UserName,
                        TransactionIsCompleted = s.IsCompleted,
                        TransactionIsDisabled = s.IsDisabled,
                        TransactionGroup = s.GroupName ?? "",
                        TransactionTags = s.Tags ?? Array.Empty<string>(),
                        ActionDepositoryId = s.DepositoryId?.ToString(),
                        ActionExpenseId = line.ExpenseId?.ToString(),
                        ActionAmount = line.Amount
                    });
                }
            }

            return Results.Ok(actions);
        }).WithTags("SpendingActions");
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
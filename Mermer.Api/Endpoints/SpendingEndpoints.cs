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

        // 1. СПИСОК АКТОВ РАСХОДОВ (СОРТИРОВКА ПО УБЫВАНИЮ ДАТЫ)
        group.MapGet("", async (DateTime? from, DateTime? till, string? depositoryId, int? limit, int? offset, MermerDbContext db, CancellationToken ct) =>
        {
            int take = limit.HasValue ? Math.Clamp(limit.Value, 1, 1000) : 500;
            int skip = offset.GetValueOrDefault(0);

            var startDate = from.HasValue ? from.Value.ToUniversalTime() : DateTime.SpecifyKind(new DateTime(2000, 1, 1), DateTimeKind.Utc);
            var endDate = till.HasValue ? till.Value.ToUniversalTime() : DateTime.SpecifyKind(new DateTime(2099, 12, 31), DateTimeKind.Utc);

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

        // 2. ПОДСЧЕТ ДЛЯ ПЛИТОК ДАТ (И ДЛЯ ALL RECORDS)
        group.MapGet("/count", async (DateTime? from, DateTime? till, string? depositoryId, MermerDbContext db, CancellationToken ct) =>
        {
            var startDate = from.HasValue ? from.Value.ToUniversalTime() : DateTime.SpecifyKind(new DateTime(2000, 1, 1), DateTimeKind.Utc);
            var endDate = till.HasValue ? till.Value.ToUniversalTime() : DateTime.SpecifyKind(new DateTime(2099, 12, 31), DateTimeKind.Utc);

            var query = db.ExpenseSlips
                .AsNoTracking()
                .Where(s => !s.IsDisabled && s.Date >= startDate && s.Date <= endDate);

            if (Guid.TryParse(depositoryId, out var depGuid))
                query = query.Where(s => s.DepositoryId == depGuid);

            var count = await query.CountAsync(ct);
            return Results.Ok(new { count });
        });

        // 3. ПОЛУЧЕНИЕ ПО ID
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

        // 4. СОХРАНЕНИЕ (POST / PUT)
        Func<HttpRequest, MermerDbContext, Task<IResult>> saveHandler = async (request, db) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(body)) return Results.BadRequest("Empty body");

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            string? idStr = GetStringProp(root, "id", "Id");
            Guid id = Guid.TryParse(idStr, out var g) && g != Guid.Empty ? g : Guid.NewGuid();

            var existing = await db.ExpenseSlips.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);

            string code = GetStringProp(root, "code", "Code") ?? $"EXP-{DateTime.UtcNow:yyMMddHHmmss}";

            Guid? depId = Guid.TryParse(GetStringProp(root, "depositoryId", "DepositoryId"), out var dG) ? dG : null;
            Guid? offId = Guid.TryParse(GetStringProp(root, "officeId", "OfficeId"), out var oG) ? oG : null;

            if (!offId.HasValue && depId.HasValue)
            {
                var dep = await db.Depositories.AsNoTracking().FirstOrDefaultAsync(d => d.Id == depId.Value);
                offId = dep?.OfficeId;
            }

            Guid? curId = Guid.TryParse(GetStringProp(root, "displayCurrencyId", "DisplayCurrencyId", "currencyId", "CurrencyId"), out var cG) ? cG : null;
            if (!curId.HasValue)
            {
                var defCur = await db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => c.IsDefault);
                curId = defCur?.Id;
            }

            Guid? userId = Guid.TryParse(GetStringProp(root, "userId", "UserId"), out var uG) ? uG : null;

            DateTime date = DateTime.UtcNow;
            string? dateStr = GetStringProp(root, "date", "Date");
            if (!string.IsNullOrEmpty(dateStr) && DateTime.TryParse(dateStr, out var d))
            {
                date = DateTime.SpecifyKind(d.ToUniversalTime(), DateTimeKind.Utc);
            }
            else
            {
                date = DateTime.SpecifyKind(date, DateTimeKind.Utc);
            }

            var tagsList = ExtractTagsFromRawJson(root);
            string groupName = GetStringProp(root, "group", "Group", "groupName", "GroupName") ?? "";
            string description = GetStringProp(root, "description", "Description") ?? "";

            var linesList = new List<ExpenseSlipLineEntity>();
            if (TryGetPropCaseInsensitive(root, "lines", out var linesProp) && linesProp.ValueKind == JsonValueKind.Array)
            {
                int order = 0;
                foreach (var l in linesProp.EnumerateArray())
                {
                    decimal amount = GetDecimalProp(l, "amount", "Amount", "total", "Total", "actionTotal", "ActionTotal");
                    Guid? expId = Guid.TryParse(GetStringProp(l, "expenseId", "ExpenseId"), out var eG) ? eG : null;
                    Guid? lineCurId = Guid.TryParse(GetStringProp(l, "currencyId", "CurrencyId"), out var lcG) ? lcG : curId;
                    Guid lineId = Guid.TryParse(GetStringProp(l, "id", "Id"), out var lId) && lId != Guid.Empty ? lId : Guid.NewGuid();

                    linesList.Add(new ExpenseSlipLineEntity
                    {
                        Id = lineId,
                        ExpenseSlipId = id,
                        ExpenseId = expId,
                        Amount = amount,
                        CurrencyId = lineCurId,
                        SortOrder = order++
                    });
                }
            }

            try
            {
                if (existing == null)
                {
                    var entity = new ExpenseSlipEntity
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
                    };
                    await db.ExpenseSlips.AddAsync(entity);
                }
                else
                {
                    existing.Code = code;
                    existing.Date = date;
                    existing.UserId = userId;
                    existing.DepositoryId = depId;
                    existing.OfficeId = offId;
                    existing.DisplayCurrencyId = curId;
                    existing.UserName = GetStringProp(root, "userName", "UserName") ?? existing.UserName;
                    existing.IsCompleted = GetBoolProp(root, "isCompleted", "IsCompleted");
                    existing.IsDisabled = GetBoolProp(root, "isDisabled", "IsDisabled");
                    existing.GroupName = groupName;
                    existing.Description = description;
                    existing.Tags = tagsList.ToArray();
                    existing.UpdatedAt = DateTime.UtcNow;

                    if (existing.Lines != null && existing.Lines.Any())
                    {
                        db.ExpenseSlipLines.RemoveRange(existing.Lines);
                    }
                    existing.Lines = linesList;
                    db.ExpenseSlips.Update(existing);
                }

                await db.SaveChangesAsync();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[EXPENSE SLIP SAVED]: {id} (Code: {code})");
                Console.ResetColor();

                return Results.Ok(new { id = id.ToString(), code });
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[EXPENSE SLIP SAVE FAILED]: {ex.Message} -> {ex.InnerException?.Message}");
                Console.ResetColor();
                return Results.Problem(detail: ex.InnerException?.Message ?? ex.Message, statusCode: 500);
            }
        };

        // Регистрация роутов сохранения идентично Bills
        group.MapPost("", saveHandler);
        group.MapPut("/{id}", saveHandler);

        // 5. УДАЛЕНИЕ
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

        // 6. ФАСЕТЫ (ИДЕНТИЧНО BILLS И FUNDS TRANSFERS)
        group.MapGet("/facets", async (string? fields, MermerDbContext db, CancellationToken ct) =>
        {
            var connStr = db.Database.GetConnectionString()!;
            var facets = await FacetsHelper.GetEntityFacetsAsync(connStr, "expense_slips", fields, ct);
            return Results.Ok(facets);
        });

        // 7. ЖУРНАЛ СТАТЕЙ РАСХОДОВ С ЛИМИТОМ
        // 7.1. БЫСТРЫЙ ПОДСЧЕТ ДЛЯ ПЛИТОК ДАТ (ИЗБАВЛЯЕТ ОТ 404 И ЗАВИСАНИЯ)
        routes.MapGet("/api/spending/actions/count", async (DateTime? from, DateTime? till, string? expenseId, HttpRequest req, MermerDbContext db, CancellationToken ct) =>
        {
            var startDate = from.HasValue ? from.Value.ToUniversalTime() : DateTime.SpecifyKind(new DateTime(2000, 1, 1), DateTimeKind.Utc);
            var endDate = till.HasValue ? till.Value.ToUniversalTime() : DateTime.SpecifyKind(new DateTime(2099, 12, 31), DateTimeKind.Utc);

            var rawDeps = req.Query["depositoryId"].ToArray();
            var depGuids = rawDeps
                .SelectMany(x => (x ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                .Select(x => Guid.TryParse(x.Trim(), out var g) ? (Guid?)g : null)
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .Distinct()
                .ToList();

            Guid? filterExpenseGuid = Guid.TryParse(expenseId, out var eGuid) ? eGuid : null;

            var query = db.ExpenseSlips
                .AsNoTracking()
                .Where(s => s.Date >= startDate && s.Date <= endDate && !s.IsDisabled);

            if (depGuids.Any())
                query = query.Where(s => s.DepositoryId.HasValue && depGuids.Contains(s.DepositoryId.Value));

            if (filterExpenseGuid.HasValue)
            {
                int countWithFilter = await query
                    .SelectMany(s => s.Lines)
                    .Where(l => l.ExpenseId == filterExpenseGuid.Value)
                    .CountAsync(ct);
                return Results.Ok(new { count = countWithFilter });
            }

            int countAll = await query.SelectMany(s => s.Lines).CountAsync(ct);
            return Results.Ok(new { count = countAll });
        }).WithTags("SpendingActions");

        // 7.2. ЖУРНАЛ СТАТЕЙ РАСХОДОВ
        routes.MapGet("/api/spending/actions", async (DateTime? from, DateTime? till, string? expenseId, int? limit, HttpRequest req, MermerDbContext db, CancellationToken ct) =>
        {
            int take = limit.HasValue ? Math.Clamp(limit.Value, 1, 10000) : 5000;
            var startDate = from.HasValue ? from.Value.ToUniversalTime() : DateTime.SpecifyKind(new DateTime(2000, 1, 1), DateTimeKind.Utc);
            var endDate = till.HasValue ? till.Value.ToUniversalTime() : DateTime.SpecifyKind(new DateTime(2099, 12, 31), DateTimeKind.Utc);

            var rawDeps = req.Query["depositoryId"].ToArray();
            var depGuids = rawDeps
                .SelectMany(x => (x ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                .Select(x => Guid.TryParse(x.Trim(), out var g) ? (Guid?)g : null)
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .Distinct()
                .ToList();

            Guid? filterExpenseGuid = Guid.TryParse(expenseId, out var eGuid) ? eGuid : null;

            var query = db.ExpenseSlips
                .Include(s => s.Lines)
                .AsNoTracking()
                .Where(s => s.Date >= startDate && s.Date <= endDate && !s.IsDisabled);

            if (depGuids.Any())
                query = query.Where(s => s.DepositoryId.HasValue && depGuids.Contains(s.DepositoryId.Value));

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
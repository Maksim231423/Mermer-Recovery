using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Mermer.Data.Postgres;
using Mermer.Data.Postgres.Entities;

namespace Mermer.Api.Endpoints;

public static class FinanceEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        PropertyNameCaseInsensitive = true
    };

    public static void MapFinanceEndpoints(this IEndpointRouteBuilder routes)
    {
        // =========================================================================
        // 1. СПИСОК ДЛЯ COMMERCE (BILLS) С ПАГИНАЦИЕЙ
        // =========================================================================
        Func<DateTime?, DateTime?, string?, string?, int?, int?, MermerDbContext, CancellationToken, Task<IResult>> getBillsHandler =
            async (from, till, depositoryId, partnerId, limit, offset, db, ct) =>
            {
                int take = limit.HasValue ? Math.Clamp(limit.Value, 1, 50000) : 50000;
                int skip = offset.GetValueOrDefault(0);

                var query = db.FundsSlips
                    .AsNoTracking()
                    .Where(s => !s.IsDisabled);

                if (from.HasValue && from.Value.Year > 2000)
                {
                    var startDate = DateTime.SpecifyKind(from.Value, DateTimeKind.Utc);
                    query = query.Where(s => s.Date >= startDate);
                }

                if (till.HasValue && till.Value.Year < 2099)
                {
                    DateTime endDt;
                    if (till.Value.TimeOfDay == TimeSpan.Zero)
                    {
                        if (from.HasValue && till.Value.Date > from.Value.Date)
                            endDt = till.Value.AddTicks(-1);
                        else
                            endDt = till.Value.Date.AddDays(1).AddTicks(-1);
                    }
                    else
                    {
                        endDt = till.Value;
                    }
                    var endDate = DateTime.SpecifyKind(endDt, DateTimeKind.Utc);
                    query = query.Where(s => s.Date <= endDate);
                }

                query = query.Where(s => s.FundsSlipType != null &&
                                         (s.FundsSlipType.ToLower() == "payment" ||
                                          s.FundsSlipType.ToLower() == "collection" ||
                                          s.FundsSlipType.ToLower() == "expense"));

                if (Guid.TryParse(depositoryId, out var depGuid))
                    query = query.Where(s => s.DepositoryId == depGuid);

                if (Guid.TryParse(partnerId, out var partGuid))
                    query = query.Where(s => s.PartnerId == partGuid);

                // Загружаем полные сущности со строками Lines без урезания через промежуточный Select
                var rawSlips = await query
                    .OrderByDescending(s => s.Date)
                    .Skip(skip)
                    .Take(take)
                    .Include(s => s.Lines)
                    .AsSplitQuery()
                    .ToListAsync(ct);

                var defaultCurrency = await db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => c.IsDefault && !c.IsDisabled, ct)
                                      ?? await db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => !c.IsDisabled, ct);
                var defaultCurrencyId = defaultCurrency?.Id.ToString();

                // Загружаем актуальные курсы валют
                var allConvertions = await GetCurrencyConvertionsAsync(db, DateTime.UtcNow, ct);

                var result = rawSlips.Select(s =>
                {
                    bool isPayment = !string.IsNullOrEmpty(s.FundsSlipType) &&
                        (s.FundsSlipType.Equals("Payment", StringComparison.OrdinalIgnoreCase) ||
                         s.FundsSlipType.Equals("Expense", StringComparison.OrdinalIgnoreCase));

                    string billTypeStr = isPayment ? "Payment" : "Collection";
                    int billTypeInt = isPayment ? 1 : 0;
                    var docCurrencyId = s.DisplayCurrencyId?.ToString() ?? defaultCurrencyId;
                    decimal totalAmount = s.Lines != null && s.Lines.Any() ? s.Lines.Sum(l => l.Amount) : 0m;

                    return new
                    {
                        Id = s.Id.ToString(),
                        Code = s.Code ?? string.Empty,
                        Date = s.Date.ToString("yyyy-MM-ddTHH:mm:ss"),
                        FundsSlipType = billTypeStr,
                        SlipType = billTypeStr,
                        BillType = billTypeInt,
                        Type = billTypeStr,
                        OfficeId = s.OfficeId?.ToString(),
                        DepositoryId = s.DepositoryId?.ToString(),
                        PartnerId = s.PartnerId?.ToString(),
                        DisplayCurrencyId = docCurrencyId,
                        CurrencyId = docCurrencyId,
                        CurrencyConvertions = allConvertions,
                        UserName = s.UserName,
                        IsCompleted = s.IsCompleted,
                        IsDisabled = s.IsDisabled,
                        Group = s.Group ?? string.Empty,
                        Tags = s.Tags != null ? s.Tags.ToList() : new List<string>(),
                        Description = s.Description ?? string.Empty,
                        Total = totalAmount,
                        DisplayTotal = totalAmount,
                        ActionTotal = totalAmount,
                        Amount = totalAmount,
                        Lines = s.Lines != null && s.Lines.Any()
                            ? s.Lines.Select(l => (object)new
                            {
                                Id = l.Id.ToString(),
                                FundsSlipId = s.Id.ToString(),
                                Amount = l.Amount,
                                Total = l.Amount,
                                ActionTotal = l.Amount,
                                CurrencyId = l.CurrencyId?.ToString() ?? docCurrencyId,
                                SortOrder = l.SortOrder
                            }).ToArray()
                            : Array.Empty<object>()
                    };
                });

                return Results.Json(result, JsonOptions);
            };

        // =========================================================================
        // 2. СПИСОК ДЛЯ FINANCE (FUNDS SLIPS) С ПАГИНАЦИЕЙ
        // =========================================================================
        Func<DateTime?, DateTime?, string?, string?, int?, int?, MermerDbContext, CancellationToken, Task<IResult>> getFundsSlipsHandler =
            async (from, till, depositoryId, partnerId, limit, offset, db, ct) =>
            {
                int take = limit.HasValue ? Math.Clamp(limit.Value, 1, 1000) : 200;
                int skip = offset.GetValueOrDefault(0);

                var startDate = from.HasValue
                    ? DateTime.SpecifyKind(from.Value, DateTimeKind.Utc)
                    : DateTime.SpecifyKind(new DateTime(2000, 1, 1), DateTimeKind.Utc);

                DateTime endDate;
                if (till.HasValue && till.Value.Year < 2099)
                {
                    DateTime tDt;
                    if (till.Value.TimeOfDay == TimeSpan.Zero)
                    {
                        // Если till больше from (клиент уже передал полночь следующего дня) — не накидываем лишние сутки
                        if (from.HasValue && till.Value.Date > from.Value.Date)
                            tDt = till.Value.AddTicks(-1);
                        else
                            tDt = till.Value.Date.AddDays(1).AddTicks(-1);
                    }
                    else
                    {
                        tDt = till.Value;
                    }

                    endDate = DateTime.SpecifyKind(tDt, DateTimeKind.Utc);
                }
                else
                {
                    endDate = DateTime.SpecifyKind(new DateTime(2099, 12, 31, 23, 59, 59), DateTimeKind.Utc);
                }

                var defaultCurrency = await db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => c.IsDefault, ct)
                                      ?? await db.Currencies.AsNoTracking().FirstOrDefaultAsync(ct);
                var defaultCurrencyId = defaultCurrency?.Id.ToString();

                var allConvertions = await GetCurrencyConvertionsAsync(db, DateTime.UtcNow, ct);

                var query = db.FundsSlips
                    .AsNoTracking()
                    .Where(s => s.Date >= startDate && s.Date <= endDate);

                query = query.Where(s => s.FundsSlipType != null &&
                                         (s.FundsSlipType.ToLower().Contains("opening") ||
                                          s.FundsSlipType.ToLower().Contains("revision")));

                if (Guid.TryParse(depositoryId, out var depGuid))
                    query = query.Where(s => s.DepositoryId == depGuid);

                if (Guid.TryParse(partnerId, out var partGuid))
                    query = query.Where(s => s.PartnerId == partGuid);

                var slips = await query
                    .OrderByDescending(s => s.Date)
                    .Skip(skip)
                    .Take(take)
                    .Include(s => s.Lines)
                    .AsSplitQuery()
                    .ToListAsync(ct);

                var result = slips.Select(s =>
                {
                    string fundsType = "FundsOpening";
                    if (!string.IsNullOrEmpty(s.FundsSlipType))
                    {
                        if (s.FundsSlipType.Equals("FundsRevisionExceed", StringComparison.OrdinalIgnoreCase)) fundsType = "FundsRevisionExceed";
                        else if (s.FundsSlipType.Equals("FundsRevisionDeficit", StringComparison.OrdinalIgnoreCase)) fundsType = "FundsRevisionDeficit";
                    }

                    var docCurrencyId = s.DisplayCurrencyId?.ToString() ?? defaultCurrencyId;
                    decimal totalAmount = s.Lines != null && s.Lines.Any() ? s.Lines.Sum(l => l.Amount) : 0m;

                    return new
                    {
                        Id = s.Id.ToString(),
                        Code = s.Code ?? string.Empty,
                        Date = s.Date.ToString("yyyy-MM-ddTHH:mm:ss"),
                        FundsSlipType = fundsType,
                        SlipType = fundsType,
                        BillType = fundsType,
                        Type = fundsType,
                        OfficeId = s.OfficeId?.ToString(),
                        DepositoryId = s.DepositoryId?.ToString(),
                        PartnerId = s.PartnerId?.ToString(),
                        DisplayCurrencyId = docCurrencyId,
                        CurrencyId = docCurrencyId,
                        CurrencyConvertions = allConvertions,
                        UserName = s.UserName,
                        IsCompleted = s.IsCompleted,
                        IsDisabled = s.IsDisabled,
                        Group = s.Group ?? string.Empty,
                        Tags = s.Tags != null ? s.Tags.ToList() : new List<string>(),
                        Description = s.Description ?? string.Empty,
                        Total = totalAmount,
                        DisplayTotal = totalAmount,
                        ActionTotal = totalAmount,
                        Amount = totalAmount,
                        Lines = s.Lines != null && s.Lines.Any()
                            ? s.Lines.Select(l => (object)new
                            {
                                Id = l.Id.ToString(),
                                FundsSlipId = s.Id.ToString(),
                                Amount = l.Amount,
                                Total = l.Amount,
                                ActionTotal = l.Amount,
                                CurrencyId = l.CurrencyId?.ToString() ?? docCurrencyId,
                                SortOrder = l.SortOrder
                            })
                            : Array.Empty<object>()
                    };
                });

                return Results.Ok(result);
            };

        // =========================================================================
        // 3. СОХРАНЕНИЕ FUNDS SLIPS (POST / PUT)
        // =========================================================================
        Func<HttpRequest, MermerDbContext, Task<IResult>> saveSlipHandler = async (request, db) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync();
            if (string.IsNullOrEmpty(body)) return Results.BadRequest("Empty body");

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            string? idStr = GetStringProperty(root, "id", "Id");
            Guid slipId = Guid.TryParse(idStr, out var parsedGuid) && parsedGuid != Guid.Empty ? parsedGuid : Guid.NewGuid();

            var existing = await db.FundsSlips.Include(s => s.Lines).FirstOrDefaultAsync(s => s.Id == slipId);

            // Фиксируем исходные валюты существующих строк
            var existingLinesMap = existing?.Lines?
                .Where(l => l.Id != Guid.Empty)
                .ToDictionary(l => l.Id) ?? new Dictionary<Guid, FundsSlipLineEntity>();

            string code = GetStringProperty(root, "code", "Code") ?? $"DOC-{DateTime.UtcNow:yyMMddHHmmss}";
            string? depIdStr = GetStringProperty(root, "depositoryId", "DepositoryId");
            Guid? depId = Guid.TryParse(depIdStr, out var parsedDep) && parsedDep != Guid.Empty ? parsedDep : null;

            if (depId == null)
            {
                var defDep = await db.Depositories.AsNoTracking().FirstOrDefaultAsync(d => !d.IsDisabled);
                depId = defDep?.Id;
            }

            string? partIdStr = GetStringProperty(root, "partnerId", "PartnerId");
            Guid? partId = Guid.TryParse(partIdStr, out var parsedPart) && parsedPart != Guid.Empty ? parsedPart : null;

            string? offIdStr = GetStringProperty(root, "officeId", "OfficeId");
            Guid? offId = Guid.TryParse(offIdStr, out var parsedOff) && parsedOff != Guid.Empty ? parsedOff : null;

            if (offId == null)
            {
                if (depId.HasValue)
                {
                    var depObj = await db.Depositories.AsNoTracking().FirstOrDefaultAsync(d => d.Id == depId.Value);
                    offId = depObj?.OfficeId;
                }

                if (offId == null)
                {
                    var defOff = await db.Offices.AsNoTracking().FirstOrDefaultAsync(o => !o.IsDisabled);
                    offId = defOff?.Id;
                }
            }

            string? dispCurStr = GetStringProperty(root, "displayCurrencyId", "DisplayCurrencyId", "currencyId", "CurrencyId");
            Guid? dispCurId = Guid.TryParse(dispCurStr, out var parsedDispCur) ? parsedDispCur : null;

            if (dispCurId == null)
            {
                var defCur = await db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => c.IsDefault)
                             ?? await db.Currencies.AsNoTracking().FirstOrDefaultAsync();
                dispCurId = defCur?.Id;
            }

            DateTime date = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Utc);
            string? dateStr = GetStringProperty(root, "date", "Date");
            if (!string.IsNullOrEmpty(dateStr) && DateTime.TryParse(dateStr, out var parsedDate))
            {
                date = DateTime.SpecifyKind(parsedDate, DateTimeKind.Utc);
            }

            string rawType = GetStringProperty(root, "fundsSlipType", "FundsSlipType", "billType", "BillType", "slipType", "SlipType", "type", "Type") ?? "FundsOpening";

            var linesList = new List<FundsSlipLineEntity>();
            if (TryGetPropertyCaseInsensitive(root, "lines", out var linesProp) && linesProp.ValueKind == JsonValueKind.Array)
            {
                int sortOrder = 0;
                foreach (var lineJson in linesProp.EnumerateArray())
                {
                    string? lineIdStr = GetStringProperty(lineJson, "id", "Id");
                    Guid lineGuid = Guid.TryParse(lineIdStr, out var lG) && lG != Guid.Empty ? lG : Guid.NewGuid();

                    decimal lineAmount = GetDecimalProperty(lineJson, "amount", "Amount", "total", "Total", "value", "Value");
                    string? curIdStr = GetStringProperty(lineJson, "currencyId", "CurrencyId");
                    Guid? currencyGuid = Guid.TryParse(curIdStr, out var cG) && cG != Guid.Empty ? cG : dispCurId;

                    // Если строка уже была в документе — сохраняем её оригинальную валюту
                    if (existingLinesMap.TryGetValue(lineGuid, out var oldLine) && oldLine.CurrencyId.HasValue)
                    {
                        currencyGuid = oldLine.CurrencyId;
                    }

                    linesList.Add(new FundsSlipLineEntity
                    {
                        Id = lineGuid,
                        FundsSlipId = slipId,
                        Amount = lineAmount,
                        CurrencyId = currencyGuid,
                        SortOrder = sortOrder++
                    });
                }
            }

            if (!linesList.Any())
            {
                decimal rootTotal = GetDecimalProperty(root, "actionTotal", "ActionTotal", "total", "Total", "displayTotal", "DisplayTotal", "amount", "Amount");
                if (rootTotal > 0)
                {
                    linesList.Add(new FundsSlipLineEntity
                    {
                        Id = Guid.NewGuid(),
                        FundsSlipId = slipId,
                        Amount = rootTotal,
                        CurrencyId = dispCurId,
                        SortOrder = 0
                    });
                }
            }

            var tagsList = ExtractTagsFromRawJson(root);

            if (existing == null)
            {
                var entity = new FundsSlipEntity
                {
                    Id = slipId,
                    Code = code,
                    Date = date,
                    FundsSlipType = rawType,
                    DepositoryId = depId,
                    PartnerId = partId,
                    OfficeId = offId,
                    DisplayCurrencyId = dispCurId,
                    IsCompleted = GetBoolProperty(root, "isCompleted", "IsCompleted"),
                    IsDisabled = GetBoolProperty(root, "isDisabled", "IsDisabled"),
                    UserName = GetStringProperty(root, "userName", "UserName") ?? "admin",
                    Group = GetStringProperty(root, "group", "Group") ?? "",
                    Description = GetStringProperty(root, "description", "Description") ?? "",
                    Tags = tagsList.ToArray(),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    Lines = linesList
                };
                await db.FundsSlips.AddAsync(entity);
            }
            else
            {
                existing.Code = code;
                existing.FundsSlipType = rawType;
                existing.DepositoryId = depId;
                existing.PartnerId = partId;
                existing.OfficeId = offId;
                // Не перезаписываем DisplayCurrencyId
                existing.IsCompleted = GetBoolProperty(root, "isCompleted", "IsCompleted");
                existing.IsDisabled = GetBoolProperty(root, "isDisabled", "IsDisabled");
                existing.Group = GetStringProperty(root, "group", "Group") ?? "";
                existing.Tags = tagsList.ToArray();
                existing.Description = GetStringProperty(root, "description", "Description") ?? "";
                existing.UpdatedAt = DateTime.UtcNow;

                if (existing.Lines != null) db.FundsSlipLines.RemoveRange(existing.Lines);
                existing.Lines = linesList;
                db.FundsSlips.Update(existing);
            }

            await db.SaveChangesAsync();
            return Results.Content($"{{\"id\":\"{slipId}\",\"code\":\"{code}\"}}", "application/json");
        };

        // =========================================================================
        // 4. РОУТЫ FINANCE
        // =========================================================================
        var financeGroup = routes.MapGroup("/api/finance").WithTags("Finance");
        financeGroup.MapGet("/slips", (DateTime? from, DateTime? till, string? depositoryId, string? partnerId, int? limit, int? offset, MermerDbContext db, CancellationToken ct) =>
            getFundsSlipsHandler(from, till, depositoryId, partnerId, limit, offset, db, ct));
        financeGroup.MapPost("/slips", saveSlipHandler);
        financeGroup.MapPut("/slips/{id}", saveSlipHandler);

        financeGroup.MapGet("/slips/count", async (DateTime? from, DateTime? till, string? depositoryId, MermerDbContext db, CancellationToken ct) =>
        {
            var query = db.FundsSlips.AsNoTracking().Where(s => !s.IsDisabled);

            query = query.Where(s => s.FundsSlipType != null &&
                                     (s.FundsSlipType.ToLower().Contains("opening") ||
                                      s.FundsSlipType.ToLower().Contains("revision")));

            if (from.HasValue && from.Value.Year > 2000)
            {
                var fUtc = DateTime.SpecifyKind(from.Value, DateTimeKind.Utc);
                query = query.Where(s => s.Date >= fUtc);
            }

            if (till.HasValue && till.Value.Year < 2099)
            {
                DateTime tDt;
                if (till.Value.TimeOfDay == TimeSpan.Zero)
                {
                    // Если from и till одинаковые (один день) — берем до конца этого дня.
                    // Если till больше from (клиент уже передал полночь следующего дня) — не прибавляем лишний день!
                    if (from.HasValue && till.Value.Date > from.Value.Date)
                        tDt = till.Value.AddTicks(-1);
                    else
                        tDt = till.Value.Date.AddDays(1).AddTicks(-1);
                }
                else
                {
                    tDt = till.Value;
                }

                var tUtc = DateTime.SpecifyKind(tDt, DateTimeKind.Utc);
                query = query.Where(s => s.Date <= tUtc);
            }

            if (Guid.TryParse(depositoryId, out var depGuid))
            {
                query = query.Where(s => s.DepositoryId == depGuid);
            }

            var count = await query.CountAsync(ct);
            return Results.Ok(new { count });
        });

        financeGroup.MapGet("/slips/facets", async (string? fields, MermerDbContext db, CancellationToken ct) =>
        {
            var today = DateTime.Today; // 18.11.2026
            var todayStart = DateTime.SpecifyKind(today, DateTimeKind.Utc);
            var tomorrowStart = todayStart.AddDays(1);
            var yesterdayStart = todayStart.AddDays(-1);

            // Начало текущей недели (понедельник)
            int diff = (7 + (today.DayOfWeek - DayOfWeek.Monday)) % 7;
            var weekStart = DateTime.SpecifyKind(today.AddDays(-1 * diff), DateTimeKind.Utc);
            var pastWeekStart = weekStart.AddDays(-7);

            var monthStart = DateTime.SpecifyKind(new DateTime(today.Year, today.Month, 1), DateTimeKind.Utc);
            var pastMonthStart = monthStart.AddMonths(-1);

            var baseQuery = db.FundsSlips.AsNoTracking().Where(s => !s.IsDisabled && s.FundsSlipType != null &&
                (s.FundsSlipType.ToLower().Contains("opening") || s.FundsSlipType.ToLower().Contains("revision")));

            // Считаем строго каждый интервал без наложений
            var countToday = await baseQuery.CountAsync(s => s.Date >= todayStart && s.Date < tomorrowStart, ct);
            var countYesterday = await baseQuery.CountAsync(s => s.Date >= yesterdayStart && s.Date < todayStart, ct);
            var countWeek = await baseQuery.CountAsync(s => s.Date >= weekStart && s.Date < tomorrowStart, ct);
            var countPastWeek = await baseQuery.CountAsync(s => s.Date >= pastWeekStart && s.Date < weekStart, ct);
            var countMonth = await baseQuery.CountAsync(s => s.Date >= monthStart && s.Date < tomorrowStart, ct);
            var countPastMonth = await baseQuery.CountAsync(s => s.Date >= pastMonthStart && s.Date < monthStart, ct);
            var countAll = await baseQuery.CountAsync(ct);

            var (groups, tags) = await GetGlobalGroupsAndTagsAsync(db, ct);

            return Results.Ok(new Dictionary<string, object>
            {
                ["Group"] = groups,
                ["GroupNames"] = groups,
                ["GroupName"] = groups,
                ["Tags"] = tags,
                ["TagNames"] = tags,
                ["Tag"] = tags,
                ["Date"] = new Dictionary<string, int>
                {
                    { "#Today", countToday },
                    { "#Yesturday", countYesterday }, // Ключ с буквой 'u' для совместимости с WPF-клиентом
                    { "#Yesterday", countYesterday },
                    { "#This Week", countWeek },
                    { "#Past Week", countPastWeek },
                    { "#This Month", countMonth },
                    { "#Past Month", countPastMonth },
                    { "#All Records", countAll }
                }
            });
        });

        financeGroup.MapGet("/slips/{id}", async (string id, MermerDbContext db, CancellationToken ct) =>
        {
            if (!Guid.TryParse(id, out var guid)) return Results.NotFound();
            var s = await db.FundsSlips.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == guid, ct);
            if (s == null) return Results.NotFound();

            var convertions = await GetCurrencyConvertionsAsync(db, s.Date, ct);
            string fundsType = "FundsOpening";
            if (!string.IsNullOrEmpty(s.FundsSlipType))
            {
                if (s.FundsSlipType.Equals("FundsRevisionExceed", StringComparison.OrdinalIgnoreCase)) fundsType = "FundsRevisionExceed";
                else if (s.FundsSlipType.Equals("FundsRevisionDeficit", StringComparison.OrdinalIgnoreCase)) fundsType = "FundsRevisionDeficit";
            }

            decimal totalAmount = s.Lines != null && s.Lines.Any() ? s.Lines.Sum(l => l.Amount) : 0m;
            var docCurrencyId = s.DisplayCurrencyId?.ToString() ?? (await db.Currencies.FirstOrDefaultAsync(c => c.IsDefault))?.Id.ToString();

            return Results.Ok(new
            {
                Id = s.Id.ToString(),
                Code = s.Code ?? string.Empty,
                Date = s.Date.ToString("yyyy-MM-ddTHH:mm:ss"),
                FundsSlipType = fundsType,
                SlipType = fundsType,
                BillType = fundsType,
                Type = fundsType,
                OfficeId = s.OfficeId?.ToString(),
                DepositoryId = s.DepositoryId?.ToString(),
                PartnerId = s.PartnerId?.ToString(),
                DisplayCurrencyId = docCurrencyId,
                CurrencyId = docCurrencyId,
                CurrencyConvertions = convertions,
                UserName = s.UserName,
                IsCompleted = s.IsCompleted,
                IsDisabled = s.IsDisabled,
                Group = s.Group ?? string.Empty,
                Tags = s.Tags != null ? s.Tags.ToList() : new List<string>(),
                Description = s.Description ?? string.Empty,
                Total = totalAmount,
                ActionTotal = totalAmount,
                DisplayTotal = totalAmount,
                Amount = totalAmount,
                Lines = s.Lines != null ? s.Lines.Select(l => new
                {
                    Id = l.Id.ToString(),
                    FundsSlipId = s.Id.ToString(),
                    Amount = l.Amount,
                    Total = l.Amount,
                    ActionTotal = l.Amount,
                    CurrencyId = l.CurrencyId?.ToString() ?? docCurrencyId,
                    SortOrder = l.SortOrder
                }) : null
            });
        });

        // =========================================================================
        // 5. РОУТЫ BILLS
        // =========================================================================
        var billsGroup = routes.MapGroup("/api/bills").WithTags("Bills");
        billsGroup.MapGet("", (DateTime? from, DateTime? till, string? depositoryId, string? partnerId, int? limit, int? offset, MermerDbContext db, CancellationToken ct) =>
            getBillsHandler(from, till, depositoryId, partnerId, limit, offset, db, ct));
        billsGroup.MapPost("", saveSlipHandler);
        billsGroup.MapPut("/{id}", saveSlipHandler);

        billsGroup.MapGet("/count", async (DateTime? from, DateTime? till, string? depositoryId, string? partnerId, MermerDbContext db, CancellationToken ct) =>
        {
            // Убираем !s.IsDisabled, чтобы удаленные записи учитывались так же, как в общем списке
            var query = db.FundsSlips.AsNoTracking();

            query = query.Where(s => s.FundsSlipType != null &&
                                     (s.FundsSlipType.ToLower() == "payment" ||
                                      s.FundsSlipType.ToLower() == "collection" ||
                                      s.FundsSlipType.ToLower() == "expense"));

            if (from.HasValue && from.Value.Year > 2000)
            {
                var fUtc = DateTime.SpecifyKind(from.Value, DateTimeKind.Utc);
                query = query.Where(s => s.Date >= fUtc);
            }

            if (till.HasValue && till.Value.Year < 2099)
            {
                DateTime tDt;
                if (till.Value.TimeOfDay == TimeSpan.Zero)
                {
                    // Если till больше from (клиент уже передал полночь следующего дня) — не накидываем лишние сутки!
                    if (from.HasValue && till.Value.Date > from.Value.Date)
                        tDt = till.Value.AddTicks(-1);
                    else
                        tDt = till.Value.Date.AddDays(1).AddTicks(-1);
                }
                else
                {
                    tDt = till.Value;
                }

                var tUtc = DateTime.SpecifyKind(tDt, DateTimeKind.Utc);
                query = query.Where(s => s.Date <= tUtc);
            }

            if (Guid.TryParse(depositoryId, out var depGuid))
                query = query.Where(s => s.DepositoryId == depGuid);

            if (Guid.TryParse(partnerId, out var partGuid))
                query = query.Where(s => s.PartnerId == partGuid);

            var count = await query.CountAsync(ct);
            return Results.Ok(new { count });
        });

        billsGroup.MapGet("/next-code", async (MermerDbContext db) =>
        {
            var count = await db.FundsSlips.CountAsync();
            return Results.Ok(new { code = $"DOC-{DateTime.UtcNow:yyMMdd}{(count + 1):D4}" });
        });

        billsGroup.MapGet("/facets", async (string? fields, MermerDbContext db, CancellationToken ct) =>
        {
            var today = DateTime.Today;
            var todayStart = DateTime.SpecifyKind(today, DateTimeKind.Utc);
            var tomorrowStart = todayStart.AddDays(1);
            var yesterdayStart = todayStart.AddDays(-1);

            // Начало текущей недели (понедельник)
            int diff = (7 + (today.DayOfWeek - DayOfWeek.Monday)) % 7;
            var weekStart = DateTime.SpecifyKind(today.AddDays(-1 * diff), DateTimeKind.Utc);
            var pastWeekStart = weekStart.AddDays(-7);

            var monthStart = DateTime.SpecifyKind(new DateTime(today.Year, today.Month, 1), DateTimeKind.Utc);
            var pastMonthStart = monthStart.AddMonths(-1);

            // Базовый запрос для ордеров оплат/поступлений (Bills)
            // Убираем !s.IsDisabled, чтобы счетчики соответствовали отображению таблицы
            var baseQuery = db.FundsSlips.AsNoTracking().Where(s => s.FundsSlipType != null &&
                (s.FundsSlipType.ToLower() == "payment" ||
                 s.FundsSlipType.ToLower() == "collection" ||
                 s.FundsSlipType.ToLower() == "expense"));

            // Считаем строго каждый интервал без наложений
            var countToday = await baseQuery.CountAsync(s => s.Date >= todayStart && s.Date < tomorrowStart, ct);
            var countYesterday = await baseQuery.CountAsync(s => s.Date >= yesterdayStart && s.Date < todayStart, ct);
            var countWeek = await baseQuery.CountAsync(s => s.Date >= weekStart && s.Date < tomorrowStart, ct);
            var countPastWeek = await baseQuery.CountAsync(s => s.Date >= pastWeekStart && s.Date < weekStart, ct);
            var countMonth = await baseQuery.CountAsync(s => s.Date >= monthStart && s.Date < tomorrowStart, ct);
            var countPastMonth = await baseQuery.CountAsync(s => s.Date >= pastMonthStart && s.Date < monthStart, ct);
            var countAll = await baseQuery.CountAsync(ct);

            var (groups, tags) = await GetGlobalGroupsAndTagsAsync(db, ct);

            return Results.Ok(new Dictionary<string, object>
            {
                ["Group"] = groups,
                ["GroupNames"] = groups,
                ["GroupName"] = groups,
                ["Tags"] = tags,
                ["TagNames"] = tags,
                ["Tag"] = tags,
                ["Date"] = new Dictionary<string, int>
        {
            { "#Today", countToday },
            { "#Yesturday", countYesterday }, // для совместимости со старыми ресурсами UI
            { "#Yesterday", countYesterday },
            { "#This Week", countWeek },
            { "#Past Week", countPastWeek },
            { "#This Month", countMonth },
            { "#Past Month", countPastMonth },
            { "#All Records", countAll }
        }
            });
        });

        billsGroup.MapGet("/{id}", async (string id, MermerDbContext db, CancellationToken ct) =>
        {
            if (!Guid.TryParse(id, out var guid)) return Results.NotFound();
            var s = await db.FundsSlips.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == guid, ct);
            if (s == null) return Results.NotFound();

            var convertions = await GetCurrencyConvertionsAsync(db, s.Date, ct);
            string billType = "Collection";
            if (!string.IsNullOrEmpty(s.FundsSlipType) && (s.FundsSlipType.Equals("Payment", StringComparison.OrdinalIgnoreCase) || s.FundsSlipType.Equals("Expense", StringComparison.OrdinalIgnoreCase)))
                billType = "Payment";

            decimal totalAmount = s.Lines != null && s.Lines.Any() ? s.Lines.Sum(l => l.Amount) : 0m;
            var docCurrencyId = s.DisplayCurrencyId?.ToString() ?? (await db.Currencies.FirstOrDefaultAsync(c => c.IsDefault))?.Id.ToString();

            return Results.Ok(new
            {
                Id = s.Id.ToString(),
                Code = s.Code ?? string.Empty,
                Date = s.Date.ToString("yyyy-MM-ddTHH:mm:ss"),
                FundsSlipType = billType,
                SlipType = billType,
                BillType = billType,
                Type = billType,
                OfficeId = s.OfficeId?.ToString(),
                DepositoryId = s.DepositoryId?.ToString(),
                PartnerId = s.PartnerId?.ToString(),
                DisplayCurrencyId = docCurrencyId,
                CurrencyId = docCurrencyId,
                CurrencyConvertions = convertions,
                UserName = s.UserName,
                IsCompleted = s.IsCompleted,
                IsDisabled = s.IsDisabled,
                Group = s.Group ?? string.Empty,
                Tags = s.Tags != null ? s.Tags.ToList() : new List<string>(),
                Description = s.Description ?? string.Empty,
                Total = totalAmount,
                ActionTotal = totalAmount,
                DisplayTotal = totalAmount,
                Amount = totalAmount,
                Lines = s.Lines != null ? s.Lines.Select(l => new
                {
                    Id = l.Id.ToString(),
                    FundsSlipId = s.Id.ToString(),
                    Amount = l.Amount,
                    Total = l.Amount,
                    ActionTotal = l.Amount,
                    CurrencyId = l.CurrencyId?.ToString() ?? docCurrencyId,
                    SortOrder = l.SortOrder
                }) : null
            });
        });

        // =========================================================================
        // 6. РОУТЫ FUNDS TRANSFERS С ПАГИНАЦИЕЙ
        // =========================================================================
        var transferGroup = routes.MapGroup("/api/finance/transfers").WithTags("FundsTransfers");

        transferGroup.MapGet("/facets", async (HttpContext ctx, MermerDbContext db, CancellationToken ct) =>
        {
            var (groups, tags) = await GetGlobalGroupsAndTagsAsync(db, ct);

            var todayLocal = DateTime.Today;
            var todayUtc = DateTime.SpecifyKind(todayLocal, DateTimeKind.Utc);
            var weekStart = DateTime.SpecifyKind(todayLocal.AddDays(-7), DateTimeKind.Utc);
            var monthStart = new DateTime(todayLocal.Year, todayLocal.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var yearStart = new DateTime(todayLocal.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            // Подсчет переводов с любым расхождением сумм отправлено/получено (включая удаленные)
            var countConflicted = await db.FundsTransfers
                .AsNoTracking()
                .Where(t => t.Lines.Sum(l => l.Amount) != t.Lines.Sum(l => l.ReceivedAmount))
                .CountAsync(ct);

            var countToday = await db.FundsTransfers.CountAsync(t => t.Date >= todayUtc, ct);
            var countWeek = await db.FundsTransfers.CountAsync(t => t.Date >= weekStart, ct);
            var countMonth = await db.FundsTransfers.CountAsync(t => t.Date >= monthStart, ct);
            var countYear = await db.FundsTransfers.CountAsync(t => t.Date >= yearStart, ct);
            var countAll = await db.FundsTransfers.CountAsync(ct);

            return Results.Ok(new Dictionary<string, object>
            {
                ["Group"] = groups,
                ["GroupNames"] = groups,
                ["GroupName"] = groups,
                ["Tags"] = tags,
                ["TagNames"] = tags,
                ["Tag"] = tags,
                ["Date"] = new Dictionary<string, int>
        {
            { "#Conflicted", countConflicted },
            { "#Today", countToday },
            { "#This Week", countWeek },
            { "#This Month", countMonth },
            { "#This Year", countYear },
            { "#All Records", countAll }
        }
            });
        });

        transferGroup.MapGet("/count", async (HttpRequest req, DateTime? from, DateTime? till, string? sourceDepositoryId, string? destinationDepositoryId, MermerDbContext db, CancellationToken ct) =>
        {
            var query = db.FundsTransfers.AsNoTracking();

            if (from.HasValue && from.Value.Year > 2000)
            {
                var fUtc = DateTime.SpecifyKind(from.Value, DateTimeKind.Utc);
                query = query.Where(t => t.Date >= fUtc);
            }

            if (till.HasValue && till.Value.Year < 2099)
            {
                var tDt = till.Value.TimeOfDay == TimeSpan.Zero ? till.Value.Date.AddDays(1).AddTicks(-1) : till.Value;
                var tUtc = DateTime.SpecifyKind(tDt, DateTimeKind.Utc);
                query = query.Where(t => t.Date <= tUtc);
            }

            if (Guid.TryParse(sourceDepositoryId, out var srcGuid))
                query = query.Where(t => t.FromDepositoryId == srcGuid);

            if (Guid.TryParse(destinationDepositoryId, out var dstGuid))
                query = query.Where(t => t.ToDepositoryId == dstGuid);

            bool isConflicted = req.Query.ContainsKey("conflicted") ||
                                req.Query["conflicted"].ToString().Equals("true", StringComparison.OrdinalIgnoreCase) ||
                                (req.QueryString.Value?.Contains("conflicted", StringComparison.OrdinalIgnoreCase) ?? false) ||
                                (req.QueryString.Value?.Contains("IsConflicted", StringComparison.OrdinalIgnoreCase) ?? false);

            if (isConflicted)
            {
                var conflictedCount = await query
                    .Where(t => t.Lines.Sum(l => l.Amount) != t.Lines.Sum(l => l.ReceivedAmount))
                    .CountAsync(ct);

                return Results.Ok(new { count = conflictedCount });
            }

            var count = await query.CountAsync(ct);
            return Results.Ok(new { count });
        });

        transferGroup.MapGet("", async (HttpRequest req, DateTime? from, DateTime? till, string? sourceDepositoryId, string? destinationDepositoryId, int? limit, int? offset, MermerDbContext db, CancellationToken ct) =>
        {
            int take = limit.HasValue ? Math.Clamp(limit.Value, 1, 10000) : 1000;
            int skip = offset.GetValueOrDefault(0);

            var startDate = from.HasValue && from.Value.Year > 2000
                ? DateTime.SpecifyKind(from.Value, DateTimeKind.Utc)
                : DateTime.SpecifyKind(new DateTime(2000, 1, 1), DateTimeKind.Utc);

            DateTime endDate;
            if (till.HasValue && till.Value.Year < 2099)
            {
                DateTime tDt;
                if (till.Value.TimeOfDay == TimeSpan.Zero)
                {
                    if (from.HasValue && till.Value.Date > from.Value.Date)
                        tDt = till.Value.AddTicks(-1);
                    else
                        tDt = till.Value.Date.AddDays(1).AddTicks(-1);
                }
                else
                {
                    tDt = till.Value;
                }

                endDate = DateTime.SpecifyKind(tDt, DateTimeKind.Utc);
            }
            else
            {
                endDate = DateTime.SpecifyKind(new DateTime(2099, 12, 31, 23, 59, 59), DateTimeKind.Utc);
            }

            var defaultCurrency = await db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => c.IsDefault, ct)
                                  ?? await db.Currencies.AsNoTracking().FirstOrDefaultAsync(ct);
            var defaultCurrencyId = defaultCurrency?.Id.ToString();
            var allConvertions = await GetCurrencyConvertionsAsync(db, DateTime.UtcNow, ct);

            // ИСПРАВЛЕНИЕ: Убрали && !t.IsDisabled, чтобы удаленные записи отдавались в таблицу
            var transferQuery = db.FundsTransfers
                .AsNoTracking()
                .Where(t => t.Date >= startDate && t.Date <= endDate);

            if (Guid.TryParse(sourceDepositoryId, out var srcGuid))
                transferQuery = transferQuery.Where(t => t.FromDepositoryId == srcGuid);

            if (Guid.TryParse(destinationDepositoryId, out var dstGuid))
                transferQuery = transferQuery.Where(t => t.ToDepositoryId == dstGuid);

            bool isConflictedReq = req.Query.ContainsKey("conflicted") ||
                                   req.Query["conflicted"].ToString().Equals("true", StringComparison.OrdinalIgnoreCase) ||
                                   (req.QueryString.Value?.Contains("conflicted", StringComparison.OrdinalIgnoreCase) ?? false) ||
                                   (req.QueryString.Value?.Contains("IsConflicted", StringComparison.OrdinalIgnoreCase) ?? false);

            if (isConflictedReq)
            {
                transferQuery = transferQuery.Where(t => t.Lines.Sum(l => l.Amount) != t.Lines.Sum(l => l.ReceivedAmount));
            }

            var transfers = await transferQuery
                .OrderByDescending(t => t.Date)
                .Skip(skip)
                .Take(take)
                .Include(t => t.Lines)
                .AsSplitQuery()
                .ToListAsync(ct);

            var result = transfers.Select(t =>
            {
                decimal totalSent = t.Lines != null && t.Lines.Any() ? t.Lines.Sum(l => l.Amount) : 0m;
                decimal totalReceived = t.Lines != null && t.Lines.Any() ? t.Lines.Sum(l => l.ReceivedAmount) : 0m;
                var docCurrencyId = t.DisplayCurrencyId?.ToString() ?? defaultCurrencyId;
                bool isConflicted = totalSent != totalReceived;

                return new
                {
                    Id = t.Id.ToString(),
                    Code = t.Code ?? string.Empty,
                    Date = t.Date.ToString("yyyy-MM-ddTHH:mm:ss"),
                    Type = "FundsTransfer",
                    DepositoryId = t.FromDepositoryId?.ToString(),
                    DestinationDepositoryId = t.ToDepositoryId?.ToString(),
                    DisplayCurrencyId = docCurrencyId,
                    CurrencyId = docCurrencyId,
                    CurrencyConvertions = allConvertions,
                    UserName = t.UserName,
                    IsCompleted = t.IsCompleted,
                    IsDisabled = t.IsDisabled,
                    IsConflicted = isConflicted,
                    Group = t.Group ?? string.Empty,
                    Tags = t.Tags != null ? t.Tags.ToList() : new List<string>(),
                    Description = t.Description ?? string.Empty,
                    ActionTotal = totalSent,
                    ActionReceivedTotal = totalReceived,
                    DisplayTotal = totalSent,
                    DisplayReceivedTotal = totalReceived,
                    Lines = t.Lines != null ? t.Lines.Select(l => (object)new
                    {
                        Id = l.Id.ToString(),
                        FundsTransferId = t.Id.ToString(),
                        Amount = l.Amount,
                        ReceivedAmount = l.ReceivedAmount,
                        CurrencyId = l.CurrencyId?.ToString() ?? docCurrencyId,
                        SortOrder = l.SortOrder
                    }).ToList() : new List<object>()
                };
            });

            return Results.Ok(result);
        });

        transferGroup.MapGet("/{id:guid}", async (Guid id, MermerDbContext db, CancellationToken ct) =>
        {
            var t = await db.FundsTransfers.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t == null) return Results.NotFound();

            var convertions = await GetCurrencyConvertionsAsync(db, t.Date, ct);
            var docCurrencyId = t.DisplayCurrencyId?.ToString() ?? (await db.Currencies.FirstOrDefaultAsync(c => c.IsDefault))?.Id.ToString();
            decimal totalSent = t.Lines != null && t.Lines.Any() ? t.Lines.Sum(l => l.Amount) : 0m;
            decimal totalReceived = t.Lines != null && t.Lines.Any() ? t.Lines.Sum(l => l.ReceivedAmount) : 0m;
            bool isConflicted = totalSent != totalReceived;

            return Results.Ok(new
            {
                Id = t.Id.ToString(),
                Code = t.Code ?? string.Empty,
                Date = t.Date.ToString("yyyy-MM-ddTHH:mm:ss"),
                Type = "FundsTransfer",
                DepositoryId = t.FromDepositoryId?.ToString(),
                DestinationDepositoryId = t.ToDepositoryId?.ToString(),
                DisplayCurrencyId = docCurrencyId,
                CurrencyId = docCurrencyId,
                CurrencyConvertions = convertions,
                UserName = t.UserName,
                IsCompleted = t.IsCompleted,
                IsDisabled = t.IsDisabled,
                IsConflicted = isConflicted,
                Group = t.Group ?? string.Empty,
                Tags = t.Tags != null ? t.Tags.ToList() : new List<string>(),
                Description = t.Description ?? string.Empty,
                ActionTotal = totalSent,
                ActionReceivedTotal = totalReceived,
                DisplayTotal = totalSent,
                DisplayReceivedTotal = totalReceived,
                Lines = t.Lines != null ? t.Lines.Select(l => new
                {
                    Id = l.Id.ToString(),
                    FundsTransferId = t.Id.ToString(),
                    Amount = l.Amount,
                    ReceivedAmount = l.ReceivedAmount,
                    CurrencyId = l.CurrencyId?.ToString() ?? docCurrencyId,
                    SortOrder = l.SortOrder
                }) : null
            });
        });

        Func<HttpRequest, MermerDbContext, Task<IResult>> saveTransferHandler = async (request, db) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync();
            if (string.IsNullOrEmpty(body)) return Results.BadRequest("Empty body");

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            string? idStr = GetStringProperty(root, "id", "Id");
            Guid transferId = Guid.TryParse(idStr, out var parsedGuid) && parsedGuid != Guid.Empty ? parsedGuid : Guid.NewGuid();

            var existing = await db.FundsTransfers.Include(t => t.Lines).FirstOrDefaultAsync(t => t.Id == transferId);

            var existingLinesMap = existing?.Lines?
                .Where(l => l.Id != Guid.Empty)
                .ToDictionary(l => l.Id) ?? new Dictionary<Guid, FundsTransferLineEntity>();

            string code = GetStringProperty(root, "code", "Code") ?? $"TR-{DateTime.UtcNow:yyMMddHHmmss}";
            string? fromDepIdStr = GetStringProperty(root, "depositoryId", "DepositoryId", "fromDepositoryId", "FromDepositoryId");
            Guid? fromDepId = Guid.TryParse(fromDepIdStr, out var pFromDep) ? pFromDep : null;

            string? toDepIdStr = GetStringProperty(root, "destinationDepositoryId", "DestinationDepositoryId", "toDepositoryId", "ToDepositoryId");
            Guid? toDepId = Guid.TryParse(toDepIdStr, out var pToDep) ? pToDep : null;

            string? dispCurStr = GetStringProperty(root, "displayCurrencyId", "DisplayCurrencyId", "currencyId", "CurrencyId");
            Guid? dispCurId = Guid.TryParse(dispCurStr, out var pCur) ? pCur : null;

            DateTime date = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Utc);
            string? dateStr = GetStringProperty(root, "date", "Date");
            if (!string.IsNullOrEmpty(dateStr) && DateTime.TryParse(dateStr, out var pDate))
                date = DateTime.SpecifyKind(pDate, DateTimeKind.Utc);

            var tagsList = ExtractTagsFromRawJson(root);

            var linesList = new List<FundsTransferLineEntity>();
            if (TryGetPropertyCaseInsensitive(root, "lines", out var linesProp) && linesProp.ValueKind == JsonValueKind.Array)
            {
                int sortOrder = 0;
                foreach (var lJson in linesProp.EnumerateArray())
                {
                    string? lineIdStr = GetStringProperty(lJson, "id", "Id");
                    Guid lineGuid = Guid.TryParse(lineIdStr, out var lG) && lG != Guid.Empty ? lG : Guid.NewGuid();

                    decimal amount = GetDecimalProperty(lJson, "amount", "Amount", "total", "Total");
                    decimal receivedAmount = GetDecimalProperty(lJson, "receivedAmount", "ReceivedAmount");

                    string? curIdStr = GetStringProperty(lJson, "currencyId", "CurrencyId");
                    Guid? currencyGuid = Guid.TryParse(curIdStr, out var cG) && cG != Guid.Empty ? cG : dispCurId;

                    if (existingLinesMap.TryGetValue(lineGuid, out var oldLine) && oldLine.CurrencyId.HasValue)
                    {
                        currencyGuid = oldLine.CurrencyId;
                    }

                    linesList.Add(new FundsTransferLineEntity
                    {
                        Id = lineGuid,
                        FundsTransferId = transferId,
                        Amount = amount,
                        ReceivedAmount = receivedAmount,
                        CurrencyId = currencyGuid,
                        SortOrder = sortOrder++
                    });
                }
            }

            if (existing == null)
            {
                var entity = new FundsTransferEntity
                {
                    Id = transferId,
                    Code = code,
                    Date = date,
                    FromDepositoryId = fromDepId,
                    ToDepositoryId = toDepId,
                    DisplayCurrencyId = dispCurId,
                    IsCompleted = GetBoolProperty(root, "isCompleted", "IsCompleted"),
                    IsDisabled = GetBoolProperty(root, "isDisabled", "IsDisabled"),
                    UserName = GetStringProperty(root, "userName", "UserName") ?? "admin",
                    Group = GetStringProperty(root, "group", "Group") ?? "",
                    Tags = tagsList.ToArray(),
                    Description = GetStringProperty(root, "description", "Description") ?? "",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    Lines = linesList
                };
                await db.FundsTransfers.AddAsync(entity);
            }
            else
            {
                existing.Code = code;
                existing.FromDepositoryId = fromDepId;
                existing.ToDepositoryId = toDepId;
                // Не перезаписываем DisplayCurrencyId
                existing.IsCompleted = GetBoolProperty(root, "isCompleted", "IsCompleted");
                existing.IsDisabled = GetBoolProperty(root, "isDisabled", "IsDisabled");
                existing.Group = GetStringProperty(root, "group", "Group") ?? "";
                existing.Tags = tagsList.ToArray();
                existing.Description = GetStringProperty(root, "description", "Description") ?? "";
                existing.UpdatedAt = DateTime.UtcNow;

                if (existing.Lines != null) db.FundsTransferLines.RemoveRange(existing.Lines);
                existing.Lines = linesList;
                db.FundsTransfers.Update(existing);
            }

            await db.SaveChangesAsync();
            return Results.Content($"{{\"id\":\"{transferId}\",\"code\":\"{code}\"}}", "application/json");
        };

        transferGroup.MapPost("", saveTransferHandler);
        transferGroup.MapPut("/{id:guid}", saveTransferHandler);
        // =========================================================================
        // 7. РОУТЫ DAILY FUNDS REGISTRIES
        // =========================================================================
        var registryGroup = routes.MapGroup("/api/finance/registeries").WithTags("DailyFundsRegistries");

        registryGroup.MapGet("", async (DateTime? from, DateTime? till, string? depositoryId, int? limit, int? offset, MermerDbContext db, CancellationToken ct) =>
        {
            int take = limit.HasValue ? Math.Clamp(limit.Value, 1, 10000) : 5000;
            int skip = offset.GetValueOrDefault(0);

            var startDate = from.HasValue && from.Value.Year > 2000
                ? DateTime.SpecifyKind(from.Value, DateTimeKind.Utc)
                : DateTime.SpecifyKind(new DateTime(2000, 1, 1), DateTimeKind.Utc);

            DateTime endDate;
            if (till.HasValue && till.Value.Year < 2099)
            {
                DateTime tDt;
                if (till.Value.TimeOfDay == TimeSpan.Zero)
                {
                    if (from.HasValue && till.Value.Date > from.Value.Date)
                        tDt = till.Value.AddTicks(-1);
                    else
                        tDt = till.Value.Date.AddDays(1).AddTicks(-1);
                }
                else
                {
                    tDt = till.Value;
                }
                endDate = DateTime.SpecifyKind(tDt, DateTimeKind.Utc);
            }
            else
            {
                endDate = DateTime.SpecifyKind(new DateTime(2099, 12, 31, 23, 59, 59), DateTimeKind.Utc);
            }

            var defaultCurrency = await db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => c.IsDefault, ct)
                                  ?? await db.Currencies.AsNoTracking().FirstOrDefaultAsync(ct);
            var defaultCurrencyId = defaultCurrency?.Id.ToString();
            var allConvertions = await GetCurrencyConvertionsAsync(db, DateTime.UtcNow, ct);

            var query = db.DailyFundsRegisteries
                .AsNoTracking()
                .Where(r => !r.IsDisabled && r.Date >= startDate && r.Date <= endDate);

            if (Guid.TryParse(depositoryId, out var depGuid))
                query = query.Where(r => r.DepositoryId == depGuid);

            var list = await query
                .OrderByDescending(r => r.Date)
                .Skip(skip)
                .Take(take)
                .Include(r => r.Lines)
                .AsSplitQuery()
                .ToListAsync(ct);

            var result = list.Select(r =>
            {
                decimal total = r.Lines != null && r.Lines.Any() ? r.Lines.Sum(l => l.Amount) : 0m;
                var docCurrencyId = r.DisplayCurrencyId?.ToString() ?? defaultCurrencyId;

                return new
                {
                    Id = r.Id.ToString(),
                    Code = r.Code ?? string.Empty,
                    Date = r.Date.ToString("yyyy-MM-ddTHH:mm:ss"),
                    Type = "DailyFundsRegistery",
                    DepositoryId = r.DepositoryId?.ToString(),
                    DisplayCurrencyId = docCurrencyId,
                    CurrencyId = docCurrencyId,
                    CurrencyConvertions = allConvertions,
                    UserId = r.UserId?.ToString(),
                    UserName = r.UserName,
                    IsCompleted = r.IsCompleted,
                    IsDisabled = r.IsDisabled,
                    Group = r.GroupName ?? string.Empty,
                    Tags = r.Tags != null ? r.Tags.ToList() : new List<string>(),
                    Description = r.Description ?? string.Empty,
                    ActionTotal = total,
                    DisplayTotal = total,
                    Lines = r.Lines != null ? r.Lines.Select(l => (object)new
                    {
                        Id = l.Id.ToString(),
                        DailyFundsRegisteryId = r.Id.ToString(),
                        Amount = l.Amount,
                        CurrencyId = l.CurrencyId?.ToString() ?? docCurrencyId,
                        SortOrder = l.SortOrder
                    }).ToList() : new List<object>()
                };
            });

            return Results.Ok(result);
        });

        registryGroup.MapGet("/count", async (DateTime? from, DateTime? till, string? depositoryId, MermerDbContext db, CancellationToken ct) =>
        {
            var query = db.DailyFundsRegisteries.AsNoTracking().Where(r => !r.IsDisabled);

            if (from.HasValue && from.Value.Year > 2000)
            {
                var fUtc = DateTime.SpecifyKind(from.Value, DateTimeKind.Utc);
                query = query.Where(r => r.Date >= fUtc);
            }

            if (till.HasValue && till.Value.Year < 2099)
            {
                DateTime tDt;
                if (till.Value.TimeOfDay == TimeSpan.Zero)
                {
                    if (from.HasValue && till.Value.Date > from.Value.Date)
                        tDt = till.Value.AddTicks(-1);
                    else
                        tDt = till.Value.Date.AddDays(1).AddTicks(-1);
                }
                else
                {
                    tDt = till.Value;
                }
                var tUtc = DateTime.SpecifyKind(tDt, DateTimeKind.Utc);
                query = query.Where(r => r.Date <= tUtc);
            }

            if (Guid.TryParse(depositoryId, out var depGuid))
                query = query.Where(r => r.DepositoryId == depGuid);

            var count = await query.CountAsync(ct);
            return Results.Ok(new { count });
        });

        registryGroup.MapGet("/facets", async (HttpContext context, MermerDbContext db, CancellationToken ct) =>
        {
            var today = DateTime.Today;
            var todayStart = DateTime.SpecifyKind(today, DateTimeKind.Utc);
            var tomorrowStart = todayStart.AddDays(1);
            var yesterdayStart = todayStart.AddDays(-1);

            int diff = (7 + (today.DayOfWeek - DayOfWeek.Monday)) % 7;
            var weekStart = DateTime.SpecifyKind(today.AddDays(-1 * diff), DateTimeKind.Utc);
            var pastWeekStart = weekStart.AddDays(-7);

            var monthStart = DateTime.SpecifyKind(new DateTime(today.Year, today.Month, 1), DateTimeKind.Utc);
            var pastMonthStart = monthStart.AddMonths(-1);

            var baseQuery = db.DailyFundsRegisteries.AsNoTracking().Where(r => !r.IsDisabled);

            var countToday = await baseQuery.CountAsync(r => r.Date >= todayStart && r.Date < tomorrowStart, ct);
            var countYesterday = await baseQuery.CountAsync(r => r.Date >= yesterdayStart && r.Date < todayStart, ct);
            var countWeek = await baseQuery.CountAsync(r => r.Date >= weekStart && r.Date < tomorrowStart, ct);
            var countPastWeek = await baseQuery.CountAsync(r => r.Date >= pastWeekStart && r.Date < weekStart, ct);
            var countMonth = await baseQuery.CountAsync(r => r.Date >= monthStart && r.Date < tomorrowStart, ct);
            var countPastMonth = await baseQuery.CountAsync(r => r.Date >= pastMonthStart && r.Date < monthStart, ct);
            var countAll = await baseQuery.CountAsync(ct);

            var (groups, tags) = await GetGlobalGroupsAndTagsAsync(db, ct);

            return Results.Ok(new Dictionary<string, object>
            {
                ["Group"] = groups,
                ["GroupNames"] = groups,
                ["GroupName"] = groups,
                ["Tags"] = tags,
                ["TagNames"] = tags,
                ["Tag"] = tags,
                ["Date"] = new Dictionary<string, int>
                {
                    { "#Today", countToday },
                    { "#Yesturday", countYesterday },
                    { "#Yesterday", countYesterday },
                    { "#This Week", countWeek },
                    { "#Past Week", countPastWeek },
                    { "#This Month", countMonth },
                    { "#Past Month", countPastMonth },
                    { "#All Records", countAll }
                }
            });
        });

        registryGroup.MapGet("/{id}", async (string id, MermerDbContext db, CancellationToken ct) =>
        {
            if (!Guid.TryParse(id, out var guid)) return Results.NotFound();
            var r = await db.DailyFundsRegisteries.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == guid, ct);
            if (r == null) return Results.NotFound();

            var convertions = await GetCurrencyConvertionsAsync(db, r.Date, ct);
            var docCurrencyId = r.DisplayCurrencyId?.ToString() ?? (await db.Currencies.FirstOrDefaultAsync(c => c.IsDefault))?.Id.ToString();
            decimal total = r.Lines != null && r.Lines.Any() ? r.Lines.Sum(l => l.Amount) : 0m;

            return Results.Ok(new
            {
                Id = r.Id.ToString(),
                Code = r.Code ?? string.Empty,
                Date = r.Date.ToString("yyyy-MM-ddTHH:mm:ss"),
                Type = "DailyFundsRegistery",
                DepositoryId = r.DepositoryId?.ToString(),
                DisplayCurrencyId = docCurrencyId,
                CurrencyId = docCurrencyId,
                CurrencyConvertions = convertions,
                UserId = r.UserId?.ToString(),
                UserName = r.UserName,
                IsCompleted = r.IsCompleted,
                IsDisabled = r.IsDisabled,
                Group = r.GroupName ?? string.Empty,
                Tags = r.Tags != null ? r.Tags.ToList() : new List<string>(),
                Description = r.Description ?? string.Empty,
                ActionTotal = total,
                DisplayTotal = total,
                Lines = r.Lines != null ? r.Lines.Select(l => (object)new
                {
                    Id = l.Id.ToString(),
                    DailyFundsRegisteryId = r.Id.ToString(),
                    Amount = l.Amount,
                    CurrencyId = l.CurrencyId?.ToString() ?? docCurrencyId,
                    SortOrder = l.SortOrder
                }).ToList() : new List<object>()
            });
        });

        Func<HttpRequest, MermerDbContext, Task<IResult>> saveRegistryHandler = async (request, db) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync();
            if (string.IsNullOrEmpty(body)) return Results.BadRequest("Empty body");

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            string? idStr = GetStringProperty(root, "id", "Id");
            Guid regId = Guid.TryParse(idStr, out var parsedGuid) && parsedGuid != Guid.Empty ? parsedGuid : Guid.NewGuid();

            var existing = await db.DailyFundsRegisteries.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == regId);

            var existingLinesMap = existing?.Lines?
                .Where(l => l.Id != Guid.Empty)
                .ToDictionary(l => l.Id) ?? new Dictionary<Guid, DailyFundsRegisteryLineEntity>();

            string code = GetStringProperty(root, "code", "Code") ?? $"REG-{DateTime.UtcNow:yyMMddHHmmss}";
            string? depIdStr = GetStringProperty(root, "depositoryId", "DepositoryId");
            Guid? depId = Guid.TryParse(depIdStr, out var pDep) ? pDep : null;

            string? dispCurStr = GetStringProperty(root, "displayCurrencyId", "DisplayCurrencyId", "currencyId", "CurrencyId");
            Guid? dispCurId = Guid.TryParse(dispCurStr, out var pCur) ? pCur : null;

            string? userIdStr = GetStringProperty(root, "userId", "UserId");
            Guid? userId = Guid.TryParse(userIdStr, out var pUser) ? pUser : null;

            DateTime date = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Utc);
            string? dateStr = GetStringProperty(root, "date", "Date");
            if (!string.IsNullOrEmpty(dateStr) && DateTime.TryParse(dateStr, out var pDate))
                date = DateTime.SpecifyKind(pDate, DateTimeKind.Utc);

            var tagsList = ExtractTagsFromRawJson(root);
            string groupName = GetStringProperty(root, "group", "Group", "groupName", "GroupName") ?? "";
            string description = GetStringProperty(root, "description", "Description") ?? "";

            var linesList = new List<DailyFundsRegisteryLineEntity>();
            if (TryGetPropertyCaseInsensitive(root, "lines", out var linesProp) && linesProp.ValueKind == JsonValueKind.Array)
            {
                int sortOrder = 0;
                foreach (var lJson in linesProp.EnumerateArray())
                {
                    string? lineIdStr = GetStringProperty(lJson, "id", "Id");
                    Guid lineGuid = Guid.TryParse(lineIdStr, out var lG) && lG != Guid.Empty ? lG : Guid.NewGuid();

                    decimal amount = GetDecimalProperty(lJson, "amount", "Amount", "total", "Total");
                    string? curIdStr = GetStringProperty(lJson, "currencyId", "CurrencyId");
                    Guid? currencyGuid = Guid.TryParse(curIdStr, out var cG) ? cG : dispCurId;

                    if (existingLinesMap.TryGetValue(lineGuid, out var oldLine) && oldLine.CurrencyId.HasValue)
                    {
                        currencyGuid = oldLine.CurrencyId;
                    }

                    linesList.Add(new DailyFundsRegisteryLineEntity
                    {
                        Id = lineGuid,
                        RegisteryId = regId,
                        Amount = amount,
                        CurrencyId = currencyGuid,
                        SortOrder = sortOrder++
                    });
                }
            }

            if (existing == null)
            {
                var entity = new DailyFundsRegisteryEntity
                {
                    Id = regId,
                    Code = code,
                    Date = date,
                    UserId = userId,
                    DepositoryId = depId,
                    DisplayCurrencyId = dispCurId,
                    IsCompleted = GetBoolProperty(root, "isCompleted", "IsCompleted"),
                    IsDisabled = GetBoolProperty(root, "isDisabled", "IsDisabled"),
                    UserName = GetStringProperty(root, "userName", "UserName") ?? "admin",
                    GroupName = groupName,
                    Description = description,
                    Tags = tagsList.ToArray(),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    Lines = linesList
                };
                await db.DailyFundsRegisteries.AddAsync(entity);
            }
            else
            {
                existing.Code = code;
                existing.UserId = userId;
                existing.DepositoryId = depId;
                // Не перезаписываем DisplayCurrencyId
                existing.IsCompleted = GetBoolProperty(root, "isCompleted", "IsCompleted");
                existing.IsDisabled = GetBoolProperty(root, "isDisabled", "IsDisabled");
                existing.GroupName = groupName;
                existing.Description = description;
                existing.Tags = tagsList.ToArray();
                existing.UpdatedAt = DateTime.UtcNow;

                if (existing.Lines != null) db.DailyFundsRegisteryLines.RemoveRange(existing.Lines);
                existing.Lines = linesList;
                db.DailyFundsRegisteries.Update(existing);
            }

            await db.SaveChangesAsync();
            return Results.Content($"{{\"id\":\"{regId}\",\"code\":\"{code}\"}}", "application/json");
        };

        registryGroup.MapPost("", saveRegistryHandler);
        registryGroup.MapPut("/{id}", saveRegistryHandler);

        registryGroup.MapDelete("/{id}", async (string id, MermerDbContext db) =>
        {
            if (!Guid.TryParse(id, out var guid)) return Results.NotFound();
            var item = await db.DailyFundsRegisteries.FirstOrDefaultAsync(x => x.Id == guid);
            if (item != null)
            {
                item.IsDisabled = true;
                item.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
            return Results.Ok();
        });

        // =========================================================================
        // 8. ЖУРНАЛ ДВИЖЕНИЯ ДЕНЕЖНЫХ СРЕДСТВ
        // =========================================================================
        routes.MapGet("/api/finance/actions/count", async (DateTime? from, DateTime? till, bool? isCompleted, HttpRequest req, MermerDbContext db, CancellationToken ct) =>
        {
            var startDate = from.HasValue
    ? DateTime.SpecifyKind(from.Value, DateTimeKind.Utc)
    : DateTime.SpecifyKind(new DateTime(2000, 1, 1), DateTimeKind.Utc);

            var endDate = till.HasValue
                ? DateTime.SpecifyKind(till.Value.TimeOfDay == TimeSpan.Zero ? till.Value.Date.AddDays(1).AddTicks(-1) : till.Value, DateTimeKind.Utc)
                : DateTime.SpecifyKind(new DateTime(2099, 12, 31, 23, 59, 59), DateTimeKind.Utc);

            bool? completedFilter = isCompleted;
            if (!completedFilter.HasValue && req.Query.ContainsKey("isCompleted"))
            {
                if (bool.TryParse(req.Query["isCompleted"], out var parsedBool))
                    completedFilter = parsedBool;
            }

            var rawDeps = req.Query["depositoryId"].ToArray();
            var depGuids = rawDeps
                .SelectMany(x => (x ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                .Select(x => Guid.TryParse(x.Trim(), out var g) ? (Guid?)g : null)
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .Distinct()
                .ToList();

            var slipsQuery = db.FundsSlips.AsNoTracking()
                .Where(s => s.Date >= startDate && s.Date <= endDate && !s.IsDisabled);
            if (completedFilter.HasValue)
                slipsQuery = slipsQuery.Where(s => s.IsCompleted == completedFilter.Value);
            if (depGuids.Any())
                slipsQuery = slipsQuery.Where(s => s.DepositoryId.HasValue && depGuids.Contains(s.DepositoryId.Value));

            int slipsCount = await slipsQuery.SelectMany(s => s.Lines).CountAsync(ct);

            var expenseQuery = db.ExpenseSlips.AsNoTracking()
                .Where(e => e.Date >= startDate && e.Date <= endDate && !e.IsDisabled);
            if (completedFilter.HasValue)
                expenseQuery = expenseQuery.Where(e => e.IsCompleted == completedFilter.Value);
            if (depGuids.Any())
                expenseQuery = expenseQuery.Where(e => e.DepositoryId.HasValue && depGuids.Contains(e.DepositoryId.Value));

            int expenseCount = await expenseQuery.SelectMany(e => e.Lines).CountAsync(ct);

            var transfersQuery = db.FundsTransfers.AsNoTracking()
                .Where(t => t.Date >= startDate && t.Date <= endDate && !t.IsDisabled);
            if (completedFilter.HasValue)
                transfersQuery = transfersQuery.Where(t => t.IsCompleted == completedFilter.Value);

            int transfersCount = 0;
            if (depGuids.Any())
            {
                int sourceLinesCount = await transfersQuery
                    .Where(t => t.FromDepositoryId.HasValue && depGuids.Contains(t.FromDepositoryId.Value))
                    .SelectMany(t => t.Lines)
                    .CountAsync(ct);

                int destLinesCount = await transfersQuery
                    .Where(t => t.ToDepositoryId.HasValue && depGuids.Contains(t.ToDepositoryId.Value))
                    .SelectMany(t => t.Lines)
                    .CountAsync(ct);

                transfersCount = sourceLinesCount + destLinesCount;
            }
            else
            {
                int totalTransferLines = await transfersQuery.SelectMany(t => t.Lines).CountAsync(ct);
                transfersCount = totalTransferLines * 2;
            }

            // Подсчет накладных с привязкой к кассе
            DateTimeOffset startOffset = startDate;
            DateTimeOffset endOffset = endDate;

            var invoicesQuery = db.Invoices.AsNoTracking()
                .Where(i => i.Date >= startOffset && i.Date <= endOffset && !i.IsDisabled && i.DepositoryId.HasValue);
            if (completedFilter.HasValue)
                invoicesQuery = invoicesQuery.Where(i => i.IsCompleted == completedFilter.Value);
            if (depGuids.Any())
                invoicesQuery = invoicesQuery.Where(i => depGuids.Contains(i.DepositoryId.Value));

            int invoicesCount = await invoicesQuery.CountAsync(ct);

            return Results.Ok(new { count = slipsCount + expenseCount + transfersCount + invoicesCount });
        }).WithTags("FundsActions");

        routes.MapGet("/api/finance/actions", async (DateTime? from, DateTime? till, int? limit, HttpRequest req, MermerDbContext db, CancellationToken ct) =>
        {
            int take = limit.HasValue ? Math.Clamp(limit.Value, 1, 50000) : 10000;
            var startDate = from.HasValue
                ? DateTime.SpecifyKind(from.Value, DateTimeKind.Utc)
                : DateTime.SpecifyKind(new DateTime(2000, 1, 1), DateTimeKind.Utc);

            var endDate = till.HasValue
                ? DateTime.SpecifyKind(till.Value.TimeOfDay == TimeSpan.Zero ? till.Value.Date.AddDays(1).AddTicks(-1) : till.Value, DateTimeKind.Utc)
                : DateTime.SpecifyKind(new DateTime(2099, 12, 31, 23, 59, 59), DateTimeKind.Utc);

            bool? completedFilter = null;
            if (req.Query.ContainsKey("isCompleted") && bool.TryParse(req.Query["isCompleted"], out var parsedBool))
                completedFilter = parsedBool;

            var rawDeps = req.Query["depositoryId"].ToArray();
            var depGuids = rawDeps
                .SelectMany(x => (x ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                .Select(x => Guid.TryParse(x.Trim(), out var g) ? (Guid?)g : null)
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .Distinct()
                .ToList();

            // 1. Загружаем базовую валюту системы (USD)
            var defaultCurrency = await db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => c.IsDefault && !c.IsDisabled, ct)
                                  ?? await db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => !c.IsDisabled, ct);
            var defaultCurrencyId = defaultCurrency?.Id;

            // 2. Загружаем курсы валют для конвертации в базовую валюту
            var allRates = await db.CurrencyRates
                .AsNoTracking()
                .OrderByDescending(r => r.ValidFrom)
                .ToListAsync(ct);

            // Локальная функция пересчета любой валюты в базовую (USD)
            decimal ConvertToBaseCurrency(decimal amount, Guid? currencyId, DateTime docDate)
            {
                if (amount == 0m || currencyId == null || currencyId == defaultCurrencyId)
                    return amount;

                var matchedRate = allRates
                    .Where(r => r.CurrencyId == currencyId.Value && r.ValidFrom <= docDate)
                    .OrderByDescending(r => r.ValidFrom)
                    .FirstOrDefault()
                    ?? allRates.FirstOrDefault(r => r.CurrencyId == currencyId.Value);

                if (matchedRate != null && matchedRate.Divider != 0m)
                {
                    return Math.Round(amount * matchedRate.Multiplier / matchedRate.Divider, 2, MidpointRounding.AwayFromZero);
                }

                return amount;
            }

            // Справочники
            var partnersMap = await db.Partners.AsNoTracking()
                .Select(p => new { p.Id, p.Name, p.Code })
                .ToDictionaryAsync(p => p.Id, p => !string.IsNullOrWhiteSpace(p.Name) ? p.Name : p.Code, ct);

            var depositoriesMap = await db.Depositories.AsNoTracking()
                .ToDictionaryAsync(d => d.Id, d => d.Name, ct);

            var expensesMap = await db.Expenses.AsNoTracking()
                .ToDictionaryAsync(e => e.Id, e => e.Name, ct);

            var actions = new List<object>();

            // 1. Кассовые ордера (FundsSlips: Collection, Payment, FundsOpening и т.д.)
            var slipsQuery = db.FundsSlips
                .Include(s => s.Lines)
                .AsNoTracking()
                .Where(s => s.Date >= startDate && s.Date <= endDate && !s.IsDisabled);

            if (completedFilter.HasValue)
                slipsQuery = slipsQuery.Where(s => s.IsCompleted == completedFilter.Value);

            if (depGuids.Any())
                slipsQuery = slipsQuery.Where(s => s.DepositoryId.HasValue && depGuids.Contains(s.DepositoryId.Value));

            var slips = await slipsQuery.OrderByDescending(s => s.Date).Take(take).ToListAsync(ct);
            foreach (var s in slips)
            {
                string partnerName = s.PartnerId.HasValue && partnersMap.TryGetValue(s.PartnerId.Value, out var pName)
                    ? pName
                    : string.Empty;

                foreach (var line in s.Lines ?? Enumerable.Empty<FundsSlipLineEntity>())
                {
                    var lineCurId = line.CurrencyId ?? s.DisplayCurrencyId ?? defaultCurrencyId;
                    // Пересчитываем сумму строки в базовую валюту USD
                    decimal baseAmount = ConvertToBaseCurrency(line.Amount, lineCurId, s.Date);

                    bool isIncome = s.FundsSlipType == "Collection" || s.FundsSlipType == "FundsOpening" || s.FundsSlipType == "FundsRevisionExceed";
                    decimal income = isIncome ? baseAmount : 0m;
                    decimal expense = !isIncome ? baseAmount : 0m;

                    actions.Add(new
                    {
                        TransactionId = s.Id.ToString(),
                        TransactionCode = s.Code ?? "",
                        TransactionDate = s.Date.ToString("yyyy-MM-ddTHH:mm:ss"),
                        TransactionType = s.FundsSlipType,
                        TransactionUserId = s.UserId?.ToString(),
                        TransactionUserName = s.UserName,
                        TransactionIsCompleted = s.IsCompleted,
                        TransactionIsDisabled = s.IsDisabled,
                        TransactionGroup = s.Group ?? "",
                        TransactionTags = s.Tags ?? Array.Empty<string>(),

                        ActionRelatedPartnerId = s.PartnerId?.ToString(),
                        ActionRelatedDepositoryId = (string?)null,
                        ActionRelatedObjectName = partnerName,

                        ActionDepositoryId = s.DepositoryId?.ToString(),
                        ActionCurrencyId = defaultCurrencyId?.ToString(),
                        ActionAmount = baseAmount,
                        ActionIncome = income,
                        ActionExpense = expense,
                        ActionEffect = income - expense
                    });
                }
            }

            // 2. Акты расходов (ExpenseSlips)
            var expenseQuery = db.ExpenseSlips
                .Include(e => e.Lines)
                .AsNoTracking()
                .Where(e => e.Date >= startDate && e.Date <= endDate && !e.IsDisabled);

            if (completedFilter.HasValue)
                expenseQuery = expenseQuery.Where(e => e.IsCompleted == completedFilter.Value);

            if (depGuids.Any())
                expenseQuery = expenseQuery.Where(e => e.DepositoryId.HasValue && depGuids.Contains(e.DepositoryId.Value));

            var expenseSlips = await expenseQuery.OrderByDescending(e => e.Date).Take(take).ToListAsync(ct);
            foreach (var e in expenseSlips)
            {
                foreach (var line in e.Lines ?? Enumerable.Empty<ExpenseSlipLineEntity>())
                {
                    string expenseName = line.ExpenseId.HasValue && expensesMap.TryGetValue(line.ExpenseId.Value, out var exName)
                        ? exName
                        : string.Empty;

                    var lineCurId = line.CurrencyId ?? e.DisplayCurrencyId ?? defaultCurrencyId;
                    decimal baseAmount = ConvertToBaseCurrency(line.Amount, lineCurId, e.Date);

                    actions.Add(new
                    {
                        TransactionId = e.Id.ToString(),
                        TransactionCode = e.Code ?? "",
                        TransactionDate = e.Date.ToString("yyyy-MM-ddTHH:mm:ss"),
                        TransactionType = "ExpenseSlip",
                        TransactionUserId = e.UserId?.ToString(),
                        TransactionUserName = e.UserName,
                        TransactionIsCompleted = e.IsCompleted,
                        TransactionIsDisabled = e.IsDisabled,
                        TransactionGroup = e.GroupName ?? "",
                        TransactionTags = e.Tags ?? Array.Empty<string>(),

                        ActionRelatedPartnerId = (string?)null,
                        ActionRelatedDepositoryId = (string?)null,
                        ActionRelatedObjectName = expenseName,

                        ActionDepositoryId = e.DepositoryId?.ToString(),
                        ActionCurrencyId = defaultCurrencyId?.ToString(),
                        ActionAmount = baseAmount,
                        ActionIncome = 0m,
                        ActionExpense = baseAmount,
                        ActionEffect = -baseAmount
                    });
                }
            }

            // 3. Переводы между кассами (FundsTransfers)
            var transfersQuery = db.FundsTransfers
                .Include(t => t.Lines)
                .AsNoTracking()
                .Where(t => t.Date >= startDate && t.Date <= endDate && !t.IsDisabled);

            if (completedFilter.HasValue)
                transfersQuery = transfersQuery.Where(t => t.IsCompleted == completedFilter.Value);

            if (depGuids.Any())
            {
                transfersQuery = transfersQuery.Where(t =>
                    (t.FromDepositoryId.HasValue && depGuids.Contains(t.FromDepositoryId.Value)) ||
                    (t.ToDepositoryId.HasValue && depGuids.Contains(t.ToDepositoryId.Value)));
            }

            var transfers = await transfersQuery.OrderByDescending(t => t.Date).Take(take).ToListAsync(ct);
            foreach (var t in transfers)
            {
                string fromDepName = t.FromDepositoryId.HasValue && depositoriesMap.TryGetValue(t.FromDepositoryId.Value, out var fdName)
                    ? fdName
                    : string.Empty;

                string toDepName = t.ToDepositoryId.HasValue && depositoriesMap.TryGetValue(t.ToDepositoryId.Value, out var tdName)
                    ? tdName
                    : string.Empty;

                foreach (var line in t.Lines ?? Enumerable.Empty<FundsTransferLineEntity>())
                {
                    var lineCurId = line.CurrencyId ?? t.DisplayCurrencyId ?? defaultCurrencyId;
                    decimal baseSent = ConvertToBaseCurrency(line.Amount, lineCurId, t.Date);
                    decimal baseReceived = ConvertToBaseCurrency(line.ReceivedAmount, lineCurId, t.Date);

                    if (!depGuids.Any() || (t.FromDepositoryId.HasValue && depGuids.Contains(t.FromDepositoryId.Value)))
                    {
                        actions.Add(new
                        {
                            TransactionId = t.Id.ToString(),
                            TransactionCode = t.Code ?? "",
                            TransactionDate = t.Date.ToString("yyyy-MM-ddTHH:mm:ss"),
                            TransactionType = "FundsTransferSource",
                            TransactionUserId = (string?)null,
                            TransactionUserName = t.UserName,
                            TransactionIsCompleted = t.IsCompleted,
                            TransactionIsDisabled = t.IsDisabled,
                            TransactionGroup = t.Group ?? "",
                            TransactionTags = t.Tags ?? Array.Empty<string>(),

                            ActionRelatedPartnerId = (string?)null,
                            ActionRelatedDepositoryId = t.ToDepositoryId?.ToString(),
                            ActionRelatedObjectName = toDepName,

                            ActionDepositoryId = t.FromDepositoryId?.ToString(),
                            ActionCurrencyId = defaultCurrencyId?.ToString(),
                            ActionAmount = baseSent,
                            ActionIncome = 0m,
                            ActionExpense = baseSent,
                            ActionEffect = -baseSent
                        });
                    }

                    if (!depGuids.Any() || (t.ToDepositoryId.HasValue && depGuids.Contains(t.ToDepositoryId.Value)))
                    {
                        actions.Add(new
                        {
                            TransactionId = t.Id.ToString(),
                            TransactionCode = t.Code ?? "",
                            TransactionDate = t.Date.ToString("yyyy-MM-ddTHH:mm:ss"),
                            TransactionType = "FundsTransferDestination",
                            TransactionUserId = (string?)null,
                            TransactionUserName = t.UserName,
                            TransactionIsCompleted = t.IsCompleted,
                            TransactionIsDisabled = t.IsDisabled,
                            TransactionGroup = t.Group ?? "",
                            TransactionTags = t.Tags ?? Array.Empty<string>(),

                            ActionRelatedPartnerId = (string?)null,
                            ActionRelatedDepositoryId = t.FromDepositoryId?.ToString(),
                            ActionRelatedObjectName = fromDepName,

                            ActionDepositoryId = t.ToDepositoryId?.ToString(),
                            ActionCurrencyId = defaultCurrencyId?.ToString(),
                            ActionAmount = baseReceived,
                            ActionIncome = baseReceived,
                            ActionExpense = 0m,
                            ActionEffect = baseReceived
                        });
                    }
                }
            }

            // 4. Накладные (Invoices)
            DateTimeOffset startOffset = startDate;
            DateTimeOffset endOffset = endDate;

            var invoicesQuery = db.Invoices
                .Include(i => i.Payments)
                .Include(i => i.Lines)
                .AsNoTracking()
                .Where(i => i.Date >= startOffset && i.Date <= endOffset && !i.IsDisabled && i.DepositoryId.HasValue);

            if (completedFilter.HasValue)
                invoicesQuery = invoicesQuery.Where(i => i.IsCompleted == completedFilter.Value);

            if (depGuids.Any())
                invoicesQuery = invoicesQuery.Where(i => depGuids.Contains(i.DepositoryId.Value));

            var invoices = await invoicesQuery.OrderByDescending(i => i.Date).Take(take).AsSplitQuery().ToListAsync(ct);
            foreach (var inv in invoices)
            {
                string partnerName = inv.PartnerId.HasValue && partnersMap.TryGetValue(inv.PartnerId.Value, out var pName)
                    ? pName
                    : "Розничный покупатель";

                bool isIncome = inv.InvoiceType == "Sales" || inv.InvoiceType == "PurchaseReturn";
                var docDate = inv.Date.UtcDateTime;

                if (inv.Payments != null && inv.Payments.Any())
                {
                    foreach (var pay in inv.Payments)
                    {
                        var payCurId = pay.CurrencyId ?? inv.DisplayCurrencyId ?? defaultCurrencyId;
                        decimal basePayAmount = ConvertToBaseCurrency(pay.Amount, payCurId, docDate);

                        decimal income = isIncome ? basePayAmount : 0m;
                        decimal expense = !isIncome ? basePayAmount : 0m;

                        actions.Add(new
                        {
                            TransactionId = inv.Id.ToString(),
                            TransactionCode = inv.Code ?? "",
                            TransactionDate = inv.Date.ToString("yyyy-MM-ddTHH:mm:ss"),
                            TransactionType = inv.InvoiceType ?? "Sales",
                            TransactionUserId = inv.UserId?.ToString(),
                            TransactionUserName = inv.UserName,
                            TransactionIsCompleted = inv.IsCompleted,
                            TransactionIsDisabled = inv.IsDisabled,
                            TransactionGroup = inv.Group ?? "",
                            TransactionTags = inv.Tags ?? Array.Empty<string>(),

                            ActionRelatedPartnerId = inv.PartnerId?.ToString(),
                            ActionRelatedDepositoryId = (string?)null,
                            ActionRelatedObjectName = partnerName,

                            ActionDepositoryId = inv.DepositoryId?.ToString(),
                            ActionCurrencyId = defaultCurrencyId?.ToString(),
                            ActionAmount = basePayAmount,
                            ActionIncome = income,
                            ActionExpense = expense,
                            ActionEffect = income - expense
                        });
                    }
                }
                else
                {
                    decimal rawTotal = inv.Lines != null && inv.Lines.Any()
                        ? inv.Lines.Sum(l => l.Quantity * l.Price)
                        : 0m;

                    decimal baseDocTotal = ConvertToBaseCurrency(rawTotal, inv.DisplayCurrencyId ?? defaultCurrencyId, docDate);

                    if (baseDocTotal > 0)
                    {
                        decimal income = isIncome ? baseDocTotal : 0m;
                        decimal expense = !isIncome ? baseDocTotal : 0m;

                        actions.Add(new
                        {
                            TransactionId = inv.Id.ToString(),
                            TransactionCode = inv.Code ?? "",
                            TransactionDate = inv.Date.ToString("yyyy-MM-ddTHH:mm:ss"),
                            TransactionType = inv.InvoiceType ?? "Sales",
                            TransactionUserId = inv.UserId?.ToString(),
                            TransactionUserName = inv.UserName,
                            TransactionIsCompleted = inv.IsCompleted,
                            TransactionIsDisabled = inv.IsDisabled,
                            TransactionGroup = inv.Group ?? "",
                            TransactionTags = inv.Tags ?? Array.Empty<string>(),

                            ActionRelatedPartnerId = inv.PartnerId?.ToString(),
                            ActionRelatedDepositoryId = (string?)null,
                            ActionRelatedObjectName = partnerName,

                            ActionDepositoryId = inv.DepositoryId?.ToString(),
                            ActionCurrencyId = defaultCurrencyId?.ToString(),
                            ActionAmount = baseDocTotal,
                            ActionIncome = income,
                            ActionExpense = expense,
                            ActionEffect = income - expense
                        });
                    }
                }
            }

            return Results.Json(actions.OrderByDescending(a => ((dynamic)a).TransactionDate), JsonOptions);
        }).WithTags("FundsActions");

        // =========================================================================
        // 9. БАЛАНСЫ КАСС
        // =========================================================================
        var balancesGroup = routes.MapGroup("/api/finance/balances").WithTags("FundsBalances");

        balancesGroup.MapGet("/bytype", async (string? depositoryId, DateTime? from, DateTime? till, MermerDbContext db, CancellationToken ct) =>
        {
            bool isAllTime = !from.HasValue || from.Value.Year <= 2000;

            DateTime start = !isAllTime
                ? DateTime.SpecifyKind(from!.Value.Date, DateTimeKind.Utc)
                : DateTime.SpecifyKind(new DateTime(2000, 1, 1), DateTimeKind.Utc);

            DateTime end = till.HasValue && till.Value.Year < 2099
                ? DateTime.SpecifyKind(till.Value.Date.AddDays(1), DateTimeKind.Utc)
                : DateTime.SpecifyKind(new DateTime(2099, 12, 31), DateTimeKind.Utc);

            var depGuids = new List<Guid>();
            if (!string.IsNullOrEmpty(depositoryId) && depositoryId != "null" && Guid.TryParse(depositoryId, out var parsedDep) && parsedDep != Guid.Empty)
            {
                depGuids.Add(parsedDep);
            }
            else
            {
                depGuids = await db.Depositories.AsNoTracking().Where(d => !d.IsDisabled).Select(d => d.Id).ToListAsync(ct);
            }

            var defCur = await db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => c.IsDefault && !c.IsDisabled, ct)
                         ?? await db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => !c.IsDisabled, ct);
            Guid defaultCurrencyId = defCur?.Id ?? Guid.Empty;

            var defOffice = await db.Offices.AsNoTracking().FirstOrDefaultAsync(o => !o.IsDisabled, ct);
            Guid? defaultOfficeId = defOffice?.Id;

            var defDep = (defaultOfficeId.HasValue
                             ? await db.Depositories.AsNoTracking().FirstOrDefaultAsync(d => d.OfficeId == defaultOfficeId && !d.IsDisabled, ct)
                             : null)
                         ?? await db.Depositories.AsNoTracking().FirstOrDefaultAsync(d => !d.IsDisabled, ct);

            Guid defaultDepositoryId = defDep?.Id ?? (depGuids.FirstOrDefault());

            const string sql = """
                WITH def_cur AS (
                    SELECT @defCurId::uuid AS id
                ),
                raw_funds AS (
                    SELECT 
                        f.depository_id, 
                        f.date, 
                        f.funds_slip_type AS type, 
                        fl.amount AS amt,
                        (f.funds_slip_type IN ('Collection', 'FundsOpening', 'FundsRevisionExceed')) AS is_inc
                    FROM funds_slip_lines fl
                    JOIN funds_slips f ON f.id = fl.funds_slip_id
                    WHERE f.depository_id = ANY(@deps) 
                      AND f.is_disabled = false

                    UNION ALL

                    SELECT 
                        e.depository_id, 
                        e.date, 
                        'ExpenseSlip' AS type, 
                        el.amount AS amt,
                        false AS is_inc
                    FROM expense_slip_lines el
                    JOIN expense_slips e ON e.id = el.expense_slip_id
                    WHERE e.depository_id = ANY(@deps) 
                      AND e.is_disabled = false

                    UNION ALL

                    SELECT 
                        t.from_depository_id AS depository_id, 
                        t.date, 
                        'FundsTransferSource' AS type, 
                        tl.amount AS amt,
                        false AS is_inc
                    FROM funds_transfer_lines tl
                    JOIN funds_transfers t ON t.id = tl.funds_transfer_id
                    WHERE t.from_depository_id = ANY(@deps) 
                      AND t.is_disabled = false

                    UNION ALL

                    SELECT 
                        t.to_depository_id AS depository_id, 
                        t.date, 
                        'FundsTransferDestination' AS type, 
                        tl.received_amount AS amt,
                        true AS is_inc
                    FROM funds_transfer_lines tl
                    JOIN funds_transfers t ON t.id = tl.funds_transfer_id
                    WHERE t.to_depository_id = ANY(@deps) 
                      AND t.is_disabled = false

                    UNION ALL

                    SELECT 
                        i.depository_id, 
                        i.date, 
                        i.invoice_type AS type, 
                        (ip.amount * CASE 
                            WHEN i.display_currency_id IS NULL OR i.display_currency_id = (SELECT id FROM def_cur) THEN 1.0
                            ELSE COALESCE(cr.multiplier / NULLIF(cr.divider, 0), 1.0)
                        END)::numeric(18,4) AS amt,
                        (i.invoice_type IN ('Sales', 'PurchaseReturn')) AS is_inc
                    FROM invoice_payments ip
                    JOIN invoices i ON i.id = ip.invoice_id
                    LEFT JOIN LATERAL (
                        SELECT r.multiplier, r.divider 
                        FROM currency_rates r 
                        WHERE r.currency_id = i.display_currency_id AND r.valid_from <= i.date 
                        ORDER BY r.valid_from DESC LIMIT 1
                    ) cr ON true
                    WHERE i.depository_id = ANY(@deps) 
                      AND i.is_completed = true 
                      AND i.is_disabled = false
                )
                SELECT 
                    d.id::text AS "DepositoryId",
                    (CASE 
                        WHEN @isAllTime THEN 0 
                        ELSE COALESCE(SUM(CASE WHEN rf.date < @start THEN (CASE WHEN rf.is_inc THEN rf.amt ELSE -rf.amt END) ELSE 0 END), 0)
                     END)::numeric(18,4) AS "StartingBalance",

                    COALESCE(SUM(CASE WHEN rf.date >= @start AND rf.date < @end AND rf.is_inc THEN rf.amt ELSE 0 END), 0)::numeric(18,4) AS "Income",
                    COALESCE(SUM(CASE WHEN rf.date >= @start AND rf.date < @end AND NOT rf.is_inc THEN rf.amt ELSE 0 END), 0)::numeric(18,4) AS "Expense",
                    
                    COALESCE(SUM(CASE WHEN rf.date >= @start AND rf.date < @end AND rf.type = 'FundsOpening' THEN rf.amt ELSE 0 END), 0)::numeric(18,4) AS "FundsOpening",
                    COALESCE(SUM(CASE WHEN rf.date >= @start AND rf.date < @end AND rf.type = 'FundsRevisionExceed' THEN rf.amt ELSE 0 END), 0)::numeric(18,4) AS "FundsRevisionExceed",
                    COALESCE(SUM(CASE WHEN rf.date >= @start AND rf.date < @end AND rf.type = 'FundsRevisionDeficit' THEN rf.amt ELSE 0 END), 0)::numeric(18,4) AS "FundsRevisionDeficit",
                    COALESCE(SUM(CASE WHEN rf.date >= @start AND rf.date < @end AND rf.type = 'Collection' THEN rf.amt ELSE 0 END), 0)::numeric(18,4) AS "Collection",
                    COALESCE(SUM(CASE WHEN rf.date >= @start AND rf.date < @end AND rf.type = 'Payment' THEN rf.amt ELSE 0 END), 0)::numeric(18,4) AS "Payment",
                    COALESCE(SUM(CASE WHEN rf.date >= @start AND rf.date < @end AND rf.type = 'ExpenseSlip' THEN rf.amt ELSE 0 END), 0)::numeric(18,4) AS "ExpenseSlip",
                    COALESCE(SUM(CASE WHEN rf.date >= @start AND rf.date < @end AND rf.type = 'FundsTransferSource' THEN rf.amt ELSE 0 END), 0)::numeric(18,4) AS "FundsTransferSource",
                    COALESCE(SUM(CASE WHEN rf.date >= @start AND rf.date < @end AND rf.type = 'FundsTransferDestination' THEN rf.amt ELSE 0 END), 0)::numeric(18,4) AS "FundsTransferDestination",
                    COALESCE(SUM(CASE WHEN rf.date >= @start AND rf.date < @end AND rf.type = 'Sales' THEN rf.amt ELSE 0 END), 0)::numeric(18,4) AS "Sales",
                    COALESCE(SUM(CASE WHEN rf.date >= @start AND rf.date < @end AND rf.type = 'SalesReturn' THEN rf.amt ELSE 0 END), 0)::numeric(18,4) AS "SalesReturn",
                    COALESCE(SUM(CASE WHEN rf.date >= @start AND rf.date < @end AND rf.type = 'Purchase' THEN rf.amt ELSE 0 END), 0)::numeric(18,4) AS "Purchase",
                    COALESCE(SUM(CASE WHEN rf.date >= @start AND rf.date < @end AND rf.type = 'PurchaseReturn' THEN rf.amt ELSE 0 END), 0)::numeric(18,4) AS "PurchaseReturn"
                FROM depositories d
                LEFT JOIN raw_funds rf ON rf.depository_id = d.id
                WHERE d.id = ANY(@deps)
                GROUP BY d.id;
                """;

            var connStr = db.Database.GetConnectionString();
            await using var conn = new NpgsqlConnection(connStr);
            var result = await conn.QueryAsync<FundsBalanceByTypeWithBalanceDto>(new CommandDefinition(
                sql,
                new
                {
                    deps = depGuids.ToArray(),
                    start,
                    end,
                    isAllTime,
                    defCurId = defaultCurrencyId,
                    defDepId = defaultDepositoryId
                },
                cancellationToken: ct));

            return Results.Ok(result);
        });

        balancesGroup.MapGet("/todate", async (string? depositoryId, DateTime date, MermerDbContext db, CancellationToken ct) =>
        {
            Guid depGuid = Guid.Empty;
            if (!string.IsNullOrEmpty(depositoryId) && Guid.TryParse(depositoryId, out var parsedGuid) && parsedGuid != Guid.Empty)
            {
                depGuid = parsedGuid;
            }
            else
            {
                var defDep = await db.Depositories.AsNoTracking().FirstOrDefaultAsync(d => !d.IsDisabled, ct);
                if (defDep != null) depGuid = defDep.Id;
                else return Results.BadRequest("No active depository found");
            }

            var defCur = await db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => c.IsDefault && !c.IsDisabled, ct)
                         ?? await db.Currencies.AsNoTracking().FirstOrDefaultAsync(ct);
            Guid defaultCurrencyId = defCur?.Id ?? Guid.Empty;

            const string sql = """
                WITH def_cur AS (
                    SELECT @defCurId::uuid AS id
                ),
                raw_funds AS (
                    SELECT fl.amount AS amt, (f.funds_slip_type IN ('Collection', 'FundsOpening', 'FundsRevisionExceed')) AS is_inc
                    FROM funds_slip_lines fl
                    JOIN funds_slips f ON f.id = fl.funds_slip_id
                    WHERE f.depository_id = @dep AND f.date < @dt AND f.is_disabled = false

                    UNION ALL

                    SELECT el.amount AS amt, false AS is_inc
                    FROM expense_slip_lines el
                    JOIN expense_slips e ON e.id = el.expense_slip_id
                    WHERE e.depository_id = @dep AND e.date < @dt AND e.is_disabled = false

                    UNION ALL

                    SELECT tl.amount AS amt, false AS is_inc
                    FROM funds_transfer_lines tl
                    JOIN funds_transfers t ON t.id = tl.funds_transfer_id
                    WHERE t.from_depository_id = @dep AND t.date < @dt AND t.is_disabled = false

                    UNION ALL

                    SELECT tl.received_amount AS amt, true AS is_inc
                    FROM funds_transfer_lines tl
                    JOIN funds_transfers t ON t.id = tl.funds_transfer_id
                    WHERE t.to_depository_id = @dep AND t.date < @dt AND t.is_disabled = false

                    UNION ALL

                    SELECT 
                        (ip.amount * CASE 
                            WHEN i.display_currency_id IS NULL OR i.display_currency_id = (SELECT id FROM def_cur) THEN 1.0
                            ELSE COALESCE(cr.multiplier / NULLIF(cr.divider, 0), 1.0)
                        END)::numeric(18,4) AS amt, 
                        (i.invoice_type IN ('Sales', 'PurchaseReturn')) AS is_inc
                    FROM invoice_payments ip
                    JOIN invoices i ON i.id = ip.invoice_id
                    LEFT JOIN LATERAL (
                        SELECT r.multiplier, r.divider 
                        FROM currency_rates r 
                        WHERE r.currency_id = i.display_currency_id AND r.valid_from <= i.date 
                        ORDER BY r.valid_from DESC LIMIT 1
                    ) cr ON true
                    WHERE i.depository_id = @dep AND i.date < @dt AND i.is_completed = true AND i.is_disabled = false
                )
                SELECT 
                    @dep::text AS "DepositoryId",
                    COALESCE(SUM(CASE WHEN is_inc THEN amt ELSE 0 END), 0)::numeric(18,4) AS "Income",
                    COALESCE(SUM(CASE WHEN NOT is_inc THEN amt ELSE 0 END), 0)::numeric(18,4) AS "Expense"
                FROM raw_funds;
                """;

            var connStr = db.Database.GetConnectionString();
            await using var conn = new NpgsqlConnection(connStr);
            var res = await conn.QueryFirstOrDefaultAsync<FundsBalanceDto>(new CommandDefinition(
                sql,
                new { dep = depGuid, dt = date, defCurId = defaultCurrencyId },
                cancellationToken: ct));

            return Results.Ok(res ?? new FundsBalanceDto { DepositoryId = depositoryId });
        });
    }

    #region Global Facets Aggregator
    private static async Task<(Dictionary<string, int> Groups, Dictionary<string, int> Tags)> GetGlobalGroupsAndTagsAsync(
        MermerDbContext db, CancellationToken ct)
    {
        var groupDict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var tagDict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var slipGroups = await db.FundsSlips.AsNoTracking()
            .Where(s => !string.IsNullOrEmpty(s.Group) && !s.IsDisabled)
            .GroupBy(s => s.Group!)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        foreach (var item in slipGroups)
        {
            var k = item.Key.Trim();
            groupDict[k] = groupDict.GetValueOrDefault(k, 0) + item.Count;
        }

        var slipTags = await db.FundsSlips.AsNoTracking()
            .Where(s => s.Tags != null && !s.IsDisabled)
            .Select(s => s.Tags!)
            .ToListAsync(ct);

        foreach (var t in slipTags.SelectMany(x => x).Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var k = t.Trim();
            tagDict[k] = tagDict.GetValueOrDefault(k, 0) + 1;
        }

        var expenseGroups = await db.ExpenseSlips.AsNoTracking()
            .Where(s => !string.IsNullOrEmpty(s.GroupName) && !s.IsDisabled)
            .GroupBy(s => s.GroupName!)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        foreach (var item in expenseGroups)
        {
            var k = item.Key.Trim();
            groupDict[k] = groupDict.GetValueOrDefault(k, 0) + item.Count;
        }

        var expenseTags = await db.ExpenseSlips.AsNoTracking()
            .Where(s => s.Tags != null && !s.IsDisabled)
            .Select(s => s.Tags!)
            .ToListAsync(ct);

        foreach (var t in expenseTags.SelectMany(x => x).Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var k = t.Trim();
            tagDict[k] = tagDict.GetValueOrDefault(k, 0) + 1;
        }

        var transferGroups = await db.FundsTransfers.AsNoTracking()
            .Where(s => !string.IsNullOrEmpty(s.Group) && !s.IsDisabled)
            .GroupBy(s => s.Group!)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        foreach (var item in transferGroups)
        {
            var k = item.Key.Trim();
            groupDict[k] = groupDict.GetValueOrDefault(k, 0) + item.Count;
        }

        var transferTags = await db.FundsTransfers.AsNoTracking()
            .Where(s => s.Tags != null && !s.IsDisabled)
            .Select(s => s.Tags!)
            .ToListAsync(ct);

        foreach (var t in transferTags.SelectMany(x => x).Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var k = t.Trim();
            tagDict[k] = tagDict.GetValueOrDefault(k, 0) + 1;
        }

        var regGroups = await db.DailyFundsRegisteries.AsNoTracking()
            .Where(s => !string.IsNullOrEmpty(s.GroupName) && !s.IsDisabled)
            .GroupBy(s => s.GroupName!)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        foreach (var item in regGroups)
        {
            var k = item.Key.Trim();
            groupDict[k] = groupDict.GetValueOrDefault(k, 0) + item.Count;
        }

        var regTags = await db.DailyFundsRegisteries.AsNoTracking()
            .Where(s => s.Tags != null && !s.IsDisabled)
            .Select(s => s.Tags!)
            .ToListAsync(ct);

        foreach (var t in regTags.SelectMany(x => x).Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var k = t.Trim();
            tagDict[k] = tagDict.GetValueOrDefault(k, 0) + 1;
        }

        return (groupDict, tagDict);
    }
    #endregion

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

    public class FundsBalanceByTypeWithBalanceDto
    {
        public string DepositoryId { get; set; } = string.Empty;
        public decimal StartingBalance { get; set; }
        public decimal Income { get; set; }
        public decimal Expense { get; set; }

        public decimal Balance => Income - Expense;

        public decimal FundsOpening { get; set; }
        public decimal FundsRevisionExceed { get; set; }
        public decimal FundsRevisionDeficit { get; set; }
        public decimal Collection { get; set; }
        public decimal Payment { get; set; }
        public decimal ExpenseSlip { get; set; }
        public decimal FundsTransferSource { get; set; }
        public decimal FundsTransferDestination { get; set; }
        public decimal Sales { get; set; }
        public decimal SalesReturn { get; set; }
        public decimal Purchase { get; set; }
        public decimal PurchaseReturn { get; set; }
    }

    public class FundsBalanceDto
    {
        public string DepositoryId { get; set; } = string.Empty;
        public decimal Income { get; set; }
        public decimal Expense { get; set; }
        public decimal Balance => Income - Expense;
    }
}
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
                int take = limit.HasValue ? Math.Clamp(limit.Value, 1, 1000) : 200;
                int skip = offset.GetValueOrDefault(0);

                var startDate = EnsureUtc(from ?? DateTime.UtcNow.AddMonths(-3));
                var endDate = EnsureUtc(till ?? DateTime.UtcNow);

                var query = db.FundsSlips
                    .AsNoTracking()
                    .Where(s => s.Date >= startDate && s.Date <= endDate && !s.IsDisabled);

                query = query.Where(s => s.FundsSlipType != null &&
                                         (s.FundsSlipType.ToLower() == "payment" ||
                                          s.FundsSlipType.ToLower() == "collection"));

                if (Guid.TryParse(depositoryId, out var depGuid))
                    query = query.Where(s => s.DepositoryId == depGuid);

                if (Guid.TryParse(partnerId, out var partGuid))
                    query = query.Where(s => s.PartnerId == partGuid);

                var rawSlips = await query
                    .OrderByDescending(s => s.Date)
                    .Skip(skip)
                    .Take(take)
                    .Select(s => new
                    {
                        Id = s.Id.ToString(),
                        Code = s.Code ?? string.Empty,
                        Date = s.Date,
                        RawType = s.FundsSlipType,
                        OfficeId = s.OfficeId.HasValue ? s.OfficeId.Value.ToString() : null,
                        DepositoryId = s.DepositoryId.HasValue ? s.DepositoryId.Value.ToString() : null,
                        PartnerId = s.PartnerId.HasValue ? s.PartnerId.Value.ToString() : null,
                        DisplayCurrencyId = s.DisplayCurrencyId.HasValue ? s.DisplayCurrencyId.Value.ToString() : null,
                        UserName = s.UserName,
                        IsCompleted = s.IsCompleted,
                        IsDisabled = s.IsDisabled,
                        Group = s.Group ?? string.Empty,
                        Tags = s.Tags,
                        Description = s.Description ?? string.Empty,
                        TotalAmount = s.Lines.Sum(l => (decimal?)l.Amount) ?? 0m
                    })
                    .ToListAsync(ct);

                var defaultCurrency = await db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => c.IsDefault, ct)
                                      ?? await db.Currencies.AsNoTracking().FirstOrDefaultAsync(ct);
                var defaultCurrencyId = defaultCurrency?.Id.ToString();

                var result = rawSlips.Select(s =>
                {
                    bool isPayment = !string.IsNullOrEmpty(s.RawType) &&
                        (s.RawType.Equals("Payment", StringComparison.OrdinalIgnoreCase) ||
                         s.RawType.Equals("Expense", StringComparison.OrdinalIgnoreCase));

                    string billTypeStr = isPayment ? "Payment" : "Collection";
                    int billTypeInt = isPayment ? 1 : 0;
                    var docCurrencyId = s.DisplayCurrencyId ?? defaultCurrencyId;

                    return new
                    {
                        s.Id,
                        s.Code,
                        s.Date,
                        FundsSlipType = billTypeStr,
                        SlipType = billTypeStr,
                        BillType = billTypeInt,
                        Type = billTypeStr,
                        s.OfficeId,
                        s.DepositoryId,
                        s.PartnerId,
                        DisplayCurrencyId = docCurrencyId,
                        CurrencyId = docCurrencyId,
                        CurrencyConvertions = Array.Empty<object>(),
                        s.UserName,
                        s.IsCompleted,
                        s.IsDisabled,
                        Group = s.Group ?? string.Empty,
                        Tags = s.Tags != null ? s.Tags.ToList() : new List<string>(),
                        Description = s.Description ?? string.Empty,
                        Total = s.TotalAmount,
                        DisplayTotal = s.TotalAmount,
                        ActionTotal = s.TotalAmount,
                        Amount = s.TotalAmount,
                        Lines = Array.Empty<object>()
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

                var startDate = from.HasValue ? from.Value.ToUniversalTime() : DateTime.SpecifyKind(new DateTime(2000, 1, 1), DateTimeKind.Utc);
                var endDate = till.HasValue ? till.Value.ToUniversalTime() : DateTime.SpecifyKind(new DateTime(2099, 12, 31), DateTimeKind.Utc);

                var defaultCurrency = await db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => c.IsDefault, ct)
                                      ?? await db.Currencies.AsNoTracking().FirstOrDefaultAsync(ct);
                var defaultCurrencyId = defaultCurrency?.Id.ToString();

                var allConvertions = await GetCurrencyConvertionsAsync(db, DateTime.UtcNow, ct);

                var query = db.FundsSlips
                    .AsNoTracking()
                    .Where(s => s.Date >= startDate && s.Date <= endDate && !s.IsDisabled);

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
                        Date = s.Date,
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

            string code = GetStringProperty(root, "code", "Code") ?? $"DOC-{DateTime.UtcNow:yyMMddHHmmss}";
            string? depIdStr = GetStringProperty(root, "depositoryId", "DepositoryId");
            Guid? depId = Guid.TryParse(depIdStr, out var parsedDep) ? parsedDep : null;

            string? partIdStr = GetStringProperty(root, "partnerId", "PartnerId");
            Guid? partId = Guid.TryParse(partIdStr, out var parsedPart) ? parsedPart : null;

            string? offIdStr = GetStringProperty(root, "officeId", "OfficeId");
            Guid? offId = Guid.TryParse(offIdStr, out var parsedOff) ? parsedOff : null;

            string? dispCurStr = GetStringProperty(root, "displayCurrencyId", "DisplayCurrencyId", "currencyId", "CurrencyId");
            Guid? dispCurId = Guid.TryParse(dispCurStr, out var parsedDispCur) ? parsedDispCur : null;

            if (dispCurId == null)
            {
                var defCur = await db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => c.IsDefault)
                             ?? await db.Currencies.AsNoTracking().FirstOrDefaultAsync();
                dispCurId = defCur?.Id;
            }

            DateTime date = DateTime.UtcNow;
            string? dateStr = GetStringProperty(root, "date", "Date");
            if (!string.IsNullOrEmpty(dateStr) && DateTime.TryParse(dateStr, out var parsedDate))
            {
                date = parsedDate.ToUniversalTime();
            }

            string rawType = GetStringProperty(root, "fundsSlipType", "FundsSlipType", "billType", "BillType", "slipType", "SlipType", "type", "Type") ?? "FundsOpening";

            var linesList = new List<FundsSlipLineEntity>();
            if (TryGetPropertyCaseInsensitive(root, "lines", out var linesProp) && linesProp.ValueKind == JsonValueKind.Array)
            {
                int sortOrder = 0;
                foreach (var lineJson in linesProp.EnumerateArray())
                {
                    decimal lineAmount = GetDecimalProperty(lineJson, "amount", "Amount", "total", "Total", "value", "Value");
                    string? curIdStr = GetStringProperty(lineJson, "currencyId", "CurrencyId");
                    Guid? currencyGuid = Guid.TryParse(curIdStr, out var cG) && cG != Guid.Empty ? cG : dispCurId;

                    string? lineIdStr = GetStringProperty(lineJson, "id", "Id");
                    Guid lineGuid = Guid.TryParse(lineIdStr, out var lG) && lG != Guid.Empty ? lG : Guid.NewGuid();

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
                existing.Date = date;
                existing.FundsSlipType = rawType;
                existing.DepositoryId = depId;
                existing.PartnerId = partId;
                existing.OfficeId = offId;
                existing.DisplayCurrencyId = dispCurId;
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

        financeGroup.MapGet("/slips/facets", async (string? fields, MermerDbContext db, CancellationToken ct) =>
        {
            var connStr = db.Database.GetConnectionString()!;

            // 1. Если запрос пришел из карточки документа за подсказками полей (GroupNames / TagNames)
            if (!string.IsNullOrEmpty(fields))
            {
                var facets = await FacetsHelper.GetEntityFacetsAsync(connStr, "funds_slips", fields, ct);
                return Results.Ok(facets);
            }

            // 2. Если запрос пришел из журнала списка документов (нужны счетчики дат и группы)
            var todayUtc = DateTime.UtcNow.Date;
            var weekStart = todayUtc.AddDays(-7);
            var monthStart = new DateTime(todayUtc.Year, todayUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            var countToday = await db.FundsSlips.CountAsync(s => !s.IsDisabled && s.Date >= todayUtc, ct);
            var countWeek = await db.FundsSlips.CountAsync(s => !s.IsDisabled && s.Date >= weekStart, ct);
            var countMonth = await db.FundsSlips.CountAsync(s => !s.IsDisabled && s.Date >= monthStart, ct);
            var countAll = await db.FundsSlips.CountAsync(s => !s.IsDisabled, ct);

            var entityFacets = await FacetsHelper.GetEntityFacetsAsync(connStr, "funds_slips", "GroupNames,TagNames,Group,Tags", ct);
            var groups = entityFacets.TryGetValue("Group", out var g) ? g : new Dictionary<string, int>();

            return Results.Ok(new Dictionary<string, object>
            {
                ["Group"] = groups,
                ["GroupNames"] = groups,
                ["Date"] = new Dictionary<string, int>
        {
            { "#Today", countToday },
            { "#This Week", countWeek },
            { "#This Month", countMonth },
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
                Date = s.Date,
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

        billsGroup.MapGet("/next-code", async (MermerDbContext db) =>
        {
            var count = await db.FundsSlips.CountAsync();
            return Results.Ok(new { code = $"DOC-{DateTime.UtcNow:yyMMdd}{(count + 1):D4}" });
        });

        billsGroup.MapGet("/facets", async (string? fields, MermerDbContext db, CancellationToken ct) =>
        {
            var connStr = db.Database.GetConnectionString()!;
            var facets = await FacetsHelper.GetEntityFacetsAsync(connStr, "funds_slips", fields, ct);
            return Results.Ok(facets);
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
                Date = s.Date,
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

        transferGroup.MapGet("/facets", async (string? fields, MermerDbContext db, CancellationToken ct) =>
        {
            var connStr = db.Database.GetConnectionString()!;
            var facets = await FacetsHelper.GetEntityFacetsAsync(connStr, "funds_transfers", fields, ct);
            return Results.Ok(facets);
        });

        transferGroup.MapGet("", async (DateTime? from, DateTime? till, string? sourceDepositoryId, string? destinationDepositoryId, int? limit, int? offset, MermerDbContext db, CancellationToken ct) =>
        {
            int take = limit.HasValue ? Math.Clamp(limit.Value, 1, 1000) : 200;
            int skip = offset.GetValueOrDefault(0);

            var startDate = from ?? DateTime.UtcNow.AddMonths(-3);
            var endDate = till ?? DateTime.UtcNow;

            var defaultCurrency = await db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => c.IsDefault, ct)
                                  ?? await db.Currencies.AsNoTracking().FirstOrDefaultAsync(ct);
            var defaultCurrencyId = defaultCurrency?.Id.ToString();
            var allConvertions = await GetCurrencyConvertionsAsync(db, DateTime.UtcNow, ct);

            var query = db.FundsTransfers
                .AsNoTracking()
                .Where(t => t.Date >= startDate && t.Date <= endDate && !t.IsDisabled);

            if (Guid.TryParse(sourceDepositoryId, out var srcGuid))
                query = query.Where(t => t.FromDepositoryId == srcGuid);

            if (Guid.TryParse(destinationDepositoryId, out var dstGuid))
                query = query.Where(t => t.ToDepositoryId == dstGuid);

            var transfers = await query
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

                return new
                {
                    Id = t.Id.ToString(),
                    Code = t.Code ?? string.Empty,
                    Date = t.Date,
                    Type = "FundsTransfer",
                    DepositoryId = t.FromDepositoryId?.ToString(),
                    DestinationDepositoryId = t.ToDepositoryId?.ToString(),
                    DisplayCurrencyId = docCurrencyId,
                    CurrencyId = docCurrencyId,
                    CurrencyConvertions = allConvertions,
                    UserName = t.UserName,
                    IsCompleted = t.IsCompleted,
                    IsDisabled = t.IsDisabled,
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

        transferGroup.MapGet("/{id}", async (string id, MermerDbContext db, CancellationToken ct) =>
        {
            if (!Guid.TryParse(id, out var guid)) return Results.NotFound();
            var t = await db.FundsTransfers.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == guid, ct);
            if (t == null) return Results.NotFound();

            var convertions = await GetCurrencyConvertionsAsync(db, t.Date, ct);
            var docCurrencyId = t.DisplayCurrencyId?.ToString() ?? (await db.Currencies.FirstOrDefaultAsync(c => c.IsDefault))?.Id.ToString();
            decimal totalSent = t.Lines != null && t.Lines.Any() ? t.Lines.Sum(l => l.Amount) : 0m;
            decimal totalReceived = t.Lines != null && t.Lines.Any() ? t.Lines.Sum(l => l.ReceivedAmount) : 0m;

            return Results.Ok(new
            {
                Id = t.Id.ToString(),
                Code = t.Code ?? string.Empty,
                Date = t.Date,
                Type = "FundsTransfer",
                DepositoryId = t.FromDepositoryId?.ToString(),
                DestinationDepositoryId = t.ToDepositoryId?.ToString(),
                DisplayCurrencyId = docCurrencyId,
                CurrencyId = docCurrencyId,
                CurrencyConvertions = convertions,
                UserName = t.UserName,
                IsCompleted = t.IsCompleted,
                IsDisabled = t.IsDisabled,
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

            string code = GetStringProperty(root, "code", "Code") ?? $"TR-{DateTime.UtcNow:yyMMddHHmmss}";
            string? fromDepIdStr = GetStringProperty(root, "depositoryId", "DepositoryId", "fromDepositoryId", "FromDepositoryId");
            Guid? fromDepId = Guid.TryParse(fromDepIdStr, out var pFromDep) ? pFromDep : null;

            string? toDepIdStr = GetStringProperty(root, "destinationDepositoryId", "DestinationDepositoryId", "toDepositoryId", "ToDepositoryId");
            Guid? toDepId = Guid.TryParse(toDepIdStr, out var pToDep) ? pToDep : null;

            string? dispCurStr = GetStringProperty(root, "displayCurrencyId", "DisplayCurrencyId", "currencyId", "CurrencyId");
            Guid? dispCurId = Guid.TryParse(dispCurStr, out var pCur) ? pCur : null;

            DateTime date = DateTime.UtcNow;
            string? dateStr = GetStringProperty(root, "date", "Date");
            if (!string.IsNullOrEmpty(dateStr) && DateTime.TryParse(dateStr, out var pDate))
                date = pDate.ToUniversalTime();

            var tagsList = ExtractTagsFromRawJson(root);

            var linesList = new List<FundsTransferLineEntity>();
            if (TryGetPropertyCaseInsensitive(root, "lines", out var linesProp) && linesProp.ValueKind == JsonValueKind.Array)
            {
                int sortOrder = 0;
                foreach (var lJson in linesProp.EnumerateArray())
                {
                    decimal amount = GetDecimalProperty(lJson, "amount", "Amount", "total", "Total");
                    decimal receivedAmount = GetDecimalProperty(lJson, "receivedAmount", "ReceivedAmount");
                    if (receivedAmount == 0 && amount > 0) receivedAmount = amount;

                    string? curIdStr = GetStringProperty(lJson, "currencyId", "CurrencyId");
                    Guid? currencyGuid = Guid.TryParse(curIdStr, out var cG) ? cG : dispCurId;

                    string? lineIdStr = GetStringProperty(lJson, "id", "Id");
                    Guid lineGuid = Guid.TryParse(lineIdStr, out var lG) && lG != Guid.Empty ? lG : Guid.NewGuid();

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
                existing.Date = date;
                existing.FromDepositoryId = fromDepId;
                existing.ToDepositoryId = toDepId;
                existing.DisplayCurrencyId = dispCurId;
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
        transferGroup.MapPut("/{id}", saveTransferHandler);

        // =========================================================================
        // 7. РОУТЫ DAILY FUNDS REGISTRIES (ВОССТАНОВЛЕНО ПОЛНОСТЬЮ)
        // =========================================================================
        var registryGroup = routes.MapGroup("/api/finance/registeries").WithTags("DailyFundsRegistries");

        registryGroup.MapGet("", async (DateTime? from, DateTime? till, string? depositoryId, int? limit, int? offset, MermerDbContext db, CancellationToken ct) =>
        {
            int take = limit.HasValue ? Math.Clamp(limit.Value, 1, 1000) : 200;
            int skip = offset.GetValueOrDefault(0);

            var startDate = from ?? DateTime.UtcNow.AddMonths(-3);
            var endDate = till ?? DateTime.UtcNow;

            var defaultCurrency = await db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => c.IsDefault, ct)
                                  ?? await db.Currencies.AsNoTracking().FirstOrDefaultAsync(ct);
            var defaultCurrencyId = defaultCurrency?.Id.ToString();
            var allConvertions = await GetCurrencyConvertionsAsync(db, DateTime.UtcNow, ct);

            var query = db.DailyFundsRegisteries
                .AsNoTracking()
                .Where(r => r.Date >= startDate && r.Date <= endDate && !r.IsDisabled);

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
                    Date = r.Date,
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
                Date = r.Date,
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

            string code = GetStringProperty(root, "code", "Code") ?? $"REG-{DateTime.UtcNow:yyMMddHHmmss}";
            string? depIdStr = GetStringProperty(root, "depositoryId", "DepositoryId");
            Guid? depId = Guid.TryParse(depIdStr, out var pDep) ? pDep : null;

            string? dispCurStr = GetStringProperty(root, "displayCurrencyId", "DisplayCurrencyId", "currencyId", "CurrencyId");
            Guid? dispCurId = Guid.TryParse(dispCurStr, out var pCur) ? pCur : null;

            string? userIdStr = GetStringProperty(root, "userId", "UserId");
            Guid? userId = Guid.TryParse(userIdStr, out var pUser) ? pUser : null;

            DateTime date = DateTime.UtcNow;
            string? dateStr = GetStringProperty(root, "date", "Date");
            if (!string.IsNullOrEmpty(dateStr) && DateTime.TryParse(dateStr, out var pDate))
                date = pDate.ToUniversalTime();

            var tagsList = ExtractTagsFromRawJson(root);
            string groupName = GetStringProperty(root, "group", "Group", "groupName", "GroupName") ?? "";
            string description = GetStringProperty(root, "description", "Description") ?? "";

            var linesList = new List<DailyFundsRegisteryLineEntity>();
            if (TryGetPropertyCaseInsensitive(root, "lines", out var linesProp) && linesProp.ValueKind == JsonValueKind.Array)
            {
                int sortOrder = 0;
                foreach (var lJson in linesProp.EnumerateArray())
                {
                    decimal amount = GetDecimalProperty(lJson, "amount", "Amount", "total", "Total");
                    string? curIdStr = GetStringProperty(lJson, "currencyId", "CurrencyId");
                    Guid? currencyGuid = Guid.TryParse(curIdStr, out var cG) ? cG : dispCurId;

                    string? lineIdStr = GetStringProperty(lJson, "id", "Id");
                    Guid lineGuid = Guid.TryParse(lineIdStr, out var lG) && lG != Guid.Empty ? lG : Guid.NewGuid();

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
                existing.Date = date;
                existing.UserId = userId;
                existing.DepositoryId = depId;
                existing.DisplayCurrencyId = dispCurId;
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

        registryGroup.MapGet("/facets", async (HttpContext context, MermerDbContext db, CancellationToken ct) =>
        {
            var todayUtc = DateTime.UtcNow.Date;
            var weekStart = todayUtc.AddDays(-7);
            var monthStart = new DateTime(todayUtc.Year, todayUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            var countToday = await db.DailyFundsRegisteries.CountAsync(r => !r.IsDisabled && r.Date >= todayUtc, ct);
            var countWeek = await db.DailyFundsRegisteries.CountAsync(r => !r.IsDisabled && r.Date >= weekStart, ct);
            var countMonth = await db.DailyFundsRegisteries.CountAsync(r => !r.IsDisabled && r.Date >= monthStart, ct);
            var countAll = await db.DailyFundsRegisteries.CountAsync(r => !r.IsDisabled, ct);

            var groups = await db.DailyFundsRegisteries.AsNoTracking()
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

        // =========================================================================
        // 8. ЖУРНАЛ ДВИЖЕНИЯ ДЕНЕЖНЫХ СРЕДСТВ С ЛИМИТОМ
        // =========================================================================
        routes.MapGet("/api/finance/actions", async (DateTime? from, DateTime? till, string? currencyId, int? limit, HttpRequest req, MermerDbContext db, CancellationToken ct) =>
        {
            int take = limit.HasValue ? Math.Clamp(limit.Value, 1, 1000) : 300;
            var startDate = from ?? DateTime.UtcNow.AddMonths(-1);
            var endDate = till ?? DateTime.UtcNow;

            var depIds = req.Query["depositoryId"]
                .Select(x => Guid.TryParse(x, out var g) ? (Guid?)g : null)
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .ToList();

            Guid? filterCurrencyGuid = Guid.TryParse(currencyId, out var cGuid) ? cGuid : null;

            var actions = new List<object>();

            var slipsQuery = db.FundsSlips
                .Include(s => s.Lines)
                .AsNoTracking()
                .Where(s => s.Date >= startDate && s.Date <= endDate && !s.IsDisabled);

            if (depIds.Any())
                slipsQuery = slipsQuery.Where(s => s.DepositoryId.HasValue && depIds.Contains(s.DepositoryId.Value));

            var slips = await slipsQuery.OrderByDescending(s => s.Date).Take(take).ToListAsync(ct);
            foreach (var s in slips)
            {
                foreach (var line in s.Lines ?? Enumerable.Empty<FundsSlipLineEntity>())
                {
                    if (filterCurrencyGuid.HasValue && line.CurrencyId != filterCurrencyGuid) continue;
                    bool isIncome = s.FundsSlipType == "Collection" || s.FundsSlipType == "FundsOpening" || s.FundsSlipType == "FundsRevisionExceed";

                    actions.Add(new
                    {
                        TransactionId = s.Id.ToString(),
                        TransactionCode = s.Code ?? "",
                        TransactionDate = s.Date,
                        TransactionType = s.FundsSlipType,
                        TransactionUserId = s.UserId?.ToString(),
                        TransactionUserName = s.UserName,
                        TransactionIsCompleted = s.IsCompleted,
                        TransactionIsDisabled = s.IsDisabled,
                        TransactionGroup = s.Group ?? "",
                        TransactionTags = s.Tags ?? Array.Empty<string>(),
                        ActionRelatedPartnerId = s.PartnerId?.ToString(),
                        ActionRelatedDepositoryId = (string?)null,
                        ActionDepositoryId = s.DepositoryId?.ToString(),
                        ActionCurrencyId = line.CurrencyId?.ToString(),
                        ActionAmount = line.Amount,
                        ActionIncome = isIncome ? line.Amount : 0m,
                        ActionExpense = !isIncome ? line.Amount : 0m
                    });
                }
            }

            return Results.Ok(actions);
        }).WithTags("FundsActions");

        // =========================================================================
        // 9. БАЛАНСЫ КАСС (SQL-АГРЕГАЦИЯ НА СТОРОНЕ POSTGRESQL)
        // =========================================================================
        var balancesGroup = routes.MapGroup("/api/finance/balances").WithTags("FundsBalances");

        balancesGroup.MapGet("/bytype", async (string? depositoryId, DateTime? from, DateTime? till, MermerDbContext db, CancellationToken ct) =>
        {
            var start = from ?? DateTime.UtcNow.AddMonths(-1);
            var end = till ?? DateTime.UtcNow;

            var depGuids = new List<Guid>();
            if (!string.IsNullOrEmpty(depositoryId) && depositoryId != "null" && Guid.TryParse(depositoryId, out var parsedDep))
            {
                depGuids.Add(parsedDep);
            }
            else
            {
                depGuids = await db.Depositories.AsNoTracking().Where(d => !d.IsDisabled).Select(d => d.Id).ToListAsync(ct);
            }

            const string sql = """
                WITH raw_funds AS (
                    -- Кассовые ордера
                    SELECT 
                        f.depository_id, 
                        f.date, 
                        f.funds_slip_type AS type, 
                        fl.amount AS amt,
                        (f.funds_slip_type IN ('Collection', 'FundsOpening', 'FundsRevisionExceed')) AS is_inc
                    FROM funds_slip_lines fl
                    JOIN funds_slips f ON f.id = fl.funds_slip_id
                    WHERE f.depository_id = ANY(@deps) AND f.is_disabled = false

                    UNION ALL

                    -- Расходы
                    SELECT 
                        e.depository_id, 
                        e.date, 
                        'ExpenseSlip' AS type, 
                        el.amount AS amt,
                        false AS is_inc
                    FROM expense_slip_lines el
                    JOIN expense_slips e ON e.id = el.expense_slip_id
                    WHERE e.depository_id = ANY(@deps) AND e.is_disabled = false

                    UNION ALL

                    -- Переводы (расход с кассы-источника)
                    SELECT 
                        t.from_depository_id AS depository_id, 
                        t.date, 
                        'FundsTransferSource' AS type, 
                        tl.amount AS amt,
                        false AS is_inc
                    FROM funds_transfer_lines tl
                    JOIN funds_transfers t ON t.id = tl.funds_transfer_id
                    WHERE t.from_depository_id = ANY(@deps) AND t.is_disabled = false

                    UNION ALL

                    -- Переводы (приход на кассу-получатель)
                    SELECT 
                        t.to_depository_id AS depository_id, 
                        t.date, 
                        'FundsTransferDestination' AS type, 
                        tl.received_amount AS amt,
                        true AS is_inc
                    FROM funds_transfer_lines tl
                    JOIN funds_transfers t ON t.id = tl.funds_transfer_id
                    WHERE t.to_depository_id = ANY(@deps) AND t.is_disabled = false

                    UNION ALL

                    -- Оплаты счетов через платежи (invoice_payments)
                    SELECT 
                        i.depository_id, 
                        i.date, 
                        i.invoice_type AS type, 
                        ip.amount AS amt,
                        (i.invoice_type IN ('Sales', 'PurchaseReturn')) AS is_inc
                    FROM invoice_payments ip
                    JOIN invoices i ON i.id = ip.invoice_id
                    WHERE i.depository_id = ANY(@deps) AND i.is_completed = true AND i.is_disabled = false
                )
                SELECT 
                    d.id::text AS "DepositoryId",
                    COALESCE(SUM(CASE WHEN rf.date < @start THEN (CASE WHEN rf.is_inc THEN rf.amt ELSE -rf.amt END) ELSE 0 END), 0)::numeric(18,4) AS "StartingBalance",
                    COALESCE(SUM(CASE WHEN rf.date >= @start AND rf.date <= @end AND rf.is_inc THEN rf.amt ELSE 0 END), 0)::numeric(18,4) AS "Income",
                    COALESCE(SUM(CASE WHEN rf.date >= @start AND rf.date <= @end AND NOT rf.is_inc THEN rf.amt ELSE 0 END), 0)::numeric(18,4) AS "Expense",
                    COALESCE(SUM(CASE WHEN rf.date >= @start AND rf.date <= @end AND rf.type = 'FundsOpening' THEN rf.amt ELSE 0 END), 0)::numeric(18,4) AS "FundsOpening",
                    COALESCE(SUM(CASE WHEN rf.date >= @start AND rf.date <= @end AND rf.type = 'FundsRevisionExceed' THEN rf.amt ELSE 0 END), 0)::numeric(18,4) AS "FundsRevisionExceed",
                    COALESCE(SUM(CASE WHEN rf.date >= @start AND rf.date <= @end AND rf.type = 'FundsRevisionDeficit' THEN rf.amt ELSE 0 END), 0)::numeric(18,4) AS "FundsRevisionDeficit",
                    COALESCE(SUM(CASE WHEN rf.date >= @start AND rf.date <= @end AND rf.type = 'Collection' THEN rf.amt ELSE 0 END), 0)::numeric(18,4) AS "Collection",
                    COALESCE(SUM(CASE WHEN rf.date >= @start AND rf.date <= @end AND rf.type = 'Payment' THEN rf.amt ELSE 0 END), 0)::numeric(18,4) AS "Payment",
                    COALESCE(SUM(CASE WHEN rf.date >= @start AND rf.date <= @end AND rf.type = 'ExpenseSlip' THEN rf.amt ELSE 0 END), 0)::numeric(18,4) AS "ExpenseSlip",
                    COALESCE(SUM(CASE WHEN rf.date >= @start AND rf.date <= @end AND rf.type = 'FundsTransferSource' THEN rf.amt ELSE 0 END), 0)::numeric(18,4) AS "FundsTransferSource",
                    COALESCE(SUM(CASE WHEN rf.date >= @start AND rf.date <= @end AND rf.type = 'FundsTransferDestination' THEN rf.amt ELSE 0 END), 0)::numeric(18,4) AS "FundsTransferDestination",
                    COALESCE(SUM(CASE WHEN rf.date >= @start AND rf.date <= @end AND rf.type = 'Sales' THEN rf.amt ELSE 0 END), 0)::numeric(18,4) AS "Sales",
                    COALESCE(SUM(CASE WHEN rf.date >= @start AND rf.date <= @end AND rf.type = 'SalesReturn' THEN rf.amt ELSE 0 END), 0)::numeric(18,4) AS "SalesReturn",
                    COALESCE(SUM(CASE WHEN rf.date >= @start AND rf.date <= @end AND rf.type = 'Purchase' THEN rf.amt ELSE 0 END), 0)::numeric(18,4) AS "Purchase",
                    COALESCE(SUM(CASE WHEN rf.date >= @start AND rf.date <= @end AND rf.type = 'PurchaseReturn' THEN rf.amt ELSE 0 END), 0)::numeric(18,4) AS "PurchaseReturn"
                FROM depositories d
                LEFT JOIN raw_funds rf ON rf.depository_id = d.id
                WHERE d.id = ANY(@deps)
                GROUP BY d.id;
                """;

            var connStr = db.Database.GetConnectionString();
            await using var conn = new NpgsqlConnection(connStr);
            var result = await conn.QueryAsync<FundsBalanceByTypeWithBalanceDto>(new CommandDefinition(
                sql,
                new { deps = depGuids.ToArray(), start, end },
                cancellationToken: ct));

            return Results.Ok(result);
        });

        balancesGroup.MapGet("/todate", async (string depositoryId, DateTime date, MermerDbContext db, CancellationToken ct) =>
        {
            if (!Guid.TryParse(depositoryId, out var depGuid)) return Results.BadRequest();

            const string sql = """
                WITH raw_funds AS (
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

                    SELECT ip.amount AS amt, (i.invoice_type IN ('Sales', 'PurchaseReturn')) AS is_inc
                    FROM invoice_payments ip
                    JOIN invoices i ON i.id = ip.invoice_id
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
                new { dep = depGuid, dt = date },
                cancellationToken: ct));

            return Results.Ok(res ?? new FundsBalanceDto { DepositoryId = depositoryId });
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

    private static DateTime EnsureUtc(DateTime dt)
    {
        if (dt == DateTime.MinValue) return DateTime.SpecifyKind(new DateTime(2000, 1, 1), DateTimeKind.Utc);
        if (dt == DateTime.MaxValue) return DateTime.SpecifyKind(new DateTime(2099, 12, 31), DateTimeKind.Utc);
        return dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();
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
        public decimal Balance => StartingBalance + Income - Expense;
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
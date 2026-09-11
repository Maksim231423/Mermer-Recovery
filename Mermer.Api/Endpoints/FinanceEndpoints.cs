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

public static class FinanceEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null, // Сохраняем PascalCase для WPF клиента
        PropertyNameCaseInsensitive = true
    };

    public static void MapFinanceEndpoints(this IEndpointRouteBuilder routes)
    {
        // 1. СПИСОК ДЛЯ COMMERCE (BILLS)
        Func<DateTime?, DateTime?, string?, string?, MermerDbContext, CancellationToken, Task<IResult>> getBillsHandler =
            async (from, till, depositoryId, partnerId, db, ct) =>
            {
                var startDate = EnsureUtc(from ?? DateTime.UtcNow.AddYears(-15));
                var endDate = EnsureUtc(till ?? DateTime.UtcNow.AddYears(15));

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

                // Оптимизированная выборка сразу в DTO без лишних Include(Lines)
                var rawSlips = await query
                    .OrderByDescending(s => s.Date)
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
                        // Считаем сумму строк сразу в БД одним SQL-выражением
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
                        Group = string.IsNullOrWhiteSpace(s.Group) ? "Общие" : s.Group, // Чтобы GroupIndex="0" не спотыкался о пустые значения
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

        // 2. СПИСОК ДЛЯ FINANCE (FUNDS SLIPS)
        Func<DateTime?, DateTime?, string?, string?, MermerDbContext, CancellationToken, Task<IResult>> getFundsSlipsHandler =
            async (from, till, depositoryId, partnerId, db, ct) =>
            {
                var startDate = from.HasValue ? from.Value.ToUniversalTime() : DateTime.SpecifyKind(new DateTime(2000, 1, 1), DateTimeKind.Utc);
                var endDate = till.HasValue ? till.Value.ToUniversalTime() : DateTime.SpecifyKind(new DateTime(2099, 12, 31), DateTimeKind.Utc);

                var defaultCurrency = await db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => c.IsDefault, ct)
                                      ?? await db.Currencies.AsNoTracking().FirstOrDefaultAsync(ct);
                var defaultCurrencyId = defaultCurrency?.Id.ToString();

                var allConvertions = await GetCurrencyConvertionsAsync(db, DateTime.UtcNow, ct);

                var query = db.FundsSlips
                    .Include(s => s.Lines)
                    .AsSplitQuery()
                    .AsNoTracking()
                    .Where(s => s.Date >= startDate && s.Date <= endDate && !s.IsDisabled);

                // Оставляем только внутренние кассовые операции (Opening, Revision)
                query = query.Where(s => s.FundsSlipType != null &&
                                        (s.FundsSlipType.ToLower().Contains("opening") ||
                                         s.FundsSlipType.ToLower().Contains("revision")));

                if (Guid.TryParse(depositoryId, out var depGuid))
                    query = query.Where(s => s.DepositoryId == depGuid);

                if (Guid.TryParse(partnerId, out var partGuid))
                    query = query.Where(s => s.PartnerId == partGuid);

                var slips = await query.OrderByDescending(s => s.Date).ToListAsync(ct);

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

        // 3. СОХРАНЕНИЕ FUNDS SLIPS (POST / PUT)
        Func<HttpRequest, MermerDbContext, Task<IResult>> saveSlipHandler = async (request, db) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync();

            if (string.IsNullOrEmpty(body))
                return Results.BadRequest("Empty body");

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

        // 4. РОУТЫ FINANCE
        var financeGroup = routes.MapGroup("/api/finance").WithTags("Finance");
        financeGroup.MapGet("/slips", getFundsSlipsHandler);
        financeGroup.MapPost("/slips", saveSlipHandler);
        financeGroup.MapPut("/slips/{id}", saveSlipHandler);

        financeGroup.MapGet("/slips/facets", async (HttpContext context, MermerDbContext db, CancellationToken ct) =>
        {
            string? fields = context.Request.Query["fields"].ToString();
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
                    var groups = await db.FundsSlips
                        .AsNoTracking()
                        .Where(x => !string.IsNullOrEmpty(x.Group))
                        .GroupBy(x => x.Group!)
                        .Select(g => new { Key = g.Key, Count = g.Count() })
                        .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

                    result[field] = groups;
                }
                else if (field.Equals("Tags", StringComparison.OrdinalIgnoreCase) || field.Equals("TagNames", StringComparison.OrdinalIgnoreCase))
                {
                    var allTags = await db.FundsSlips
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
                else if (field.Equals("Date", StringComparison.OrdinalIgnoreCase) || field.Equals("transaction", StringComparison.OrdinalIgnoreCase))
                {
                    var now = DateTime.Now.Date;
                    var slips = await db.FundsSlips.AsNoTracking().Where(s => !s.IsDisabled).Select(s => s.Date).ToListAsync(ct);
                    var localDates = slips.Select(d => d.ToLocalTime().Date).ToList();

                    var dateFacets = new Dictionary<string, int>
                    {
                        { "#Today", localDates.Count(d => d == now) },
                        { "#Yesturday", localDates.Count(d => d == now.AddDays(-1)) },
                        { "#This Week", localDates.Count(d => d >= now.AddDays(-7)) },
                        { "#Past Week", localDates.Count(d => d >= now.AddDays(-14) && d < now.AddDays(-7)) },
                        { "#This Month", localDates.Count(d => d.Month == now.Month && d.Year == now.Year) },
                        { "#Past Month", localDates.Count(d => d.Month == now.AddMonths(-1).Month && d.Year == now.AddMonths(-1).Year) },
                        { "#This Year", localDates.Count(d => d.Year == now.Year) },
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

        // 5. РОУТЫ BILLS
        var billsGroup = routes.MapGroup("/api/bills").WithTags("Bills");
        billsGroup.MapGet("", getBillsHandler);
        billsGroup.MapPost("", saveSlipHandler);
        billsGroup.MapPut("/{id}", saveSlipHandler);
        billsGroup.MapGet("/next-code", async (MermerDbContext db) =>
        {
            var count = await db.FundsSlips.CountAsync();
            return Results.Ok(new { code = $"DOC-{DateTime.UtcNow:yyMMdd}{(count + 1):D4}" });
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

        billsGroup.MapGet("/facets", async (HttpContext context, MermerDbContext db, CancellationToken ct) =>
        {
            string? fields = context.Request.Query["fields"].ToString();
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
                    var groups = await db.FundsSlips
                        .AsNoTracking()
                        .Where(x => !string.IsNullOrEmpty(x.Group))
                        .GroupBy(x => x.Group!)
                        .Select(g => new { Key = g.Key, Count = g.Count() })
                        .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

                    result[field] = groups;
                }
                else if (field.Equals("Tags", StringComparison.OrdinalIgnoreCase) || field.Equals("TagNames", StringComparison.OrdinalIgnoreCase))
                {
                    var allTags = await db.FundsSlips
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
                else if (field.Equals("Date", StringComparison.OrdinalIgnoreCase) || field.Equals("transaction", StringComparison.OrdinalIgnoreCase))
                {
                    var now = DateTime.Now.Date;
                    var slips = await db.FundsSlips.AsNoTracking().Where(s => !s.IsDisabled).Select(s => s.Date).ToListAsync(ct);
                    var localDates = slips.Select(d => d.ToLocalTime().Date).ToList();

                    var dateFacets = new Dictionary<string, int>
                    {
                        { "#Today", localDates.Count(d => d == now) },
                        { "#Yesturday", localDates.Count(d => d == now.AddDays(-1)) },
                        { "#This Week", localDates.Count(d => d >= now.AddDays(-7)) },
                        { "#Past Week", localDates.Count(d => d >= now.AddDays(-14) && d < now.AddDays(-7)) },
                        { "#This Month", localDates.Count(d => d.Month == now.Month && d.Year == now.Year) },
                        { "#Past Month", localDates.Count(d => d.Month == now.AddMonths(-1).Month && d.Year == now.AddMonths(-1).Year) },
                        { "#This Year", localDates.Count(d => d.Year == now.Year) },
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

        // 6. РОУТЫ FUNDS TRANSFERS
        var transferGroup = routes.MapGroup("/api/finance/transfers").WithTags("FundsTransfers");

        transferGroup.MapGet("", async (DateTime? from, DateTime? till, string? sourceDepositoryId, string? destinationDepositoryId, MermerDbContext db, CancellationToken ct) =>
        {
            var startDate = from ?? DateTime.MinValue;
            var endDate = till ?? DateTime.MaxValue;

            var defaultCurrency = await db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => c.IsDefault, ct)
                                  ?? await db.Currencies.AsNoTracking().FirstOrDefaultAsync(ct);
            var defaultCurrencyId = defaultCurrency?.Id.ToString();
            var allConvertions = await GetCurrencyConvertionsAsync(db, DateTime.UtcNow, ct);

            var query = db.FundsTransfers
                .Include(t => t.Lines)
                .AsSplitQuery()
                .AsNoTracking()
                .Where(t => t.Date >= startDate && t.Date <= endDate);

            if (Guid.TryParse(sourceDepositoryId, out var srcGuid))
                query = query.Where(t => t.FromDepositoryId == srcGuid);

            if (Guid.TryParse(destinationDepositoryId, out var dstGuid))
                query = query.Where(t => t.ToDepositoryId == dstGuid);

            var transfers = await query.OrderByDescending(t => t.Date).ToListAsync(ct);

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

        transferGroup.MapGet("/facets", async (HttpContext context, MermerDbContext db, CancellationToken ct) =>
        {
            string? fields = context.Request.Query["fields"].ToString();
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
                    var groups = await db.FundsTransfers
                        .AsNoTracking()
                        .Where(x => !string.IsNullOrEmpty(x.Group))
                        .GroupBy(x => x.Group!)
                        .Select(g => new { Key = g.Key, Count = g.Count() })
                        .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

                    result[field] = groups;
                }
                else if (field.Equals("Tags", StringComparison.OrdinalIgnoreCase) || field.Equals("TagNames", StringComparison.OrdinalIgnoreCase))
                {
                    var allTags = await db.FundsTransfers
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
                    var list = await db.FundsTransfers.AsNoTracking().Where(r => !r.IsDisabled).Select(r => r.Date).ToListAsync(ct);
                    var localDates = list.Select(d => d.ToLocalTime().Date).ToList();

                    var dateFacets = new Dictionary<string, int>
                    {
                        { "#Today", localDates.Count(d => d == now) },
                        { "#This Week", localDates.Count(d => d >= now.AddDays(-7)) },
                        { "#This Month", localDates.Count(d => d.Month == now.Month && d.Year == now.Year) },
                        { "#This Year", localDates.Count(d => d.Year == now.Year) },
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

        // 7. РОУТЫ DAILY FUNDS REGISTRIES
        var registryGroup = routes.MapGroup("/api/finance/registeries").WithTags("DailyFundsRegistries");

        registryGroup.MapGet("", async (DateTime? from, DateTime? till, string? depositoryId, MermerDbContext db, CancellationToken ct) =>
        {
            var startDate = from ?? DateTime.MinValue;
            var endDate = till ?? DateTime.MaxValue;

            var defaultCurrency = await db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => c.IsDefault, ct)
                                  ?? await db.Currencies.AsNoTracking().FirstOrDefaultAsync(ct);
            var defaultCurrencyId = defaultCurrency?.Id.ToString();
            var allConvertions = await GetCurrencyConvertionsAsync(db, DateTime.UtcNow, ct);

            var query = db.DailyFundsRegisteries
                .Include(r => r.Lines)
                .AsSplitQuery()
                .AsNoTracking()
                .Where(r => r.Date >= startDate && r.Date <= endDate);

            if (Guid.TryParse(depositoryId, out var depGuid))
                query = query.Where(r => r.DepositoryId == depGuid);

            var list = await query.OrderByDescending(r => r.Date).ToListAsync(ct);

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

        registryGroup.MapPost("", async (HttpRequest request, MermerDbContext db) =>
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
        });

        registryGroup.MapPut("/{id}", async (string id, HttpRequest request, MermerDbContext db) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync();
            if (string.IsNullOrEmpty(body)) return Results.BadRequest("Empty body");

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            Guid regId = Guid.TryParse(id, out var parsedGuid) && parsedGuid != Guid.Empty ? parsedGuid : Guid.NewGuid();

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
        });

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
            string? fields = context.Request.Query["fields"].ToString();
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
                    var groups = await db.DailyFundsRegisteries
                        .AsNoTracking()
                        .Where(x => !string.IsNullOrEmpty(x.GroupName))
                        .GroupBy(x => x.GroupName!)
                        .Select(g => new { Key = g.Key, Count = g.Count() })
                        .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

                    result[field] = groups;
                }
                else if (field.Equals("Tags", StringComparison.OrdinalIgnoreCase) || field.Equals("TagNames", StringComparison.OrdinalIgnoreCase))
                {
                    var allTags = await db.DailyFundsRegisteries
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
                    var list = await db.DailyFundsRegisteries.AsNoTracking().Where(r => !r.IsDisabled).Select(r => r.Date).ToListAsync(ct);
                    var localDates = list.Select(d => d.ToLocalTime().Date).ToList();

                    var dateFacets = new Dictionary<string, int>
                    {
                        { "#Today", localDates.Count(d => d == now) },
                        { "#This Week", localDates.Count(d => d >= now.AddDays(-7)) },
                        { "#This Month", localDates.Count(d => d.Month == now.Month && d.Year == now.Year) },
                        { "#This Year", localDates.Count(d => d.Year == now.Year) },
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

        // 8. ЖУРНАЛ ДВИЖЕНИЯ ДЕНЕЖНЫХ СРЕДСТВ
        routes.MapGet("/api/finance/actions", async (DateTime? from, DateTime? till, string? currencyId, HttpRequest req, MermerDbContext db, CancellationToken ct) =>
        {
            var startDate = from ?? DateTime.MinValue;
            var endDate = till ?? DateTime.MaxValue;

            var depIds = req.Query["depositoryId"].Select(x => Guid.TryParse(x, out var g) ? (Guid?)g : null).Where(x => x.HasValue).Select(x => x!.Value).ToList();
            Guid? filterCurrencyGuid = Guid.TryParse(currencyId, out var cGuid) ? cGuid : null;

            var actions = new List<object>();

            var slipsQuery = db.FundsSlips
                .Include(s => s.Lines)
                .AsNoTracking()
                .Where(s => s.Date >= startDate && s.Date <= endDate && !s.IsDisabled);

            if (depIds.Any())
                slipsQuery = slipsQuery.Where(s => s.DepositoryId.HasValue && depIds.Contains(s.DepositoryId.Value));

            var slips = await slipsQuery.ToListAsync(ct);
            foreach (var s in slips)
            {
                foreach (var line in s.Lines ?? Enumerable.Empty<FundsSlipLineEntity>())
                {
                    if (filterCurrencyGuid.HasValue && line.CurrencyId != filterCurrencyGuid) continue;

                    bool isIncome = s.FundsSlipType == "Collection" || s.FundsSlipType == "FundsOpening" || s.FundsSlipType == "FundsRevisionExceed";
                    decimal amount = line.Amount;

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
                        ActionAmount = amount,
                        ActionIncome = isIncome ? amount : 0m,
                        ActionExpense = !isIncome ? amount : 0m
                    });
                }
            }

            var expenseQuery = db.ExpenseSlips
                .Include(s => s.Lines)
                .AsNoTracking()
                .Where(s => s.Date >= startDate && s.Date <= endDate && !s.IsDisabled);

            if (depIds.Any())
                expenseQuery = expenseQuery.Where(s => s.DepositoryId.HasValue && depIds.Contains(s.DepositoryId.Value));

            var expenseSlips = await expenseQuery.ToListAsync(ct);
            foreach (var s in expenseSlips)
            {
                foreach (var line in s.Lines ?? Enumerable.Empty<ExpenseSlipLineEntity>())
                {
                    if (filterCurrencyGuid.HasValue && line.CurrencyId != filterCurrencyGuid) continue;

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
                        ActionRelatedPartnerId = (string?)null,
                        ActionRelatedDepositoryId = (string?)null,
                        ActionDepositoryId = s.DepositoryId?.ToString(),
                        ActionCurrencyId = line.CurrencyId?.ToString(),
                        ActionAmount = line.Amount,
                        ActionIncome = 0m,
                        ActionExpense = line.Amount
                    });
                }
            }

            var transfersQuery = db.FundsTransfers
                .Include(t => t.Lines)
                .AsNoTracking()
                .Where(t => t.Date >= startDate && t.Date <= endDate && !t.IsDisabled);

            var transfers = await transfersQuery.ToListAsync(ct);
            foreach (var t in transfers)
            {
                foreach (var line in t.Lines ?? Enumerable.Empty<FundsTransferLineEntity>())
                {
                    if (filterCurrencyGuid.HasValue && line.CurrencyId != filterCurrencyGuid) continue;

                    if (!depIds.Any() || (t.FromDepositoryId.HasValue && depIds.Contains(t.FromDepositoryId.Value)))
                    {
                        actions.Add(new
                        {
                            TransactionId = t.Id.ToString(),
                            TransactionCode = t.Code ?? "",
                            TransactionDate = t.Date,
                            TransactionType = "FundsTransferSource",
                            TransactionUserId = t.UserId?.ToString(),
                            TransactionUserName = t.UserName,
                            TransactionIsCompleted = t.IsCompleted,
                            TransactionIsDisabled = t.IsDisabled,
                            TransactionGroup = t.Group ?? "",
                            TransactionTags = t.Tags ?? Array.Empty<string>(),
                            ActionRelatedPartnerId = (string?)null,
                            ActionRelatedDepositoryId = t.ToDepositoryId?.ToString(),
                            ActionDepositoryId = t.FromDepositoryId?.ToString(),
                            ActionCurrencyId = line.CurrencyId?.ToString(),
                            ActionAmount = line.Amount,
                            ActionIncome = 0m,
                            ActionExpense = line.Amount
                        });
                    }

                    if (!depIds.Any() || (t.ToDepositoryId.HasValue && depIds.Contains(t.ToDepositoryId.Value)))
                    {
                        actions.Add(new
                        {
                            TransactionId = t.Id.ToString(),
                            TransactionCode = t.Code ?? "",
                            TransactionDate = t.Date,
                            TransactionType = "FundsTransferDestination",
                            TransactionUserId = t.UserId?.ToString(),
                            TransactionUserName = t.UserName,
                            TransactionIsCompleted = t.IsCompleted,
                            TransactionIsDisabled = t.IsDisabled,
                            TransactionGroup = t.Group ?? "",
                            TransactionTags = t.Tags ?? Array.Empty<string>(),
                            ActionRelatedPartnerId = (string?)null,
                            ActionRelatedDepositoryId = t.FromDepositoryId?.ToString(),
                            ActionDepositoryId = t.ToDepositoryId?.ToString(),
                            ActionCurrencyId = line.CurrencyId?.ToString(),
                            ActionAmount = line.ReceivedAmount,
                            ActionIncome = line.ReceivedAmount,
                            ActionExpense = 0m
                        });
                    }
                }
            }

            return Results.Ok(actions);
        }).WithTags("FundsActions");

        // 9. БАЛАНСЫ КАСС
        var balancesGroup = routes.MapGroup("/api/finance/balances").WithTags("FundsBalances");

        balancesGroup.MapGet("/bytype", async (string? depositoryId, DateTime? from, DateTime? till, MermerDbContext db, CancellationToken ct) =>
        {
            var start = from ?? DateTime.MinValue;
            var end = till ?? DateTime.MaxValue;

            var depositories = string.IsNullOrEmpty(depositoryId) || depositoryId == "null"
                ? await db.Depositories.Select(d => d.Id).ToListAsync(ct)
                : new List<Guid> { Guid.Parse(depositoryId) };

            var result = new List<FundsBalanceByTypeWithBalanceDto>();

            var allSlips = await db.FundsSlips.Include(s => s.Lines).Where(s => !s.IsDisabled && s.DepositoryId != null).ToListAsync(ct);
            var allExpenses = await db.ExpenseSlips.Include(s => s.Lines).Where(s => !s.IsDisabled && s.DepositoryId != null).ToListAsync(ct);
            var allTransfers = await db.FundsTransfers.Include(s => s.Lines).Where(s => !s.IsDisabled).ToListAsync(ct);
            var allInvoices = await db.Invoices.Include(s => s.Lines).Where(i => !i.IsDisabled && i.IsCompleted && i.DepositoryId != null).ToListAsync(ct);

            foreach (var dep in depositories)
            {
                decimal startBal = 0m;
                var current = new FundsBalanceByTypeWithBalanceDto
                {
                    DepositoryId = dep.ToString()
                };

                void AddAmount(DateTime date, string type, decimal amount, bool isIncome)
                {
                    if (date < start)
                    {
                        startBal += isIncome ? amount : -amount;
                    }
                    else if (date <= end)
                    {
                        if (isIncome) current.Income += amount;
                        else current.Expense += amount;

                        if (type == "FundsOpening") current.FundsOpening += amount;
                        if (type == "FundsRevisionExceed") current.FundsRevisionExceed += amount;
                        if (type == "FundsRevisionDeficit") current.FundsRevisionDeficit += amount;
                        if (type == "Collection") current.Collection += amount;
                        if (type == "Payment") current.Payment += amount;
                        if (type == "ExpenseSlip") current.ExpenseSlip += amount;
                        if (type == "FundsTransferSource") current.FundsTransferSource += amount;
                        if (type == "FundsTransferDestination") current.FundsTransferDestination += amount;
                        if (type == "Sales") current.Sales += amount;
                        if (type == "SalesReturn") current.SalesReturn += amount;
                        if (type == "Purchase") current.Purchase += amount;
                        if (type == "PurchaseReturn") current.PurchaseReturn += amount;
                    }
                }

                foreach (var s in allSlips.Where(x => x.DepositoryId == dep))
                {
                    decimal amount = s.Lines?.Sum(l => l.Amount) ?? 0m;
                    bool isIncome = s.FundsSlipType == "Collection" || s.FundsSlipType == "FundsOpening" || s.FundsSlipType == "FundsRevisionExceed";
                    AddAmount(s.Date, s.FundsSlipType ?? "Collection", amount, isIncome);
                }

                foreach (var e in allExpenses.Where(x => x.DepositoryId == dep))
                {
                    decimal amount = e.Lines?.Sum(l => l.Amount) ?? 0m;
                    AddAmount(e.Date, "ExpenseSlip", amount, false);
                }

                foreach (var ts in allTransfers.Where(x => x.FromDepositoryId == dep))
                {
                    decimal amount = ts.Lines?.Sum(l => l.Amount) ?? 0m;
                    AddAmount(ts.Date, "FundsTransferSource", amount, false);
                }

                foreach (var td in allTransfers.Where(x => x.ToDepositoryId == dep))
                {
                    decimal amount = td.Lines?.Sum(l => l.ReceivedAmount) ?? 0m;
                    AddAmount(td.Date, "FundsTransferDestination", amount, true);
                }

                foreach (var inv in allInvoices.Where(x => x.DepositoryId == dep))
                {
                    decimal amount = inv.Lines?.Sum(l => l.Quantity * l.Price) ?? 0m;
                    bool isIncome = inv.InvoiceType == "Sales" || inv.InvoiceType == "PurchaseReturn";
                    AddAmount(inv.Date.DateTime, inv.InvoiceType, amount, isIncome);
                }

                current.StartingBalance = startBal;
                result.Add(current);
            }

            return Results.Ok(result);
        });

        balancesGroup.MapGet("/todate", async (string depositoryId, DateTime date, MermerDbContext db, CancellationToken ct) =>
        {
            if (!Guid.TryParse(depositoryId, out var depGuid)) return Results.BadRequest();

            var slips = await db.FundsSlips.Include(x => x.Lines).Where(s => s.DepositoryId == depGuid && s.Date < date && !s.IsDisabled).ToListAsync(ct);
            var expenses = await db.ExpenseSlips.Include(x => x.Lines).Where(s => s.DepositoryId == depGuid && s.Date < date && !s.IsDisabled).ToListAsync(ct);
            var ts = await db.FundsTransfers.Include(x => x.Lines).Where(t => t.FromDepositoryId == depGuid && t.Date < date && !t.IsDisabled).ToListAsync(ct);
            var td = await db.FundsTransfers.Include(x => x.Lines).Where(t => t.ToDepositoryId == depGuid && t.Date < date && !t.IsDisabled).ToListAsync(ct);
            var invs = await db.Invoices.Include(x => x.Lines).Where(i => i.DepositoryId == depGuid && i.Date < date && !i.IsDisabled && i.IsCompleted).ToListAsync(ct);

            decimal income = 0m;
            decimal expense = 0m;

            foreach (var s in slips)
            {
                var amt = s.Lines?.Sum(l => l.Amount) ?? 0m;
                if (s.FundsSlipType == "Collection" || s.FundsSlipType == "FundsOpening" || s.FundsSlipType == "FundsRevisionExceed") income += amt;
                else expense += amt;
            }
            foreach (var e in expenses) expense += e.Lines?.Sum(l => l.Amount) ?? 0m;
            foreach (var t in ts) expense += t.Lines?.Sum(l => l.Amount) ?? 0m;
            foreach (var t in td) income += t.Lines?.Sum(l => l.ReceivedAmount) ?? 0m;
            foreach (var i in invs)
            {
                var amt = i.Lines?.Sum(l => l.Quantity * l.Price) ?? 0m;
                if (i.InvoiceType == "Sales" || i.InvoiceType == "PurchaseReturn") income += amt;
                else expense += amt;
            }

            return Results.Ok(new FundsBalanceDto
            {
                DepositoryId = depositoryId,
                Income = income,
                Expense = expense
            });
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
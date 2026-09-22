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

public static class PartnersEndpoints
{
    public static void MapPartnersEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/partners").WithTags("Partners");

        // 1. СПИСОК ПАРТНЕРОВ
        group.MapGet("/", async (MermerDbContext db, CancellationToken ct) =>
        {
            var partners = await db.Partners
                .AsNoTracking()
                .Where(p => !p.IsDisabled)
                .OrderBy(p => p.Name)
                .ToListAsync(ct);
            return Results.Ok(partners);
        });

        // 2. ФАСЕТЫ ДЛЯ ПАРТНЕРОВ
        group.MapGet("/facets", async (string? fields, MermerDbContext db, CancellationToken ct) =>
        {
            var fieldList = fields?.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                  .Select(f => f.Trim())
                                  .ToArray() ?? Array.Empty<string>();

            var result = new Dictionary<string, Dictionary<string, int>>();

            foreach (var field in fieldList)
            {
                if (field.Equals("Group", StringComparison.OrdinalIgnoreCase) || field.Equals("GroupNames", StringComparison.OrdinalIgnoreCase))
                {
                    var groups = await db.Partners
                        .AsNoTracking()
                        .Where(x => !string.IsNullOrEmpty(x.Group) && !x.IsDisabled)
                        .GroupBy(x => x.Group!)
                        .Select(g => new { Key = g.Key, Count = g.Count() })
                        .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

                    result[field] = groups;
                }
                else if (field.Equals("Tags", StringComparison.OrdinalIgnoreCase) || field.Equals("TagNames", StringComparison.OrdinalIgnoreCase))
                {
                    var allTags = await db.Partners
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
                else
                {
                    result[field] = new Dictionary<string, int>();
                }
            }

            return Results.Ok(result);
        })
        .WithName("PartnersGetFacets");

        group.MapGet("/next-code", async (MermerDbContext db) =>
        {
            var count = await db.Partners.CountAsync();
            var nextCode = $"P-{(count + 1):D5}";
            return Results.Ok(new { code = nextCode });
        });

        // 3. РАСЧЕТ БАЛАНСОВ ПАРТНЕРОВ (БЫСТРАЯ SQL-АГРЕГАЦИЯ ВМЕСТО ЦИКЛА O(N*M))
        group.MapGet("/balances/by-type", async (string? partnerId, DateTime? from, DateTime? till, [Microsoft.AspNetCore.Mvc.FromQuery] string[]? officeIds, MermerDbContext db, CancellationToken ct) =>
        {
            DateTime fUtc = from?.ToUniversalTime() ?? DateTime.UtcNow.AddMonths(-1);
            DateTime tUtc = till?.ToUniversalTime() ?? DateTime.UtcNow;

            Guid? singlePartnerGuid = Guid.TryParse(partnerId, out var pG) ? pG : null;

            var targetOfficeGuids = officeIds?
                .Select(x => Guid.TryParse(x, out var g) ? (Guid?)g : null)
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .ToArray() ?? Array.Empty<Guid>();

            const string sql = """
                WITH raw_moves AS (
                    -- Накладные (Продажи и Возвраты)
                    SELECT 
                        i.partner_id,
                        i.date,
                        CASE WHEN i.invoice_type = 'Sales' THEN (il.quantity * il.price) ELSE 0 END AS sales,
                        CASE WHEN i.invoice_type = 'SalesReturn' THEN (il.quantity * il.price) ELSE 0 END AS sales_return,
                        CASE WHEN i.invoice_type = 'Purchase' THEN (il.quantity * il.price) ELSE 0 END AS purchase,
                        CASE WHEN i.invoice_type = 'PurchaseReturn' THEN (il.quantity * il.price) ELSE 0 END AS purchase_return,
                        0 AS opening,
                        0 AS revision
                    FROM invoice_lines il
                    JOIN invoices i ON i.id = il.invoice_id
                    WHERE i.partner_id IS NOT NULL 
                      AND i.is_completed = true 
                      AND i.is_disabled = false
                      AND (@singlePartner::uuid IS NULL OR i.partner_id = @singlePartner)
                      AND (cardinality(@offices::uuid[]) = 0 OR i.office_id = ANY(@offices))

                    UNION ALL

                    -- Акты сверки и начальные остатки
                    SELECT 
                        psl.partner_id,
                        ps.date,
                        0 AS sales, 0 AS sales_return, 0 AS purchase, 0 AS purchase_return,
                        CASE WHEN ps.slip_type = 'PartnerOpeningBalance' THEN (psl.debit_amount - psl.credit_amount) ELSE 0 END AS opening,
                        CASE WHEN ps.slip_type = 'PartnerBalanceRevision' THEN (psl.debit_amount - psl.credit_amount) ELSE 0 END AS revision
                    FROM partner_slip_lines psl
                    JOIN partner_slips ps ON ps.id = psl.partner_slip_id
                    WHERE psl.partner_id IS NOT NULL
                      AND ps.is_disabled = false
                      AND (@singlePartner::uuid IS NULL OR psl.partner_id = @singlePartner)
                      AND (cardinality(@offices::uuid[]) = 0 OR ps.office_id = ANY(@offices))
                )
                SELECT 
                    p.id::text AS "PartnerId",
                    COALESCE(@firstOffice::text, '') AS "OfficeId",
                    COALESCE(SUM(CASE WHEN rm.date < @from THEN (rm.opening + rm.revision + rm.sales - rm.sales_return - rm.purchase + rm.purchase_return) ELSE 0 END), 0)::numeric(18,4) AS "StartingBalance",
                    COALESCE(SUM(CASE WHEN rm.date >= @from AND rm.date <= @till THEN rm.opening ELSE 0 END), 0)::numeric(18,4) AS "Opening",
                    COALESCE(SUM(CASE WHEN rm.date >= @from AND rm.date <= @till THEN rm.revision ELSE 0 END), 0)::numeric(18,4) AS "Revision",
                    0::numeric(18,4) AS "Transfer",
                    COALESCE(SUM(CASE WHEN rm.date >= @from AND rm.date <= @till THEN rm.sales ELSE 0 END), 0)::numeric(18,4) AS "Sales",
                    COALESCE(SUM(CASE WHEN rm.date >= @from AND rm.date <= @till THEN rm.sales_return ELSE 0 END), 0)::numeric(18,4) AS "SalesReturn",
                    COALESCE(SUM(CASE WHEN rm.date >= @from AND rm.date <= @till THEN rm.purchase ELSE 0 END), 0)::numeric(18,4) AS "Purchase",
                    COALESCE(SUM(CASE WHEN rm.date >= @from AND rm.date <= @till THEN rm.purchase_return ELSE 0 END), 0)::numeric(18,4) AS "PurchaseReturn",
                    0::numeric(18,4) AS "Payment",
                    0::numeric(18,4) AS "Collection",
                    COALESCE(SUM(rm.opening + rm.revision + rm.sales - rm.sales_return - rm.purchase + rm.purchase_return), 0)::numeric(18,4) AS "ResultingBalance"
                FROM partners p
                LEFT JOIN raw_moves rm ON rm.partner_id = p.id
                WHERE NOT p.is_disabled
                  AND (@singlePartner::uuid IS NULL OR p.id = @singlePartner)
                GROUP BY p.id;
                """;

            var connStr = db.Database.GetConnectionString();
            await using var conn = new NpgsqlConnection(connStr);
            var result = await conn.QueryAsync(new CommandDefinition(
                sql,
                new
                {
                    singlePartner = singlePartnerGuid,
                    offices = targetOfficeGuids,
                    firstOffice = targetOfficeGuids.FirstOrDefault(),
                    from = fUtc,
                    till = tUtc
                },
                cancellationToken: ct));

            return Results.Ok(result);
        });

        group.MapGet("/balances", () => Results.Ok(Array.Empty<object>()));

        // 4. СОХРАНЕНИЕ ПАРТНЕРА (POST / PUT)
        Func<HttpRequest, MermerDbContext, Task<IResult>> savePartnerHandler = async (request, db) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync();
            if (string.IsNullOrEmpty(body)) return Results.BadRequest("Empty body");

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            string idStr = GetJsonString(root, "id", "Id");
            Guid partnerId = Guid.TryParse(idStr, out var parsedGuid) ? parsedGuid : Guid.NewGuid();

            string code = GetJsonString(root, "code", "Code") ?? $"P-{DateTime.UtcNow:yyMMddHHmmss}";
            string name = GetJsonString(root, "name", "Name") ?? "Новый партнер";
            string phone = GetJsonString(root, "phone", "Phone") ?? "";
            string address = GetJsonString(root, "address", "Address") ?? "";
            string groupName = GetJsonString(root, "group", "Group") ?? "";

            var tagsList = ExtractTagsFromRawJson(root);

            var existing = await db.Partners.FirstOrDefaultAsync(p => p.Id == partnerId);
            if (existing == null)
            {
                var entity = new PartnerEntity
                {
                    Id = partnerId,
                    Code = code,
                    Name = name,
                    Phone = phone,
                    Address = address,
                    Group = groupName,
                    Tags = tagsList.ToArray(),
                    IsDisabled = false,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                await db.Partners.AddAsync(entity);
            }
            else
            {
                existing.Code = code;
                existing.Name = name;
                existing.Phone = phone;
                existing.Address = address;
                existing.Group = groupName;
                existing.Tags = tagsList.ToArray();
                existing.UpdatedAt = DateTime.UtcNow;
            }

            await db.SaveChangesAsync();
            return Results.Content($"{{\"id\":\"{partnerId}\",\"code\":\"{code}\"}}", "application/json");
        };

        group.MapPost("/", savePartnerHandler);
        group.MapPut("/{id}", savePartnerHandler);
        routes.MapPost("/api/catalog/partners", savePartnerHandler);

        // 5. ФАСЕТЫ ДЛЯ PARTNER SLIPS
        group.MapGet("/slips/facets", async (HttpContext context, MermerDbContext db, CancellationToken ct) =>
        {
            var todayUtc = DateTime.UtcNow.Date;
            var weekStart = todayUtc.AddDays(-7);
            var monthStart = new DateTime(todayUtc.Year, todayUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            var countToday = await db.PartnerSlips.CountAsync(s => !s.IsDisabled && s.Date >= todayUtc, ct);
            var countWeek = await db.PartnerSlips.CountAsync(s => !s.IsDisabled && s.Date >= weekStart, ct);
            var countMonth = await db.PartnerSlips.CountAsync(s => !s.IsDisabled && s.Date >= monthStart, ct);
            var countAll = await db.PartnerSlips.CountAsync(s => !s.IsDisabled, ct);

            var groups = await db.PartnerSlips.AsNoTracking()
                .Where(x => !string.IsNullOrEmpty(x.Group) && !x.IsDisabled)
                .GroupBy(x => x.Group!)
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
        })
        .WithName("PartnerSlipsGetFacets");

        // 6. ПОДГРУЗКА PARTNER SLIPS (С ПАГИНАЦИЕЙ)
        group.MapGet("/slips", async (int? limit, int? offset, MermerDbContext db, CancellationToken ct) =>
        {
            int take = limit.HasValue ? Math.Clamp(limit.Value, 1, 1000) : 200;
            int skip = offset.GetValueOrDefault(0);

            var slips = await db.PartnerSlips
                .AsNoTracking()
                .Where(s => !s.IsDisabled)
                .OrderByDescending(s => s.Date)
                .Skip(skip)
                .Take(take)
                .Include(s => s.Lines)
                .AsSplitQuery()
                .ToListAsync(ct);

            var result = slips.Select(s => new
            {
                Id = s.Id.ToString(),
                Code = s.Code,
                Date = s.Date,
                SlipType = s.SlipType == "PartnerOpeningBalance" ? 0 : 1,
                Type = s.SlipType,
                OfficeId = s.OfficeId?.ToString(),
                UserName = s.UserName ?? "admin",
                Group = s.Group ?? string.Empty,
                Tags = s.Tags != null ? s.Tags.ToList() : new List<string>(),
                Description = s.Description ?? string.Empty,
                IsDisabled = s.IsDisabled,
                IsCompleted = s.IsCompleted,
                DocType = "PartnerSlip",
                DebitTotal = s.Lines?.Sum(l => l.DebitAmount) ?? 0m,
                CreditTotal = s.Lines?.Sum(l => l.CreditAmount) ?? 0m,
                Lines = s.Lines != null && s.Lines.Any()
                    ? s.Lines.Select(l => (object)new
                    {
                        Id = l.Id.ToString(),
                        PartnerId = l.PartnerId?.ToString(),
                        DebitAmount = l.DebitAmount,
                        DebitCurrencyId = l.DebitCurrencyId?.ToString(),
                        CreditAmount = l.CreditAmount,
                        CreditCurrencyId = l.CreditCurrencyId?.ToString()
                    }).ToList()
                    : new List<object>(),
                CurrencyConvertions = new List<object>()
            });

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = null,
                ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
            };

            return Results.Json(result, jsonOptions);
        });

        // 7. СОХРАНЕНИЕ PARTNER SLIPS
        group.MapPost("/slips", async (HttpRequest request, MermerDbContext db) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync();
            if (string.IsNullOrEmpty(body)) return Results.BadRequest("Empty body");

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            string idStr = GetJsonString(root, "id", "Id");
            Guid slipId = Guid.TryParse(idStr, out var parsedGuid) && parsedGuid != Guid.Empty ? parsedGuid : Guid.NewGuid();

            string code = GetJsonString(root, "code", "Code") ?? $"DOC-{DateTime.UtcNow:yyMMddHHmmss}";
            string slipType = GetJsonString(root, "type", "Type", "slipType", "SlipType") ?? "PartnerOpeningBalance";

            string offStr = GetJsonString(root, "officeId", "OfficeId");
            Guid? officeGuid = Guid.TryParse(offStr, out var parsedOff) ? parsedOff : null;

            string dateStr = GetJsonString(root, "date", "Date");
            DateTime slipDate = DateTime.TryParse(dateStr, out var pDate) ? pDate.ToUniversalTime() : DateTime.UtcNow;

            string groupName = GetJsonString(root, "group", "Group") ?? string.Empty;
            string description = GetJsonString(root, "description", "Description") ?? string.Empty;
            string userName = GetJsonString(root, "userName", "UserName") ?? "admin";
            var tagsList = ExtractTagsFromRawJson(root);

            var existing = await db.PartnerSlips.FirstOrDefaultAsync(s => s.Id == slipId);

            var linesList = new List<PartnerSlipLineEntity>();
            if (TryGetPropertyCaseInsensitive(root, "lines", out var linesProp) && linesProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var l in linesProp.EnumerateArray())
                {
                    string partnerIdStr = GetJsonString(l, "partnerId", "PartnerId");
                    Guid? pGuid = Guid.TryParse(partnerIdStr, out var parsedP) ? parsedP : null;

                    string debitCurStr = GetJsonString(l, "debitCurrencyId", "DebitCurrencyId");
                    Guid? debitCurGuid = Guid.TryParse(debitCurStr, out var pDebCur) ? pDebCur : null;

                    string creditCurStr = GetJsonString(l, "creditCurrencyId", "CreditCurrencyId");
                    Guid? creditCurGuid = Guid.TryParse(creditCurStr, out var pCredCur) ? pCredCur : null;

                    decimal debit = GetJsonDecimal(l, "debitAmount", "DebitAmount");
                    decimal credit = GetJsonDecimal(l, "creditAmount", "CreditAmount");

                    linesList.Add(new PartnerSlipLineEntity
                    {
                        Id = Guid.NewGuid(),
                        PartnerSlipId = slipId,
                        PartnerId = pGuid,
                        DebitAmount = debit,
                        DebitCurrencyId = debitCurGuid,
                        CreditAmount = credit,
                        CreditCurrencyId = creditCurGuid
                    });
                }
            }

            if (existing == null)
            {
                var entity = new PartnerSlipEntity
                {
                    Id = slipId,
                    Code = code,
                    Date = slipDate,
                    SlipType = slipType,
                    OfficeId = officeGuid,
                    UserName = userName,
                    Group = groupName,
                    Tags = tagsList.ToArray(),
                    Description = description,
                    IsCompleted = true,
                    IsDisabled = false,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                await db.PartnerSlips.AddAsync(entity);
            }
            else
            {
                existing.Code = code;
                existing.Date = slipDate;
                existing.SlipType = slipType;
                existing.OfficeId = officeGuid;
                existing.UserName = userName;
                existing.Group = groupName;
                existing.Tags = tagsList.ToArray();
                existing.Description = description;
                existing.UpdatedAt = DateTime.UtcNow;
            }

            await db.SaveChangesAsync();

            await db.Database.ExecuteSqlRawAsync("DELETE FROM partner_slip_lines WHERE partner_slip_id = {0}", slipId);

            if (linesList.Any())
            {
                await db.PartnerSlipLines.AddRangeAsync(linesList);
                await db.SaveChangesAsync();
            }

            return Results.Content($"{{\"id\":\"{slipId}\",\"code\":\"{code}\"}}", "application/json");
        });

        // 8. ФАСЕТЫ ДЛЯ PARTNER TRANSFERS
        group.MapGet("/transfers/facets", async (HttpContext context, MermerDbContext db, CancellationToken ct) =>
        {
            var groups = await db.PartnerTransfers
                .AsNoTracking()
                .Where(x => !string.IsNullOrEmpty(x.Group) && !x.IsDisabled)
                .GroupBy(x => x.Group!)
                .Select(g => new { Key = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

            return Results.Ok(new Dictionary<string, object>
            {
                ["Group"] = groups
            });
        })
        .WithName("PartnerTransfersGetFacets");

        // 9. ПОДГРУЗКА ПЕРЕВОДОВ PARTNER TRANSFERS (С ПАГИНАЦИЕЙ И ПОЛНЫМ РАСЧЕТОМ КУРСОВ)
        group.MapGet("/transfers", async (int? limit, int? offset, MermerDbContext db, CancellationToken ct) =>
        {
            int take = limit.HasValue ? Math.Clamp(limit.Value, 1, 1000) : 200;
            int skip = offset.GetValueOrDefault(0);

            var transfers = await db.PartnerTransfers
                .AsNoTracking()
                .Where(t => !t.IsDisabled)
                .OrderByDescending(t => t.Date)
                .Skip(skip)
                .Take(take)
                .Include(t => t.Lines)
                .AsSplitQuery()
                .ToListAsync(ct);

            var allRates = await db.CurrencyRates.AsNoTracking().ToListAsync(ct);

            var result = transfers.Select(t =>
            {
                var usedCurrencyIds = t.Lines != null
                    ? t.Lines.Select(l => l.DebitCurrencyId)
                             .Union(t.Lines.Select(l => l.CreditCurrencyId))
                             .Where(c => c.HasValue)
                             .Select(c => c!.Value)
                             .Distinct()
                             .ToList()
                    : new List<Guid>();

                var currencyConvertions = usedCurrencyIds.Select(cId =>
                {
                    var rate = allRates
                        .Where(r => r.CurrencyId == cId && r.ValidFrom <= t.Date)
                        .OrderByDescending(r => r.ValidFrom)
                        .FirstOrDefault();

                    return (object)new
                    {
                        Id = Guid.NewGuid().ToString(),
                        CurrencyId = cId.ToString(),
                        Multiplier = rate?.Multiplier ?? 1m,
                        Divider = rate?.Divider ?? 1m
                    };
                }).ToList();

                return new
                {
                    Id = t.Id.ToString(),
                    Code = t.Code,
                    Date = t.Date,
                    Type = "PartnerTransfer",
                    UserName = t.UserName ?? "admin",
                    Group = t.Group ?? string.Empty,
                    Tags = t.Tags != null ? t.Tags.ToList() : new List<string>(),
                    Description = t.Description ?? string.Empty,
                    IsDisabled = t.IsDisabled,
                    IsCompleted = t.IsCompleted,
                    DocType = "PartnerTransfer",
                    Lines = t.Lines != null && t.Lines.Any()
                        ? t.Lines.Select(l => (object)new
                        {
                            Id = l.Id.ToString(),
                            OfficeId = l.OfficeId?.ToString(),
                            PartnerId = l.PartnerId?.ToString(),
                            DebitAmount = l.DebitAmount,
                            DebitCurrencyId = l.DebitCurrencyId?.ToString(),
                            CreditAmount = l.CreditAmount,
                            CreditCurrencyId = l.CreditCurrencyId?.ToString()
                        }).ToList()
                        : new List<object>(),
                    CurrencyConvertions = currencyConvertions
                };
            });

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = null,
                ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
            };

            return Results.Json(result, jsonOptions);
        });

        // 10. СОХРАНЕНИЕ ПЕРЕВОДОВ PARTNER TRANSFERS
        group.MapPost("/transfers", async (HttpRequest request, MermerDbContext db) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync();
            if (string.IsNullOrEmpty(body)) return Results.BadRequest("Empty body");

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            string idStr = GetJsonString(root, "id", "Id");
            Guid transferId = Guid.TryParse(idStr, out var parsedGuid) && parsedGuid != Guid.Empty ? parsedGuid : Guid.NewGuid();

            string code = GetJsonString(root, "code", "Code") ?? $"DOC-{DateTime.UtcNow:yyMMddHHmmss}";
            string dateStr = GetJsonString(root, "date", "Date");
            DateTime transferDate = DateTime.TryParse(dateStr, out var pDate) ? pDate.ToUniversalTime() : DateTime.UtcNow;

            string groupName = GetJsonString(root, "group", "Group") ?? string.Empty;
            string description = GetJsonString(root, "description", "Description") ?? string.Empty;
            string userName = GetJsonString(root, "userName", "UserName") ?? "admin";
            var tagsList = ExtractTagsFromRawJson(root);

            var existing = await db.PartnerTransfers.FirstOrDefaultAsync(t => t.Id == transferId);

            var linesList = new List<PartnerTransferLineEntity>();
            if (TryGetPropertyCaseInsensitive(root, "lines", out var linesProp) && linesProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var l in linesProp.EnumerateArray())
                {
                    string officeIdStr = GetJsonString(l, "officeId", "OfficeId");
                    Guid? oGuid = Guid.TryParse(officeIdStr, out var parsedO) ? parsedO : null;

                    string partnerIdStr = GetJsonString(l, "partnerId", "PartnerId");
                    Guid? pGuid = Guid.TryParse(partnerIdStr, out var parsedP) ? parsedP : null;

                    string debitCurStr = GetJsonString(l, "debitCurrencyId", "DebitCurrencyId");
                    Guid? debitCurGuid = Guid.TryParse(debitCurStr, out var pDebCur) ? pDebCur : null;

                    string creditCurStr = GetJsonString(l, "creditCurrencyId", "CreditCurrencyId");
                    Guid? creditCurGuid = Guid.TryParse(creditCurStr, out var pCredCur) ? pCredCur : null;

                    decimal debit = GetJsonDecimal(l, "debitAmount", "DebitAmount");
                    decimal credit = GetJsonDecimal(l, "creditAmount", "CreditAmount");

                    linesList.Add(new PartnerTransferLineEntity
                    {
                        Id = Guid.NewGuid(),
                        PartnerTransferId = transferId,
                        OfficeId = oGuid,
                        PartnerId = pGuid,
                        DebitAmount = debit,
                        DebitCurrencyId = debitCurGuid,
                        CreditAmount = credit,
                        CreditCurrencyId = creditCurGuid
                    });
                }
            }

            if (existing == null)
            {
                var entity = new PartnerTransferEntity
                {
                    Id = transferId,
                    Code = code,
                    Date = transferDate,
                    UserName = userName,
                    Group = groupName,
                    Tags = tagsList.ToArray(),
                    Description = description,
                    IsCompleted = true,
                    IsDisabled = false,
                    Lines = linesList,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                await db.PartnerTransfers.AddAsync(entity);
            }
            else
            {
                existing.Code = code;
                existing.Date = transferDate;
                existing.UserName = userName;
                existing.Group = groupName;
                existing.Tags = tagsList.ToArray();
                existing.Description = description;
                existing.UpdatedAt = DateTime.UtcNow;
            }

            await db.SaveChangesAsync();

            await db.Database.ExecuteSqlRawAsync("DELETE FROM partner_transfer_lines WHERE partner_transfer_id = {0}", transferId);

            if (linesList.Any())
            {
                await db.PartnerTransferLines.AddRangeAsync(linesList);
                await db.SaveChangesAsync();
            }

            return Results.Content($"{{\"id\":\"{transferId}\",\"code\":\"{code}\"}}", "application/json");
        });

        // 11. РЕЕСТР ДВИЖЕНИЙ ПО ПАРТНЕРАМ (БЫСТРАЯ SQL-ВЫБОРКА С ЛИМИТОМ)
        group.MapGet("/actions", async (string? partnerId, DateTime? from, DateTime? till, int? limit, MermerDbContext db, CancellationToken ct) =>
        {
            int take = limit.HasValue ? Math.Clamp(limit.Value, 1, 1000) : 300;
            DateTime fUtc = from?.ToUniversalTime() ?? DateTime.UtcNow.AddMonths(-1);
            DateTime tUtc = till?.ToUniversalTime() ?? DateTime.UtcNow;

            Guid? pGuid = Guid.TryParse(partnerId, out var g) ? g : null;

            const string sql = """
                WITH raw_actions AS (
                    -- Накладные
                    SELECT 
                        i.id::text           AS "TransactionId",
                        i.code               AS "TransactionCode",
                        i.invoice_type       AS "TransactionType",
                        i.date               AS "TransactionDate",
                        COALESCE(i.office_id::text, '') AS "ActionOfficeId",
                        i.partner_id::text   AS "ActionPartnerId",
                        CASE WHEN i.invoice_type IN ('Sales', 'PurchaseReturn') THEN (il.quantity * il.price) ELSE 0 END AS "ActionDebit",
                        CASE WHEN i.invoice_type IN ('Purchase', 'SalesReturn') THEN (il.quantity * il.price) ELSE 0 END AS "ActionCredit",
                        i.user_name          AS "TransactionUserName",
                        i.is_completed       AS "TransactionIsCompleted",
                        i.is_disabled        AS "TransactionIsDisabled"
                    FROM invoice_lines il
                    JOIN invoices i ON i.id = il.invoice_id
                    WHERE i.partner_id IS NOT NULL 
                      AND i.is_completed = true 
                      AND i.is_disabled = false
                      AND (@partner::uuid IS NULL OR i.partner_id = @partner)
                      AND i.date >= @from AND i.date <= @till

                    UNION ALL

                    -- Акты сверки
                    SELECT 
                        ps.id::text          AS "TransactionId",
                        ps.code              AS "TransactionCode",
                        ps.slip_type         AS "TransactionType",
                        ps.date              AS "TransactionDate",
                        COALESCE(ps.office_id::text, '') AS "ActionOfficeId",
                        psl.partner_id::text AS "ActionPartnerId",
                        psl.debit_amount     AS "ActionDebit",
                        psl.credit_amount    AS "ActionCredit",
                        ps.user_name         AS "TransactionUserName",
                        true                 AS "TransactionIsCompleted",
                        ps.is_disabled       AS "TransactionIsDisabled"
                    FROM partner_slip_lines psl
                    JOIN partner_slips ps ON ps.id = psl.partner_slip_id
                    WHERE psl.partner_id IS NOT NULL
                      AND ps.is_disabled = false
                      AND (@partner::uuid IS NULL OR psl.partner_id = @partner)
                      AND ps.date >= @from AND ps.date <= @till

                    UNION ALL

                    -- Переводы
                    SELECT 
                        pt.id::text          AS "TransactionId",
                        pt.code              AS "TransactionCode",
                        'PartnerTransfer'    AS "TransactionType",
                        pt.date              AS "TransactionDate",
                        COALESCE(ptl.office_id::text, '') AS "ActionOfficeId",
                        ptl.partner_id::text AS "ActionPartnerId",
                        ptl.debit_amount     AS "ActionDebit",
                        ptl.credit_amount    AS "ActionCredit",
                        pt.user_name         AS "TransactionUserName",
                        true                 AS "TransactionIsCompleted",
                        pt.is_disabled       AS "TransactionIsDisabled"
                    FROM partner_transfer_lines ptl
                    JOIN partner_transfers pt ON pt.id = ptl.partner_transfer_id
                    WHERE ptl.partner_id IS NOT NULL
                      AND pt.is_disabled = false
                      AND (@partner::uuid IS NULL OR ptl.partner_id = @partner)
                      AND pt.date >= @from AND pt.date <= @till
                )
                SELECT 
                    *,
                    ("ActionDebit" - "ActionCredit")::numeric(18,4) AS "ActionEffect"
                FROM raw_actions
                ORDER BY "TransactionDate" DESC
                LIMIT @take;
                """;

            var connStr = db.Database.GetConnectionString();
            await using var conn = new NpgsqlConnection(connStr);
            var result = await conn.QueryAsync<PartnerActionDto>(new CommandDefinition(
                sql,
                new { partner = pGuid, from = fUtc, till = tUtc, take },
                cancellationToken: ct));

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = null,
                ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
            };

            return Results.Json(result, jsonOptions);
        });
    }

    #region JSON Helpers
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

    private static string GetJsonString(JsonElement element, params string[] propNames)
    {
        foreach (var name in propNames)
        {
            if (TryGetPropertyCaseInsensitive(element, name, out var prop) && prop.ValueKind == JsonValueKind.String)
                return prop.GetString();
        }
        return null;
    }

    private static decimal GetJsonDecimal(JsonElement element, params string[] propNames)
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
    #endregion

    public class PartnerActionDto
    {
        public string TransactionId { get; set; } = null!;
        public string TransactionCode { get; set; } = null!;
        public string TransactionType { get; set; } = null!;
        public DateTime TransactionDate { get; set; }
        public string ActionOfficeId { get; set; } = null!;
        public string ActionPartnerId { get; set; } = null!;
        public decimal ActionDebit { get; set; }
        public decimal ActionCredit { get; set; }
        public decimal ActionEffect { get; set; }
        public string TransactionUserName { get; set; } = null!;
        public bool TransactionIsCompleted { get; set; }
        public bool TransactionIsDisabled { get; set; }
    }
}
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
                .OrderBy(p => p.Name)
                .ToListAsync(ct);
            return Results.Ok(partners);
        });

        // 2. СЛИЯНИЕ КОНТРАГЕНТОВ (MERGE)
        group.MapPost("/merge", async (PartnerMergeDto dto, MermerDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrEmpty(dto.MainItemId) || dto.MergeItemIds == null || dto.MergeItemIds.Length == 0)
            {
                return Results.BadRequest("Invalid merge parameters");
            }

            if (!Guid.TryParse(dto.MainItemId, out var mainGuid))
            {
                return Results.BadRequest("Invalid MainItemId format");
            }

            var mergeGuids = dto.MergeItemIds
                .Select(x => Guid.TryParse(x, out var g) ? (Guid?)g : null)
                .Where(x => x.HasValue && x.Value != mainGuid)
                .Select(x => x!.Value)
                .Distinct()
                .ToArray();

            if (mergeGuids.Length == 0)
            {
                return Results.Ok();
            }

            var connStr = db.Database.GetConnectionString();
            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            try
            {
                var prms = new { mainId = mainGuid, mergeIds = mergeGuids, now = DateTime.UtcNow };

                await conn.ExecuteAsync(
                    "UPDATE invoices SET partner_id = @mainId WHERE partner_id = ANY(@mergeIds);",
                    prms, tx);

                await conn.ExecuteAsync(
                    "UPDATE funds_slips SET partner_id = @mainId WHERE partner_id = ANY(@mergeIds);",
                    prms, tx);

                await conn.ExecuteAsync(
                    "UPDATE partner_slip_lines SET partner_id = @mainId WHERE partner_id = ANY(@mergeIds);",
                    prms, tx);

                await conn.ExecuteAsync(
                    "UPDATE partner_transfer_lines SET partner_id = @mainId WHERE partner_id = ANY(@mergeIds);",
                    prms, tx);

                if (dto.DisableMergedItems)
                {
                    await conn.ExecuteAsync(
                        "UPDATE partners SET is_disabled = true, updated_at = @now WHERE id = ANY(@mergeIds);",
                        prms, tx);
                }

                await tx.CommitAsync(ct);
                return Results.Ok();
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                Console.WriteLine($"[MERGE ERROR]: {ex}");
                return Results.Problem($"Merge failed: {ex.Message}");
            }
        });

        // 3. ФАСЕТЫ ДЛЯ ПАРТНЕРОВ
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

        // 4. РАСЧЕТ БАЛАНСОВ ПАРТНЕРОВ (ОБОРОТНО-САЛЬДОВАЯ ВЕДОМОСТЬ С НОРМАЛИЗАЦИЕЙ ВАЛЮТЫ)
        group.MapGet("/balances/by-type", async (string? partnerId, DateTime? from, DateTime? till, HttpRequest req, MermerDbContext db, CancellationToken ct) =>
        {
            DateTime fUtc = DateTime.SpecifyKind(new DateTime(2000, 1, 1), DateTimeKind.Utc);
            if (from.HasValue && from.Value.Year > 2000)
            {
                fUtc = from.Value.Kind == DateTimeKind.Utc ? from.Value : from.Value.ToUniversalTime();
            }

            DateTime tUtc = DateTime.SpecifyKind(new DateTime(2099, 12, 31, 23, 59, 59), DateTimeKind.Utc);
            if (till.HasValue && till.Value.Year < 2099)
            {
                var localTill = till.Value;
                if (localTill.TimeOfDay == TimeSpan.Zero)
                    localTill = localTill.Date.AddDays(1).AddTicks(-1);

                tUtc = localTill.Kind == DateTimeKind.Utc ? localTill : localTill.ToUniversalTime();
            }

            Guid? singlePartnerGuid = Guid.TryParse(partnerId, out var pG) ? pG : null;

            var officeParamValues = req.Query["officeIds"].Concat(req.Query["officeId"]);
            var targetOfficeGuids = officeParamValues
                .Select(x => Guid.TryParse(x, out var g) ? (Guid?)g : null)
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .Distinct()
                .ToArray();

            // 1. Дефолтная валюта
            var defCur = await db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => c.IsDefault && !c.IsDisabled, ct)
                         ?? await db.Currencies.AsNoTracking().FirstOrDefaultAsync(ct);
            Guid defaultCurrencyId = defCur?.Id ?? Guid.Empty;

            // 2. Дефолтный офис предприятия
            var defOffice = await db.Offices.AsNoTracking().FirstOrDefaultAsync(o => !o.IsDisabled, ct);
            Guid defaultOfficeGuid = defOffice?.Id ?? Guid.Empty;

            const string sql = """
                WITH def_cur AS (
                    SELECT @defCurId::uuid AS id
                ),
                raw_moves AS (
                    -- 1. Накладные (Invoices) с конвертацией в базовую валюту USD
                    SELECT 
                        i.partner_id,
                        COALESCE(i.office_id, @defOffice) AS office_id,
                        i.date,
                        CASE WHEN i.invoice_type = 'Sales' 
                             THEN (il.quantity * (il.price * CASE 
                                     WHEN i.display_currency_id IS NULL OR i.display_currency_id = (SELECT id FROM def_cur) THEN 1.0
                                     ELSE COALESCE(cr.multiplier / NULLIF(cr.divider, 0), 1.0)
                                   END)) 
                             ELSE 0 
                        END AS sales,
                        CASE WHEN i.invoice_type = 'SalesReturn' 
                             THEN (il.quantity * (il.price * CASE 
                                     WHEN i.display_currency_id IS NULL OR i.display_currency_id = (SELECT id FROM def_cur) THEN 1.0
                                     ELSE COALESCE(cr.multiplier / NULLIF(cr.divider, 0), 1.0)
                                   END)) 
                             ELSE 0 
                        END AS sales_return,
                        CASE WHEN i.invoice_type = 'Purchase' 
                             THEN (il.quantity * (il.price * CASE 
                                     WHEN i.display_currency_id IS NULL OR i.display_currency_id = (SELECT id FROM def_cur) THEN 1.0
                                     ELSE COALESCE(cr.multiplier / NULLIF(cr.divider, 0), 1.0)
                                   END)) 
                             ELSE 0 
                        END AS purchase,
                        CASE WHEN i.invoice_type = 'PurchaseReturn' 
                             THEN (il.quantity * (il.price * CASE 
                                     WHEN i.display_currency_id IS NULL OR i.display_currency_id = (SELECT id FROM def_cur) THEN 1.0
                                     ELSE COALESCE(cr.multiplier / NULLIF(cr.divider, 0), 1.0)
                                   END)) 
                             ELSE 0 
                        END AS purchase_return,
                        0::numeric AS opening,
                        0::numeric AS revision,
                        0::numeric AS transfer,
                        0::numeric AS payment,
                        0::numeric AS collection
                    FROM invoice_lines il
                    JOIN invoices i ON i.id = il.invoice_id
                    LEFT JOIN LATERAL (
                        SELECT r.multiplier, r.divider 
                        FROM currency_rates r 
                        WHERE r.currency_id = i.display_currency_id AND r.valid_from <= i.date 
                        ORDER BY r.valid_from DESC LIMIT 1
                    ) cr ON true
                    WHERE i.partner_id IS NOT NULL 
                      AND i.is_disabled = false
                      AND (@singlePartner::uuid IS NULL OR i.partner_id = @singlePartner)
                      AND (cardinality(@offices::uuid[]) = 0 OR COALESCE(i.office_id, @defOffice) = ANY(@offices))

                    UNION ALL

                    -- 2. Акты сверки и начальные остатки (Partner Slips)
                    SELECT 
                        psl.partner_id,
                        COALESCE(ps.office_id, @defOffice) AS office_id,
                        ps.date,
                        0, 0, 0, 0,
                        CASE WHEN ps.slip_type = 'PartnerOpeningBalance' THEN (psl.debit_amount - psl.credit_amount) ELSE 0 END AS opening,
                        CASE WHEN ps.slip_type = 'PartnerBalanceRevision' THEN (psl.debit_amount - psl.credit_amount) ELSE 0 END AS revision,
                        0, 0, 0
                    FROM partner_slip_lines psl
                    JOIN partner_slips ps ON ps.id = psl.partner_slip_id
                    WHERE psl.partner_id IS NOT NULL 
                      AND ps.is_disabled = false
                      AND (@singlePartner::uuid IS NULL OR psl.partner_id = @singlePartner)
                      AND (cardinality(@offices::uuid[]) = 0 OR COALESCE(ps.office_id, @defOffice) = ANY(@offices))

                    UNION ALL

                    -- 3. Взаимозачеты и переводы долга (Partner Transfers)
                    SELECT 
                        ptl.partner_id,
                        COALESCE(ptl.office_id, @defOffice) AS office_id,
                        pt.date,
                        0, 0, 0, 0, 0, 0,
                        (ptl.debit_amount - ptl.credit_amount) AS transfer,
                        0, 0
                    FROM partner_transfer_lines ptl
                    JOIN partner_transfers pt ON pt.id = ptl.partner_transfer_id
                    WHERE ptl.partner_id IS NOT NULL 
                      AND pt.is_disabled = false
                      AND (@singlePartner::uuid IS NULL OR ptl.partner_id = @singlePartner)
                      AND (cardinality(@offices::uuid[]) = 0 OR COALESCE(ptl.office_id, @defOffice) = ANY(@offices))

                    UNION ALL

                    -- 4. Оплаты и приходы денег (Funds Slips) с нормализацией валюты
                    SELECT 
                        fs.partner_id,
                        COALESCE(fs.office_id, @defOffice) AS office_id,
                        fs.date,
                        0, 0, 0, 0, 0, 0, 0,
                        CASE WHEN fs.funds_slip_type = 'Expense' OR fs.funds_slip_type = 'Payment' 
                             THEN (fsl.amount * CASE 
                                     WHEN fs.display_currency_id IS NULL OR fs.display_currency_id = (SELECT id FROM def_cur) THEN 1.0
                                     ELSE COALESCE(cr.multiplier / NULLIF(cr.divider, 0), 1.0)
                                   END) 
                             ELSE 0 
                        END AS payment,
                        CASE WHEN fs.funds_slip_type = 'Income' OR fs.funds_slip_type = 'Collection' 
                             THEN (fsl.amount * CASE 
                                     WHEN fs.display_currency_id IS NULL OR fs.display_currency_id = (SELECT id FROM def_cur) THEN 1.0
                                     ELSE COALESCE(cr.multiplier / NULLIF(cr.divider, 0), 1.0)
                                   END) 
                             ELSE 0 
                        END AS collection
                    FROM funds_slip_lines fsl
                    JOIN funds_slips fs ON fs.id = fsl.funds_slip_id
                    LEFT JOIN LATERAL (
                        SELECT r.multiplier, r.divider 
                        FROM currency_rates r 
                        WHERE r.currency_id = fs.display_currency_id AND r.valid_from <= fs.date 
                        ORDER BY r.valid_from DESC LIMIT 1
                    ) cr ON true
                    WHERE fs.partner_id IS NOT NULL 
                      AND fs.is_disabled = false
                      AND (@singlePartner::uuid IS NULL OR fs.partner_id = @singlePartner)
                      AND (cardinality(@offices::uuid[]) = 0 OR COALESCE(fs.office_id, @defOffice) = ANY(@offices))
                ),
                aggregated AS (
                    SELECT 
                        rm.partner_id,
                        rm.office_id,
                        COALESCE(SUM(CASE WHEN rm.date < @from THEN 
                            (rm.opening + rm.revision + rm.transfer + rm.sales - rm.sales_return - rm.purchase + rm.purchase_return + rm.payment - rm.collection) 
                        ELSE 0 END), 0)::numeric(18,4) AS "StartingBalance",

                        COALESCE(SUM(CASE WHEN rm.date >= @from AND rm.date <= @till THEN rm.opening ELSE 0 END), 0)::numeric(18,4) AS "PartnerOpeningBalance",
                        COALESCE(SUM(CASE WHEN rm.date >= @from AND rm.date <= @till THEN rm.revision ELSE 0 END), 0)::numeric(18,4) AS "PartnerBalanceRevision",
                        COALESCE(SUM(CASE WHEN rm.date >= @from AND rm.date <= @till THEN rm.transfer ELSE 0 END), 0)::numeric(18,4) AS "PartnerTransfer",
                        COALESCE(SUM(CASE WHEN rm.date >= @from AND rm.date <= @till THEN rm.sales ELSE 0 END), 0)::numeric(18,4) AS "Sales",
                        COALESCE(SUM(CASE WHEN rm.date >= @from AND rm.date <= @till THEN rm.sales_return ELSE 0 END), 0)::numeric(18,4) AS "SalesReturn",
                        COALESCE(SUM(CASE WHEN rm.date >= @from AND rm.date <= @till THEN rm.purchase ELSE 0 END), 0)::numeric(18,4) AS "Purchase",
                        COALESCE(SUM(CASE WHEN rm.date >= @from AND rm.date <= @till THEN rm.purchase_return ELSE 0 END), 0)::numeric(18,4) AS "PurchaseReturn",
                        COALESCE(SUM(CASE WHEN rm.date >= @from AND rm.date <= @till THEN rm.payment ELSE 0 END), 0)::numeric(18,4) AS "Payment",
                        COALESCE(SUM(CASE WHEN rm.date >= @from AND rm.date <= @till THEN rm.collection ELSE 0 END), 0)::numeric(18,4) AS "Collection"
                    FROM raw_moves rm
                    GROUP BY rm.partner_id, rm.office_id
                )
                SELECT 
                    a.partner_id::text AS "PartnerId",
                    a.office_id::text AS "OfficeId",
                    a."StartingBalance",
                    a."PartnerOpeningBalance",
                    a."PartnerBalanceRevision",
                    a."PartnerTransfer",
                    a."Sales",
                    a."SalesReturn",
                    a."Purchase",
                    a."PurchaseReturn",
                    a."Payment",
                    a."Collection",
                    (a."Sales" + a."PurchaseReturn" + a."Payment" + 
                     CASE WHEN a."PartnerOpeningBalance" > 0 THEN a."PartnerOpeningBalance" ELSE 0 END + 
                     CASE WHEN a."PartnerBalanceRevision" > 0 THEN a."PartnerBalanceRevision" ELSE 0 END + 
                     CASE WHEN a."PartnerTransfer" > 0 THEN a."PartnerTransfer" ELSE 0 END
                    )::numeric(18,4) AS "Debit",
                    (a."Purchase" + a."SalesReturn" + a."Collection" + 
                     CASE WHEN a."PartnerOpeningBalance" < 0 THEN -a."PartnerOpeningBalance" ELSE 0 END + 
                     CASE WHEN a."PartnerBalanceRevision" < 0 THEN -a."PartnerBalanceRevision" ELSE 0 END + 
                     CASE WHEN a."PartnerTransfer" < 0 THEN -a."PartnerTransfer" ELSE 0 END
                    )::numeric(18,4) AS "Credit",
                    (a."StartingBalance" + 
                     (a."PartnerOpeningBalance" + a."PartnerBalanceRevision" + a."PartnerTransfer" + a."Sales" - a."SalesReturn" - a."Purchase" + a."PurchaseReturn" + a."Payment" - a."Collection")
                    )::numeric(18,4) AS "ResultingBalanceInCustomCurrency"
                FROM aggregated a
                JOIN partners p ON p.id = a.partner_id
                WHERE NOT p.is_disabled
                  AND (
                      a."StartingBalance" != 0 OR 
                      a."Sales" != 0 OR 
                      a."SalesReturn" != 0 OR 
                      a."Purchase" != 0 OR 
                      a."PurchaseReturn" != 0 OR 
                      a."Payment" != 0 OR 
                      a."Collection" != 0 OR 
                      a."PartnerOpeningBalance" != 0 OR 
                      a."PartnerBalanceRevision" != 0 OR 
                      a."PartnerTransfer" != 0
                  )
                ORDER BY a."Sales" DESC;
                """;

            var connStr = db.Database.GetConnectionString();
            await using var conn = new NpgsqlConnection(connStr);
            var result = await conn.QueryAsync(new CommandDefinition(
                sql,
                new
                {
                    singlePartner = singlePartnerGuid,
                    offices = targetOfficeGuids,
                    defOffice = defaultOfficeGuid,
                    defCurId = defaultCurrencyId,
                    from = fUtc,
                    till = tUtc
                },
                cancellationToken: ct));

            var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = null };
            return Results.Json(result, jsonOptions);
        });

        group.MapGet("/balances", () => Results.Ok(Array.Empty<object>()));

        // 5. СОХРАНЕНИЕ ПАРТНЕРА (POST / PUT)
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

        // 6. ФАСЕТЫ ДЛЯ PARTNER SLIPS
        group.MapGet("/slips/facets", async (string? fields, MermerDbContext db, CancellationToken ct) =>
        {
            var connStr = db.Database.GetConnectionString()!;

            if (!string.IsNullOrEmpty(fields))
            {
                var facets = await FacetsHelper.GetEntityFacetsAsync(connStr, "partner_slips", fields, ct);
                return Results.Ok(facets);
            }

            var todayUtc = DateTime.UtcNow.Date;
            var weekStart = todayUtc.AddDays(-7);
            var monthStart = new DateTime(todayUtc.Year, todayUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            var countToday = await db.PartnerSlips.CountAsync(s => !s.IsDisabled && s.Date >= todayUtc, ct);
            var countWeek = await db.PartnerSlips.CountAsync(s => !s.IsDisabled && s.Date >= weekStart, ct);
            var countMonth = await db.PartnerSlips.CountAsync(s => !s.IsDisabled && s.Date >= monthStart, ct);
            var countAll = await db.PartnerSlips.CountAsync(s => !s.IsDisabled, ct);

            var entityFacets = await FacetsHelper.GetEntityFacetsAsync(connStr, "partner_slips", "GroupNames,TagNames,Group,Tags", ct);
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
        })
        .WithName("PartnerSlipsGetFacets");

        group.MapGet("/slips/count", async (DateTime? from, DateTime? till, MermerDbContext db, CancellationToken ct) =>
        {
            var query = db.PartnerSlips.AsNoTracking().Where(s => !s.IsDisabled);

            if (from.HasValue && from.Value.Year > 2000)
            {
                var fUtc = from.Value.ToUniversalTime();
                query = query.Where(s => s.Date >= fUtc);
            }

            if (till.HasValue && till.Value.Year < 2099)
            {
                var tUtc = till.Value.ToUniversalTime();
                query = query.Where(s => s.Date <= tUtc);
            }

            var count = await query.CountAsync(ct);
            return Results.Ok(new { count });
        });

        group.MapGet("/slips/next-code", async (MermerDbContext db, CancellationToken ct) =>
        {
            var count = await db.PartnerSlips.CountAsync(ct);
            return Results.Ok(new { code = $"DOC-{DateTime.UtcNow:yyMMdd}{(count + 1):D4}" });
        });

        // 7. ПОДГРУЗКА PARTNER SLIPS
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
                Date = s.Date.ToLocalTime().ToString("yyyy-MM-ddTHH:mm:ss"),
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

        // 8. СОХРАНЕНИЕ PARTNER SLIPS
        Func<HttpRequest, MermerDbContext, Task<IResult>> saveSlipHandler = async (request, db) =>
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
            Guid? officeGuid = Guid.TryParse(offStr, out var parsedOff) && parsedOff != Guid.Empty ? parsedOff : null;

            if (!officeGuid.HasValue)
            {
                var defOffice = await db.Offices.AsNoTracking().FirstOrDefaultAsync(o => !o.IsDisabled);
                officeGuid = defOffice?.Id;
            }

            string dateStr = GetJsonString(root, "date", "Date");
            DateTime slipDate = DateTime.TryParse(dateStr, out var pDate) ? pDate.ToUniversalTime() : DateTime.UtcNow;

            string groupName = GetJsonString(root, "group", "Group") ?? string.Empty;
            string description = GetJsonString(root, "description", "Description") ?? string.Empty;
            string userName = GetJsonString(root, "userName", "UserName") ?? "admin";
            var tagsList = ExtractTagsFromRawJson(root);

            var existing = await db.PartnerSlips
                .Include(s => s.Lines)
                .FirstOrDefaultAsync(s => s.Id == slipId);

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
                    Lines = linesList,
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

                if (existing.Lines != null && existing.Lines.Any())
                {
                    db.PartnerSlipLines.RemoveRange(existing.Lines);
                }

                if (linesList.Any())
                {
                    await db.PartnerSlipLines.AddRangeAsync(linesList);
                }
            }

            await db.SaveChangesAsync();
            return Results.Content($"{{\"id\":\"{slipId}\",\"code\":\"{code}\"}}", "application/json");
        };

        group.MapPost("/slips", saveSlipHandler);
        group.MapPut("/slips/{id}", saveSlipHandler);

        // 9. ФАСЕТЫ ДЛЯ PARTNER TRANSFERS
        group.MapGet("/transfers/facets", async (string? fields, MermerDbContext db, CancellationToken ct) =>
        {
            var todayUtc = DateTime.UtcNow.Date;
            var weekStart = todayUtc.AddDays(-7);
            var monthStart = new DateTime(todayUtc.Year, todayUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var yearStart = new DateTime(todayUtc.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            var countToday = await db.PartnerTransfers.CountAsync(t => !t.IsDisabled && t.Date >= todayUtc, ct);
            var countWeek = await db.PartnerTransfers.CountAsync(t => !t.IsDisabled && t.Date >= weekStart, ct);
            var countMonth = await db.PartnerTransfers.CountAsync(t => !t.IsDisabled && t.Date >= monthStart, ct);
            var countYear = await db.PartnerTransfers.CountAsync(t => !t.IsDisabled && t.Date >= yearStart, ct);
            var countAll = await db.PartnerTransfers.CountAsync(t => !t.IsDisabled, ct);

            var connStr = db.Database.GetConnectionString()!;
            var entityFacets = await FacetsHelper.GetEntityFacetsAsync(connStr, "partner_transfers", fields ?? "GroupNames,TagNames,Group,Tags", ct);
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
                    { "#This Year", countYear },
                    { "#All Records", countAll }
                }
            });
        })
        .WithName("PartnerTransfersGetFacets");

        group.MapGet("/transfers/count", async (DateTime? from, DateTime? till, MermerDbContext db, CancellationToken ct) =>
        {
            var query = db.PartnerTransfers.AsNoTracking().Where(t => !t.IsDisabled);

            if (from.HasValue && from.Value.Year > 2000)
            {
                var fUtc = from.Value.Kind == DateTimeKind.Utc ? from.Value : from.Value.ToUniversalTime();
                query = query.Where(t => t.Date >= fUtc);
            }

            if (till.HasValue && till.Value.Year < 2099)
            {
                var tUtc = till.Value.Kind == DateTimeKind.Utc ? till.Value : till.Value.ToUniversalTime();
                query = query.Where(t => t.Date <= tUtc);
            }

            var count = await query.CountAsync(ct);
            return Results.Ok(new { count });
        })
        .WithName("PartnerTransfersGetCount");

        // 10. ПОДГРУЗКА ПЕРЕВОДОВ PARTNER TRANSFERS
        group.MapGet("/transfers", async (int? limit, int? offset, MermerDbContext db, CancellationToken ct) =>
        {
            int take = limit.HasValue ? Math.Clamp(limit.Value, 1, 1000) : 200;
            int skip = offset.GetValueOrDefault(0);

            var transfers = await db.PartnerTransfers
                .AsNoTracking()
                .Where(t => !t.IsDisabled)
                .OrderBy(t => t.Group == null ? string.Empty : t.Group.Trim())
                .ThenByDescending(t => t.Date)
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

                string normalizedGroup = string.IsNullOrWhiteSpace(t.Group) ? string.Empty : t.Group.Trim();

                return new
                {
                    Id = t.Id.ToString(),
                    Code = t.Code,
                    Date = t.Date.ToLocalTime().ToString("yyyy-MM-ddTHH:mm:ss"),
                    Type = "PartnerTransfer",
                    UserName = t.UserName ?? "admin",
                    Group = normalizedGroup,
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

        // 11. СОХРАНЕНИЕ ПЕРЕВОДОВ PARTNER TRANSFERS
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

            string rawGroup = GetJsonString(root, "group", "Group");
            string groupName = string.IsNullOrWhiteSpace(rawGroup) ? string.Empty : rawGroup.Trim();

            string description = GetJsonString(root, "description", "Description") ?? string.Empty;
            string userName = GetJsonString(root, "userName", "UserName") ?? "admin";
            var tagsList = ExtractTagsFromRawJson(root);

            // Ищем дефолтный офис предприятия для строк, в которых офис не указан
            var defOffice = await db.Offices.AsNoTracking().FirstOrDefaultAsync(o => !o.IsDisabled);
            Guid? defaultOfficeId = defOffice?.Id;

            // Ищем дефолтную валюту предприятия
            var defCur = await db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => c.IsDefault && !c.IsDisabled)
                         ?? await db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => !c.IsDisabled);
            Guid? defaultCurrencyId = defCur?.Id;

            var existing = await db.PartnerTransfers
                .Include(t => t.Lines)
                .FirstOrDefaultAsync(t => t.Id == transferId);

            var linesList = new List<PartnerTransferLineEntity>();
            if (TryGetPropertyCaseInsensitive(root, "lines", out var linesProp) && linesProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var l in linesProp.EnumerateArray())
                {
                    string officeIdStr = GetJsonString(l, "officeId", "OfficeId");
                    Guid? oGuid = Guid.TryParse(officeIdStr, out var parsedO) && parsedO != Guid.Empty
                        ? parsedO
                        : defaultOfficeId;

                    string partnerIdStr = GetJsonString(l, "partnerId", "PartnerId");
                    Guid? pGuid = Guid.TryParse(partnerIdStr, out var parsedP) ? parsedP : null;

                    string debitCurStr = GetJsonString(l, "debitCurrencyId", "DebitCurrencyId");
                    Guid? debitCurGuid = Guid.TryParse(debitCurStr, out var pDebCur) && pDebCur != Guid.Empty
                        ? pDebCur
                        : defaultCurrencyId;

                    string creditCurStr = GetJsonString(l, "creditCurrencyId", "CreditCurrencyId");
                    Guid? creditCurGuid = Guid.TryParse(creditCurStr, out var pCredCur) && pCredCur != Guid.Empty
                        ? pCredCur
                        : defaultCurrencyId;

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

            if (existing != null)
            {
                if (existing.Lines != null && existing.Lines.Any())
                {
                    db.PartnerTransferLines.RemoveRange(existing.Lines);
                }
                if (linesList.Any())
                {
                    await db.PartnerTransferLines.AddRangeAsync(linesList);
                }
                await db.SaveChangesAsync();
            }

            return Results.Content($"{{\"id\":\"{transferId}\",\"code\":\"{code}\"}}", "application/json");
        });

        // 12. ПОДСЧЕТ КОЛИЧЕСТВА ДВИЖЕНИЙ ДЛЯ ПЛИТОК ДАТ
        group.MapGet("/actions/count", async (string? partnerId, DateTime? from, DateTime? till, HttpRequest req, MermerDbContext db, CancellationToken ct) =>
        {
            DateTime fUtc = from?.ToUniversalTime() ?? DateTime.SpecifyKind(new DateTime(2000, 1, 1), DateTimeKind.Utc);
            DateTime tUtc = till?.ToUniversalTime() ?? DateTime.SpecifyKind(new DateTime(2099, 12, 31), DateTimeKind.Utc);

            Guid? pGuid = Guid.TryParse(partnerId, out var g) ? g : null;

            var officeParamValues = req.Query["officeIds"].Concat(req.Query["officeId"]);
            var targetOfficeGuids = officeParamValues
                .Select(x => Guid.TryParse(x, out var og) ? (Guid?)og : null)
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .Distinct()
                .ToArray();

            var defOffice = await db.Offices.AsNoTracking().FirstOrDefaultAsync(o => !o.IsDisabled, ct);
            Guid defaultOfficeGuid = defOffice?.Id ?? Guid.Empty;

            const string sql = """
                SELECT 
                    (
                        SELECT COUNT(*)
                        FROM invoices i
                        WHERE i.partner_id IS NOT NULL 
                          AND i.is_disabled = false
                          AND (@partner::uuid IS NULL OR i.partner_id = @partner)
                          AND (cardinality(@offices::uuid[]) = 0 OR COALESCE(i.office_id, @defOffice) = ANY(@offices))
                          AND i.date >= @from AND i.date <= @till
                    )
                    +
                    (
                        SELECT COUNT(*)
                        FROM partner_slip_lines psl
                        JOIN partner_slips ps ON ps.id = psl.partner_slip_id
                        WHERE psl.partner_id IS NOT NULL
                          AND ps.is_disabled = false
                          AND (@partner::uuid IS NULL OR psl.partner_id = @partner)
                          AND (cardinality(@offices::uuid[]) = 0 OR COALESCE(ps.office_id, @defOffice) = ANY(@offices))
                          AND ps.date >= @from AND ps.date <= @till
                    )
                    +
                    (
                        SELECT COUNT(*)
                        FROM partner_transfer_lines ptl
                        JOIN partner_transfers pt ON pt.id = ptl.partner_transfer_id
                        WHERE ptl.partner_id IS NOT NULL
                          AND pt.is_disabled = false
                          AND (@partner::uuid IS NULL OR ptl.partner_id = @partner)
                          AND (cardinality(@offices::uuid[]) = 0 OR COALESCE(ptl.office_id, @defOffice) = ANY(@offices))
                          AND pt.date >= @from AND pt.date <= @till
                    )
                    +
                    (
                        SELECT COUNT(*)
                        FROM funds_slips fs
                        WHERE fs.partner_id IS NOT NULL
                          AND fs.is_disabled = false
                          AND (@partner::uuid IS NULL OR fs.partner_id = @partner)
                          AND (cardinality(@offices::uuid[]) = 0 OR COALESCE(fs.office_id, @defOffice) = ANY(@offices))
                          AND fs.date >= @from AND fs.date <= @till
                    ) AS "Count";
                """;

            var connStr = db.Database.GetConnectionString();
            await using var conn = new NpgsqlConnection(connStr);
            var totalCount = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                sql,
                new { partner = pGuid, offices = targetOfficeGuids, defOffice = defaultOfficeGuid, from = fUtc, till = tUtc },
                cancellationToken: ct));

            return Results.Ok(new { count = totalCount });
        });

        // 13. РЕЕСТР ДВИЖЕНИЙ ПО ПАРТНЕРАМ (ВКЛЮЧАЯ ОПЛАТЫ И КОЛЛЕКЦИИ)
        group.MapGet("/actions", async (string? partnerId, DateTime? from, DateTime? till, int? limit, HttpRequest req, MermerDbContext db, CancellationToken ct) =>
        {
            int take = limit.HasValue ? Math.Clamp(limit.Value, 1, 50000) : 10000;
            DateTime fUtc = from?.ToUniversalTime() ?? DateTime.SpecifyKind(new DateTime(2000, 1, 1), DateTimeKind.Utc);
            DateTime tUtc = till?.ToUniversalTime() ?? DateTime.SpecifyKind(new DateTime(2099, 12, 31), DateTimeKind.Utc);

            Guid? pGuid = Guid.TryParse(partnerId, out var g) ? g : null;

            var officeParamValues = req.Query["officeIds"].Concat(req.Query["officeId"]);
            var targetOfficeGuids = officeParamValues
                .Select(x => Guid.TryParse(x, out var og) ? (Guid?)og : null)
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .Distinct()
                .ToArray();

            var defCur = await db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => c.IsDefault && !c.IsDisabled, ct)
                         ?? await db.Currencies.AsNoTracking().FirstOrDefaultAsync(ct);
            Guid defaultCurrencyId = defCur?.Id ?? Guid.Empty;

            var defOffice = await db.Offices.AsNoTracking().FirstOrDefaultAsync(o => !o.IsDisabled, ct);
            Guid defaultOfficeGuid = defOffice?.Id ?? Guid.Empty;

            const string sql = """
                WITH def_cur AS (
                    SELECT @defCurId::uuid AS id
                ),
                filtered_invoices AS (
                    SELECT 
                        i.id, i.code, i.invoice_type, i.date, i.office_id, i.partner_id, i.user_name, i.is_completed, i.is_disabled, i.display_currency_id
                    FROM invoices i
                    WHERE i.partner_id IS NOT NULL 
                      AND i.is_disabled = false
                      AND (@partner::uuid IS NULL OR i.partner_id = @partner)
                      AND (cardinality(@offices::uuid[]) = 0 OR COALESCE(i.office_id, @defOffice) = ANY(@offices))
                      AND i.date >= @from AND i.date <= @till
                    ORDER BY i.date DESC
                    LIMIT @take
                ),
                raw_actions AS (
                    -- 1. Накладные (Invoices: Sales / Purchase)
                    SELECT 
                        fi.id::text                              AS "TransactionId",
                        fi.code                                   AS "TransactionCode",
                        fi.invoice_type                           AS "TransactionType",
                        fi.date                                   AS "TransactionDate",
                        COALESCE(fi.office_id, @defOffice)::text  AS "ActionOfficeId",
                        fi.partner_id::text                       AS "ActionPartnerId",
                        CASE WHEN fi.invoice_type IN ('Sales', 'PurchaseReturn') 
                             THEN COALESCE((
                                 SELECT SUM(il.quantity * (il.price * CASE 
                                     WHEN fi.display_currency_id IS NULL OR fi.display_currency_id = (SELECT id FROM def_cur) THEN 1.0
                                     ELSE COALESCE(cr.multiplier / NULLIF(cr.divider, 0), 1.0)
                                 END))
                                 FROM invoice_lines il 
                                 WHERE il.invoice_id = fi.id
                             ), 0) 
                             ELSE 0 END AS "ActionDebit",
                        CASE WHEN fi.invoice_type IN ('Purchase', 'SalesReturn') 
                             THEN COALESCE((
                                 SELECT SUM(il.quantity * (il.price * CASE 
                                     WHEN fi.display_currency_id IS NULL OR fi.display_currency_id = (SELECT id FROM def_cur) THEN 1.0
                                     ELSE COALESCE(cr.multiplier / NULLIF(cr.divider, 0), 1.0)
                                 END))
                                 FROM invoice_lines il 
                                 WHERE il.invoice_id = fi.id
                             ), 0) 
                             ELSE 0 END AS "ActionCredit",
                        fi.user_name                             AS "TransactionUserName",
                        fi.is_completed                           AS "TransactionIsCompleted",
                        fi.is_disabled                            AS "TransactionIsDisabled"
                    FROM filtered_invoices fi
                    LEFT JOIN LATERAL (
                        SELECT r.multiplier, r.divider 
                        FROM currency_rates r 
                        WHERE r.currency_id = fi.display_currency_id AND r.valid_from <= fi.date 
                        ORDER BY r.valid_from DESC LIMIT 1
                    ) cr ON true

                    UNION ALL

                    -- 2. Кассовые ордера (Funds Slips: Collection / Payment)
                    SELECT 
                        fs.id::text                              AS "TransactionId",
                        fs.code                                   AS "TransactionCode",
                        COALESCE(fs.funds_slip_type, 'Collection') AS "TransactionType",
                        fs.date                                   AS "TransactionDate",
                        COALESCE(fs.office_id, @defOffice)::text  AS "ActionOfficeId",
                        fs.partner_id::text                       AS "ActionPartnerId",
                        CASE WHEN fs.funds_slip_type IN ('Expense', 'Payment') 
                             THEN COALESCE((
                                 SELECT SUM(fsl.amount * CASE 
                                     WHEN fs.display_currency_id IS NULL OR fs.display_currency_id = (SELECT id FROM def_cur) THEN 1.0
                                     ELSE COALESCE(cr.multiplier / NULLIF(cr.divider, 0), 1.0)
                                 END)
                                 FROM funds_slip_lines fsl 
                                 WHERE fsl.funds_slip_id = fs.id
                             ), 0) 
                             ELSE 0 END AS "ActionDebit",
                        CASE WHEN fs.funds_slip_type IN ('Income', 'Collection', 'FundsOpening', 'FundsRevisionExceed') 
                             THEN COALESCE((
                                 SELECT SUM(fsl.amount * CASE 
                                     WHEN fs.display_currency_id IS NULL OR fs.display_currency_id = (SELECT id FROM def_cur) THEN 1.0
                                     ELSE COALESCE(cr.multiplier / NULLIF(cr.divider, 0), 1.0)
                                 END)
                                 FROM funds_slip_lines fsl 
                                 WHERE fsl.funds_slip_id = fs.id
                             ), 0) 
                             ELSE 0 END AS "ActionCredit",
                        COALESCE(fs.user_name, 'admin')           AS "TransactionUserName",
                        fs.is_completed                           AS "TransactionIsCompleted",
                        fs.is_disabled                            AS "TransactionIsDisabled"
                    FROM funds_slips fs
                    LEFT JOIN LATERAL (
                        SELECT r.multiplier, r.divider 
                        FROM currency_rates r 
                        WHERE r.currency_id = fs.display_currency_id AND r.valid_from <= fs.date 
                        ORDER BY r.valid_from DESC LIMIT 1
                    ) cr ON true
                    WHERE fs.partner_id IS NOT NULL 
                      AND fs.is_disabled = false
                      AND (@partner::uuid IS NULL OR fs.partner_id = @partner)
                      AND (cardinality(@offices::uuid[]) = 0 OR COALESCE(fs.office_id, @defOffice) = ANY(@offices))
                      AND fs.date >= @from AND fs.date <= @till

                    UNION ALL

                    -- 3. Акты сверки и начальные остатки (Partner Slips)
                    SELECT 
                        ps.id::text                             AS "TransactionId",
                        ps.code                                 AS "TransactionCode",
                        ps.slip_type                            AS "TransactionType",
                        ps.date                                 AS "TransactionDate",
                        COALESCE(ps.office_id, @defOffice)::text AS "ActionOfficeId",
                        psl.partner_id::text                    AS "ActionPartnerId",
                        psl.debit_amount                        AS "ActionDebit",
                        psl.credit_amount                       AS "ActionCredit",
                        ps.user_name                            AS "TransactionUserName",
                        true                                    AS "TransactionIsCompleted",
                        ps.is_disabled                          AS "TransactionIsDisabled"
                    FROM partner_slip_lines psl
                    JOIN partner_slips ps ON ps.id = psl.partner_slip_id
                    WHERE psl.partner_id IS NOT NULL
                      AND ps.is_disabled = false
                      AND (@partner::uuid IS NULL OR psl.partner_id = @partner)
                      AND (cardinality(@offices::uuid[]) = 0 OR COALESCE(ps.office_id, @defOffice) = ANY(@offices))
                      AND ps.date >= @from AND ps.date <= @till

                    UNION ALL

                    -- 4. Переводы долга между партнерами (Partner Transfers)
                    SELECT 
                        pt.id::text                             AS "TransactionId",
                        pt.code                                 AS "TransactionCode",
                        'PartnerTransfer'                       AS "TransactionType",
                        pt.date                                 AS "TransactionDate",
                        COALESCE(ptl.office_id, @defOffice)::text AS "ActionOfficeId",
                        ptl.partner_id::text                    AS "ActionPartnerId",
                        ptl.debit_amount                        AS "ActionDebit",
                        ptl.credit_amount                       AS "ActionCredit",
                        pt.user_name                            AS "TransactionUserName",
                        true                                    AS "TransactionIsCompleted",
                        pt.is_disabled                          AS "TransactionIsDisabled"
                    FROM partner_transfer_lines ptl
                    JOIN partner_transfers pt ON pt.id = ptl.partner_transfer_id
                    WHERE ptl.partner_id IS NOT NULL
                      AND pt.is_disabled = false
                      AND (@partner::uuid IS NULL OR ptl.partner_id = @partner)
                      AND (cardinality(@offices::uuid[]) = 0 OR COALESCE(ptl.office_id, @defOffice) = ANY(@offices))
                      AND pt.date >= @from AND pt.date <= @till
                )
                SELECT 
                    *,
                    ("ActionDebit" - "ActionCredit")::numeric(18,4) AS "ActionEffect",
                    ("ActionDebit" - "ActionCredit")::numeric(18,4) AS "ActionEffectInCustomCurrency",
                    '' AS "TransactionGroup",
                    ARRAY[]::text[] AS "TransactionTags"
                FROM raw_actions
                ORDER BY "TransactionDate" DESC
                LIMIT @take;
                """;

            var connStr = db.Database.GetConnectionString();
            await using var conn = new NpgsqlConnection(connStr);
            var rawList = await conn.QueryAsync(new CommandDefinition(
                sql,
                new { partner = pGuid, offices = targetOfficeGuids, defOffice = defaultOfficeGuid, defCurId = defaultCurrencyId, from = fUtc, till = tUtc, take },
                cancellationToken: ct));

            var result = rawList.Select(r => new
            {
                TransactionId = (string)r.TransactionId,
                TransactionCode = (string)r.TransactionCode,
                TransactionType = (string)r.TransactionType,
                TransactionDate = ((DateTime)r.TransactionDate).ToLocalTime().ToString("yyyy-MM-ddTHH:mm:ss"),
                ActionOfficeId = (string)r.ActionOfficeId,
                ActionPartnerId = (string)r.ActionPartnerId,
                ActionDebit = (decimal)r.ActionDebit,
                ActionCredit = (decimal)r.ActionCredit,
                ActionEffect = (decimal)r.ActionEffect,
                ActionEffectInCustomCurrency = (decimal)r.ActionEffectInCustomCurrency,
                TransactionUserName = (string)r.TransactionUserName,
                TransactionGroup = (string)r.TransactionGroup,
                TransactionTags = (string[])r.TransactionTags,
                TransactionIsCompleted = (bool)r.TransactionIsCompleted,
                TransactionIsDisabled = (bool)r.TransactionIsDisabled
            }).ToList();

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = null,
                ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
            };

            return Results.Json(result, jsonOptions);
        });

        // 14. ПОЛУЧЕНИЕ СУММАРНОГО САЛЬДО ПО ПАРТНЕРУ НА ДАТУ
        group.MapGet("/balance-to-date", async (string officeId, string partnerId, DateTime? date, string? excludeId, MermerDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(officeId) || string.IsNullOrWhiteSpace(partnerId))
                return Results.Ok(new { Balance = 0m });

            Guid? pGuid = Guid.TryParse(partnerId, out var g) ? g : null;
            Guid? oGuid = Guid.TryParse(officeId, out var og) ? og : null;
            Guid? exGuid = Guid.TryParse(excludeId, out var eg) ? eg : null;

            if (!pGuid.HasValue)
                return Results.Ok(new { Balance = 0m });

            DateTime dUtc = date?.ToUniversalTime() ?? DateTime.UtcNow;

            var defCur = await db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => c.IsDefault && !c.IsDisabled, ct)
                         ?? await db.Currencies.AsNoTracking().FirstOrDefaultAsync(ct);
            Guid defaultCurrencyId = defCur?.Id ?? Guid.Empty;

            var defOffice = await db.Offices.AsNoTracking().FirstOrDefaultAsync(o => !o.IsDisabled, ct);
            Guid defaultOfficeGuid = defOffice?.Id ?? Guid.Empty;

            const string sql = """
        WITH def_cur AS (
            SELECT @defCurId::uuid AS id
        ),
        moves AS (
            -- 1. Накладные (Invoices)
            SELECT 
                i.date,
                CASE 
                    WHEN i.invoice_type IN ('Sales', 'PurchaseReturn') THEN
                        COALESCE((SELECT SUM(il.quantity * (il.price * CASE 
                            WHEN i.display_currency_id IS NULL OR i.display_currency_id = (SELECT id FROM def_cur) THEN 1.0
                            ELSE COALESCE(cr.multiplier / NULLIF(cr.divider, 0), 1.0)
                        END)) FROM invoice_lines il WHERE il.invoice_id = i.id), 0)
                    ELSE 0
                END AS debit,
                CASE 
                    WHEN i.invoice_type IN ('Purchase', 'SalesReturn') THEN
                        COALESCE((SELECT SUM(il.quantity * (il.price * CASE 
                            WHEN i.display_currency_id IS NULL OR i.display_currency_id = (SELECT id FROM def_cur) THEN 1.0
                            ELSE COALESCE(cr.multiplier / NULLIF(cr.divider, 0), 1.0)
                        END)) FROM invoice_lines il WHERE il.invoice_id = i.id), 0)
                    ELSE 0
                END AS credit
            FROM invoices i
            LEFT JOIN LATERAL (
                SELECT r.multiplier, r.divider 
                FROM currency_rates r 
                WHERE r.currency_id = i.display_currency_id AND r.valid_from <= i.date 
                ORDER BY r.valid_from DESC LIMIT 1
            ) cr ON true
            WHERE i.partner_id = @partner
              AND i.is_disabled = false
              AND COALESCE(i.office_id, @defOffice) = COALESCE(@office, @defOffice)
              AND i.date < @docDate
              AND (@excludeId::uuid IS NULL OR i.id <> @excludeId)

            UNION ALL

            -- 2. Оплаты и касса (Funds Slips)
            SELECT 
                fs.date,
                CASE 
                    WHEN fs.funds_slip_type IN ('Expense', 'Payment') THEN
                        COALESCE((SELECT SUM(fsl.amount * CASE 
                            WHEN fs.display_currency_id IS NULL OR fs.display_currency_id = (SELECT id FROM def_cur) THEN 1.0
                            ELSE COALESCE(cr.multiplier / NULLIF(cr.divider, 0), 1.0)
                        END) FROM funds_slip_lines fsl WHERE fsl.funds_slip_id = fs.id), 0)
                    ELSE 0
                END AS debit,
                CASE 
                    WHEN fs.funds_slip_type IN ('Income', 'Collection', 'FundsOpening', 'FundsRevisionExceed') THEN
                        COALESCE((SELECT SUM(fsl.amount * CASE 
                            WHEN fs.display_currency_id IS NULL OR fs.display_currency_id = (SELECT id FROM def_cur) THEN 1.0
                            ELSE COALESCE(cr.multiplier / NULLIF(cr.divider, 0), 1.0)
                        END) FROM funds_slip_lines fsl WHERE fsl.funds_slip_id = fs.id), 0)
                    ELSE 0
                END AS credit
            FROM funds_slips fs
            LEFT JOIN LATERAL (
                SELECT r.multiplier, r.divider 
                FROM currency_rates r 
                WHERE r.currency_id = fs.display_currency_id AND r.valid_from <= fs.date 
                ORDER BY r.valid_from DESC LIMIT 1
            ) cr ON true
            WHERE fs.partner_id = @partner
              AND fs.is_disabled = false
              AND COALESCE(fs.office_id, @defOffice) = COALESCE(@office, @defOffice)
              AND fs.date < @docDate
              AND (@excludeId::uuid IS NULL OR fs.id <> @excludeId)

            UNION ALL

            -- 3. Акты сверки и остатки (Partner Slips)
            SELECT 
                ps.date,
                psl.debit_amount AS debit,
                psl.credit_amount AS credit
            FROM partner_slip_lines psl
            JOIN partner_slips ps ON ps.id = psl.partner_slip_id
            WHERE psl.partner_id = @partner
              AND ps.is_disabled = false
              AND COALESCE(ps.office_id, @defOffice) = COALESCE(@office, @defOffice)
              AND ps.date < @docDate
              AND (@excludeId::uuid IS NULL OR ps.id <> @excludeId)

            UNION ALL

            -- 4. Переводы долга (Partner Transfers)
            SELECT 
                pt.date,
                ptl.debit_amount AS debit,
                ptl.credit_amount AS credit
            FROM partner_transfer_lines ptl
            JOIN partner_transfers pt ON pt.id = ptl.partner_transfer_id
            WHERE ptl.partner_id = @partner
              AND pt.is_disabled = false
              AND COALESCE(ptl.office_id, @defOffice) = COALESCE(@office, @defOffice)
              AND pt.date < @docDate
              AND (@excludeId::uuid IS NULL OR pt.id <> @excludeId)
        )
        SELECT COALESCE(SUM(debit - credit), 0)::numeric(18,4) AS "Balance"
        FROM moves;
        """;

            var connStr = db.Database.GetConnectionString();
            await using var conn = new NpgsqlConnection(connStr);
            var balance = await conn.ExecuteScalarAsync<decimal>(new CommandDefinition(
                sql,
                new { partner = pGuid, office = oGuid, defOffice = defaultOfficeGuid, defCurId = defaultCurrencyId, docDate = dUtc, excludeId = exGuid },
                cancellationToken: ct));

            return Results.Ok(new { Balance = balance });
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

    public class PartnerMergeDto
    {
        public string MainItemId { get; set; } = string.Empty;
        public string[] MergeItemIds { get; set; } = Array.Empty<string>();
        public bool DisableMergedItems { get; set; }
    }
}
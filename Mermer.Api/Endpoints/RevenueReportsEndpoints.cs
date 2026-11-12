using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Mermer.Data.Postgres;

namespace Mermer.Api.Endpoints;

public static class RevenueReportsEndpoints
{
    public static IEndpointRouteBuilder MapRevenueReportsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/revenue-reports").WithTags("RevenueReports");

        group.MapGet("/", async (HttpRequest req, MermerDbContext db, CancellationToken ct) =>
        {
            string? fromStr = req.Query["dateFrom"].FirstOrDefault();
            string? tillStr = req.Query["dateTill"].FirstOrDefault();

            DateTime dateFrom = ParseSafeDate(fromStr, isTill: false);
            DateTime dateTill = ParseSafeDate(tillStr, isTill: true);

            if (dateFrom > dateTill)
            {
                var temp = dateFrom;
                dateFrom = dateTill;
                dateTill = temp;
            }

            var whIds = req.Query["warehouseId"]
                .Select(x => Guid.TryParse(x, out var g) ? (Guid?)g : null)
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .ToArray();

            // ПОДСТАНОВКА DEFAULT СКЛАДА
            if (whIds.Length == 0)
            {
                var defOffice = await db.Offices.AsNoTracking().FirstOrDefaultAsync(o => !o.IsDisabled, ct);
                var defWh = (defOffice != null
                                ? await db.Warehouses.AsNoTracking().FirstOrDefaultAsync(w => w.OfficeId == defOffice.Id && !w.IsDisabled, ct)
                                : null)
                            ?? await db.Warehouses.AsNoTracking().FirstOrDefaultAsync(w => !w.IsDisabled, ct);

                if (defWh != null)
                {
                    whIds = new[] { defWh.Id };
                }
                else
                {
                    return Results.Ok(Array.Empty<object>());
                }
            }

            int limit = int.TryParse(req.Query["limit"], out var l) ? Math.Clamp(l, 1, 100000) : 100000;
            int offset = int.TryParse(req.Query["offset"], out var o) ? Math.Max(0, o) : 0;

            var defCur = await db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => c.IsDefault && !c.IsDisabled, ct)
                         ?? await db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => !c.IsDisabled, ct);
            var defaultCurrencyId = defCur?.Id ?? Guid.Empty;

            const string sql = """
                WITH def_cur AS (
                    SELECT @defCurId::uuid AS id
                ),
                target_sales AS (
                    SELECT 
                        il.id AS line_id,
                        il.source_id,
                        il.stock_id,
                        il.quantity,
                        (il.price * CASE 
                            WHEN i.display_currency_id IS NULL OR i.display_currency_id = (SELECT id FROM def_cur) THEN 1.0
                            ELSE COALESCE(cr.multiplier / NULLIF(cr.divider, 0), 1.0)
                        END) AS actual_price,
                        i.warehouse_id,
                        i.date,
                        i.invoice_type,
                        CASE WHEN i.invoice_type = 'SalesReturn' THEN -il.quantity ELSE il.quantity END AS signed_qty
                    FROM invoice_lines il
                    JOIN invoices i ON i.id = il.invoice_id
                    LEFT JOIN LATERAL (
                        SELECT r.multiplier, r.divider 
                        FROM currency_rates r 
                        WHERE r.currency_id = i.display_currency_id AND r.valid_from <= i.date 
                        ORDER BY r.valid_from DESC LIMIT 1
                    ) cr ON true
                    WHERE i.is_completed = true 
                      AND i.is_disabled = false
                      AND i.warehouse_id = ANY(@whs)
                      AND i.date >= @from AND i.date <= @till
                      AND i.invoice_type IN ('Sales', 'SalesReturn')
                      AND il.stock_id IS NOT NULL
                    ORDER BY i.date DESC
                    LIMIT @limit OFFSET @offset
                ),
                avg_purchases AS (
                    SELECT 
                        il.stock_id,
                        CASE 
                            WHEN SUM(CASE WHEN i.invoice_type = 'PurchaseReturn' THEN -il.quantity ELSE il.quantity END) > 0 
                            THEN SUM(
                                (CASE WHEN i.invoice_type = 'PurchaseReturn' THEN -il.quantity ELSE il.quantity END) * 
                                (il.price * CASE 
                                    WHEN i.display_currency_id IS NULL OR i.display_currency_id = (SELECT id FROM def_cur) THEN 1.0
                                    ELSE COALESCE(cr.multiplier / NULLIF(cr.divider, 0), 1.0)
                                 END)
                            ) / SUM(CASE WHEN i.invoice_type = 'PurchaseReturn' THEN -il.quantity ELSE il.quantity END)
                            ELSE 0 
                        END AS avg_cost
                    FROM invoice_lines il
                    JOIN invoices i ON i.id = il.invoice_id
                    LEFT JOIN LATERAL (
                        SELECT r.multiplier, r.divider 
                        FROM currency_rates r 
                        WHERE r.currency_id = i.display_currency_id AND r.valid_from <= i.date 
                        ORDER BY r.valid_from DESC LIMIT 1
                    ) cr ON true
                    WHERE il.stock_id IN (SELECT DISTINCT stock_id FROM target_sales)
                      AND i.is_completed = true 
                      AND i.is_disabled = false
                      AND i.invoice_type IN ('Purchase', 'StockOpening', 'PurchaseReturn')
                    GROUP BY il.stock_id
                ),
                latest_prices AS (
                    SELECT DISTINCT ON (sp.stock_id) 
                        sp.stock_id, 
                        (sp.price * CASE 
                            WHEN sp.currency_id IS NULL OR sp.currency_id = (SELECT id FROM def_cur) THEN 1.0
                            ELSE COALESCE(cr.multiplier / NULLIF(cr.divider, 0), 1.0)
                        END) AS price
                    FROM stock_prices sp
                    LEFT JOIN LATERAL (
                        SELECT r.multiplier, r.divider 
                        FROM currency_rates r 
                        WHERE r.currency_id = sp.currency_id AND r.valid_from <= sp.valid_from 
                        ORDER BY r.valid_from DESC LIMIT 1
                    ) cr ON true
                    WHERE sp.stock_id IN (SELECT DISTINCT stock_id FROM target_sales)
                    ORDER BY sp.stock_id, sp.valid_from DESC
                ),
                source_costs AS (
                    SELECT 
                        src_l.id AS source_line_id,
                        (src_l.price * CASE 
                            WHEN src_i.display_currency_id IS NULL OR src_i.display_currency_id = (SELECT id FROM def_cur) THEN 1.0
                            ELSE COALESCE(cr.multiplier / NULLIF(cr.divider, 0), 1.0)
                        END) AS converted_cost
                    FROM invoice_lines src_l
                    JOIN invoices src_i ON src_i.id = src_l.invoice_id
                    LEFT JOIN LATERAL (
                        SELECT r.multiplier, r.divider 
                        FROM currency_rates r 
                        WHERE r.currency_id = src_i.display_currency_id AND r.valid_from <= src_i.date 
                        ORDER BY r.valid_from DESC LIMIT 1
                    ) cr ON true
                    WHERE src_l.id IN (SELECT DISTINCT source_id FROM target_sales WHERE source_id IS NOT NULL)
                )
                SELECT 
                    ts.date                                                                         AS "Date",
                    ts.warehouse_id::text                                                           AS "WarehouseId",
                    ts.stock_id::text                                                               AS "StockId",
                    COALESCE(s.code, '')                                                            AS "StockCode",
                    COALESCE(s.name, '')                                                            AS "StockName",
                    COALESCE(s.type, '')                                                            AS "StockType",
                    COALESCE(s.group_name, '')                                                      AS "StockGroup",
                    ts.signed_qty                                                                   AS "Quantity",
                    (ts.signed_qty * COALESCE(sc.converted_cost, ap.avg_cost, 0))::numeric(18,4)   AS "InitialCosts",
                    0::numeric(18,4)                                                                AS "OverheadsCosts",
                    COALESCE(lp.price, 0)::numeric(18,4)                                           AS "RecommendedPrice",
                    ts.actual_price::numeric(18,4)                                                  AS "ActualPrice"
                FROM target_sales ts
                LEFT JOIN stocks s ON s.id = ts.stock_id
                LEFT JOIN source_costs sc ON sc.source_line_id = ts.source_id
                LEFT JOIN avg_purchases ap ON ap.stock_id = ts.stock_id
                LEFT JOIN latest_prices lp ON lp.stock_id = ts.stock_id
                ORDER BY ts.date;
                """;

            var connStr = db.Database.GetConnectionString();
            await using var conn = new NpgsqlConnection(connStr);
            var rawResults = await conn.QueryAsync(new CommandDefinition(
                sql,
                new { whs = whIds, from = dateFrom, till = dateTill, defCurId = defaultCurrencyId, limit, offset },
                cancellationToken: ct));

            var results = rawResults.Select(r => new
            {
                Date = ((DateTime)r.Date).ToLocalTime().ToString("yyyy-MM-ddTHH:mm:ss"),
                WarehouseId = (string)r.WarehouseId,
                StockId = (string)r.StockId,
                StockCode = (string)r.StockCode,
                StockName = (string)r.StockName,
                StockUnit = "",
                StockType = (string)r.StockType,
                StockGroup = (string)r.StockGroup,
                StockTags = new List<string>(),
                Quantity = (decimal)r.Quantity,
                InitialCosts = Math.Round((decimal)r.InitialCosts, 2, MidpointRounding.AwayFromZero),
                OverheadsCosts = Math.Round((decimal)r.OverheadsCosts, 2, MidpointRounding.AwayFromZero),
                RecommendedPrice = Math.Round((decimal)r.RecommendedPrice, 2, MidpointRounding.AwayFromZero),
                ActualPrice = Math.Round((decimal)r.ActualPrice, 2, MidpointRounding.AwayFromZero)
            }).ToList();

            return Results.Ok(results);
        });

        return app;
    }

    private static DateTime ParseSafeDate(string? raw, bool isTill)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return isTill ? DateTime.SpecifyKind(new DateTime(2099, 12, 31), DateTimeKind.Utc) : DateTime.SpecifyKind(new DateTime(2000, 1, 1), DateTimeKind.Utc);

        raw = raw.Replace(" ", "+").Trim();
        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
        {
            var res = dto.UtcDateTime;
            if (isTill && dto.TimeOfDay == TimeSpan.Zero && !raw.EndsWith("Z", StringComparison.OrdinalIgnoreCase))
            {
                res = res.Date.AddDays(1).AddTicks(-1);
            }
            return res;
        }

        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            var res = dt.ToUniversalTime();
            if (isTill && dt.TimeOfDay == TimeSpan.Zero)
            {
                res = res.Date.AddDays(1).AddTicks(-1);
            }
            return res;
        }

        return isTill ? DateTime.SpecifyKind(new DateTime(2099, 12, 31), DateTimeKind.Utc) : DateTime.SpecifyKind(new DateTime(2000, 1, 1), DateTimeKind.Utc);
    }
}
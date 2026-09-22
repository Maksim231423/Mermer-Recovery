using System;
using System.Collections.Generic;
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
            DateTime dateFrom = DateTime.UtcNow.AddMonths(-1);
            DateTime dateTill = DateTime.UtcNow;

            if (DateTime.TryParse(req.Query["dateFrom"].FirstOrDefault()?.Replace(" ", "+"), out var pf))
                dateFrom = pf.ToUniversalTime();
            if (DateTime.TryParse(req.Query["dateTill"].FirstOrDefault()?.Replace(" ", "+"), out var pt))
                dateTill = pt.ToUniversalTime();

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

            if (whIds.Length == 0)
            {
                return Results.Ok(Array.Empty<RevenueReportDto>());
            }

            int limit = int.TryParse(req.Query["limit"], out var l) ? Math.Clamp(l, 1, 2000) : 500;
            int offset = int.TryParse(req.Query["offset"], out var o) ? Math.Max(0, o) : 0;

            const string sql = """
                WITH target_sales AS (
                    SELECT 
                        il.id AS line_id,
                        il.source_id,
                        il.stock_id,
                        il.quantity,
                        il.price AS actual_price,
                        i.warehouse_id,
                        i.date,
                        i.invoice_type,
                        CASE WHEN i.invoice_type = 'SalesReturn' THEN -il.quantity ELSE il.quantity END AS signed_qty
                    FROM invoice_lines il
                    JOIN invoices i ON i.id = il.invoice_id
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
                            THEN SUM(CASE WHEN i.invoice_type = 'PurchaseReturn' THEN -il.quantity * il.price ELSE il.quantity * il.price END) /
                                 SUM(CASE WHEN i.invoice_type = 'PurchaseReturn' THEN -il.quantity ELSE il.quantity END)
                            ELSE 0 
                        END AS avg_cost
                    FROM invoice_lines il
                    JOIN invoices i ON i.id = il.invoice_id
                    WHERE il.stock_id IN (SELECT DISTINCT stock_id FROM target_sales)
                      AND i.is_completed = true 
                      AND i.is_disabled = false
                      AND i.invoice_type IN ('Purchase', 'StockOpening', 'PurchaseReturn')
                    GROUP BY il.stock_id
                ),
                latest_prices AS (
                    SELECT DISTINCT ON (sp.stock_id) 
                        sp.stock_id, 
                        sp.price
                    FROM stock_prices sp
                    WHERE sp.stock_id IN (SELECT DISTINCT stock_id FROM target_sales)
                    ORDER BY sp.stock_id, sp.valid_from DESC
                )
                SELECT 
                    ts.date                         AS "Date",
                    ts.warehouse_id::text          AS "WarehouseId",
                    ts.stock_id::text              AS "StockId",
                    COALESCE(s.code, '')           AS "StockCode",
                    COALESCE(s.name, '')           AS "StockName",
                    COALESCE(s.type, '')           AS "StockType",
                    COALESCE(s.group_name, '')     AS "StockGroup",
                    ts.signed_qty                  AS "Quantity",
                    (ts.signed_qty * COALESCE(src.price, ap.avg_cost, 0))::numeric(18,4) AS "InitialCosts",
                    0::numeric(18,4)               AS "OverheadsCosts",
                    COALESCE(lp.price, 0)::numeric(18,4) AS "RecommendedPrice",
                    ts.actual_price                AS "ActualPrice"
                FROM target_sales ts
                LEFT JOIN stocks s ON s.id = ts.stock_id
                LEFT JOIN invoice_lines src ON src.id = ts.source_id AND src.price > 0
                LEFT JOIN avg_purchases ap ON ap.stock_id = ts.stock_id
                LEFT JOIN latest_prices lp ON lp.stock_id = ts.stock_id
                ORDER BY ts.date;
                """;

            var connStr = db.Database.GetConnectionString();
            await using var conn = new NpgsqlConnection(connStr);
            var results = await conn.QueryAsync<RevenueReportDto>(new CommandDefinition(
                sql,
                new { whs = whIds, from = dateFrom, till = dateTill, limit, offset },
                cancellationToken: ct));

            return Results.Ok(results);
        });

        return app;
    }

    public class RevenueReportDto
    {
        public DateTime Date { get; set; }
        public string WarehouseId { get; set; } = string.Empty;
        public string StockId { get; set; } = string.Empty;
        public string StockCode { get; set; } = string.Empty;
        public string StockName { get; set; } = string.Empty;
        public string StockUnit { get; set; } = string.Empty;
        public string StockType { get; set; } = string.Empty;
        public string StockGroup { get; set; } = string.Empty;
        public List<string> StockTags { get; set; } = new();

        public decimal Quantity { get; set; }
        public decimal InitialCosts { get; set; }
        public decimal OverheadsCosts { get; set; }
        public decimal RecommendedPrice { get; set; }
        public decimal ActualPrice { get; set; }
    }
}
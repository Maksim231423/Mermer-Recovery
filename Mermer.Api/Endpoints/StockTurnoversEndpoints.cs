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

public static class StockTurnoversEndpoints
{
    public static IEndpointRouteBuilder MapStockTurnoversEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/stock-turnovers").WithTags("StockTurnovers");

        group.MapGet("/", async (HttpRequest req, MermerDbContext db, CancellationToken ct) =>
        {
            string? warehouseIdStr = req.Query["warehouseId"].FirstOrDefault();
            Guid? warehouseId = Guid.TryParse(warehouseIdStr, out var w) && w != Guid.Empty ? w : null;
            int limit = int.TryParse(req.Query["limit"], out var l) ? Math.Clamp(l, 1, 1000) : 250;
            int offset = int.TryParse(req.Query["offset"], out var o) ? Math.Max(0, o) : 0;

            const string sql = """
                WITH raw_moves AS (
                    -- 1. Накладные (Покупки / Продажи / Возвраты)
                    SELECT 
                        i.warehouse_id, 
                        il.stock_id,
                        CASE WHEN i.invoice_type IN ('Purchase', 'SalesReturn') THEN il.quantity ELSE 0 END AS income,
                        CASE WHEN i.invoice_type IN ('Sales', 'PurchaseReturn') THEN il.quantity ELSE 0 END AS expense,
                        CASE WHEN i.invoice_type = 'Sales' THEN il.quantity ELSE 0 END AS sold
                    FROM invoice_lines il
                    JOIN invoices i ON i.id = il.invoice_id
                    WHERE i.is_completed = true 
                      AND i.is_disabled = false
                      AND (@wh IS NULL OR i.warehouse_id = @wh)

                    UNION ALL

                    -- 2. Складские ордера
                    SELECT 
                        ss.warehouse_id, 
                        sl.stock_id,
                        CASE WHEN ss.slip_type IN ('StockOpening', 'RevisionExceed') THEN sl.quantity ELSE 0 END AS income,
                        CASE WHEN ss.slip_type NOT IN ('StockOpening', 'RevisionExceed') THEN sl.quantity ELSE 0 END AS expense,
                        0 AS sold
                    FROM stock_slip_lines sl
                    JOIN stock_slips ss ON ss.id = sl.stock_slip_id
                    WHERE ss.is_completed = true
                      AND (@wh IS NULL OR ss.warehouse_id = @wh)

                    UNION ALL

                    -- 3. Перемещения (Расход с источника)
                    SELECT 
                        st.warehouse_id, 
                        stl.stock_id,
                        0 AS income,
                        stl.quantity AS expense,
                        0 AS sold
                    FROM stock_transfer_lines stl
                    JOIN stock_transfers st ON st.id = stl.stock_transfer_id
                    WHERE st.is_completed = true 
                      AND st.is_disabled = false
                      AND (@wh IS NULL OR st.warehouse_id = @wh)

                    UNION ALL

                    -- 4. Перемещения (Приход на получателя)
                    SELECT 
                        st.destination_warehouse_id AS warehouse_id, 
                        stl.stock_id,
                        stl.received_quantity AS income,
                        0 AS expense,
                        0 AS sold
                    FROM stock_transfer_lines stl
                    JOIN stock_transfers st ON st.id = stl.stock_transfer_id
                    WHERE st.is_completed = true 
                      AND st.is_disabled = false
                      AND (@wh IS NULL OR st.destination_warehouse_id = @wh)
                ),
                aggregated AS (
                    SELECT 
                        warehouse_id, 
                        stock_id,
                        SUM(income)::numeric(18,4) AS income,
                        SUM(expense)::numeric(18,4) AS expense,
                        SUM(sold)::numeric(18,4) AS sold
                    FROM raw_moves
                    WHERE warehouse_id IS NOT NULL AND stock_id IS NOT NULL
                    GROUP BY warehouse_id, stock_id
                    HAVING SUM(income) > 0 OR SUM(expense) > 0 OR SUM(sold) > 0
                )
                SELECT 
                    a.warehouse_id::text AS "WarehouseId",
                    a.stock_id::text     AS "StockId",
                    COALESCE(s.code, 'N/A') AS "StockCode",
                    COALESCE(s.name, 'N/A') AS "StockName",
                    COALESCE(s.type, '')    AS "StockType",
                    COALESCE(s.group_name, '') AS "StockGroup",
                    s.tags                  AS "StockTags",
                    a.income                AS "Income",
                    a.expense               AS "Expense",
                    a.sold                  AS "Sold"
                FROM aggregated a
                LEFT JOIN stocks s ON s.id = a.stock_id
                ORDER BY s.name
                LIMIT @limit OFFSET @offset;
                """;

            var connStr = db.Database.GetConnectionString();
            await using var conn = new NpgsqlConnection(connStr);
            var result = await conn.QueryAsync<StockTurnoverDataDto>(new CommandDefinition(
                sql,
                new { wh = warehouseId, limit, offset },
                cancellationToken: ct));

            return Results.Ok(result);
        });

        return app;
    }

    public class StockTurnoverDataDto
    {
        public string WarehouseId { get; set; } = string.Empty;
        public string StockId { get; set; } = string.Empty;
        public string StockCode { get; set; } = string.Empty;
        public string StockName { get; set; } = string.Empty;
        public string StockType { get; set; } = string.Empty;
        public string StockGroup { get; set; } = string.Empty;
        public string[] StockTags { get; set; } = Array.Empty<string>();
        public decimal Income { get; set; }
        public decimal Expense { get; set; }
        public decimal Sold { get; set; }
    }
}
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

public static class AggregatedReportsEndpoints
{
    public static IEndpointRouteBuilder MapAggregatedReportsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/aggregated-reports").WithTags("AggregatedReports");

        group.MapGet("/", async (HttpRequest req, MermerDbContext db, CancellationToken ct) =>
        {
            DateTime dateFrom = DateTime.UtcNow.AddMonths(-1);
            DateTime dateTill = DateTime.UtcNow;

            string? fromStr = req.Query["dateFrom"].FirstOrDefault();
            if (!string.IsNullOrEmpty(fromStr) && DateTime.TryParse(fromStr.Replace(" ", "+"), out var pf))
                dateFrom = pf.ToUniversalTime();

            string? tillStr = req.Query["dateTill"].FirstOrDefault();
            if (!string.IsNullOrEmpty(tillStr) && DateTime.TryParse(tillStr.Replace(" ", "+"), out var pt))
                dateTill = pt.ToUniversalTime();

            var officeIds = req.Query["officeId"]
                .Select(x => Guid.TryParse(x, out var g) ? (Guid?)g : null)
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .ToArray();

            if (officeIds.Length == 0)
            {
                return Results.Ok(new
                {
                    StocksReport = new { StartingBalance = 0m, Income = 0m, Expense = 0m, Lines = Array.Empty<object>() },
                    FundsReport = new { StartingBalance = 0m, Income = 0m, Expense = 0m, Lines = Array.Empty<object>() },
                    PartnersReport = new { StartingBalance = 0m, Debit = 0m, Credit = 0m, Lines = Array.Empty<object>() }
                });
            }

            var connStr = db.Database.GetConnectionString();
            await using var conn = new NpgsqlConnection(connStr);

            // 1. Склады и Номенклатура (Stocks) - SQL Агрегация
            const string stocksSql = """
                WITH target_wh AS (
                    SELECT id FROM warehouses WHERE office_id = ANY(@offices) AND is_disabled = false
                ),
                raw_moves AS (
                    -- Накладные
                    SELECT i.date, i.invoice_type AS type, il.quantity AS qty,
                           (i.invoice_type IN ('Purchase', 'SalesReturn')) AS is_inc
                    FROM invoice_lines il
                    JOIN invoices i ON i.id = il.invoice_id
                    WHERE i.warehouse_id IN (SELECT id FROM target_wh)
                      AND i.is_completed = true AND i.is_disabled = false

                    UNION ALL

                    -- Ордера
                    SELECT s.date, s.slip_type AS type, sl.quantity AS qty,
                           (s.slip_type IN ('StockOpening', 'RevisionExceed')) AS is_inc
                    FROM stock_slip_lines sl
                    JOIN stock_slips s ON s.id = sl.stock_slip_id
                    WHERE s.warehouse_id IN (SELECT id FROM target_wh)
                      AND s.is_completed = true

                    UNION ALL

                    -- Перемещения расход
                    SELECT t.date, 'StockTransferSource' AS type, tl.quantity AS qty, false AS is_inc
                    FROM stock_transfer_lines tl
                    JOIN stock_transfers t ON t.id = tl.stock_transfer_id
                    WHERE t.warehouse_id IN (SELECT id FROM target_wh)
                      AND t.is_completed = true AND t.is_disabled = false

                    UNION ALL

                    -- Перемещения приход
                    SELECT t.date, 'StockTransferDestination' AS type, tl.received_quantity AS qty, true AS is_inc
                    FROM stock_transfer_lines tl
                    JOIN stock_transfers t ON t.id = tl.stock_transfer_id
                    WHERE t.destination_warehouse_id IN (SELECT id FROM target_wh)
                      AND t.is_completed = true AND t.is_disabled = false
                )
                SELECT 
                    type,
                    SUM(CASE WHEN is_inc THEN qty ELSE 0 END)::numeric(18,4) AS income,
                    SUM(CASE WHEN NOT is_inc THEN qty ELSE 0 END)::numeric(18,4) AS expense,
                    SUM(CASE WHEN is_inc THEN qty ELSE -qty END)::numeric(18,4) AS effect,
                    false AS is_starting
                FROM raw_moves
                WHERE date >= @from AND date <= @till
                GROUP BY type

                UNION ALL

                SELECT 
                    '__START__' AS type,
                    0 AS income,
                    0 AS expense,
                    COALESCE(SUM(CASE WHEN is_inc THEN qty ELSE -qty END), 0)::numeric(18,4) AS effect,
                    true AS is_starting
                FROM raw_moves
                WHERE date < @from;
                """;

            // 2. Кассы и Фонды (Funds) - SQL Агрегация
            const string fundsSql = """
                WITH target_dep AS (
                    SELECT id FROM depositories WHERE office_id = ANY(@offices) AND is_disabled = false
                ),
                raw_funds AS (
                    -- Кассовые ордера
                    SELECT f.date, f.funds_slip_type AS type, fl.amount AS amt,
                           (f.funds_slip_type = 'Income') AS is_inc
                    FROM funds_slip_lines fl
                    JOIN funds_slips f ON f.id = fl.funds_slip_id
                    WHERE f.depository_id IN (SELECT id FROM target_dep)
                      AND f.is_completed = true AND f.is_disabled = false

                    UNION ALL

                    -- Переводы касс расход
                    SELECT t.date, 'FundsTransferOut' AS type, tl.amount AS amt, false AS is_inc
                    FROM funds_transfer_lines tl
                    JOIN funds_transfers t ON t.id = tl.funds_transfer_id
                    WHERE t.from_depository_id IN (SELECT id FROM target_dep)
                      AND t.is_completed = true AND t.is_disabled = false

                    UNION ALL

                    -- Переводы касс приход
                    SELECT t.date, 'FundsTransferIn' AS type, tl.received_amount AS amt, true AS is_inc
                    FROM funds_transfer_lines tl
                    JOIN funds_transfers t ON t.id = tl.funds_transfer_id
                    WHERE t.to_depository_id IN (SELECT id FROM target_dep)
                      AND t.is_completed = true AND t.is_disabled = false

                    UNION ALL

                    -- Оплаты счетов
                    SELECT i.date, (i.invoice_type || 'Payment') AS type, ip.amount AS amt,
                           (i.invoice_type IN ('Sales', 'PurchaseReturn')) AS is_inc
                    FROM invoice_payments ip
                    JOIN invoices i ON i.id = ip.invoice_id
                    WHERE i.depository_id IN (SELECT id FROM target_dep)
                      AND i.is_completed = true AND i.is_disabled = false
                )
                SELECT 
                    type,
                    SUM(CASE WHEN is_inc THEN amt ELSE 0 END)::numeric(18,4) AS income,
                    SUM(CASE WHEN NOT is_inc THEN amt ELSE 0 END)::numeric(18,4) AS expense,
                    SUM(CASE WHEN is_inc THEN amt ELSE -amt END)::numeric(18,4) AS effect,
                    false AS is_starting
                FROM raw_funds
                WHERE date >= @from AND date <= @till
                GROUP BY type

                UNION ALL

                SELECT 
                    '__START__' AS type,
                    0 AS income,
                    0 AS expense,
                    COALESCE(SUM(CASE WHEN is_inc THEN amt ELSE -amt END), 0)::numeric(18,4) AS effect,
                    true AS is_starting
                FROM raw_funds
                WHERE date < @from;
                """;

            // 3. Контрагенты (Partners) - SQL Агрегация
            const string partnersSql = """
                WITH raw_partners AS (
                    -- Акты сверки/ордера
                    SELECT ps.date, ps.slip_type AS type, psl.debit_amount AS deb, psl.credit_amount AS cre
                    FROM partner_slip_lines psl
                    JOIN partner_slips ps ON ps.id = psl.partner_slip_id
                    WHERE ps.office_id = ANY(@offices) AND ps.is_disabled = false

                    UNION ALL

                    -- Переводы контрагентов
                    SELECT pt.date, 'PartnerTransfer' AS type, ptl.debit_amount AS deb, ptl.credit_amount AS cre
                    FROM partner_transfer_lines ptl
                    JOIN partner_transfers pt ON pt.id = ptl.partner_transfer_id
                    WHERE ptl.office_id = ANY(@offices) AND pt.is_disabled = false

                    UNION ALL

                    -- Накладные (Покупки / Продажи)
                    SELECT i.date, i.invoice_type AS type,
                           CASE WHEN i.invoice_type IN ('Sales', 'PurchaseReturn') THEN (il.quantity * il.price) ELSE 0 END AS deb,
                           CASE WHEN i.invoice_type IN ('Purchase', 'SalesReturn') THEN (il.quantity * il.price) ELSE 0 END AS cre
                    FROM invoice_lines il
                    JOIN invoices i ON i.id = il.invoice_id
                    WHERE i.office_id = ANY(@offices) AND i.is_completed = true AND i.is_disabled = false AND i.partner_id IS NOT NULL

                    UNION ALL

                    -- Оплаты накладных
                    SELECT i.date, (i.invoice_type || 'Payment') AS type,
                           CASE WHEN i.invoice_type IN ('Purchase', 'SalesReturn') THEN ip.amount ELSE 0 END AS deb,
                           CASE WHEN i.invoice_type IN ('Sales', 'PurchaseReturn') THEN ip.amount ELSE 0 END AS cre
                    FROM invoice_payments ip
                    JOIN invoices i ON i.id = ip.invoice_id
                    WHERE i.office_id = ANY(@offices) AND i.is_completed = true AND i.is_disabled = false AND i.partner_id IS NOT NULL
                )
                SELECT 
                    type,
                    SUM(deb)::numeric(18,4) AS debit,
                    SUM(cre)::numeric(18,4) AS credit,
                    SUM(deb - cre)::numeric(18,4) AS effect,
                    false AS is_starting
                FROM raw_partners
                WHERE date >= @from AND date <= @till
                GROUP BY type

                UNION ALL

                SELECT 
                    '__START__' AS type,
                    0 AS debit,
                    0 AS credit,
                    COALESCE(SUM(deb - cre), 0)::numeric(18,4) AS effect,
                    true AS is_starting
                FROM raw_partners
                WHERE date < @from;
                """;

            var p = new { offices = officeIds, from = dateFrom, till = dateTill };

            var stocksRows = (await conn.QueryAsync(new CommandDefinition(stocksSql, p, cancellationToken: ct))).ToList();
            var fundsRows = (await conn.QueryAsync(new CommandDefinition(fundsSql, p, cancellationToken: ct))).ToList();
            var partnersRows = (await conn.QueryAsync(new CommandDefinition(partnersSql, p, cancellationToken: ct))).ToList();

            // Парсинг результата складов
            decimal stocksStart = stocksRows.FirstOrDefault(r => (bool)r.is_starting)?.effect ?? 0m;
            var stocksLines = stocksRows.Where(r => !(bool)r.is_starting).Select(r => new
            {
                Type = (string)r.type,
                Income = (decimal)r.income,
                Expense = (decimal)r.expense,
                Effect = (decimal)r.effect
            }).ToList();

            // Парсинг результата кассы
            decimal fundsStart = fundsRows.FirstOrDefault(r => (bool)r.is_starting)?.effect ?? 0m;
            var fundsLines = fundsRows.Where(r => !(bool)r.is_starting).Select(r => new
            {
                Type = (string)r.type,
                Income = (decimal)r.income,
                Expense = (decimal)r.expense,
                Effect = (decimal)r.effect
            }).ToList();

            // Парсинг результата контрагентов
            decimal partnersStart = partnersRows.FirstOrDefault(r => (bool)r.is_starting)?.effect ?? 0m;
            var partnersLines = partnersRows.Where(r => !(bool)r.is_starting).Select(r => new
            {
                Type = (string)r.type,
                Debit = (decimal)r.debit,
                Credit = (decimal)r.credit,
                Effect = (decimal)r.effect
            }).ToList();

            return Results.Ok(new
            {
                StocksReport = new
                {
                    StartingBalance = stocksStart,
                    Income = stocksLines.Sum(x => x.Income),
                    Expense = stocksLines.Sum(x => x.Expense),
                    Lines = stocksLines
                },
                FundsReport = new
                {
                    StartingBalance = fundsStart,
                    Income = fundsLines.Sum(x => x.Income),
                    Expense = fundsLines.Sum(x => x.Expense),
                    Lines = fundsLines
                },
                PartnersReport = new
                {
                    StartingBalance = partnersStart,
                    Debit = partnersLines.Sum(x => x.Debit),
                    Credit = partnersLines.Sum(x => x.Credit),
                    Lines = partnersLines
                }
            });
        });

        return app;
    }
}
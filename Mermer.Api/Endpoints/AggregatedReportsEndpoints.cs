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
            DateTime dateTill = DateTime.UtcNow.AddDays(1);

            string? fromStr = req.Query["dateFrom"].FirstOrDefault();
            if (!string.IsNullOrEmpty(fromStr) && DateTime.TryParse(fromStr.Replace(" ", "+"), out var pf))
            {
                dateFrom = pf.Kind == DateTimeKind.Utc ? pf : pf.ToUniversalTime();
            }

            string? tillStr = req.Query["dateTill"].FirstOrDefault();
            if (!string.IsNullOrEmpty(tillStr) && DateTime.TryParse(tillStr.Replace(" ", "+"), out var pt))
            {
                var utcTill = pt.Kind == DateTimeKind.Utc ? pt : pt.ToUniversalTime();
                // Если передана дата без времени (полночь), расширяем фильтр до конца дня
                dateTill = pt.TimeOfDay == TimeSpan.Zero && pt.Kind != DateTimeKind.Utc
                    ? utcTill.AddDays(1)
                    : utcTill;
            }

            var officeIds = req.Query["officeId"]
                .Select(x => Guid.TryParse(x, out var g) ? (Guid?)g : null)
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .ToArray();

            // 1. Подстановка дефолтного офиса, если фильтр не выбран в UI
            if (officeIds.Length == 0)
            {
                var defOffice = await db.Offices.AsNoTracking().FirstOrDefaultAsync(o => !o.IsDisabled, ct);
                if (defOffice != null)
                {
                    officeIds = new[] { defOffice.Id };
                }
            }

            // 2. Дефолтная базовая валюта (USD)
            var defCur = await db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => c.IsDefault && !c.IsDisabled, ct)
                         ?? await db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => !c.IsDisabled, ct);
            var defaultCurrencyId = defCur?.Id ?? Guid.Empty;

            var connStr = db.Database.GetConnectionString();
            await using var conn = new NpgsqlConnection(connStr);

            // 1. Склады и Номенклатура (Stocks)
            const string stocksSql = """
                WITH target_wh AS (
                    SELECT id FROM warehouses 
                    WHERE (cardinality(@offices) = 0 OR office_id = ANY(@offices)) 
                      AND is_disabled = false
                ),
                raw_moves AS (
                    -- Накладные
                    SELECT i.date, i.invoice_type AS type, il.quantity AS qty,
                           (i.invoice_type IN ('Purchase', 'SalesReturn')) AS is_inc
                    FROM invoice_lines il
                    JOIN invoices i ON i.id = il.invoice_id
                    WHERE (cardinality(@offices) = 0 OR i.warehouse_id IN (SELECT id FROM target_wh))
                      AND i.is_completed = true AND i.is_disabled = false

                    UNION ALL

                    -- Ордера
                    SELECT s.date, s.slip_type AS type, sl.quantity AS qty,
                           (s.slip_type IN ('StockOpening', 'RevisionExceed')) AS is_inc
                    FROM stock_slip_lines sl
                    JOIN stock_slips s ON s.id = sl.stock_slip_id
                    WHERE (cardinality(@offices) = 0 OR s.warehouse_id IN (SELECT id FROM target_wh))
                      AND s.is_completed = true

                    UNION ALL

                    -- Перемещения расход
                    SELECT t.date, 'StockTransferSource' AS type, tl.quantity AS qty, false AS is_inc
                    FROM stock_transfer_lines tl
                    JOIN stock_transfers t ON t.id = tl.stock_transfer_id
                    WHERE (cardinality(@offices) = 0 OR t.warehouse_id IN (SELECT id FROM target_wh))
                      AND t.is_completed = true AND t.is_disabled = false

                    UNION ALL

                    -- Перемещения приход
                    SELECT t.date, 'StockTransferDestination' AS type, 
                           COALESCE(NULLIF(tl.received_quantity, 0), tl.quantity) AS qty, true AS is_inc
                    FROM stock_transfer_lines tl
                    JOIN stock_transfers t ON t.id = tl.stock_transfer_id
                    WHERE (cardinality(@offices) = 0 OR t.destination_warehouse_id IN (SELECT id FROM target_wh))
                      AND t.is_completed = true AND t.is_disabled = false
                )
                SELECT 
                    type,
                    SUM(CASE WHEN is_inc THEN qty ELSE 0 END)::numeric(18,4) AS income,
                    SUM(CASE WHEN NOT is_inc THEN qty ELSE 0 END)::numeric(18,4) AS expense,
                    SUM(CASE WHEN is_inc THEN qty ELSE -qty END)::numeric(18,4) AS effect,
                    false AS is_starting
                FROM raw_moves
                WHERE date >= @from AND date < @till
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

            // 2. Кассы и Фонды (Funds)
            const string fundsSql = """
                WITH def_cur AS (
                    SELECT @defCurId::uuid AS id
                ),
                target_dep AS (
                    SELECT id FROM depositories 
                    WHERE (cardinality(@offices) = 0 OR office_id = ANY(@offices)) 
                      AND is_disabled = false
                ),
                raw_funds AS (
                    -- Кассовые ордера
                    SELECT f.date, f.funds_slip_type AS type, fl.amount AS amt,
                           (f.funds_slip_type IN ('Income', 'Collection', 'FundsOpening', 'FundsRevisionExceed')) AS is_inc
                    FROM funds_slip_lines fl
                    JOIN funds_slips f ON f.id = fl.funds_slip_id
                    WHERE (cardinality(@offices) = 0 OR f.depository_id IN (SELECT id FROM target_dep) OR f.office_id = ANY(@offices))
                      AND f.is_completed = true AND f.is_disabled = false

                    UNION ALL

                    -- Акты расхода
                    SELECT e.date, 'ExpenseSlip' AS type, el.amount AS amt, false AS is_inc
                    FROM expense_slip_lines el
                    JOIN expense_slips e ON e.id = el.expense_slip_id
                    WHERE (cardinality(@offices) = 0 OR e.depository_id IN (SELECT id FROM target_dep) OR e.office_id = ANY(@offices))
                      AND e.is_completed = true AND e.is_disabled = false

                    UNION ALL

                    -- Переводы касс расход
                    SELECT t.date, 'FundsTransferSource' AS type, tl.amount AS amt, false AS is_inc
                    FROM funds_transfer_lines tl
                    JOIN funds_transfers t ON t.id = tl.funds_transfer_id
                    WHERE (cardinality(@offices) = 0 OR t.from_depository_id IN (SELECT id FROM target_dep))
                      AND t.is_completed = true AND t.is_disabled = false

                    UNION ALL

                    -- Переводы касс приход
                    SELECT t.date, 'FundsTransferDestination' AS type, 
                           COALESCE(NULLIF(tl.received_amount, 0), tl.amount) AS amt, true AS is_inc
                    FROM funds_transfer_lines tl
                    JOIN funds_transfers t ON t.id = tl.funds_transfer_id
                    WHERE (cardinality(@offices) = 0 OR t.to_depository_id IN (SELECT id FROM target_dep))
                      AND t.is_completed = true AND t.is_disabled = false

                    UNION ALL

                    -- Оплаты счетов продаж и покупок
                    SELECT i.date, i.invoice_type AS type, 
                           (ip.amount * CASE 
                               WHEN i.display_currency_id IS NULL OR i.display_currency_id = (SELECT id FROM def_cur) THEN 1.0
                               ELSE COALESCE(cr.multiplier / NULLIF(cr.divider, 0), 1.0)
                           END) AS amt,
                           (i.invoice_type IN ('Sales', 'PurchaseReturn')) AS is_inc
                    FROM invoice_payments ip
                    JOIN invoices i ON i.id = ip.invoice_id
                    LEFT JOIN LATERAL (
                        SELECT r.multiplier, r.divider 
                        FROM currency_rates r 
                        WHERE r.currency_id = i.display_currency_id AND r.valid_from <= i.date 
                        ORDER BY r.valid_from DESC LIMIT 1
                    ) cr ON true
                    WHERE (cardinality(@offices) = 0 OR i.depository_id IN (SELECT id FROM target_dep) OR i.office_id = ANY(@offices))
                      AND i.is_completed = true AND i.is_disabled = false
                )
                SELECT 
                    type,
                    SUM(CASE WHEN is_inc THEN amt ELSE 0 END)::numeric(18,4) AS income,
                    SUM(CASE WHEN NOT is_inc THEN amt ELSE 0 END)::numeric(18,4) AS expense,
                    SUM(CASE WHEN is_inc THEN amt ELSE -amt END)::numeric(18,4) AS effect,
                    false AS is_starting
                FROM raw_funds
                WHERE date >= @from AND date < @till
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

            // 3. Контрагенты (Partners)
            const string partnersSql = """
                WITH def_cur AS (
                    SELECT @defCurId::uuid AS id
                ),
                raw_partners AS (
                    -- Акты сверки/ордера
                    SELECT ps.date, ps.slip_type AS type, psl.debit_amount AS deb, psl.credit_amount AS cre
                    FROM partner_slip_lines psl
                    JOIN partner_slips ps ON ps.id = psl.partner_slip_id
                    WHERE (cardinality(@offices) = 0 OR ps.office_id = ANY(@offices)) AND ps.is_disabled = false

                    UNION ALL

                    -- Переводы контрагентов
                    SELECT pt.date, 'PartnerTransfer' AS type, ptl.debit_amount AS deb, ptl.credit_amount AS cre
                    FROM partner_transfer_lines ptl
                    JOIN partner_transfers pt ON pt.id = ptl.partner_transfer_id
                    WHERE (cardinality(@offices) = 0 OR ptl.office_id = ANY(@offices)) AND pt.is_disabled = false

                    UNION ALL

                    -- Накладные (Покупки / Продажи с нормализацией валюты)
                    SELECT i.date, i.invoice_type AS type,
                           CASE WHEN i.invoice_type IN ('Sales', 'PurchaseReturn') 
                                THEN (il.quantity * (il.price * CASE 
                                        WHEN i.display_currency_id IS NULL OR i.display_currency_id = (SELECT id FROM def_cur) THEN 1.0
                                        ELSE COALESCE(cr.multiplier / NULLIF(cr.divider, 0), 1.0)
                                      END)) 
                                ELSE 0 
                           END AS deb,
                           CASE WHEN i.invoice_type IN ('Purchase', 'SalesReturn') 
                                THEN (il.quantity * (il.price * CASE 
                                        WHEN i.display_currency_id IS NULL OR i.display_currency_id = (SELECT id FROM def_cur) THEN 1.0
                                        ELSE COALESCE(cr.multiplier / NULLIF(cr.divider, 0), 1.0)
                                      END)) 
                                ELSE 0 
                           END AS cre
                    FROM invoice_lines il
                    JOIN invoices i ON i.id = il.invoice_id
                    LEFT JOIN LATERAL (
                        SELECT r.multiplier, r.divider 
                        FROM currency_rates r 
                        WHERE r.currency_id = i.display_currency_id AND r.valid_from <= i.date 
                        ORDER BY r.valid_from DESC LIMIT 1
                    ) cr ON true
                    WHERE (cardinality(@offices) = 0 OR i.office_id = ANY(@offices)) 
                      AND i.is_completed = true AND i.is_disabled = false AND i.partner_id IS NOT NULL

                    UNION ALL

                    -- Оплаты накладных
                    SELECT i.date, 
                           CASE WHEN i.invoice_type IN ('Purchase', 'SalesReturn') THEN 'Payment' ELSE 'Collection' END AS type,
                           CASE WHEN i.invoice_type IN ('Purchase', 'SalesReturn') 
                                THEN (ip.amount * CASE 
                                        WHEN i.display_currency_id IS NULL OR i.display_currency_id = (SELECT id FROM def_cur) THEN 1.0
                                        ELSE COALESCE(cr.multiplier / NULLIF(cr.divider, 0), 1.0)
                                      END) 
                                ELSE 0 
                           END AS deb,
                           CASE WHEN i.invoice_type IN ('Sales', 'PurchaseReturn') 
                                THEN (ip.amount * CASE 
                                        WHEN i.display_currency_id IS NULL OR i.display_currency_id = (SELECT id FROM def_cur) THEN 1.0
                                        ELSE COALESCE(cr.multiplier / NULLIF(cr.divider, 0), 1.0)
                                      END) 
                                ELSE 0 
                           END AS cre
                    FROM invoice_payments ip
                    JOIN invoices i ON i.id = ip.invoice_id
                    LEFT JOIN LATERAL (
                        SELECT r.multiplier, r.divider 
                        FROM currency_rates r 
                        WHERE r.currency_id = i.display_currency_id AND r.valid_from <= i.date 
                        ORDER BY r.valid_from DESC LIMIT 1
                    ) cr ON true
                    WHERE (cardinality(@offices) = 0 OR i.office_id = ANY(@offices)) 
                      AND i.is_completed = true AND i.is_disabled = false AND i.partner_id IS NOT NULL

                    UNION ALL

                    -- Кассовые ордера с контрагентами
                    SELECT fs.date, 
                           CASE WHEN fs.funds_slip_type IN ('Expense', 'Payment') THEN 'Payment' ELSE 'Collection' END AS type,
                           CASE WHEN fs.funds_slip_type IN ('Expense', 'Payment') 
                                THEN (fsl.amount * CASE 
                                        WHEN fs.display_currency_id IS NULL OR fs.display_currency_id = (SELECT id FROM def_cur) THEN 1.0
                                        ELSE COALESCE(cr.multiplier / NULLIF(cr.divider, 0), 1.0)
                                      END) 
                                ELSE 0 
                           END AS deb,
                           CASE WHEN fs.funds_slip_type IN ('Income', 'Collection') 
                                THEN (fsl.amount * CASE 
                                        WHEN fs.display_currency_id IS NULL OR fs.display_currency_id = (SELECT id FROM def_cur) THEN 1.0
                                        ELSE COALESCE(cr.multiplier / NULLIF(cr.divider, 0), 1.0)
                                      END) 
                                ELSE 0 
                           END AS cre
                    FROM funds_slip_lines fsl
                    JOIN funds_slips fs ON fs.id = fsl.funds_slip_id
                    LEFT JOIN LATERAL (
                        SELECT r.multiplier, r.divider 
                        FROM currency_rates r 
                        WHERE r.currency_id = fs.display_currency_id AND r.valid_from <= fs.date 
                        ORDER BY r.valid_from DESC LIMIT 1
                    ) cr ON true
                    WHERE (cardinality(@offices) = 0 OR fs.office_id = ANY(@offices)) 
                      AND fs.is_completed = true AND fs.is_disabled = false AND fs.partner_id IS NOT NULL
                )
                SELECT 
                    type,
                    SUM(deb)::numeric(18,4) AS debit,
                    SUM(cre)::numeric(18,4) AS credit,
                    SUM(deb - cre)::numeric(18,4) AS effect,
                    false AS is_starting
                FROM raw_partners
                WHERE date >= @from AND date < @till
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

            var p = new { offices = officeIds, from = dateFrom, till = dateTill, defCurId = defaultCurrencyId };

            var stocksRows = (await conn.QueryAsync(new CommandDefinition(stocksSql, p, cancellationToken: ct))).ToList();
            var fundsRows = (await conn.QueryAsync(new CommandDefinition(fundsSql, p, cancellationToken: ct))).ToList();
            var partnersRows = (await conn.QueryAsync(new CommandDefinition(partnersSql, p, cancellationToken: ct))).ToList();

            // =========================================================================
            // СТРОКИ СКЛАДОВ (Zero-Fill)
            // =========================================================================
            var defaultStockTypes = new[]
            {
                "StockOpening",
                "RevisionExceed",
                "RevisionDeficit",
                "StockSpoilage",
                "StockUsage",
                "Purchase",
                "Sales",
                "SalesReturn",
                "PurchaseReturn",
                "StockTransferSource",
                "StockTransferDestination"
            };

            decimal stocksStart = stocksRows.FirstOrDefault(r => (bool)r.is_starting)?.effect ?? 0m;
            var stocksDict = stocksRows.Where(r => !(bool)r.is_starting).ToDictionary(
                r => (string)r.type,
                r => new
                {
                    Type = (string)r.type,
                    Income = (decimal)r.income,
                    Expense = (decimal)r.expense,
                    Effect = (decimal)r.effect
                }, StringComparer.OrdinalIgnoreCase);

            var stocksLines = new List<dynamic>();
            foreach (var t in defaultStockTypes)
            {
                if (stocksDict.TryGetValue(t, out var row))
                {
                    stocksLines.Add(row);
                    stocksDict.Remove(t);
                }
                else
                {
                    stocksLines.Add(new { Type = t, Income = 0m, Expense = 0m, Effect = 0m });
                }
            }
            foreach (var rem in stocksDict.Values) stocksLines.Add(rem);

            // =========================================================================
            // СТРОКИ КАСС (Zero-Fill)
            // =========================================================================
            var defaultFundsTypes = new[]
            {
                "FundsOpening",
                "FundsRevisionExceed",
                "FundsRevisionDeficit",
                "Collection",
                "Payment",
                "ExpenseSlip",
                "FundsTransferSource",
                "FundsTransferDestination",
                "Sales",
                "PurchaseReturn",
                "Purchase",
                "SalesReturn"
            };

            decimal fundsStart = fundsRows.FirstOrDefault(r => (bool)r.is_starting)?.effect ?? 0m;
            var fundsDict = fundsRows.Where(r => !(bool)r.is_starting).ToDictionary(
                r => (string)r.type,
                r => new
                {
                    Type = (string)r.type,
                    Income = (decimal)r.income,
                    Expense = (decimal)r.expense,
                    Effect = (decimal)r.effect
                }, StringComparer.OrdinalIgnoreCase);

            var fundsLines = new List<dynamic>();
            foreach (var t in defaultFundsTypes)
            {
                if (fundsDict.TryGetValue(t, out var row))
                {
                    fundsLines.Add(row);
                    fundsDict.Remove(t);
                }
                else
                {
                    fundsLines.Add(new { Type = t, Income = 0m, Expense = 0m, Effect = 0m });
                }
            }
            foreach (var rem in fundsDict.Values) fundsLines.Add(rem);

            // =========================================================================
            // СТРОКИ КОНТРАГЕНТОВ (Zero-Fill)
            // =========================================================================
            var defaultPartnerTypes = new[]
            {
                "PartnerOpeningBalance",
                "PartnerBalanceRevision",
                "PartnerTransfer",
                "Sales",
                "SalesReturn",
                "Purchase",
                "PurchaseReturn",
                "Payment",
                "Collection"
            };

            decimal partnersStart = partnersRows.FirstOrDefault(r => (bool)r.is_starting)?.effect ?? 0m;
            var partnersDict = partnersRows.Where(r => !(bool)r.is_starting).ToDictionary(
                r => (string)r.type,
                r => new
                {
                    Type = (string)r.type,
                    Debit = (decimal)r.debit,
                    Credit = (decimal)r.credit,
                    Effect = (decimal)r.effect
                }, StringComparer.OrdinalIgnoreCase);

            var partnersLines = new List<dynamic>();
            foreach (var t in defaultPartnerTypes)
            {
                if (partnersDict.TryGetValue(t, out var row))
                {
                    partnersLines.Add(row);
                    partnersDict.Remove(t);
                }
                else
                {
                    partnersLines.Add(new { Type = t, Debit = 0m, Credit = 0m, Effect = 0m });
                }
            }
            foreach (var rem in partnersDict.Values) partnersLines.Add(rem);

            return Results.Ok(new
            {
                StocksReport = new
                {
                    StartingBalance = stocksStart,
                    Income = stocksLines.Sum(x => (decimal)x.Income),
                    Expense = stocksLines.Sum(x => (decimal)x.Expense),
                    Lines = stocksLines
                },
                FundsReport = new
                {
                    StartingBalance = fundsStart,
                    Income = fundsLines.Sum(x => (decimal)x.Income),
                    Expense = fundsLines.Sum(x => (decimal)x.Expense),
                    Lines = fundsLines
                },
                PartnersReport = new
                {
                    StartingBalance = partnersStart,
                    Debit = partnersLines.Sum(x => (decimal)x.Debit),
                    Credit = partnersLines.Sum(x => (decimal)x.Credit),
                    Lines = partnersLines
                }
            });
        });

        return app;
    }
}
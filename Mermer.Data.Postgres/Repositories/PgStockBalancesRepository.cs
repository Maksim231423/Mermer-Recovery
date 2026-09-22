using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Npgsql;
using Mermer.Data.Postgres.Abstractions;
using Mermer.Data.Postgres.Models;

namespace Mermer.Data.Postgres.Repositories;

/// <summary>
/// PostgreSQL implementation of <see cref="IStockBalancesRepository"/>.
/// Replaces a fistful of Couchbase Map/Reduce views with single-query SQL.
/// </summary>
public class PgStockBalancesRepository : IStockBalancesRepository
{
    private readonly string _connectionString;

    public PgStockBalancesRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IReadOnlyList<StockBalance>> GetAsync(
        string stockId, DateTime date, string[]? warehouseIds = null, CancellationToken ct = default)
    {
        if (!Guid.TryParse(stockId, out var stockGuid))
            return Array.Empty<StockBalance>();

        var warehouseGuids = ParseGuids(warehouseIds);

        const string sql = """
            SELECT
                i.warehouse_id,
                il.stock_id,
                COALESCE(SUM(il.quantity) FILTER (WHERE i.invoice_type IN ('Purchase','SalesReturn')), 0)::numeric(18,4) AS income,
                COALESCE(SUM(il.quantity) FILTER (WHERE i.invoice_type IN ('Sales','PurchaseReturn')),  0)::numeric(18,4) AS expense
            FROM invoice_lines il
            JOIN invoices i ON i.id = il.invoice_id
            WHERE il.stock_id   = @stockId
              AND i.is_completed = true
              AND i.is_disabled  = false
              AND i.date::date  <= @date::date
              AND i.warehouse_id IS NOT NULL
              AND (@warehouseIds::uuid[] IS NULL OR i.warehouse_id = ANY(@warehouseIds))
            GROUP BY i.warehouse_id, il.stock_id
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        var rows = await conn.QueryAsync(new CommandDefinition(sql, new
        {
            stockId      = stockGuid,
            warehouseIds = warehouseGuids.Length > 0 ? warehouseGuids : null,
            date
        }, cancellationToken: ct));

        return MapRows(rows).ToList();
    }

    public async Task<IReadOnlyList<StockBalance>> GetAsync(
        string warehouseId, string[] stockIds, CancellationToken ct = default)
    {
        if (!Guid.TryParse(warehouseId, out var warehouseGuid))
            return Array.Empty<StockBalance>();

        var stockGuids = ParseGuids(stockIds);

        const string sql = """
            SELECT
                warehouse_id,
                stock_id,
                income,
                expense
            FROM stock_balances
            WHERE warehouse_id = @warehouseId
              AND (@stockIds::uuid[] IS NULL OR stock_id = ANY(@stockIds))
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        var rows = await conn.QueryAsync(new CommandDefinition(sql, new
        {
            warehouseId = warehouseGuid,
            stockIds    = stockGuids.Length > 0 ? stockGuids : null
        }, cancellationToken: ct));

        return MapRows(rows).ToList();
    }

    public async Task<IReadOnlyList<StockBalance>> GetAsync(
        string[] warehouseIds, string[] stockIds, CancellationToken ct = default)
    {
        var warehouseGuids = ParseGuids(warehouseIds);
        var stockGuids     = ParseGuids(stockIds);

        const string sql = """
            SELECT
                warehouse_id,
                stock_id,
                income,
                expense
            FROM stock_balances
            WHERE (@warehouseIds::uuid[] IS NULL OR warehouse_id = ANY(@warehouseIds))
              AND (@stockIds::uuid[]     IS NULL OR stock_id     = ANY(@stockIds))
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        var rows = await conn.QueryAsync(new CommandDefinition(sql, new
        {
            warehouseIds = warehouseGuids.Length > 0 ? warehouseGuids : null,
            stockIds     = stockGuids.Length > 0 ? stockGuids : null
        }, cancellationToken: ct));

        return MapRows(rows).ToList();
    }

    public async Task<IReadOnlyList<StockBalanceWithCodeAndName>> GetAsync(
        string warehouseId, string[] stockIds, string? excludedTransactionId, CancellationToken ct = default)
    {
        if (!Guid.TryParse(warehouseId, out var warehouseGuid))
            return Array.Empty<StockBalanceWithCodeAndName>();

        Guid? excludedGuid = Guid.TryParse(excludedTransactionId, out var eg) ? eg : null;
        var stockGuids = ParseGuids(stockIds);

        const string sql = """
            SELECT
                sb.warehouse_id,
                sb.stock_id,
                s.code,
                s.name,
                COALESCE((
                    SELECT SUM(il.quantity)
                    FROM invoice_lines il
                    JOIN invoices i ON i.id = il.invoice_id
                    WHERE il.stock_id    = sb.stock_id
                      AND i.warehouse_id = sb.warehouse_id
                      AND i.is_completed = true
                      AND i.invoice_type IN ('Purchase','SalesReturn')
                      AND (@excludedId IS NULL OR i.id != @excludedId)
                ), 0)::numeric(18,4) AS income,
                COALESCE((
                    SELECT SUM(il.quantity)
                    FROM invoice_lines il
                    JOIN invoices i ON i.id = il.invoice_id
                    WHERE il.stock_id    = sb.stock_id
                      AND i.warehouse_id = sb.warehouse_id
                      AND i.is_completed = true
                      AND i.invoice_type IN ('Sales','PurchaseReturn')
                      AND (@excludedId IS NULL OR i.id != @excludedId)
                ), 0)::numeric(18,4) AS expense
            FROM stock_balances sb
            JOIN stocks s ON s.id = sb.stock_id
            WHERE sb.warehouse_id = @warehouseId
              AND (@stockIds::uuid[] IS NULL OR sb.stock_id = ANY(@stockIds))
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        var rows = await conn.QueryAsync(new CommandDefinition(sql, new
        {
            warehouseId = warehouseGuid,
            stockIds    = stockGuids.Length > 0 ? stockGuids : null,
            excludedId  = excludedGuid
        }, cancellationToken: ct));

        return rows.Select(r => new StockBalanceWithCodeAndName
        {
            WarehouseId = ((Guid)r.warehouse_id).ToString(),
            StockId     = ((Guid)r.stock_id).ToString(),
            Code        = (string?)r.code,
            Name        = (string)r.name,
            Income      = (decimal)r.income,
            Expense     = (decimal)r.expense
        }).ToList();
    }

    public async Task<IReadOnlyList<StockBalanceByTypeWithBalanceAndData>> GetByTypeAsync(
        string[] warehouseIds, string stockId,
        DateTime dateFrom, DateTime dateTill, bool aggregate, CancellationToken ct = default)
    {
        if (!Guid.TryParse(stockId, out var stockGuid))
            return Array.Empty<StockBalanceByTypeWithBalanceAndData>();

        var warehouseGuids = ParseGuids(warehouseIds);

        const string sql = """
            SELECT
                i.date::date                                      AS action_date,
                i.invoice_type,
                i.warehouse_id,
                w.name                                            AS warehouse_name,
                SUM(il.quantity)::numeric(18,4)                  AS quantity,
                SUM(il.quantity * il.price)::numeric(18,4)       AS total,
                SUM(SUM(CASE WHEN i.invoice_type IN ('Purchase','SalesReturn')
                              THEN il.quantity ELSE -il.quantity END))
                OVER (
                    PARTITION BY i.warehouse_id
                    ORDER BY i.date::date
                    ROWS UNBOUNDED PRECEDING
                )::numeric(18,4)                                  AS running_balance
            FROM invoice_lines il
            JOIN invoices  i ON i.id = il.invoice_id
            JOIN warehouses w ON w.id = i.warehouse_id
            WHERE il.stock_id     = @stockId
              AND i.is_completed  = true
              AND i.date::date   >= @dateFrom
              AND i.date::date   <= @dateTill
              AND (@warehouseIds::uuid[] IS NULL OR i.warehouse_id = ANY(@warehouseIds))
            GROUP BY i.date::date, i.invoice_type, i.warehouse_id, w.name
            ORDER BY i.date::date
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        var rows = await conn.QueryAsync(new CommandDefinition(sql, new
        {
            stockId      = stockGuid,
            dateFrom,
            dateTill,
            warehouseIds = warehouseGuids.Length > 0 ? warehouseGuids : null
        }, cancellationToken: ct));

        return rows.Select(r => new StockBalanceByTypeWithBalanceAndData
        {
            Date           = (DateTime)r.action_date,
            InvoiceType    = (string)r.invoice_type,
            WarehouseId    = ((Guid)r.warehouse_id).ToString(),
            WarehouseName  = (string)r.warehouse_name,
            Quantity       = (decimal)r.quantity,
            Total          = (decimal)r.total,
            RunningBalance = (decimal)r.running_balance
        }).ToList();
    }

    public async Task<IReadOnlyList<StockBalanceByWarehouses>> GetByDateAndWarehousesAsync(
        DateTime date,
        IEnumerable<string>? warehouseIds,
        string? displayCurrencyId,
        IEnumerable<string>? stockIds = null,
        string? priceGroup = null,
        CancellationToken ct = default)
    {
        var warehouseGuids = ParseGuids(warehouseIds?.ToArray());
        var stockGuids = ParseGuids(stockIds?.ToArray());

        const string sql = """
            WITH default_units AS (
                SELECT stock_id, name
                FROM stock_units
                WHERE is_default = true
            ),
            latest_prices AS (
                SELECT DISTINCT ON (stock_id) 
                    stock_id, price, currency_id
                FROM stock_prices
                WHERE (@priceGroup IS NULL AND price_group IS NULL)
                   OR (@priceGroup IS NOT NULL AND price_group = @priceGroup)
                ORDER BY stock_id, valid_from DESC
            )
            SELECT
                s.id          AS stock_id,
                s.code,
                s.name,
                s.group_name  AS group_name,
                s.type,
                u.name        AS unit,
                JSON_AGG(JSON_BUILD_OBJECT(
                    'warehouseId',   sb.warehouse_id,
                    'warehouseName', w.name,
                    'balance',       sb.income - sb.expense
                ) ORDER BY w.name)  AS warehouse_balances,
                COALESCE(p.price, 0) AS price,
                p.currency_id
            FROM stock_balances sb
            JOIN stocks     s  ON s.id = sb.stock_id
            JOIN warehouses w  ON w.id = sb.warehouse_id
            LEFT JOIN default_units u ON u.stock_id = s.id
            LEFT JOIN latest_prices p ON p.stock_id = s.id
            WHERE NOT s.is_disabled
              AND (@warehouseIds::uuid[] IS NULL OR sb.warehouse_id = ANY(@warehouseIds))
              AND (@stockIds::uuid[]     IS NULL OR sb.stock_id     = ANY(@stockIds))
              AND (sb.income - sb.expense) > 0
            GROUP BY s.id, s.code, s.name, s.group_name, s.type, u.name, p.price, p.currency_id
            ORDER BY s.name;
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        var rows = await conn.QueryAsync(new CommandDefinition(sql, new
        {
            warehouseIds = warehouseGuids.Length > 0 ? warehouseGuids : null,
            stockIds = stockGuids.Length > 0 ? stockGuids : null,
            priceGroup
        }, cancellationToken: ct));

        return rows.Select(r => new StockBalanceByWarehouses
        {
            StockId = ((Guid)r.stock_id).ToString(),
            Code = (string?)r.code,
            Name = (string)r.name,
            Group = (string?)r.group_name,
            Type = (string?)r.type,
            Unit = (string?)r.unit,
            Price = (decimal?)r.price ?? 0m,
            CurrencyId = ((Guid?)r.currency_id)?.ToString(),
            WarehouseBalances = (string?)r.warehouse_balances
        }).ToList();
    }

    private static Guid[] ParseGuids(string[]? ids) =>
        ids?.Select(id => Guid.TryParse(id, out var g) ? g : Guid.Empty)
            .Where(g => g != Guid.Empty)
            .ToArray() ?? Array.Empty<Guid>();

    private static IEnumerable<StockBalance> MapRows(IEnumerable<dynamic> rows) =>
        rows.Select(r => new StockBalance
        {
            WarehouseId = ((Guid)r.warehouse_id).ToString(),
            StockId     = ((Guid)r.stock_id).ToString(),
            Income      = (decimal)r.income,
            Expense     = (decimal)r.expense
        });
}

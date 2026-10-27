using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Npgsql;
using Mermer.Data.Postgres.Abstractions;

namespace Mermer.Data.Postgres.Repositories;

/// <summary>
/// PostgreSQL implementation of stock search using pg_trgm extension.
/// 
/// Performance: Single SQL query replaces 3 sequential Couchbase round-trips:
///   1. FTS query → IDs
///   2. N1QL JOIN Stock+Currency → data
///   3. GetAsync balancesRepository → balances
/// 
/// All combined into one query with JOINs, targeting < 100ms response.
/// Uses Dapper for raw SQL performance (no EF Core overhead for search).
/// </summary>
public class PgStockSearchService : IStockSearchService
{
    private readonly string _connectionString;

    public PgStockSearchService(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public async Task<IReadOnlyList<PgStockSearchResult>> SearchAsync(
        string searchText,
        string? warehouseId = null,
        string? priceGroup = null,
        int limit = 32,
        double minSimilarity = 0.1,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(searchText))
            return Array.Empty<PgStockSearchResult>();

        var normalizedSearch = searchText.Trim().ToLowerInvariant();

        // Build optimized query:
        // 1. Try exact match on code/barcode first (highest priority)
        // 2. Trigram similarity on name, code, short_name
        // 3. LEFT JOIN prices, units, balances — single round-trip
        var sql = BuildSearchQuery(warehouseId, priceGroup);

        var parameters = new DynamicParameters();
        parameters.Add("@search", normalizedSearch);
        parameters.Add("@searchPattern", $"%{normalizedSearch}%");
        parameters.Add("@limit", limit);
        parameters.Add("@minSimilarity", minSimilarity);

        if (!string.IsNullOrEmpty(warehouseId))
            parameters.Add("@warehouseId", Guid.Parse(warehouseId));

        if (!string.IsNullOrEmpty(priceGroup))
            parameters.Add("@priceGroup", priceGroup);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // Wrap in a transaction so SET LOCAL is scoped to this query only.
        // Plain `SET pg_trgm.similarity_threshold = ...` would leak across the
        // pooled connection's lifetime and corrupt subsequent searches.
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);

        await connection.ExecuteAsync(
            new CommandDefinition(
                $"SET LOCAL pg_trgm.similarity_threshold = {minSimilarity.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                transaction: tx,
                cancellationToken: cancellationToken));

        var results = await connection.QueryAsync<PgStockSearchResult>(
            new CommandDefinition(sql, parameters, transaction: tx, cancellationToken: cancellationToken));

        await tx.CommitAsync(cancellationToken);

        // Apply highlight formatting to match old behavior
        var resultList = results.ToList();
        var searchTerms = normalizedSearch.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        foreach (var item in resultList)
        {
            item.CodeHtml = FormatHighlight(item.Code, searchTerms);
            item.NameHtml = FormatHighlight(item.Name, searchTerms);
        }

        return resultList;
    }

    /// <summary>
    /// Builds the optimized SQL query that combines stock data, price, unit, balance,
    /// and trigram similarity scoring in a single round-trip.
    /// </summary>
    private static string BuildSearchQuery(string? warehouseId, string? priceGroup)
    {
        var sb = new StringBuilder(2048);

        sb.AppendLine(@"
WITH scored_stocks AS (
    SELECT
        s.id,
        s.code,
        s.name,
        s.short_name,
        s.barcodes,
        s.is_disabled,
        GREATEST(
            COALESCE(similarity(LOWER(s.name), @search), 0),
            COALESCE(similarity(LOWER(COALESCE(s.code, '')), @search), 0) * 1.5,
            COALESCE(similarity(LOWER(COALESCE(s.short_name, '')), @search), 0),
            CASE WHEN LOWER(COALESCE(s.code, '')) = @search THEN 10.0 ELSE 0 END,
            CASE WHEN @search = ANY(SELECT LOWER(unnest(s.barcodes))) THEN 10.0 ELSE 0 END
        ) AS match_score
    FROM stocks s
    WHERE NOT s.is_disabled
      AND (
          -- Exact code match
          LOWER(COALESCE(s.code, '')) = @search
          -- Exact barcode match
          OR @search = ANY(SELECT LOWER(unnest(s.barcodes)))
          -- Trigram similarity on name
          OR LOWER(s.name) % @search
          -- Trigram similarity on code
          OR LOWER(COALESCE(s.code, '')) % @search
          -- Trigram similarity on short_name
          OR LOWER(COALESCE(s.short_name, '')) % @search
          -- ILIKE fallback for substring matches
          OR s.name ILIKE @searchPattern
          OR COALESCE(s.code, '') ILIKE @searchPattern
          -- Full-text search fallback
          OR s.search_vector @@ plainto_tsquery('simple', @search)
      )
)
SELECT
    ss.id::text    AS ""Id"",
    ss.code        AS ""Code"",
    ss.name        AS ""Name"",
    ss.short_name  AS ""ShortName"",
    ss.is_disabled AS ""IsDisabled"",
    ss.match_score AS ""Similarity"",

    -- Current price (latest valid price entry)
    COALESCE(sp.price, 0)       AS ""Price"",
    COALESCE(c.name, (SELECT name FROM currencies WHERE is_default = true AND is_disabled = false LIMIT 1)) AS ""Currency"",
    COALESCE(sp.currency_id::text, (SELECT id::text FROM currencies WHERE is_default = true AND is_disabled = false LIMIT 1)) AS ""CurrencyId"",

    -- Default unit
    su.id::text     AS ""UnitId"",
    su.name         AS ""Unit"",

    -- Stock balance");

        if (!string.IsNullOrEmpty(warehouseId))
        {
            sb.AppendLine(@"
    COALESCE(sb.income - sb.expense, 0) AS ""Balance""");
        }
        else
        {
            sb.AppendLine(@"
    COALESCE(sba.total_balance, 0) AS ""Balance""");
        }

        sb.AppendLine(@"
FROM scored_stocks ss

-- Latest price
LEFT JOIN LATERAL (
    SELECT sp2.price, sp2.currency_id
    FROM stock_prices sp2
    WHERE sp2.stock_id = ss.id");

        if (!string.IsNullOrEmpty(priceGroup))
            sb.AppendLine("      AND sp2.price_group = @priceGroup");
        else
            sb.AppendLine("      AND sp2.price_group IS NULL");

        sb.AppendLine(@"
    ORDER BY sp2.valid_from DESC
    LIMIT 1
) sp ON TRUE

-- Currency name
LEFT JOIN currencies c ON c.id = sp.currency_id

-- Default unit
LEFT JOIN stock_units su ON su.stock_id = ss.id AND su.is_default = TRUE");

        // Balance join
        if (!string.IsNullOrEmpty(warehouseId))
        {
            sb.AppendLine(@"
-- Balance for specific warehouse
LEFT JOIN stock_balances sb ON sb.stock_id = ss.id AND sb.warehouse_id = @warehouseId");
        }
        else
        {
            sb.AppendLine(@"
-- Aggregated balance across all warehouses
LEFT JOIN LATERAL (
    SELECT COALESCE(SUM(sb2.income - sb2.expense), 0) AS total_balance
    FROM stock_balances sb2
    WHERE sb2.stock_id = ss.id
) sba ON TRUE");
        }

        sb.AppendLine(@"
ORDER BY ss.match_score DESC, ss.name
LIMIT @limit;");

        return sb.ToString();
    }

    /// <summary>
    /// Highlights matching terms in text using HTML bold tags.
    /// Replaces the O(n*m) string.Replace loop from StockSearchServiceOld
    /// with a more efficient approach.
    /// </summary>
    private static string? FormatHighlight(string? text, string[] searchTerms)
    {
        if (string.IsNullOrEmpty(text) || searchTerms.Length == 0)
            return text;

        var result = text;
        foreach (var term in searchTerms.Where(t => t.Length > 0))
        {
            var idx = result.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            while (idx >= 0)
            {
                var original = result.Substring(idx, term.Length);
                var replacement = $"<b style=\"background-color:yellow;color:black\">{original}</b>";
                result = result.Substring(0, idx) + replacement + result.Substring(idx + term.Length);
                idx = result.IndexOf(term, idx + replacement.Length, StringComparison.OrdinalIgnoreCase);
            }
        }

        return result;
    }
}

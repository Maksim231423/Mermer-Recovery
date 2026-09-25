using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Npgsql;

namespace Mermer.Api.Endpoints;

public static class FacetsHelper
{
    private static readonly string[] CandidateTables = new[]
    {
        "funds_slips",
        "funds_transfers",
        "partner_slips",
        "partner_transfers",
        "invoices",
        "stock_slips",
        "partners",
        "daily_funds_registeries"
    };

    public static async Task<Dictionary<string, Dictionary<string, int>>> GetEntityFacetsAsync(
        string connectionString,
        string primaryTableName,
        string? fields,
        CancellationToken ct = default)
    {
        var result = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);

        var requested = (fields ?? "GroupNames,TagNames")
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(f => f.Trim())
            .ToArray();

        foreach (var req in requested)
        {
            result[req] = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        // Получаем реальную схему всех существующих колонок
        const string schemaSql = @"
            SELECT table_name, column_name 
            FROM information_schema.columns 
            WHERE table_schema = 'public';";

        var allColumns = (await conn.QueryAsync<(string Table, string Column)>(schemaSql)).ToList();

        bool wantsGroups = requested.Any(f => f.Equals("GroupNames", StringComparison.OrdinalIgnoreCase) || f.Equals("Group", StringComparison.OrdinalIgnoreCase));
        bool wantsTags = requested.Any(f => f.Equals("TagNames", StringComparison.OrdinalIgnoreCase) || f.Equals("Tags", StringComparison.OrdinalIgnoreCase));

        // 1. СКВОЗНОЙ СБОР ГРУПП
        if (wantsGroups)
        {
            var queries = new List<string>();

            foreach (var tbl in CandidateTables)
            {
                var tableCols = allColumns.Where(c => c.Table.Equals(tbl, StringComparison.OrdinalIgnoreCase)).Select(c => c.Column).ToList();
                if (!tableCols.Any()) continue;

                // Определяем колонку группы: group, group_name или Group
                var grpCol = tableCols.FirstOrDefault(c => c.Equals("group", StringComparison.OrdinalIgnoreCase)
                                                        || c.Equals("group_name", StringComparison.OrdinalIgnoreCase));
                if (grpCol == null) continue;

                // Проверяем наличие колонки отключения
                var disCol = tableCols.FirstOrDefault(c => c.Equals("is_disabled", StringComparison.OrdinalIgnoreCase));
                var disClause = disCol != null ? $"AND \"{disCol}\" = false" : "";

                queries.Add($@"
                    SELECT TRIM(""{grpCol}"") AS ""Key"" 
                    FROM {tbl} 
                    WHERE ""{grpCol}"" IS NOT NULL 
                      AND TRIM(""{grpCol}"") <> '' 
                      {disClause}");
            }

            if (queries.Any())
            {
                var sqlUnionGroups = $@"
                    WITH all_groups AS (
                        {string.Join("\nUNION ALL\n", queries)}
                    )
                    SELECT ""Key"", COUNT(*)::int AS ""Count""
                    FROM all_groups
                    GROUP BY ""Key""
                    ORDER BY 1;";

                try
                {
                    var rows = (await conn.QueryAsync<(string Key, int Count)>(sqlUnionGroups)).ToList();
                    var dict = rows.ToDictionary(x => x.Key, x => x.Count, StringComparer.OrdinalIgnoreCase);

                    result["GroupNames"] = dict;
                    result["Group"] = dict;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GLOBAL FACETS ERROR Groups]: {ex.Message}");
                }
            }
        }

        // 2. СКВОЗНОЙ СБОР ТЕГОВ
        if (wantsTags)
        {
            var queries = new List<string>();

            foreach (var tbl in CandidateTables)
            {
                var tableCols = allColumns.Where(c => c.Table.Equals(tbl, StringComparison.OrdinalIgnoreCase)).Select(c => c.Column).ToList();
                if (!tableCols.Any()) continue;

                // Ищем колонку tags
                var tagCol = tableCols.FirstOrDefault(c => c.Equals("tags", StringComparison.OrdinalIgnoreCase));
                if (tagCol == null) continue;

                var disCol = tableCols.FirstOrDefault(c => c.Equals("is_disabled", StringComparison.OrdinalIgnoreCase));
                var disClause = disCol != null ? $"AND \"{disCol}\" = false" : "";

                queries.Add($@"
                    SELECT TRIM(t) AS ""Key"" 
                    FROM {tbl}, unnest(""{tagCol}"") AS t 
                    WHERE ""{tagCol}"" IS NOT NULL 
                      AND TRIM(t) <> '' 
                      {disClause}");
            }

            if (queries.Any())
            {
                var sqlUnionTags = $@"
                    WITH all_tags AS (
                        {string.Join("\nUNION ALL\n", queries)}
                    )
                    SELECT ""Key"", COUNT(*)::int AS ""Count""
                    FROM all_tags
                    GROUP BY ""Key""
                    ORDER BY 1;";

                try
                {
                    var rows = (await conn.QueryAsync<(string Key, int Count)>(sqlUnionTags)).ToList();
                    var dict = rows.ToDictionary(x => x.Key, x => x.Count, StringComparer.OrdinalIgnoreCase);

                    result["TagNames"] = dict;
                    result["Tags"] = dict;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GLOBAL FACETS ERROR Tags]: {ex.Message}");
                }
            }
        }

        return result;
    }
}
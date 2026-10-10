using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Mermer.Data.Postgres;
using Mermer.Data.Postgres.Abstractions;
using Mermer.Data.Postgres.Entities;

namespace Mermer.Api.Endpoints;

public static class StocksEndpoints
{
    public static IEndpointRouteBuilder MapStocksEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/stocks").WithTags("Stocks");
        // ВАЖНО: сохраняем точный PascalCase свойств (Code, Price, CurrencyId, etc.)
        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = null };

        // 1. СПИСОК ТОВАРОВ
        group.MapGet("/", async (int? limit, int? offset, string? search, bool? includeDisabled, MermerDbContext db, CancellationToken ct) =>
        {
            int take = limit.HasValue ? Math.Clamp(limit.Value, 1, 50000) : 50000;
            int skip = offset.GetValueOrDefault(0);

            var query = db.Stocks.AsNoTracking();

            if (includeDisabled.HasValue && !includeDisabled.Value)
            {
                query = query.Where(s => !s.IsDisabled);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                string term = search.Trim();
                query = query.Where(s => EF.Functions.ILike(s.Name, $"%{term}%") ||
                                         (s.Code != null && EF.Functions.ILike(s.Code, $"%{term}%")));
            }

            // Получаем дефолтную валюту (USD), если у товара нет явного CurrencyId
            var defaultCurrency = await db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => c.IsDefault && !c.IsDisabled, ct);
            string? defaultCurrencyId = defaultCurrency?.Id.ToString();

            var list = await query
                .OrderBy(s => s.Name)
                .Skip(skip)
                .Take(take)
                .Include(s => s.Prices)
                .Include(s => s.Units)
                .AsSplitQuery()
                .ToListAsync(ct);

            var result = list.Select(s =>
            {
                var currentPrice = s.Prices?
                    .OrderByDescending(p => p.ValidFrom)
                    .FirstOrDefault();

                var defaultUnit = s.Units?.FirstOrDefault(u => u.IsDefault) ?? s.Units?.FirstOrDefault();

                string? currencyId = currentPrice?.CurrencyId?.ToString() ?? defaultCurrencyId;

                return new
                {
                    Id = s.Id.ToString(),
                    Code = s.Code ?? string.Empty,
                    Name = s.Name ?? string.Empty,
                    ShortName = s.ShortName ?? string.Empty,
                    IsDisabled = s.IsDisabled,
                    Type = s.Type ?? string.Empty,
                    Group = s.Group ?? string.Empty,
                    Barcodes = s.Barcodes != null ? s.Barcodes.ToList() : new List<string>(),
                    Tags = s.Tags != null ? s.Tags.ToList() : new List<string>(),
                    Price = currentPrice?.Price ?? 0m,
                    CurrencyId = currencyId,
                    Unit = defaultUnit?.Name ?? string.Empty,
                    UnitId = defaultUnit?.Id.ToString(),
                    Prices = s.Prices != null && s.Prices.Any()
                        ? s.Prices.Select(p => (object)new
                        {
                            Id = p.Id.ToString(),
                            Price = p.Price,
                            CurrencyId = p.CurrencyId?.ToString() ?? defaultCurrencyId,
                            PriceGroup = p.PriceGroup,
                            ValidFrom = p.ValidFrom
                        }).ToList()
                        : new List<object>(),
                    Units = s.Units != null && s.Units.Any()
                        ? s.Units.Select(u => (object)new
                        {
                            Id = u.Id.ToString(),
                            Name = u.Name,
                            Multiplier = u.Multiplier,
                            Divider = u.Divider,
                            IsDefault = u.IsDefault
                        }).ToList()
                        : new List<object>()
                };
            });

            return Results.Json(result, jsonOptions);
        })
        .WithName("StocksList");

        // 2. БЫСТРЫЙ ПОИСК ТОВАРОВ
        group.MapGet("/search", async (string q, string? warehouseId, string? priceGroup, int? limit, double? minSimilarity, IStockSearchService search, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(q)) return Results.Json(Array.Empty<object>(), jsonOptions);

            int maxResults = limit.HasValue ? Math.Clamp(limit.Value, 1, 100) : 30;
            var result = await search.SearchAsync(q.Trim(), warehouseId, priceGroup, maxResults, minSimilarity ?? 0.1, ct);
            return Results.Json(result, jsonOptions);
        })
        .WithName("StocksSearch");

        // 3. СЛЕДУЮЩИЙ КОД ТОВАРА
        group.MapGet("/next-code", async (MermerDbContext db) =>
        {
            var count = await db.Stocks.CountAsync();
            return Results.Ok(new { code = $"ST-{(count + 1):D6}" });
        })
        .WithName("StocksGetNextCode");

        // 4. ПОЛУЧЕНИЕ ПО ID
        group.MapGet("/{id}", async (string id, MermerDbContext db, CancellationToken ct) =>
        {
            if (!Guid.TryParse(id, out var stockGuid)) return Results.NotFound();

            var s = await db.Stocks
                .Include(x => x.Prices)
                .Include(x => x.Units)
                .AsSplitQuery()
                .FirstOrDefaultAsync(x => x.Id == stockGuid, ct);

            if (s == null) return Results.NotFound();

            var currentPrice = s.Prices?.OrderByDescending(p => p.ValidFrom).FirstOrDefault();
            var defaultUnit = s.Units?.FirstOrDefault(u => u.IsDefault) ?? s.Units?.FirstOrDefault();

            return Results.Json(new
            {
                Id = s.Id.ToString(),
                Code = s.Code ?? string.Empty,
                Name = s.Name ?? string.Empty,
                ShortName = s.ShortName ?? string.Empty,
                Type = s.Type ?? string.Empty,
                Group = s.Group ?? string.Empty,
                Description = s.Description ?? string.Empty,
                Barcodes = s.Barcodes != null ? s.Barcodes.ToList() : new List<string>(),
                Tags = s.Tags != null ? s.Tags.ToList() : new List<string>(),
                Price = currentPrice?.Price ?? 0m,
                CurrencyId = currentPrice?.CurrencyId?.ToString(),
                Unit = defaultUnit?.Name ?? string.Empty,
                UnitId = defaultUnit?.Id.ToString(),
                IsDisabled = s.IsDisabled,
                Prices = s.Prices?.Select(p => new
                {
                    Id = p.Id.ToString(),
                    Price = p.Price,
                    CurrencyId = p.CurrencyId?.ToString(),
                    PriceGroup = p.PriceGroup,
                    ValidFrom = p.ValidFrom
                }),
                Units = s.Units?.Select(u => new
                {
                    Id = u.Id.ToString(),
                    Name = u.Name,
                    Multiplier = u.Multiplier,
                    Divider = u.Divider,
                    IsDefault = u.IsDefault
                })
            }, jsonOptions);
        })
        .WithName("StocksGetById");

        // 5. ФАСЕТЫ
        group.MapGet("/facets", async (HttpContext context, MermerDbContext db, CancellationToken ct) =>
        {
            var result = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);

            var priceGroups = await db.StockPrices
                .AsNoTracking()
                .Where(x => !string.IsNullOrEmpty(x.PriceGroup))
                .GroupBy(x => x.PriceGroup!)
                .Select(g => new { Key = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

            result["PriceGroupNames"] = priceGroups;

            var groups = await db.Stocks
                .AsNoTracking()
                .Where(x => !string.IsNullOrEmpty(x.Group) && !x.IsDisabled)
                .GroupBy(x => x.Group!)
                .Select(g => new { Key = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

            result["Group"] = groups;
            result["GroupNames"] = groups;

            var allTags = await db.Stocks
                .AsNoTracking()
                .Where(x => x.Tags != null && x.Tags.Length > 0 && !x.IsDisabled)
                .Select(x => x.Tags)
                .ToListAsync(ct);

            var tagCounts = allTags
                .SelectMany(t => t!)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .GroupBy(t => t.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count());

            result["Tags"] = tagCounts;
            result["TagNames"] = tagCounts;

            var typeCounts = await db.Stocks
                .AsNoTracking()
                .Where(x => !string.IsNullOrEmpty(x.Type) && !x.IsDisabled)
                .GroupBy(x => x.Type!)
                .Select(g => new { Key = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

            result["TypeNames"] = typeCounts;
            result["Types"] = typeCounts;

            var unitCounts = await db.StockUnits
                .AsNoTracking()
                .Where(u => !string.IsNullOrEmpty(u.Name))
                .GroupBy(u => u.Name)
                .Select(g => new { Key = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

            var defaultCommonUnits = new[] { "шт", "кг", "г", "м", "см", "л", "мл", "уп", "пачка", "ящик", "рулон", "компл" };
            foreach (var du in defaultCommonUnits)
            {
                if (!unitCounts.ContainsKey(du))
                    unitCounts[du] = 1;
            }

            result["UnitNames"] = unitCounts;
            result["Units"] = unitCounts;
            result["Unit"] = unitCounts;

            return Results.Ok(result);
        })
        .WithName("StocksFacets");

        // 6. ЖУРНАЛ ДВИЖЕНИЯ ТОВАРОВ (ОПТИМИЗИРОВАННЫЙ БЫСТРЫЙ ЗАПРОС)
        group.MapGet("/actions/count", async (DateTime? from, DateTime? till, string? stockId, HttpRequest req, MermerDbContext db, CancellationToken ct) =>
        {
            DateTimeOffset startDate = from.HasValue && from.Value.Year > 2000
                ? new DateTimeOffset(from.Value.ToUniversalTime())
                : DateTimeOffset.MinValue;

            DateTimeOffset endDate = till.HasValue && till.Value.Year < 2099
                ? (till.Value.TimeOfDay == TimeSpan.Zero
                    ? new DateTimeOffset(till.Value.Date.AddDays(1).AddTicks(-1).ToUniversalTime())
                    : new DateTimeOffset(till.Value.ToUniversalTime()))
                : DateTimeOffset.MaxValue;

            var whIds = req.Query["warehouseId"]
                .Select(x => Guid.TryParse(x, out var g) ? (Guid?)g : null)
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .ToList();

            Guid? filterStockGuid = Guid.TryParse(stockId, out var sG) ? sG : null;

            var slipsQuery = db.StockSlipLines.AsNoTracking()
                .Where(l => l.StockSlip != null && l.StockSlip.Date >= startDate && l.StockSlip.Date <= endDate);
            if (filterStockGuid.HasValue) slipsQuery = slipsQuery.Where(l => l.StockId == filterStockGuid);
            if (whIds.Any()) slipsQuery = slipsQuery.Where(l => l.StockSlip!.WarehouseId.HasValue && whIds.Contains(l.StockSlip.WarehouseId.Value));
            int countSlips = await slipsQuery.CountAsync(ct);

            var invQuery = db.InvoiceLines.AsNoTracking()
                .Where(l => l.Invoice != null && !l.Invoice.IsDisabled && l.Invoice.Date >= startDate && l.Invoice.Date <= endDate);
            if (filterStockGuid.HasValue) invQuery = invQuery.Where(l => l.StockId == filterStockGuid);
            if (whIds.Any()) invQuery = invQuery.Where(l => l.Invoice!.WarehouseId.HasValue && whIds.Contains(l.Invoice.WarehouseId.Value));
            int countInvoices = await invQuery.CountAsync(ct);

            var trBaseQuery = db.StockTransferLines.AsNoTracking()
                .Where(l => l.StockTransfer != null && !l.StockTransfer.IsDisabled && l.StockTransfer.Date >= startDate && l.StockTransfer.Date <= endDate);
            if (filterStockGuid.HasValue) trBaseQuery = trBaseQuery.Where(l => l.StockId == filterStockGuid);

            int countTransfers = 0;
            if (!whIds.Any())
            {
                countTransfers = (await trBaseQuery.CountAsync(ct)) * 2;
            }
            else
            {
                int sourceCount = await trBaseQuery.Where(l => l.StockTransfer!.WarehouseId.HasValue && whIds.Contains(l.StockTransfer.WarehouseId.Value)).CountAsync(ct);
                int destCount = await trBaseQuery.Where(l => l.StockTransfer!.DestinationWarehouseId.HasValue && whIds.Contains(l.StockTransfer.DestinationWarehouseId.Value)).CountAsync(ct);
                countTransfers = sourceCount + destCount;
            }

            return Results.Ok(new { count = countSlips + countTransfers + countInvoices });
        })
        .WithName("StockActionsCount");

        group.MapGet("/actions", async (DateTime? from, DateTime? till, string? stockId, int? limit, int? offset, HttpRequest req, MermerDbContext db, CancellationToken ct) =>
        {
            int take = limit.HasValue ? Math.Clamp(limit.Value, 1, 5000) : 500;
            int skip = offset.GetValueOrDefault(0);

            DateTimeOffset startDate = from.HasValue && from.Value.Year > 2000
                ? new DateTimeOffset(from.Value.ToUniversalTime())
                : DateTimeOffset.MinValue;

            DateTimeOffset endDate = till.HasValue && till.Value.Year < 2099
                ? (till.Value.TimeOfDay == TimeSpan.Zero
                    ? new DateTimeOffset(till.Value.Date.AddDays(1).AddTicks(-1).ToUniversalTime())
                    : new DateTimeOffset(till.Value.ToUniversalTime()))
                : DateTimeOffset.MaxValue;

            var whIds = req.Query["warehouseId"]
                .Select(x => Guid.TryParse(x, out var g) ? (Guid?)g : null)
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .ToArray();

            Guid? filterStockGuid = Guid.TryParse(stockId, out var sG) ? sG : null;

            const string sql = """
                WITH raw_actions AS (
                    -- 1. Складские ордера
                    SELECT 
                        s.id AS "TransactionId",
                        s.code AS "TransactionCode",
                        s.date AS "TransactionDate",
                        COALESCE(s.slip_type, 'StockSlip') AS "TransactionType",
                        COALESCE(s.user_id::text, '00000000-0000-0000-0000-000000000000') AS "TransactionUserId",
                        'admin' AS "TransactionUserName",
                        'admin' AS "Author",
                        'admin' AS "UserName",
                        true AS "TransactionIsCompleted",
                        false AS "TransactionIsDisabled",
                        false AS "TransactionIsCash",
                        COALESCE(s.tags, ARRAY[]::text[]) AS "TransactionTags",
                        COALESCE(s.group_name, '') AS "TransactionGroup",
                        l.id::text AS "ActionId",
                        s.warehouse_id::text AS "ActionWarehouseId",
                        '' AS "ActionRelatedObjectName",
                        l.stock_id::text AS "ActionStockId",
                        l.stock_id::text AS "StockId",
                        COALESCE(stk.code, '') AS "StockCode",
                        COALESCE(stk.name, '') AS "StockName",
                        COALESCE(stk.type, '') AS "StockType",
                        COALESCE(stk.group_name, '') AS "StockGroup",
                        COALESCE(stk.tags, ARRAY[]::text[]) AS "StockTags",
                        l.price AS "RecommendedPrice",
                        l.price AS "ActionPrice",
                        CASE WHEN s.slip_type IN ('StockOpening', 'RevisionExceed') THEN l.quantity ELSE 0 END AS "ActionIncome",
                        CASE WHEN s.slip_type NOT IN ('StockOpening', 'RevisionExceed') THEN l.quantity ELSE 0 END AS "ActionExpense",
                        CASE WHEN s.slip_type IN ('StockOpening', 'RevisionExceed') THEN l.quantity ELSE -l.quantity END AS "ActionEffect",
                        (l.quantity * l.price) AS "GrandTotal",
                        (l.quantity * l.price) AS "RecommendedTotal",
                        (l.quantity * l.price) AS "GrandTotalInCustomCurrency"
                    FROM stock_slip_lines l
                    JOIN stock_slips s ON s.id = l.stock_slip_id
                    LEFT JOIN stocks stk ON stk.id = l.stock_id
                    WHERE s.date >= @start AND s.date <= @end
                      AND (@stockGuid IS NULL OR l.stock_id = @stockGuid)
                      AND (cardinality(@whs) = 0 OR s.warehouse_id = ANY(@whs))

                    UNION ALL

                    -- 2. Перемещения: Источник
                    SELECT 
                        t.id AS "TransactionId",
                        t.code AS "TransactionCode",
                        t.date AS "TransactionDate",
                        'StockTransferSource' AS "TransactionType",
                        '00000000-0000-0000-0000-000000000000' AS "TransactionUserId",
                        COALESCE(t.user_name, 'admin') AS "TransactionUserName",
                        COALESCE(t.user_name, 'admin') AS "Author",
                        COALESCE(t.user_name, 'admin') AS "UserName",
                        true AS "TransactionIsCompleted",
                        false AS "TransactionIsDisabled",
                        false AS "TransactionIsCash",
                        COALESCE(t.tags, ARRAY[]::text[]) AS "TransactionTags",
                        COALESCE(t.group_name, '') AS "TransactionGroup",
                        l.id::text AS "ActionId",
                        t.warehouse_id::text AS "ActionWarehouseId",
                        COALESCE(t.destination_warehouse_id::text, '') AS "ActionRelatedObjectName",
                        l.stock_id::text AS "ActionStockId",
                        l.stock_id::text AS "StockId",
                        COALESCE(stk.code, '') AS "StockCode",
                        COALESCE(stk.name, '') AS "StockName",
                        COALESCE(stk.type, '') AS "StockType",
                        COALESCE(stk.group_name, '') AS "StockGroup",
                        COALESCE(stk.tags, ARRAY[]::text[]) AS "StockTags",
                        l.price AS "RecommendedPrice",
                        l.price AS "ActionPrice",
                        0 AS "ActionIncome",
                        l.quantity AS "ActionExpense",
                        -l.quantity AS "ActionEffect",
                        (l.quantity * l.price) AS "GrandTotal",
                        (l.quantity * l.price) AS "RecommendedTotal",
                        (l.quantity * l.price) AS "GrandTotalInCustomCurrency"
                    FROM stock_transfer_lines l
                    JOIN stock_transfers t ON t.id = l.stock_transfer_id
                    LEFT JOIN stocks stk ON stk.id = l.stock_id
                    WHERE t.is_disabled = false
                      AND t.date >= @start AND t.date <= @end
                      AND (@stockGuid IS NULL OR l.stock_id = @stockGuid)
                      AND (cardinality(@whs) = 0 OR t.warehouse_id = ANY(@whs))

                    UNION ALL

                    -- 3. Перемещения: Получатель
                    SELECT 
                        t.id AS "TransactionId",
                        t.code AS "TransactionCode",
                        t.date AS "TransactionDate",
                        'StockTransferDestination' AS "TransactionType",
                        '00000000-0000-0000-0000-000000000000' AS "TransactionUserId",
                        COALESCE(t.user_name, 'admin') AS "TransactionUserName",
                        COALESCE(t.user_name, 'admin') AS "Author",
                        COALESCE(t.user_name, 'admin') AS "UserName",
                        true AS "TransactionIsCompleted",
                        false AS "TransactionIsDisabled",
                        false AS "TransactionIsCash",
                        COALESCE(t.tags, ARRAY[]::text[]) AS "TransactionTags",
                        COALESCE(t.group_name, '') AS "TransactionGroup",
                        l.id::text AS "ActionId",
                        t.destination_warehouse_id::text AS "ActionWarehouseId",
                        COALESCE(t.warehouse_id::text, '') AS "ActionRelatedObjectName",
                        l.stock_id::text AS "ActionStockId",
                        l.stock_id::text AS "StockId",
                        COALESCE(stk.code, '') AS "StockCode",
                        COALESCE(stk.name, '') AS "StockName",
                        COALESCE(stk.type, '') AS "StockType",
                        COALESCE(stk.group_name, '') AS "StockGroup",
                        COALESCE(stk.tags, ARRAY[]::text[]) AS "StockTags",
                        l.price AS "RecommendedPrice",
                        l.price AS "ActionPrice",
                        COALESCE(NULLIF(l.received_quantity, 0), l.quantity) AS "ActionIncome",
                        0 AS "ActionExpense",
                        COALESCE(NULLIF(l.received_quantity, 0), l.quantity) AS "ActionEffect",
                        (COALESCE(NULLIF(l.received_quantity, 0), l.quantity) * l.price) AS "GrandTotal",
                        (COALESCE(NULLIF(l.received_quantity, 0), l.quantity) * l.price) AS "RecommendedTotal",
                        (COALESCE(NULLIF(l.received_quantity, 0), l.quantity) * l.price) AS "GrandTotalInCustomCurrency"
                    FROM stock_transfer_lines l
                    JOIN stock_transfers t ON t.id = l.stock_transfer_id
                    LEFT JOIN stocks stk ON stk.id = l.stock_id
                    WHERE t.is_disabled = false
                      AND t.date >= @start AND t.date <= @end
                      AND (@stockGuid IS NULL OR l.stock_id = @stockGuid)
                      AND (cardinality(@whs) = 0 OR t.destination_warehouse_id = ANY(@whs))

                    UNION ALL

                    -- 4. Накладные (Invoices)
                    SELECT 
                        i.id AS "TransactionId",
                        i.code AS "TransactionCode",
                        i.date AS "TransactionDate",
                        COALESCE(i.invoice_type, 'Invoice') AS "TransactionType",
                        '00000000-0000-0000-0000-000000000000' AS "TransactionUserId",
                        COALESCE(i.user_name, 'admin') AS "TransactionUserName",
                        COALESCE(i.user_name, 'admin') AS "Author",
                        COALESCE(i.user_name, 'admin') AS "UserName",
                        true AS "TransactionIsCompleted",
                        false AS "TransactionIsDisabled",
                        false AS "TransactionIsCash",
                        COALESCE(i.tags, ARRAY[]::text[]) AS "TransactionTags",
                        COALESCE(i.group_name, '') AS "TransactionGroup",
                        l.id::text AS "ActionId",
                        i.warehouse_id::text AS "ActionWarehouseId",
                        COALESCE(p.name, '') AS "ActionRelatedObjectName",
                        l.stock_id::text AS "ActionStockId",
                        l.stock_id::text AS "StockId",
                        COALESCE(stk.code, '') AS "StockCode",
                        COALESCE(stk.name, '') AS "StockName",
                        COALESCE(stk.type, '') AS "StockType",
                        COALESCE(stk.group_name, '') AS "StockGroup",
                        COALESCE(stk.tags, ARRAY[]::text[]) AS "StockTags",
                        l.price AS "RecommendedPrice",
                        l.price AS "ActionPrice",
                        CASE WHEN i.invoice_type IN ('Purchase', 'SalesReturn') THEN l.quantity ELSE 0 END AS "ActionIncome",
                        CASE WHEN i.invoice_type NOT IN ('Purchase', 'SalesReturn') THEN l.quantity ELSE 0 END AS "ActionExpense",
                        CASE WHEN i.invoice_type IN ('Purchase', 'SalesReturn') THEN l.quantity ELSE -l.quantity END AS "ActionEffect",
                        (l.quantity * l.price) AS "GrandTotal",
                        (l.quantity * l.price) AS "RecommendedTotal",
                        (l.quantity * l.price) AS "GrandTotalInCustomCurrency"
                    FROM invoice_lines l
                    JOIN invoices i ON i.id = l.invoice_id
                    LEFT JOIN partners p ON p.id = i.partner_id
                    LEFT JOIN stocks stk ON stk.id = l.stock_id
                    WHERE i.is_disabled = false
                      AND i.date >= @start AND i.date <= @end
                      AND (@stockGuid IS NULL OR l.stock_id = @stockGuid)
                      AND (cardinality(@whs) = 0 OR i.warehouse_id = ANY(@whs))
                )
                SELECT * 
                FROM raw_actions
                ORDER BY "TransactionDate" DESC
                LIMIT @take OFFSET @skip;
                """;

            var connStr = db.Database.GetConnectionString();
            await using var conn = new Npgsql.NpgsqlConnection(connStr);
            var results = await Dapper.SqlMapper.QueryAsync(conn, new Dapper.CommandDefinition(
                sql,
                new { start = startDate, end = endDate, stockGuid = filterStockGuid, whs = whIds, take, skip },
                cancellationToken: ct));

            return Results.Json(results, jsonOptions);
        })
        .WithName("StockActionsGet");

        // 7. СОХРАНЕНИЕ ТОВАРА
        Func<HttpRequest, MermerDbContext, Task<IResult>> saveStockHandler = async (request, db) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync();
            if (string.IsNullOrEmpty(body)) return Results.BadRequest("Empty body");

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            string? idStr = GetStringProp(root, "id", "Id");
            Guid stockId = Guid.TryParse(idStr, out var parsedGuid) && parsedGuid != Guid.Empty ? parsedGuid : Guid.NewGuid();

            string code = GetStringProp(root, "code", "Code") ?? $"ST-{DateTime.UtcNow:yyMMddHHmmss}";
            string name = GetStringProp(root, "name", "Name") ?? "Новый товар";
            string shortName = GetStringProp(root, "shortName", "ShortName") ?? string.Empty;
            string type = GetStringProp(root, "type", "Type") ?? string.Empty;
            string groupName = GetStringProp(root, "group", "Group", "groupName", "GroupName") ?? string.Empty;
            string description = GetStringProp(root, "description", "Description") ?? string.Empty;
            bool isDisabled = GetBoolProp(root, "isDisabled", "IsDisabled");

            decimal? limitMin = null;
            if (TryGetPropCaseInsensitive(root, "limitMin", out var lmProp) || TryGetPropCaseInsensitive(root, "LimitMin", out lmProp))
            {
                if (lmProp.ValueKind == JsonValueKind.Number) limitMin = lmProp.GetDecimal();
            }

            decimal? limitMax = null;
            if (TryGetPropCaseInsensitive(root, "limitMax", out var lxProp) || TryGetPropCaseInsensitive(root, "LimitMax", out lxProp))
            {
                if (lxProp.ValueKind == JsonValueKind.Number) limitMax = lxProp.GetDecimal();
            }

            var tagsList = ExtractArrayProp(root, "tags", "Tags");
            var barcodesList = ExtractArrayProp(root, "barcodes", "Barcodes");

            var pricesList = new List<StockPriceEntity>();
            if (TryGetPropCaseInsensitive(root, "prices", out var pricesProp) && pricesProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var pJson in pricesProp.EnumerateArray())
                {
                    decimal priceVal = 0m;
                    if (TryGetPropCaseInsensitive(pJson, "price", out var pValProp) || TryGetPropCaseInsensitive(pJson, "Price", out pValProp))
                        priceVal = pValProp.GetDecimal();

                    string? curStr = GetStringProp(pJson, "currencyId", "CurrencyId");
                    Guid? curGuid = Guid.TryParse(curStr, out var cG) ? cG : null;

                    DateTime validFrom = DateTime.UtcNow.Date;
                    string? vfStr = GetStringProp(pJson, "validFrom", "ValidFrom");
                    if (!string.IsNullOrEmpty(vfStr) && DateTime.TryParse(vfStr, out var parsedVf))
                        validFrom = parsedVf.ToUniversalTime();

                    pricesList.Add(new StockPriceEntity
                    {
                        Id = Guid.NewGuid(),
                        StockId = stockId,
                        Price = priceVal,
                        CurrencyId = curGuid,
                        PriceGroup = GetStringProp(pJson, "priceGroup", "PriceGroup", "group", "Group") ?? string.Empty,
                        ValidFrom = validFrom
                    });
                }
            }

            decimal rootPrice = 0m;
            if (TryGetPropCaseInsensitive(root, "price", out var rPriceProp) || TryGetPropCaseInsensitive(root, "Price", out rPriceProp))
                rootPrice = rPriceProp.GetDecimal();

            string? rootCurStr = GetStringProp(root, "currencyId", "CurrencyId");
            Guid? rootCurGuid = Guid.TryParse(rootCurStr, out var rcG) ? rcG : null;

            if (!pricesList.Any() && rootPrice > 0)
            {
                pricesList.Add(new StockPriceEntity
                {
                    Id = Guid.NewGuid(),
                    StockId = stockId,
                    Price = rootPrice,
                    CurrencyId = rootCurGuid,
                    PriceGroup = string.Empty,
                    ValidFrom = DateTime.UtcNow.Date
                });
            }

            var unitsList = new List<StockUnitEntity>();
            if (TryGetPropCaseInsensitive(root, "units", out var unitsProp) && unitsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var uJson in unitsProp.EnumerateArray())
                {
                    string uName = GetStringProp(uJson, "name", "Name") ?? "шт";
                    decimal mult = 1m;
                    if (TryGetPropCaseInsensitive(uJson, "multiplier", out var mProp) || TryGetPropCaseInsensitive(uJson, "Multiplier", out mProp))
                        mult = mProp.GetDecimal();

                    decimal div = 1m;
                    if (TryGetPropCaseInsensitive(uJson, "divider", out var dProp) || TryGetPropCaseInsensitive(uJson, "Divider", out dProp))
                        div = dProp.GetDecimal();

                    bool isDef = GetBoolProp(uJson, "isDefault", "IsDefault");

                    unitsList.Add(new StockUnitEntity
                    {
                        Id = Guid.NewGuid(),
                        StockId = stockId,
                        Name = uName,
                        Multiplier = mult,
                        Divider = div,
                        IsDefault = isDef
                    });
                }
            }

            string? rootUnit = GetStringProp(root, "unit", "Unit");
            if (!unitsList.Any() && !string.IsNullOrEmpty(rootUnit))
            {
                unitsList.Add(new StockUnitEntity
                {
                    Id = Guid.NewGuid(),
                    StockId = stockId,
                    Name = rootUnit,
                    Multiplier = 1m,
                    Divider = 1m,
                    IsDefault = true
                });
            }

            var existing = await db.Stocks
                .Include(s => s.Prices)
                .Include(s => s.Units)
                .FirstOrDefaultAsync(p => p.Id == stockId || (!string.IsNullOrEmpty(code) && p.Code == code), request.HttpContext.RequestAborted);

            if (existing == null)
            {
                var newEntity = new StockEntity
                {
                    Id = stockId,
                    Code = code,
                    Name = name,
                    ShortName = shortName,
                    Type = type,
                    Group = groupName,
                    Description = description,
                    LimitMin = limitMin,
                    LimitMax = limitMax,
                    Tags = tagsList.ToArray(),
                    Barcodes = barcodesList.ToArray(),
                    IsDisabled = isDisabled,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    Prices = pricesList,
                    Units = unitsList
                };
                await db.Stocks.AddAsync(newEntity, request.HttpContext.RequestAborted);
            }
            else
            {
                stockId = existing.Id;

                existing.Code = code;
                existing.Name = name;
                existing.ShortName = shortName;
                existing.Type = type;
                existing.Group = groupName;
                existing.Description = description;
                existing.LimitMin = limitMin;
                existing.LimitMax = limitMax;
                existing.Tags = tagsList.ToArray();
                existing.Barcodes = barcodesList.ToArray();
                existing.IsDisabled = isDisabled;
                existing.UpdatedAt = DateTime.UtcNow;

                if (existing.Prices != null && existing.Prices.Any())
                {
                    db.StockPrices.RemoveRange(existing.Prices);
                }
                foreach (var p in pricesList)
                {
                    p.StockId = existing.Id;
                    await db.StockPrices.AddAsync(p, request.HttpContext.RequestAborted);
                }

                if (existing.Units != null && existing.Units.Any())
                {
                    db.StockUnits.RemoveRange(existing.Units);
                }
                foreach (var u in unitsList)
                {
                    u.StockId = existing.Id;
                    await db.StockUnits.AddAsync(u, request.HttpContext.RequestAborted);
                }
            }

            await db.SaveChangesAsync(request.HttpContext.RequestAborted);
            return Results.Json(new { id = stockId, code }, jsonOptions);
        };

        group.MapPost("/", saveStockHandler);
        group.MapPut("/{id}", saveStockHandler);

        // 8. УДАЛЕНИЕ
        group.MapDelete("/{id}", async (string id, MermerDbContext db, CancellationToken ct) =>
        {
            if (Guid.TryParse(id, out var guid))
            {
                var stock = await db.Stocks.FirstOrDefaultAsync(x => x.Id == guid, ct);
                if (stock != null)
                {
                    stock.IsDisabled = true;
                    stock.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                }
            }
            return Results.NoContent();
        })
        .WithName("StocksDelete");

        // 9. СЛИЯНИЕ ТОВАРОВ
        group.MapPost("/merge", async (HttpRequest request, MermerDbContext db, CancellationToken ct) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync();
            if (string.IsNullOrEmpty(body)) return Results.BadRequest("Empty body");

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            string? mainIdStr = GetStringProp(root, "MainStockId", "mainStockId");
            if (!Guid.TryParse(mainIdStr, out var mainStockId))
                return Results.BadRequest("Invalid MainStockId");

            var mergeStockIds = new List<Guid>();
            if (TryGetPropCaseInsensitive(root, "MergeStockIds", out var idsProp) && idsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in idsProp.EnumerateArray())
                {
                    if (Guid.TryParse(el.GetString(), out var g) && g != mainStockId)
                        mergeStockIds.Add(g);
                }
            }

            if (!mergeStockIds.Any()) return Results.Json(new { success = true, count = 0 }, jsonOptions);

            bool disableMerged = true;
            if (TryGetPropCaseInsensitive(root, "DisableMergedItems", out var disProp) ||
                TryGetPropCaseInsensitive(root, "disableMergedItems", out disProp))
            {
                if (disProp.ValueKind == JsonValueKind.False) disableMerged = false;
            }

            var mainStock = await db.Stocks.FirstOrDefaultAsync(s => s.Id == mainStockId, ct);
            if (mainStock == null) return Results.NotFound("Main stock not found");

            var duplicateStocks = await db.Stocks.Where(s => mergeStockIds.Contains(s.Id)).ToListAsync(ct);

            var allBarcodes = (mainStock.Barcodes ?? Array.Empty<string>()).ToList();
            var allTags = (mainStock.Tags ?? Array.Empty<string>()).ToList();

            foreach (var d in duplicateStocks)
            {
                if (d.Barcodes != null) allBarcodes.AddRange(d.Barcodes);
                if (d.Tags != null) allTags.AddRange(d.Tags);

                if (disableMerged)
                {
                    d.IsDisabled = true;
                    d.UpdatedAt = DateTime.UtcNow;
                }
            }

            mainStock.Barcodes = allBarcodes.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            mainStock.Tags = allTags.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            mainStock.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);

            var mergeArray = mergeStockIds.ToArray();

            await db.Database.ExecuteSqlRawAsync(
                @"UPDATE stock_slip_lines SET stock_id = {0} WHERE stock_id = ANY({1})",
                mainStockId, mergeArray);

            await db.Database.ExecuteSqlRawAsync(
                @"UPDATE stock_transfer_lines SET stock_id = {0} WHERE stock_id = ANY({1})",
                mainStockId, mergeArray);

            await db.Database.ExecuteSqlRawAsync(
                @"UPDATE invoice_lines SET stock_id = {0} WHERE stock_id = ANY({1})",
                mainStockId, mergeArray);

            try
            {
                await db.Database.ExecuteSqlRawAsync(
                    @"UPDATE stock_order_lines SET stock_id = {0} WHERE stock_id = ANY({1})",
                    mainStockId, mergeArray);
            }
            catch { }

            return Results.Json(new { success = true, mergedCount = mergeStockIds.Count }, jsonOptions);
        })
        .WithName("StocksMerge");

        return app;
    }

    #region Helpers
    private static List<string> ExtractArrayProp(JsonElement root, params string[] propNames)
    {
        var list = new List<string>();
        foreach (var name in propNames)
        {
            if (TryGetPropCaseInsensitive(root, name, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in prop.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            var s = item.GetString();
                            if (!string.IsNullOrWhiteSpace(s)) list.Add(s.Trim());
                        }
                        else if (item.ValueKind == JsonValueKind.Object)
                        {
                            if (item.TryGetProperty("Text", out var t) || item.TryGetProperty("Value", out t) || item.TryGetProperty("Name", out t))
                            {
                                var s = t.GetString();
                                if (!string.IsNullOrWhiteSpace(s)) list.Add(s.Trim());
                            }
                        }
                    }
                }
                else if (prop.ValueKind == JsonValueKind.String)
                {
                    var raw = prop.GetString();
                    if (!string.IsNullOrWhiteSpace(raw))
                    {
                        list.AddRange(raw.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                                         .Select(x => x.Trim())
                                         .Where(x => !string.IsNullOrWhiteSpace(x)));
                    }
                }
                break;
            }
        }
        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool TryGetPropCaseInsensitive(JsonElement el, string name, out JsonElement val)
    {
        foreach (var p in el.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                val = p.Value;
                return true;
            }
        }
        val = default;
        return false;
    }

    private static string? GetStringProp(JsonElement el, params string[] names)
    {
        foreach (var n in names)
        {
            if (TryGetPropCaseInsensitive(el, n, out var p) && p.ValueKind == JsonValueKind.String)
                return p.GetString();
        }
        return null;
    }

    private static bool GetBoolProp(JsonElement el, params string[] names)
    {
        foreach (var n in names)
        {
            if (TryGetPropCaseInsensitive(el, n, out var p))
            {
                if (p.ValueKind == JsonValueKind.True) return true;
                if (p.ValueKind == JsonValueKind.False) return false;
            }
        }
        return false;
    }
    #endregion
}
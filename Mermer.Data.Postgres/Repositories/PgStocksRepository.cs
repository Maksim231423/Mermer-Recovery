using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Mermer.Data.Postgres.Abstractions;
using Mermer.Data.Postgres.Entities;
using Mermer.Data.Postgres.Models;

namespace Mermer.Data.Postgres.Repositories;

/// <summary>
/// PostgreSQL implementation of <see cref="IStocksRepository"/>.
///
/// Replaces the Couchbase StocksRepository. Domain IDs are <c>string</c>
/// (legacy contract); on the wire we always parse them to <c>Guid</c>.
/// </summary>
public class PgStocksRepository : IStocksRepository
{
    private readonly MermerDbContext _db;
    private readonly string _connectionString;

    public PgStocksRepository(MermerDbContext db, string connectionString)
    {
        _db = db;
        _connectionString = connectionString;
    }

    public async Task<Stock?> GetAsync(string id, CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var guid))
            return null;

        var entity = await _db.Stocks
            .Include(s => s.Units)
            .Include(s => s.Prices)
            .Include(s => s.AdditionalPrices)
            .FirstOrDefaultAsync(s => s.Id == guid, ct);

        return entity == null ? null : MapToModel(entity);
    }

    public async Task<IReadOnlyList<Stock>> GetAllAsync(CancellationToken ct = default)
    {
        var entities = await _db.Stocks
            .Include(s => s.Units)
            .Include(s => s.Prices)
            .Include(s => s.AdditionalPrices)
            .Where(s => !s.IsDisabled)
            .OrderBy(s => s.Name)
            .ToListAsync(ct);

        return entities.Select(MapToModel).ToList();
    }

    public async Task<IReadOnlyList<Stock>> GetListAsync(string[] stockIds, CancellationToken ct = default)
    {
        var guids = ParseGuids(stockIds);
        if (guids.Length == 0)
            return Array.Empty<Stock>();

        var entities = await _db.Stocks
            .Include(s => s.Units)
            .Include(s => s.Prices)
            .Include(s => s.AdditionalPrices)
            .Where(s => guids.Contains(s.Id))
            .ToListAsync(ct);

        var byId = entities.ToDictionary(e => e.Id);
        return guids
            .Where(byId.ContainsKey)
            .Select(g => MapToModel(byId[g]))
            .ToList();
    }

    public async Task<IReadOnlyList<StockInfo>> GetInfoAsync(string[]? stockIds = null, CancellationToken ct = default)
    {
        // ОПТИМИЗИРОВАННЫЙ ЗАПРОС: убраны тяжелые LATERAL subqueries в пользу плоских JOIN
        const string sql = """
            WITH default_units AS (
                SELECT stock_id, id, name
                FROM stock_units
                WHERE is_default = true
            ),
            latest_prices AS (
                SELECT DISTINCT ON (stock_id) 
                    stock_id, price, currency_id
                FROM stock_prices
                WHERE price_group IS NULL
                ORDER BY stock_id, valid_from DESC
            )
            SELECT
                s.id,
                s.code,
                s.name,
                s.short_name,
                s.type,
                s.group_name,
                s.tags,
                s.barcodes,
                s.is_disabled,
                u.name        AS unit,
                u.id          AS unit_id,
                COALESCE(p.price, 0) AS price,
                p.currency_id
            FROM stocks s
            LEFT JOIN default_units u ON u.stock_id = s.id
            LEFT JOIN latest_prices p ON p.stock_id = s.id
            WHERE NOT s.is_disabled
              AND (@ids::uuid[] IS NULL OR s.id = ANY(@ids))
            ORDER BY s.name;
            """;

        var guids = stockIds is { Length: > 0 } ? ParseGuids(stockIds) : null;
        Guid[]? param = guids is { Length: > 0 } ? guids : null;

        await using var conn = new NpgsqlConnection(_connectionString);
        var rows = await conn.QueryAsync(new CommandDefinition(sql, new { ids = param }, cancellationToken: ct));

        return rows.Select(r => new StockInfo
        {
            Id = ((Guid)r.id).ToString(),
            Code = (string?)r.code,
            Name = (string)r.name,
            ShortName = (string?)r.short_name,
            Unit = (string?)r.unit,
            Price = (decimal?)r.price ?? 0m,
            CurrencyId = ((Guid?)r.currency_id)?.ToString(),
            Type = (string?)r.type,
            Group = (string?)r.group_name,
            Tags = ((string[]?)r.tags)?.ToList(),
            Barcodes = ((string[]?)r.barcodes)?.ToList(),
            IsDisabled = (bool)r.is_disabled
        }).ToList();
    }

    public async Task<IReadOnlyList<StockInfo>> GetInfoAsync(
        string? additionalPriceCurrencyId,
        string? additionalPriceGroup,
        CancellationToken ct = default)
    {
        var infos = (await GetInfoAsync(stockIds: null, ct)).ToList();

        if (string.IsNullOrEmpty(additionalPriceCurrencyId) &&
            string.IsNullOrEmpty(additionalPriceGroup))
            return infos;

        const string sql = """
            SELECT
                ap.stock_id,
                ap.price,
                ap.currency_id
            FROM stock_additional_prices ap
            WHERE (@group IS NULL OR ap.price_group = @group)
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        var apRows = await conn.QueryAsync(new CommandDefinition(sql,
            new { group = string.IsNullOrEmpty(additionalPriceGroup) ? null : additionalPriceGroup },
            cancellationToken: ct));

        var byStock = apRows.ToDictionary(r => ((Guid)r.stock_id).ToString());

        foreach (var info in infos)
        {
            if (info.Id != null && byStock.TryGetValue(info.Id, out var ap))
            {
                info.AdditionalPrice           = (decimal?)ap.price ?? 0m;
                info.AdditionalPriceCurrencyId = ((Guid?)ap.currency_id)?.ToString();
            }
        }

        return infos;
    }

    public async Task<Stock> CreateAsync(Stock model, CancellationToken ct = default)
    {
        model.Id ??= Guid.NewGuid().ToString();
        var entity = MapToEntity(model);
        entity.CreatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        _db.Stocks.Add(entity);
        await _db.SaveChangesAsync(ct);
        return model;
    }

    public async Task<Stock> UpdateAsync(Stock model, CancellationToken ct = default)
    {
        if (!Guid.TryParse(model.Id, out var guid))
            throw new ArgumentException("Invalid stock ID", nameof(model));

        var entity = await _db.Stocks
            .Include(s => s.Units)
            .Include(s => s.Prices)
            .Include(s => s.AdditionalPrices)
            .FirstOrDefaultAsync(s => s.Id == guid, ct)
            ?? throw new InvalidOperationException($"Stock {guid} not found");

        entity.Code        = model.Code;
        entity.Name        = model.Name;
        entity.ShortName   = model.ShortName;
        entity.Type        = model.Type;
        entity.Group       = model.Group;
        entity.Tags        = model.Tags?.ToArray();
        entity.Barcodes    = model.Barcodes?.ToArray();
        entity.LimitMin    = model.LimitMin;
        entity.LimitMax    = model.LimitMax;
        entity.Description = model.Description;
        entity.IsDisabled  = model.IsDisabled;
        entity.UpdatedAt   = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
        return model;
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var guid))
            return;
        var entity = await _db.Stocks.FindAsync(new object[] { guid }, ct);
        if (entity != null)
        {
            entity.IsDisabled = true;
            entity.UpdatedAt  = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task MergeAsync(string mainStockId, string[] mergeStockIds, bool disableMergedItems, CancellationToken ct = default)
    {
        if (!Guid.TryParse(mainStockId, out var mainGuid))
            throw new ArgumentException("Invalid mainStockId", nameof(mainStockId));

        var mergeGuids = ParseGuids(mergeStockIds);
        if (mergeGuids.Length == 0)
            return;

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            await _db.Database.ExecuteSqlRawAsync(
                "UPDATE invoice_lines SET stock_id = {0} WHERE stock_id = ANY({1})",
                new object[] { mainGuid, mergeGuids }, ct);

            if (disableMergedItems)
            {
                var mergedStocks = await _db.Stocks
                    .Where(s => mergeGuids.Contains(s.Id))
                    .ToListAsync(ct);

                var mainStock = await _db.Stocks.FindAsync(new object[] { mainGuid }, ct);
                if (mainStock != null)
                {
                    var allBarcodes = mainStock.Barcodes?.ToList() ?? new List<string>();
                    foreach (var s in mergedStocks)
                    {
                        allBarcodes.Add(s.Code ?? s.Id.ToString());
                        if (s.Barcodes != null) allBarcodes.AddRange(s.Barcodes);
                        s.IsDisabled = true;
                        s.UpdatedAt  = DateTimeOffset.UtcNow;
                    }
                    mainStock.Barcodes  = allBarcodes.Distinct().ToArray();
                    mainStock.UpdatedAt = DateTimeOffset.UtcNow;
                }
            }

            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>>> GetFacetsAsync(
        string[] fields, CancellationToken ct = default)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, int>>();

        if (fields.Contains("group", StringComparer.OrdinalIgnoreCase))
        {
            var groups = await _db.Stocks
                .Where(s => s.Group != null && !s.IsDisabled)
                .GroupBy(s => s.Group!)
                .Select(g => new { Key = g.Key, Count = g.Count() })
                .ToListAsync(ct);
            result["group"] = groups.ToDictionary(x => x.Key, x => x.Count);
        }

        if (fields.Contains("type", StringComparer.OrdinalIgnoreCase))
        {
            var types = await _db.Stocks
                .Where(s => s.Type != null && !s.IsDisabled)
                .GroupBy(s => s.Type!)
                .Select(g => new { Key = g.Key, Count = g.Count() })
                .ToListAsync(ct);
            result["type"] = types.ToDictionary(x => x.Key, x => x.Count);
        }

        return result;
    }

    private static Guid[] ParseGuids(string[]? ids) =>
        ids?.Select(id => Guid.TryParse(id, out var g) ? g : Guid.Empty)
            .Where(g => g != Guid.Empty)
            .ToArray() ?? Array.Empty<Guid>();

    private static Stock MapToModel(StockEntity e)
    {
        var stock = new Stock
        {
            Id          = e.Id.ToString(),
            Code        = e.Code,
            Name        = e.Name,
            ShortName   = e.ShortName,
            Type        = e.Type,
            Group       = e.Group,
            Tags        = e.Tags?.ToList(),
            Barcodes    = e.Barcodes?.ToList(),
            LimitMin    = e.LimitMin,
            LimitMax    = e.LimitMax,
            Description = e.Description,
            IsDisabled  = e.IsDisabled
        };

        if (e.Units?.Any() == true)
        {
            stock.Units = e.Units.Select(u => new StockUnit
            {
                Id         = u.Id.ToString(),
                Name       = u.Name,
                IsDefault  = u.IsDefault,
                Multiplier = u.Multiplier,
                Divider    = u.Divider
            }).ToList();
        }

        if (e.Prices?.Any() == true)
        {
            stock.Prices = e.Prices.Select(p => new StockPrice
            {
                Id         = p.Id.ToString(),
                Price      = p.Price,
                CurrencyId = p.CurrencyId?.ToString(),
                PriceGroup = p.PriceGroup,
                ValidFrom  = p.ValidFrom
            }).ToList();
        }

        if (e.AdditionalPrices?.Any() == true)
        {
            stock.AdditionalPrices = e.AdditionalPrices.Select(p => new StockAdditionalPrice
            {
                Id         = p.Id.ToString(),
                Price      = p.Price,
                CurrencyId = p.CurrencyId?.ToString(),
                PriceGroup = p.PriceGroup,
                ValidFrom  = p.ValidFrom
            }).ToList();
        }

        return stock;
    }

    private static StockEntity MapToEntity(Stock m)
    {
        Guid.TryParse(m.Id, out var guid);
        return new StockEntity
        {
            Id          = guid == Guid.Empty ? Guid.NewGuid() : guid,
            Code        = m.Code,
            Name        = m.Name,
            ShortName   = m.ShortName,
            Type        = m.Type,
            Group       = m.Group,
            Tags        = m.Tags?.ToArray(),
            Barcodes    = m.Barcodes?.ToArray(),
            LimitMin    = m.LimitMin,
            LimitMax    = m.LimitMax,
            Description = m.Description,
            IsDisabled  = m.IsDisabled,
            CreatedAt   = DateTimeOffset.UtcNow,
            UpdatedAt   = DateTimeOffset.UtcNow
        };
    }
}

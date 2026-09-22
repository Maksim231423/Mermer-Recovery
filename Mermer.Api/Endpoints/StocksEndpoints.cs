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

        // 1. СПИСОК ТОВАРОВ (БЫСТРАЯ ВЫБОРКА ДЛЯ АВТОКОМПЛИТА)
        group.MapGet("/", async (int? limit, int? offset, string? search, MermerDbContext db, CancellationToken ct) =>
        {
            int take = limit.HasValue ? Math.Clamp(limit.Value, 1, 500) : 100;
            int skip = offset.GetValueOrDefault(0);

            var query = db.Stocks
                .AsNoTracking()
                .Where(s => !s.IsDisabled);

            if (!string.IsNullOrWhiteSpace(search))
            {
                string term = search.Trim();
                if (term.Length <= 2)
                {
                    // Для 1-2 букв ищем по префиксу (мгновенно по B-Tree индексу)
                    query = query.Where(s => EF.Functions.ILike(s.Name, $"{term}%") ||
                                             (s.Code != null && EF.Functions.ILike(s.Code, $"{term}%")));
                }
                else
                {
                    // Для длинных слов ищем подстроку
                    query = query.Where(s => EF.Functions.ILike(s.Name, $"%{term}%") ||
                                             (s.Code != null && EF.Functions.ILike(s.Code, $"%{term}%")));
                }
            }

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
                    CurrencyId = currentPrice?.CurrencyId?.ToString(),
                    Unit = defaultUnit?.Name ?? string.Empty,
                    UnitId = defaultUnit?.Id.ToString(),
                    Prices = s.Prices != null && s.Prices.Any()
                        ? s.Prices.Select(p => (object)new
                        {
                            Id = p.Id.ToString(),
                            Price = p.Price,
                            CurrencyId = p.CurrencyId?.ToString(),
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

            return Results.Ok(result);
        })
        .WithName("StocksList");

        // 2. БЫСТРЫЙ ПОИСК ТОВАРОВ
        group.MapGet("/search", async (string q, string? warehouseId, string? priceGroup, int? limit, double? minSimilarity, IStockSearchService search, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(q)) return Results.Ok(Array.Empty<object>());

            // Ограничиваем выдачу 30 товарами, чтобы ответ прилетал моментально
            int maxResults = limit.HasValue ? Math.Clamp(limit.Value, 1, 50) : 30;
            var result = await search.SearchAsync(q.Trim(), warehouseId, priceGroup, maxResults, minSimilarity ?? 0.1, ct);
            return Results.Ok(result);
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

            return Results.Ok(new
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
            });
        })
        .WithName("StocksGetById");

        // 5. ФАСЕТЫ (GroupNames, TagNames, PriceGroupNames)
        group.MapGet("/facets", async (HttpContext context, MermerDbContext db, CancellationToken ct) =>
        {
            string? fields = context.Request.Query["fields"].ToString();
            var fieldList = string.IsNullOrEmpty(fields)
                ? new[] { "Group", "Tags", "PriceGroupNames" }
                : fields.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var result = new Dictionary<string, Dictionary<string, int>>();

            foreach (var field in fieldList)
            {
                if (field.Equals("Group", StringComparison.OrdinalIgnoreCase) || field.Equals("GroupNames", StringComparison.OrdinalIgnoreCase))
                {
                    var groups = await db.Stocks
                        .AsNoTracking()
                        .Where(x => !string.IsNullOrEmpty(x.Group))
                        .GroupBy(x => x.Group!)
                        .Select(g => new { Key = g.Key, Count = g.Count() })
                        .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

                    result[field] = groups;
                }
                else if (field.Equals("Tags", StringComparison.OrdinalIgnoreCase) || field.Equals("TagNames", StringComparison.OrdinalIgnoreCase))
                {
                    var allTags = await db.Stocks
                        .AsNoTracking()
                        .Where(x => x.Tags != null && x.Tags.Length > 0)
                        .Select(x => x.Tags)
                        .ToListAsync(ct);

                    var tagCounts = allTags
                        .SelectMany(t => t!)
                        .GroupBy(t => t)
                        .ToDictionary(g => g.Key, g => g.Count());

                    result[field] = tagCounts;
                }
                else if (field.Equals("PriceGroupNames", StringComparison.OrdinalIgnoreCase))
                {
                    var priceGroups = await db.StockPrices
                        .AsNoTracking()
                        .Where(x => !string.IsNullOrEmpty(x.PriceGroup))
                        .GroupBy(x => x.PriceGroup!)
                        .Select(g => new { Key = g.Key, Count = g.Count() })
                        .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

                    result[field] = priceGroups;
                }
                else
                {
                    result[field] = new Dictionary<string, int>();
                }
            }

            return Results.Ok(result);
        })
        .WithName("StocksFacets");

        // 6. ЖУРНАЛ ДВИЖЕНИЯ ТОВАРОВ (STOCK ACTIONS)
        group.MapGet("/actions", async (DateTime? from, DateTime? till, string? stockId, HttpRequest req, MermerDbContext db, CancellationToken ct) =>
        {
            DateTimeOffset startDate = from.HasValue ? new DateTimeOffset(from.Value.ToUniversalTime()) : DateTimeOffset.UtcNow.AddMonths(-1);
            DateTimeOffset endDate = till.HasValue ? new DateTimeOffset(till.Value.ToUniversalTime()) : DateTimeOffset.UtcNow;

            var whIds = req.Query["warehouseId"].Select(x => Guid.TryParse(x, out var g) ? (Guid?)g : null).Where(x => x.HasValue).Select(x => x!.Value).ToList();
            Guid? filterStockGuid = Guid.TryParse(stockId, out var sG) ? sG : null;

            var actions = new List<object>();

            // 1. Из складских ордеров (StockSlips)
            var slipsQuery = db.StockSlips.Include(s => s.Lines).ThenInclude(l => l.Stock).AsSplitQuery().AsNoTracking()
                .Where(s => s.Date >= startDate && s.Date <= endDate);

            if (whIds.Any()) slipsQuery = slipsQuery.Where(s => s.WarehouseId.HasValue && whIds.Contains(s.WarehouseId.Value));

            var slips = await slipsQuery.OrderByDescending(s => s.Date).Take(300).ToListAsync(ct);
            foreach (var s in slips)
            {
                foreach (var l in s.Lines ?? Enumerable.Empty<StockSlipLineEntity>())
                {
                    if (filterStockGuid.HasValue && l.StockId != filterStockGuid) continue;

                    bool isIncome = s.SlipType == "StockOpening" || s.SlipType == "RevisionExceed";
                    actions.Add(new
                    {
                        TransactionId = s.Id.ToString(),
                        TransactionCode = s.Code ?? "",
                        TransactionDate = s.Date.UtcDateTime,
                        TransactionType = s.SlipType,
                        TransactionUserId = s.UserId?.ToString() ?? Guid.Empty.ToString(),
                        TransactionUserName = "admin",
                        Author = "admin",
                        UserName = "admin",
                        TransactionIsCompleted = s.IsCompleted,
                        TransactionIsDisabled = false,
                        ActionId = l.Id.ToString(),
                        ActionWarehouseId = s.WarehouseId?.ToString(),
                        ActionStockId = l.StockId?.ToString(),
                        StockCode = l.Stock?.Code ?? "",
                        StockName = l.Stock?.Name ?? "",
                        ActionPrice = l.Price,
                        ActionIncome = isIncome ? l.Quantity : 0m,
                        ActionExpense = !isIncome ? l.Quantity : 0m,
                        GrandTotal = l.Price * l.Quantity
                    });
                }
            }

            // 2. Из перемещений (StockTransfers)
            var transfersQuery = db.StockTransfers.Include(t => t.Lines).ThenInclude(l => l.Stock).AsSplitQuery().AsNoTracking()
                .Where(t => t.Date >= startDate && t.Date <= endDate && !t.IsDisabled);

            if (whIds.Any()) transfersQuery = transfersQuery.Where(t => t.WarehouseId.HasValue && whIds.Contains(t.WarehouseId.Value));

            var transfers = await transfersQuery.OrderByDescending(t => t.Date).Take(300).ToListAsync(ct);
            foreach (var t in transfers)
            {
                foreach (var l in t.Lines ?? Enumerable.Empty<StockTransferLineEntity>())
                {
                    if (filterStockGuid.HasValue && l.StockId != filterStockGuid) continue;

                    if (!whIds.Any() || (t.WarehouseId.HasValue && whIds.Contains(t.WarehouseId.Value)))
                    {
                        actions.Add(new
                        {
                            TransactionId = t.Id.ToString(),
                            TransactionCode = t.Code ?? "",
                            TransactionDate = t.Date.UtcDateTime,
                            TransactionType = "StockTransferSource",
                            TransactionUserId = Guid.Empty.ToString(),
                            TransactionUserName = "admin",
                            Author = "admin",
                            UserName = "admin",
                            TransactionIsCompleted = t.IsCompleted,
                            TransactionIsDisabled = t.IsDisabled,
                            ActionId = l.Id.ToString(),
                            ActionWarehouseId = t.WarehouseId?.ToString(),
                            ActionRelatedWarehouseId = t.DestinationWarehouseId?.ToString(),
                            ActionStockId = l.StockId?.ToString(),
                            StockCode = l.Stock?.Code ?? "",
                            StockName = l.Stock?.Name ?? "",
                            ActionPrice = l.Price,
                            ActionIncome = 0m,
                            ActionExpense = l.Quantity,
                            GrandTotal = l.Price * l.Quantity
                        });
                    }

                    if (!whIds.Any() || (t.DestinationWarehouseId.HasValue && whIds.Contains(t.DestinationWarehouseId.Value)))
                    {
                        actions.Add(new
                        {
                            TransactionId = t.Id.ToString(),
                            TransactionCode = t.Code ?? "",
                            TransactionDate = t.Date.UtcDateTime,
                            TransactionType = "StockTransferDestination",
                            TransactionUserId = Guid.Empty.ToString(),
                            TransactionUserName = "admin",
                            Author = "admin",
                            UserName = "admin",
                            TransactionIsCompleted = t.IsCompleted,
                            TransactionIsDisabled = t.IsDisabled,
                            ActionId = l.Id.ToString(),
                            ActionWarehouseId = t.DestinationWarehouseId?.ToString(),
                            ActionRelatedWarehouseId = t.WarehouseId?.ToString(),
                            ActionStockId = l.StockId?.ToString(),
                            StockCode = l.Stock?.Code ?? "",
                            StockName = l.Stock?.Name ?? "",
                            ActionPrice = l.Price,
                            ActionIncome = l.ReceivedQuantity,
                            ActionExpense = 0m,
                            GrandTotal = l.Price * l.ReceivedQuantity
                        });
                    }
                }
            }

            // 3. Из накладных (Invoices)
            var invQuery = db.Invoices.Include(i => i.Lines).ThenInclude(l => l.Stock).AsSplitQuery().AsNoTracking()
                .Where(i => i.Date >= startDate && i.Date <= endDate && !i.IsDisabled && i.IsCompleted);

            if (whIds.Any()) invQuery = invQuery.Where(i => i.WarehouseId.HasValue && whIds.Contains(i.WarehouseId.Value));

            var invoices = await invQuery.OrderByDescending(i => i.Date).Take(300).ToListAsync(ct);
            foreach (var i in invoices)
            {
                foreach (var l in i.Lines ?? Enumerable.Empty<InvoiceLineEntity>())
                {
                    if (filterStockGuid.HasValue && l.StockId != filterStockGuid) continue;

                    bool isIncome = i.InvoiceType == "Purchase" || i.InvoiceType == "SalesReturn";
                    actions.Add(new
                    {
                        TransactionId = i.Id.ToString(),
                        TransactionCode = i.Code ?? "",
                        TransactionDate = i.Date.UtcDateTime,
                        TransactionType = i.InvoiceType,
                        TransactionUserId = Guid.Empty.ToString(),
                        TransactionUserName = "admin",
                        Author = "admin",
                        UserName = "admin",
                        TransactionIsCompleted = i.IsCompleted,
                        TransactionIsDisabled = i.IsDisabled,
                        ActionId = l.Id.ToString(),
                        ActionWarehouseId = i.WarehouseId?.ToString(),
                        ActionRelatedPartnerId = i.PartnerId?.ToString(),
                        ActionStockId = l.StockId?.ToString(),
                        StockCode = l.Stock?.Code ?? "",
                        StockName = l.Stock?.Name ?? "",
                        ActionPrice = l.Price,
                        ActionIncome = isIncome ? l.Quantity : 0m,
                        ActionExpense = !isIncome ? l.Quantity : 0m,
                        GrandTotal = l.Price * l.Quantity
                    });
                }
            }

            return Results.Ok(actions.OrderByDescending(a => ((dynamic)a).TransactionDate));
        });

        // 7. СОХРАНЕНИЕ ТОВАРА (POST / PUT)
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

            var tagsList = ExtractArrayProp(root, "tags", "Tags");
            var barcodesList = ExtractArrayProp(root, "barcodes", "Barcodes");

            var existing = await db.Stocks.FirstOrDefaultAsync(p => p.Id == stockId);
            if (existing == null)
            {
                await db.Stocks.AddAsync(new StockEntity
                {
                    Id = stockId,
                    Code = code,
                    Name = name,
                    ShortName = shortName,
                    Type = type,
                    Group = groupName,
                    Description = description,
                    Tags = tagsList.ToArray(),
                    Barcodes = barcodesList.ToArray(),
                    IsDisabled = isDisabled,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }
            else
            {
                existing.Code = code;
                existing.Name = name;
                existing.ShortName = shortName;
                existing.Type = type;
                existing.Group = groupName;
                existing.Description = description;
                existing.Tags = tagsList.ToArray();
                existing.Barcodes = barcodesList.ToArray();
                existing.IsDisabled = isDisabled;
                existing.UpdatedAt = DateTime.UtcNow;
                db.Stocks.Update(existing);
            }

            await db.SaveChangesAsync();
            return Results.Ok(new { id = stockId, code });
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
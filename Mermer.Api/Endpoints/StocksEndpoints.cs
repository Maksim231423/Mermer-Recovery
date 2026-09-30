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

        // 1. СПИСОК ТОВАРОВ (БЕЗ ЖЕСТКОГО ОГРАНИЧЕНИЯ В 100 ШТУК)
        group.MapGet("/", async (int? limit, int? offset, string? search, bool? includeDisabled, MermerDbContext db, CancellationToken ct) =>
        {
            int take = limit.HasValue ? Math.Clamp(limit.Value, 1, 10000) : 5000;
            int skip = offset.GetValueOrDefault(0);

            var query = db.Stocks.AsNoTracking();

            // По умолчанию отдаем все записи (включая IsDisabled), чтобы UI применял стиль зачеркивания
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
            var result = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);

            // 1. Группы цен из stock_prices
            var priceGroups = await db.StockPrices
                .AsNoTracking()
                .Where(x => !string.IsNullOrEmpty(x.PriceGroup))
                .GroupBy(x => x.PriceGroup!)
                .Select(g => new { Key = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

            result["PriceGroupNames"] = priceGroups;

            // 2. Группы товаров
            var groups = await db.Stocks
                .AsNoTracking()
                .Where(x => !string.IsNullOrEmpty(x.Group) && !x.IsDisabled)
                .GroupBy(x => x.Group!)
                .Select(g => new { Key = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

            result["Group"] = groups;
            result["GroupNames"] = groups;

            // 3. Теги
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

            return Results.Ok(result);
        })
        .WithName("StocksFacets");

        // 6. ЖУРНАЛ ДВИЖЕНИЯ ТОВАРОВ (STOCK ACTIONS)
        // 6.1. ПОДСЧЕТ КОЛИЧЕСТВА ДВИЖЕНИЙ ТОВАРОВ (STOCK ACTIONS COUNT)
        group.MapGet("/actions/count", async (DateTime? from, DateTime? till, string? stockId, HttpRequest req, MermerDbContext db, CancellationToken ct) =>
        {
            DateTimeOffset startDate = from.HasValue && from.Value.Year > 2000
                ? new DateTimeOffset(from.Value.ToUniversalTime())
                : DateTimeOffset.MinValue;

            DateTimeOffset endDate = till.HasValue && till.Value.Year < 2099
                ? new DateTimeOffset(till.Value.ToUniversalTime())
                : DateTimeOffset.MaxValue;

            var whIds = req.Query["warehouseId"]
                .Select(x => Guid.TryParse(x, out var g) ? (Guid?)g : null)
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .ToList();

            Guid? filterStockGuid = Guid.TryParse(stockId, out var sG) ? sG : null;

            // 1. Складские ордера
            var slipsQuery = db.StockSlipLines.AsNoTracking()
                .Where(l => l.StockSlip != null && l.StockSlip.Date >= startDate && l.StockSlip.Date <= endDate);
            if (filterStockGuid.HasValue) slipsQuery = slipsQuery.Where(l => l.StockId == filterStockGuid);
            if (whIds.Any()) slipsQuery = slipsQuery.Where(l => l.StockSlip!.WarehouseId.HasValue && whIds.Contains(l.StockSlip.WarehouseId.Value));
            int countSlips = await slipsQuery.CountAsync(ct);

            // 2. Перемещения
            var trQuery = db.StockTransferLines.AsNoTracking()
                .Where(l => l.StockTransfer != null && !l.StockTransfer.IsDisabled && l.StockTransfer.Date >= startDate && l.StockTransfer.Date <= endDate);
            if (filterStockGuid.HasValue) trQuery = trQuery.Where(l => l.StockId == filterStockGuid);
            if (whIds.Any())
            {
                trQuery = trQuery.Where(l =>
                    (l.StockTransfer!.WarehouseId.HasValue && whIds.Contains(l.StockTransfer.WarehouseId.Value)) ||
                    (l.StockTransfer!.DestinationWarehouseId.HasValue && whIds.Contains(l.StockTransfer.DestinationWarehouseId.Value)));
            }
            int countTransfers = await trQuery.CountAsync(ct);

            // 3. Накладные
            var invQuery = db.InvoiceLines.AsNoTracking()
                .Where(l => l.Invoice != null && !l.Invoice.IsDisabled && l.Invoice.Date >= startDate && l.Invoice.Date <= endDate);
            if (filterStockGuid.HasValue) invQuery = invQuery.Where(l => l.StockId == filterStockGuid);
            if (whIds.Any()) invQuery = invQuery.Where(l => l.Invoice!.WarehouseId.HasValue && whIds.Contains(l.Invoice.WarehouseId.Value));
            int countInvoices = await invQuery.CountAsync(ct);

            return Results.Ok(new { count = countSlips + countTransfers + countInvoices });
        })
        .WithName("StockActionsCount");

        // 6.2. ЖУРНАЛ ДВИЖЕНИЯ ТОВАРОВ (STOCK ACTIONS)
        group.MapGet("/actions", async (DateTime? from, DateTime? till, string? stockId, HttpRequest req, MermerDbContext db, CancellationToken ct) =>
        {
            DateTimeOffset startDate = from.HasValue && from.Value.Year > 2000
                ? new DateTimeOffset(from.Value.ToUniversalTime())
                : DateTimeOffset.MinValue;

            DateTimeOffset endDate = till.HasValue && till.Value.Year < 2099
                ? new DateTimeOffset(till.Value.ToUniversalTime())
                : DateTimeOffset.MaxValue;

            var whIds = req.Query["warehouseId"]
                .Select(x => Guid.TryParse(x, out var g) ? (Guid?)g : null)
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .ToList();

            Guid? filterStockGuid = Guid.TryParse(stockId, out var sG) ? sG : null;

            var actions = new List<object>();

            // 1. Складские ордера (StockSlips)
            var slipsQuery = db.StockSlipLines
                .Include(l => l.StockSlip)
                .Include(l => l.Stock)
                .AsSplitQuery().AsNoTracking()
                .Where(l => l.StockSlip != null && l.StockSlip.Date >= startDate && l.StockSlip.Date <= endDate);

            if (whIds.Any()) slipsQuery = slipsQuery.Where(l => l.StockSlip!.WarehouseId.HasValue && whIds.Contains(l.StockSlip.WarehouseId.Value));
            if (filterStockGuid.HasValue) slipsQuery = slipsQuery.Where(l => l.StockId == filterStockGuid);

            var slipLines = await slipsQuery.OrderByDescending(l => l.StockSlip!.Date).Take(2000).ToListAsync(ct);
            foreach (var l in slipLines)
            {
                var s = l.StockSlip!;
                bool isIncome = s.SlipType == "StockOpening" || s.SlipType == "RevisionExceed";
                decimal qty = l.Quantity;
                decimal price = l.Price;
                decimal total = l.ActionTotal != 0m ? l.ActionTotal : (qty * price);
                decimal recPrice = price;

                actions.Add(new
                {
                    TransactionId = s.Id.ToString(),
                    TransactionCode = s.Code ?? "",
                    TransactionDate = s.Date.UtcDateTime,
                    TransactionType = s.SlipType ?? "StockSlip",
                    TransactionUserId = s.UserId?.ToString() ?? Guid.Empty.ToString(),
                    TransactionUserName = "admin",
                    Author = "admin",
                    UserName = "admin",
                    TransactionIsCompleted = true,
                    TransactionIsDisabled = false,
                    TransactionIsCash = false,
                    TransactionTags = s.Tags?.ToList() ?? new List<string>(),
                    TransactionGroup = s.GroupName ?? string.Empty,

                    ActionId = l.Id.ToString(),
                    ActionWarehouseId = s.WarehouseId?.ToString(),
                    ActionRelatedObjectName = string.Empty,
                    ActionStockId = l.StockId?.ToString(),
                    StockId = l.StockId?.ToString(),
                    StockCode = l.Stock?.Code ?? "",
                    StockName = l.Stock?.Name ?? "",
                    StockType = l.Stock?.Type ?? "",
                    StockGroup = l.Stock?.Group ?? "",
                    StockTags = l.Stock?.Tags?.ToList() ?? new List<string>(),

                    RecommendedPrice = recPrice,
                    ActionPrice = price,
                    ActionIncome = isIncome ? qty : 0m,
                    ActionExpense = !isIncome ? qty : 0m,
                    ActionEffect = isIncome ? qty : -qty,
                    GrandTotal = total,
                    RecommendedTotal = recPrice * qty,
                    GrandTotalInCustomCurrency = total,
                    ActionOverhead = 0m,
                    ActionDiscount = 0m,
                    IsCheaperThanRecommended = false
                });
            }

            // 2. Перемещения (StockTransfers)
            var trQuery = db.StockTransferLines
                .Include(l => l.StockTransfer)
                .Include(l => l.Stock)
                .AsSplitQuery().AsNoTracking()
                .Where(l => l.StockTransfer != null && l.StockTransfer.Date >= startDate && l.StockTransfer.Date <= endDate && !l.StockTransfer.IsDisabled);

            if (filterStockGuid.HasValue) trQuery = trQuery.Where(l => l.StockId == filterStockGuid);
            if (whIds.Any())
            {
                trQuery = trQuery.Where(l =>
                    (l.StockTransfer!.WarehouseId.HasValue && whIds.Contains(l.StockTransfer.WarehouseId.Value)) ||
                    (l.StockTransfer!.DestinationWarehouseId.HasValue && whIds.Contains(l.StockTransfer.DestinationWarehouseId.Value)));
            }

            var trLines = await trQuery.OrderByDescending(l => l.StockTransfer!.Date).Take(2000).ToListAsync(ct);
            foreach (var l in trLines)
            {
                var t = l.StockTransfer!;
                decimal qtySent = l.Quantity;
                decimal qtyRec = l.ReceivedQuantity != 0m ? l.ReceivedQuantity : l.Quantity;
                decimal price = l.Price;
                decimal recPrice = price;

                // Расход со склада-отправителя
                if (!whIds.Any() || (t.WarehouseId.HasValue && whIds.Contains(t.WarehouseId.Value)))
                {
                    actions.Add(new
                    {
                        TransactionId = t.Id.ToString(),
                        TransactionCode = t.Code ?? "",
                        TransactionDate = t.Date.UtcDateTime,
                        TransactionType = "StockTransferSource",
                        TransactionUserId = Guid.Empty.ToString(),
                        TransactionUserName = t.UserName ?? "admin",
                        Author = t.UserName ?? "admin",
                        UserName = t.UserName ?? "admin",
                        TransactionIsCompleted = true,
                        TransactionIsDisabled = false,
                        TransactionIsCash = false,
                        TransactionTags = t.Tags?.ToList() ?? new List<string>(),
                        TransactionGroup = t.GroupName ?? string.Empty,

                        ActionId = l.Id.ToString(),
                        ActionWarehouseId = t.WarehouseId?.ToString(),
                        ActionRelatedObjectName = t.DestinationWarehouseId?.ToString() ?? "",
                        ActionStockId = l.StockId?.ToString(),
                        StockId = l.StockId?.ToString(),
                        StockCode = l.Stock?.Code ?? "",
                        StockName = l.Stock?.Name ?? "",
                        StockType = l.Stock?.Type ?? "",
                        StockGroup = l.Stock?.Group ?? "",
                        StockTags = l.Stock?.Tags?.ToList() ?? new List<string>(),

                        RecommendedPrice = recPrice,
                        ActionPrice = price,
                        ActionIncome = 0m,
                        ActionExpense = qtySent,
                        ActionEffect = -qtySent,
                        GrandTotal = l.ActionTotal != 0m ? l.ActionTotal : (price * qtySent),
                        RecommendedTotal = recPrice * qtySent,
                        GrandTotalInCustomCurrency = l.ActionTotal != 0m ? l.ActionTotal : (price * qtySent),
                        ActionOverhead = 0m,
                        ActionDiscount = 0m,
                        IsCheaperThanRecommended = false
                    });
                }

                // Приход на склад-получатель
                if (!whIds.Any() || (t.DestinationWarehouseId.HasValue && whIds.Contains(t.DestinationWarehouseId.Value)))
                {
                    actions.Add(new
                    {
                        TransactionId = t.Id.ToString(),
                        TransactionCode = t.Code ?? "",
                        TransactionDate = t.Date.UtcDateTime,
                        TransactionType = "StockTransferDestination",
                        TransactionUserId = Guid.Empty.ToString(),
                        TransactionUserName = t.UserName ?? "admin",
                        Author = t.UserName ?? "admin",
                        UserName = t.UserName ?? "admin",
                        TransactionIsCompleted = true,
                        TransactionIsDisabled = false,
                        TransactionIsCash = false,
                        TransactionTags = t.Tags?.ToList() ?? new List<string>(),
                        TransactionGroup = t.GroupName ?? string.Empty,

                        ActionId = l.Id.ToString(),
                        ActionWarehouseId = t.DestinationWarehouseId?.ToString(),
                        ActionRelatedObjectName = t.WarehouseId?.ToString() ?? "",
                        ActionStockId = l.StockId?.ToString(),
                        StockId = l.StockId?.ToString(),
                        StockCode = l.Stock?.Code ?? "",
                        StockName = l.Stock?.Name ?? "",
                        StockType = l.Stock?.Type ?? "",
                        StockGroup = l.Stock?.Group ?? "",
                        StockTags = l.Stock?.Tags?.ToList() ?? new List<string>(),

                        RecommendedPrice = recPrice,
                        ActionPrice = price,
                        ActionIncome = qtyRec,
                        ActionExpense = 0m,
                        ActionEffect = qtyRec,
                        GrandTotal = l.ActionReceivedTotal != 0m ? l.ActionReceivedTotal : (price * qtyRec),
                        RecommendedTotal = recPrice * qtyRec,
                        GrandTotalInCustomCurrency = l.ActionReceivedTotal != 0m ? l.ActionReceivedTotal : (price * qtyRec),
                        ActionOverhead = 0m,
                        ActionDiscount = 0m,
                        IsCheaperThanRecommended = false
                    });
                }
            }

            // 3. Накладные (Invoices)
            var invQuery = db.InvoiceLines
                .Include(l => l.Invoice)
                    .ThenInclude(i => i!.Partner)
                .Include(l => l.Stock)
                .AsSplitQuery().AsNoTracking()
                .Where(l => l.Invoice != null && l.Invoice.Date >= startDate && l.Invoice.Date <= endDate && !l.Invoice.IsDisabled);

            if (whIds.Any()) invQuery = invQuery.Where(l => l.Invoice!.WarehouseId.HasValue && whIds.Contains(l.Invoice.WarehouseId.Value));
            if (filterStockGuid.HasValue) invQuery = invQuery.Where(l => l.StockId == filterStockGuid);

            var invLines = await invQuery.OrderByDescending(l => l.Invoice!.Date).Take(2000).ToListAsync(ct);
            foreach (var l in invLines)
            {
                var i = l.Invoice!;
                bool isIncome = i.InvoiceType == "Purchase" || i.InvoiceType == "SalesReturn";
                decimal qty = l.Quantity;
                decimal price = l.Price;
                decimal total = qty * price;
                decimal recPrice = price;

                actions.Add(new
                {
                    TransactionId = i.Id.ToString(),
                    TransactionCode = i.Code ?? "",
                    TransactionDate = i.Date.UtcDateTime,
                    TransactionType = i.InvoiceType ?? "Invoice",
                    TransactionUserId = Guid.Empty.ToString(),
                    TransactionUserName = i.UserName ?? "admin",
                    Author = i.UserName ?? "admin",
                    UserName = i.UserName ?? "admin",
                    TransactionIsCompleted = true,
                    TransactionIsDisabled = false,
                    TransactionIsCash = false,
                    TransactionTags = i.Tags?.ToList() ?? new List<string>(),
                    TransactionGroup = i.Group ?? string.Empty,

                    ActionId = l.Id.ToString(),
                    ActionWarehouseId = i.WarehouseId?.ToString(),
                    ActionRelatedObjectName = i.Partner?.Name ?? "",
                    ActionStockId = l.StockId?.ToString(),
                    StockId = l.StockId?.ToString(),
                    StockCode = l.Stock?.Code ?? "",
                    StockName = l.Stock?.Name ?? "",
                    StockType = l.Stock?.Type ?? "",
                    StockGroup = l.Stock?.Group ?? "",
                    StockTags = l.Stock?.Tags?.ToList() ?? new List<string>(),

                    RecommendedPrice = recPrice,
                    ActionPrice = price,
                    ActionIncome = isIncome ? qty : 0m,
                    ActionExpense = !isIncome ? qty : 0m,
                    ActionEffect = isIncome ? qty : -qty,
                    GrandTotal = total,
                    RecommendedTotal = recPrice * qty,
                    GrandTotalInCustomCurrency = total,
                    ActionOverhead = 0m,
                    ActionDiscount = 0m,
                    IsCheaperThanRecommended = false
                });
            }

            var sorted = actions.OrderByDescending(a => ((dynamic)a).TransactionDate).ToList();
            return Results.Ok(sorted);
        })
        .WithName("StockActionsGet");

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

        // 9. СЛИЯНИЕ ТОВАРОВ (STOCK MERGE)
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

            if (!mergeStockIds.Any()) return Results.Ok(new { success = true, count = 0 });

            // Если флаг не передали явно — считаем true (поведение по умолчанию для слияния)
            bool disableMerged = true;
            if (TryGetPropCaseInsensitive(root, "DisableMergedItems", out var disProp) ||
                TryGetPropCaseInsensitive(root, "disableMergedItems", out disProp))
            {
                if (disProp.ValueKind == JsonValueKind.False) disableMerged = false;
            }

            var mainStock = await db.Stocks.FirstOrDefaultAsync(s => s.Id == mainStockId, ct);
            if (mainStock == null) return Results.NotFound("Main stock not found");

            var duplicateStocks = await db.Stocks.Where(s => mergeStockIds.Contains(s.Id)).ToListAsync(ct);

            // 1. Объединяем штрихкоды и теги в основной товар
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

            // Сначала фиксируем изменения сущностей в EF
            await db.SaveChangesAsync(ct);

            // 2. Затем перенаправляем ссылки в документах прямым SQL (без дедлоков)
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

            return Results.Ok(new { success = true, mergedCount = mergeStockIds.Count });
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
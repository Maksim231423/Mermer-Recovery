using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Mermer.Data.Postgres;

namespace Mermer.Api.Endpoints;

public static class StockBalancesEndpoints
{
    public static IEndpointRouteBuilder MapStockBalancesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/stock-balances").WithTags("StockBalances");

        // 1. ТЕКУЩИЕ ОСТАТКИ (Чтение напрямую из таблицы stock_balances с лимитом)
        group.MapGet("/", async (HttpRequest req, MermerDbContext db, CancellationToken ct) =>
        {
            int limit = int.TryParse(req.Query["limit"], out var l) ? Math.Clamp(l, 1, 1000) : 250;
            int offset = int.TryParse(req.Query["offset"], out var o) ? Math.Max(0, o) : 0;

            var whIds = req.Query["warehouseId"]
                .Select(x => Guid.TryParse(x, out var g) ? (Guid?)g : null)
                .Where(x => x.HasValue).Select(x => x!.Value).ToList();

            var stockIds = req.Query["stockId"]
                .Select(x => Guid.TryParse(x, out var g) ? (Guid?)g : null)
                .Where(x => x.HasValue).Select(x => x!.Value).ToList();

            // Читаем напрямую из таблицы остатков, исключая нулевые
            var query = db.StockBalances
                .AsNoTracking()
                .Where(sb => (sb.Income - sb.Expense) != 0);

            if (whIds.Any())
                query = query.Where(sb => whIds.Contains(sb.WarehouseId));

            if (stockIds.Any())
                query = query.Where(sb => stockIds.Contains(sb.StockId));

            var balances = await query
                .OrderBy(sb => sb.StockId)
                .Skip(offset)
                .Take(limit)
                .Select(sb => new
                {
                    WarehouseId = sb.WarehouseId.ToString(),
                    StockId = sb.StockId.ToString(),
                    Income = sb.Income,
                    Expense = sb.Expense
                })
                .ToListAsync(ct);

            return Results.Ok(balances);
        });

        // 2. ОТЧЕТ ПО ТИПАМ ДОКУМЕНТОВ
        group.MapGet("/by-type", async (HttpRequest req, MermerDbContext db, CancellationToken ct) =>
        {
            DateTimeOffset dateFrom = DateTimeOffset.MinValue;
            DateTimeOffset dateTill = DateTimeOffset.MaxValue;

            string? fromStr = req.Query["dateFrom"].FirstOrDefault();
            if (!string.IsNullOrEmpty(fromStr) && DateTimeOffset.TryParse(fromStr.Replace(" ", "+"), out var pFrom))
                dateFrom = pFrom.ToUniversalTime();

            string? tillStr = req.Query["dateTill"].FirstOrDefault();
            if (!string.IsNullOrEmpty(tillStr) && DateTimeOffset.TryParse(tillStr.Replace(" ", "+"), out var pTill))
                dateTill = pTill.ToUniversalTime();

            bool aggregate = bool.TryParse(req.Query["aggregate"].FirstOrDefault(), out var agg) && agg;
            string? stockIdStr = req.Query["stockId"].FirstOrDefault();
            Guid? filterStockGuid = Guid.TryParse(stockIdStr, out var sG) ? sG : null;

            var whIds = req.Query["warehouseId"]
                .Select(x => Guid.TryParse(x, out var g) ? (Guid?)g : null)
                .Where(x => x.HasValue).Select(x => x!.Value).ToList();

            var stocksQuery = db.Stocks
                .Include(s => s.Units)
                .Include(s => s.Prices)
                .AsSplitQuery()
                .AsNoTracking()
                .Where(s => !s.IsDisabled);

            if (filterStockGuid.HasValue)
                stocksQuery = stocksQuery.Where(s => s.Id == filterStockGuid.Value);
            else
                stocksQuery = stocksQuery.Take(150); // Ограничение выборки номенклатуры

            var stocks = await stocksQuery.ToListAsync(ct);
            if (!stocks.Any()) return Results.Ok(Array.Empty<object>());

            var stockGuids = stocks.Select(s => s.Id).ToList();

            var slipsQuery = db.StockSlips.Include(s => s.Lines).AsNoTracking().Where(s => s.IsCompleted);
            if (whIds.Any()) slipsQuery = slipsQuery.Where(s => s.WarehouseId.HasValue && whIds.Contains(s.WarehouseId.Value));
            var slips = await slipsQuery.ToListAsync(ct);

            var transfersQuery = db.StockTransfers.Include(t => t.Lines).AsNoTracking().Where(t => !t.IsDisabled && t.IsCompleted);
            if (whIds.Any()) transfersQuery = transfersQuery.Where(t => (t.WarehouseId.HasValue && whIds.Contains(t.WarehouseId.Value)) || (t.DestinationWarehouseId.HasValue && whIds.Contains(t.DestinationWarehouseId.Value)));
            var transfers = await transfersQuery.ToListAsync(ct);

            var invoicesQuery = db.Invoices.Include(i => i.Lines).AsNoTracking().Where(i => !i.IsDisabled && i.IsCompleted);
            if (whIds.Any()) invoicesQuery = invoicesQuery.Where(i => i.WarehouseId.HasValue && whIds.Contains(i.WarehouseId.Value));
            var invoices = await invoicesQuery.ToListAsync(ct);

            var allMovements = new List<StockMovementRecord>();

            foreach (var s in slips)
            {
                foreach (var l in s.Lines ?? Enumerable.Empty<Data.Postgres.Entities.StockSlipLineEntity>())
                {
                    if (!l.StockId.HasValue || !s.WarehouseId.HasValue || !stockGuids.Contains(l.StockId.Value)) continue;
                    allMovements.Add(new StockMovementRecord(
                        s.WarehouseId.Value, l.StockId.Value, s.Date, s.SlipType,
                        s.SlipType == "StockOpening" || s.SlipType == "RevisionExceed" ? l.Quantity : 0m,
                        s.SlipType != "StockOpening" && s.SlipType != "RevisionExceed" ? l.Quantity : 0m
                    ));
                }
            }

            foreach (var t in transfers)
            {
                foreach (var l in t.Lines ?? Enumerable.Empty<Data.Postgres.Entities.StockTransferLineEntity>())
                {
                    if (!l.StockId.HasValue || !stockGuids.Contains(l.StockId.Value)) continue;
                    if (t.WarehouseId.HasValue)
                        allMovements.Add(new StockMovementRecord(t.WarehouseId.Value, l.StockId.Value, t.Date, "StockTransferSource", 0m, l.Quantity));
                    if (t.DestinationWarehouseId.HasValue)
                        allMovements.Add(new StockMovementRecord(t.DestinationWarehouseId.Value, l.StockId.Value, t.Date, "StockTransferDestination", l.ReceivedQuantity, 0m));
                }
            }

            foreach (var inv in invoices)
            {
                foreach (var l in inv.Lines ?? Enumerable.Empty<Data.Postgres.Entities.InvoiceLineEntity>())
                {
                    if (!l.StockId.HasValue || !inv.WarehouseId.HasValue || !stockGuids.Contains(l.StockId.Value)) continue;
                    bool isIncome = inv.InvoiceType == "Purchase" || inv.InvoiceType == "SalesReturn";
                    allMovements.Add(new StockMovementRecord(
                        inv.WarehouseId.Value, l.StockId.Value, inv.Date, inv.InvoiceType,
                        isIncome ? l.Quantity : 0m, !isIncome ? l.Quantity : 0m
                    ));
                }
            }

            var result = new List<object>();

            if (aggregate)
            {
                foreach (var stock in stocks)
                {
                    var movements = allMovements.Where(m => m.StockId == stock.Id).ToList();
                    var startMovements = movements.Where(m => m.Date < dateFrom).ToList();
                    var periodMovements = movements.Where(m => m.Date >= dateFrom && m.Date <= dateTill).ToList();

                    decimal starting = startMovements.Sum(m => m.Income - m.Expense);
                    decimal inc = periodMovements.Sum(m => m.Income);
                    decimal exp = periodMovements.Sum(m => m.Expense);

                    if (starting == 0 && inc == 0 && exp == 0) continue;

                    var defaultUnit = stock.Units?.FirstOrDefault(u => u.IsDefault) ?? stock.Units?.FirstOrDefault();
                    var currentPrice = stock.Prices?.OrderByDescending(p => p.ValidFrom).FirstOrDefault();

                    result.Add(new
                    {
                        WarehouseId = (string?)null,
                        StockId = stock.Id.ToString(),
                        StockCode = stock.Code ?? "",
                        StockName = stock.Name ?? "",
                        StockShortName = stock.ShortName ?? "",
                        StockUnit = defaultUnit?.Name ?? "",
                        StockPrice = currentPrice?.Price ?? 0m,
                        StockCurrencyId = currentPrice?.CurrencyId?.ToString(),
                        StockType = stock.Type ?? "",
                        StockGroup = stock.Group ?? "",
                        StockTags = stock.Tags ?? Array.Empty<string>(),
                        StartingBalance = starting,
                        Income = inc,
                        Expense = exp,
                        StockOpening = periodMovements.Where(m => m.Type == "StockOpening").Sum(m => m.Income),
                        StockSpoilage = periodMovements.Where(m => m.Type == "StockSpoilage").Sum(m => m.Expense),
                        StockUsage = periodMovements.Where(m => m.Type == "StockUsage").Sum(m => m.Expense),
                        RevisionExceed = periodMovements.Where(m => m.Type == "RevisionExceed").Sum(m => m.Income),
                        RevisionDeficit = periodMovements.Where(m => m.Type == "RevisionDeficit").Sum(m => m.Expense),
                        StockTransferSource = periodMovements.Where(m => m.Type == "StockTransferSource").Sum(m => m.Expense),
                        StockTransferDestination = periodMovements.Where(m => m.Type == "StockTransferDestination").Sum(m => m.Income),
                        Sales = periodMovements.Where(m => m.Type == "Sales").Sum(m => m.Expense),
                        SalesReturn = periodMovements.Where(m => m.Type == "SalesReturn").Sum(m => m.Income),
                        Purchase = periodMovements.Where(m => m.Type == "Purchase").Sum(m => m.Income),
                        PurchaseReturn = periodMovements.Where(m => m.Type == "PurchaseReturn").Sum(m => m.Expense),
                        ResultingBalance = starting + inc - exp
                    });
                }
            }
            else
            {
                var targetWhIds = whIds.Any() ? whIds : allMovements.Select(m => m.WarehouseId).Distinct().ToList();

                foreach (var whId in targetWhIds)
                {
                    foreach (var stock in stocks)
                    {
                        var movements = allMovements.Where(m => m.WarehouseId == whId && m.StockId == stock.Id).ToList();
                        var startMovements = movements.Where(m => m.Date < dateFrom).ToList();
                        var periodMovements = movements.Where(m => m.Date >= dateFrom && m.Date <= dateTill).ToList();

                        decimal starting = startMovements.Sum(m => m.Income - m.Expense);
                        decimal inc = periodMovements.Sum(m => m.Income);
                        decimal exp = periodMovements.Sum(m => m.Expense);

                        if (starting == 0 && inc == 0 && exp == 0) continue;

                        var defaultUnit = stock.Units?.FirstOrDefault(u => u.IsDefault) ?? stock.Units?.FirstOrDefault();
                        var currentPrice = stock.Prices?.OrderByDescending(p => p.ValidFrom).FirstOrDefault();

                        result.Add(new
                        {
                            WarehouseId = whId.ToString(),
                            StockId = stock.Id.ToString(),
                            StockCode = stock.Code ?? "",
                            StockName = stock.Name ?? "",
                            StockShortName = stock.ShortName ?? "",
                            StockUnit = defaultUnit?.Name ?? "",
                            StockPrice = currentPrice?.Price ?? 0m,
                            StockCurrencyId = currentPrice?.CurrencyId?.ToString(),
                            StockType = stock.Type ?? "",
                            StockGroup = stock.Group ?? "",
                            StockTags = stock.Tags ?? Array.Empty<string>(),
                            StartingBalance = starting,
                            Income = inc,
                            Expense = exp,
                            StockOpening = periodMovements.Where(m => m.Type == "StockOpening").Sum(m => m.Income),
                            StockSpoilage = periodMovements.Where(m => m.Type == "StockSpoilage").Sum(m => m.Expense),
                            StockUsage = periodMovements.Where(m => m.Type == "StockUsage").Sum(m => m.Expense),
                            RevisionExceed = periodMovements.Where(m => m.Type == "RevisionExceed").Sum(m => m.Income),
                            RevisionDeficit = periodMovements.Where(m => m.Type == "RevisionDeficit").Sum(m => m.Expense),
                            StockTransferSource = periodMovements.Where(m => m.Type == "StockTransferSource").Sum(m => m.Expense),
                            StockTransferDestination = periodMovements.Where(m => m.Type == "StockTransferDestination").Sum(m => m.Income),
                            Sales = periodMovements.Where(m => m.Type == "Sales").Sum(m => m.Expense),
                            SalesReturn = periodMovements.Where(m => m.Type == "SalesReturn").Sum(m => m.Income),
                            Purchase = periodMovements.Where(m => m.Type == "Purchase").Sum(m => m.Income),
                            PurchaseReturn = periodMovements.Where(m => m.Type == "PurchaseReturn").Sum(m => m.Expense),
                            ResultingBalance = starting + inc - exp
                        });
                    }
                }
            }

            return Results.Ok(result);
        });

        // 3. ОТЧЕТ ПО СКЛАДАМ НА ДАТУ (С ограничением выборки)
        group.MapGet("/by-date-warehouses", async (HttpRequest req, MermerDbContext db, CancellationToken ct) =>
        {
            DateTimeOffset date = DateTimeOffset.UtcNow;
            string? dateStr = req.Query["date"].FirstOrDefault();
            if (!string.IsNullOrEmpty(dateStr) && DateTimeOffset.TryParse(dateStr.Replace(" ", "+"), out var pDate))
                date = pDate.ToUniversalTime();

            string? displayCurrencyId = req.Query["displayCurrencyId"].FirstOrDefault();
            Guid? displayCurrGuid = Guid.TryParse(displayCurrencyId, out var dcG) ? dcG : null;

            var whIds = req.Query["warehouseId"]
                .Select(x => Guid.TryParse(x, out var g) ? (Guid?)g : null)
                .Where(x => x.HasValue).Select(x => x!.Value).ToList();

            var stockIds = req.Query["stockId"]
                .Select(x => Guid.TryParse(x, out var g) ? (Guid?)g : null)
                .Where(x => x.HasValue).Select(x => x!.Value).ToList();

            var invQuery = db.InvoiceLines.Where(l => l.Invoice.IsCompleted && !l.Invoice.IsDisabled && l.Invoice.Date <= date);
            if (whIds.Any()) invQuery = invQuery.Where(l => l.Invoice.WarehouseId.HasValue && whIds.Contains(l.Invoice.WarehouseId.Value));
            if (stockIds.Any()) invQuery = invQuery.Where(l => l.StockId.HasValue && stockIds.Contains(l.StockId.Value));

            var invSums = await invQuery.GroupBy(l => new { Wh = l.Invoice.WarehouseId, St = l.StockId })
                .Select(g => new {
                    Wh = g.Key.Wh,
                    St = g.Key.St,
                    Inc = g.Sum(x => x.Invoice.InvoiceType == "Purchase" || x.Invoice.InvoiceType == "SalesReturn" ? x.Quantity : 0),
                    Exp = g.Sum(x => x.Invoice.InvoiceType == "Sales" || x.Invoice.InvoiceType == "PurchaseReturn" ? x.Quantity : 0)
                }).ToListAsync(ct);

            var slipQuery = db.StockSlipLines.Where(l => l.StockSlip.IsCompleted && l.StockSlip.Date <= date);
            if (whIds.Any()) slipQuery = slipQuery.Where(l => l.StockSlip.WarehouseId.HasValue && whIds.Contains(l.StockSlip.WarehouseId.Value));
            if (stockIds.Any()) slipQuery = slipQuery.Where(l => l.StockId.HasValue && stockIds.Contains(l.StockId.Value));

            var slipSums = await slipQuery.GroupBy(l => new { Wh = l.StockSlip.WarehouseId, St = l.StockId })
                .Select(g => new {
                    Wh = g.Key.Wh,
                    St = g.Key.St,
                    Inc = g.Sum(x => x.StockSlip.SlipType == "StockOpening" || x.StockSlip.SlipType == "RevisionExceed" ? x.Quantity : 0),
                    Exp = g.Sum(x => x.StockSlip.SlipType != "StockOpening" && x.StockSlip.SlipType != "RevisionExceed" ? x.Quantity : 0)
                }).ToListAsync(ct);

            var trOutQuery = db.StockTransferLines.Where(l => l.StockTransfer.IsCompleted && !l.StockTransfer.IsDisabled && l.StockTransfer.Date <= date);
            if (whIds.Any()) trOutQuery = trOutQuery.Where(l => l.StockTransfer.WarehouseId.HasValue && whIds.Contains(l.StockTransfer.WarehouseId.Value));
            if (stockIds.Any()) trOutQuery = trOutQuery.Where(l => l.StockId.HasValue && stockIds.Contains(l.StockId.Value));

            var trOutSums = await trOutQuery.GroupBy(l => new { Wh = l.StockTransfer.WarehouseId, St = l.StockId })
                .Select(g => new { Wh = g.Key.Wh, St = g.Key.St, Inc = 0m, Exp = g.Sum(x => x.Quantity) }).ToListAsync(ct);

            var trInQuery = db.StockTransferLines.Where(l => l.StockTransfer.IsCompleted && !l.StockTransfer.IsDisabled && l.StockTransfer.Date <= date);
            if (whIds.Any()) trInQuery = trInQuery.Where(l => l.StockTransfer.DestinationWarehouseId.HasValue && whIds.Contains(l.StockTransfer.DestinationWarehouseId.Value));
            if (stockIds.Any()) trInQuery = trInQuery.Where(l => l.StockId.HasValue && stockIds.Contains(l.StockId.Value));

            var trInSums = await trInQuery.GroupBy(l => new { Wh = l.StockTransfer.DestinationWarehouseId, St = l.StockId })
                .Select(g => new { Wh = g.Key.Wh, St = g.Key.St, Inc = g.Sum(x => x.ReceivedQuantity), Exp = 0m }).ToListAsync(ct);

            var allBals = invSums.Concat(slipSums).Concat(trOutSums).Concat(trInSums)
                .Where(x => x.Wh.HasValue && x.St.HasValue)
                .GroupBy(x => new { Wh = x.Wh!.Value, St = x.St!.Value })
                .Select(g => new { Wh = g.Key.Wh, St = g.Key.St, Balance = g.Sum(x => x.Inc - x.Exp) })
                .Where(x => x.Balance != 0)
                .ToList();

            var validStockIds = allBals.Select(x => x.St).Distinct().Take(200).ToList();
            if (stockIds.Any()) validStockIds = validStockIds.Union(stockIds).Distinct().Take(200).ToList();

            if (!validStockIds.Any()) return Results.Ok(Array.Empty<object>());

            var stocks = await db.Stocks
                .Include(s => s.Units)
                .Include(s => s.Prices)
                .AsSplitQuery()
                .Where(s => validStockIds.Contains(s.Id))
                .AsNoTracking()
                .ToListAsync(ct);

            var currencies = await db.Currencies.AsNoTracking().ToListAsync(ct);
            var rates = await db.CurrencyRates.AsNoTracking().ToListAsync(ct);

            var displayCurrency = displayCurrGuid.HasValue ? currencies.FirstOrDefault(c => c.Id == displayCurrGuid.Value) : currencies.FirstOrDefault(c => c.IsDefault);
            var dispRate = displayCurrency != null ? rates.Where(r => r.CurrencyId == displayCurrency.Id && r.ValidFrom <= date.Date).OrderByDescending(r => r.ValidFrom).FirstOrDefault() : null;
            decimal dispMult = dispRate?.Multiplier ?? 1m;
            decimal dispDiv = dispRate?.Divider ?? 1m;
            int dispDecimals = displayCurrency?.Decimals ?? 2;

            var result = stocks.Select(stock => {
                var stockBals = allBals.Where(b => b.St == stock.Id);
                if (whIds.Any()) stockBals = stockBals.Where(b => whIds.Contains(b.Wh));

                var balancesDict = stockBals.ToDictionary(b => b.Wh.ToString(), b => b.Balance);

                if (!balancesDict.Any() && !stockIds.Contains(stock.Id)) return null;

                var defaultUnit = stock.Units?.FirstOrDefault(u => u.IsDefault) ?? stock.Units?.FirstOrDefault();
                var currentPrice = stock.Prices?.Where(p => p.ValidFrom <= date.Date).OrderByDescending(p => p.ValidFrom).FirstOrDefault();

                decimal convertedPrice = 0m;
                if (currentPrice != null)
                {
                    var currRate = rates.Where(r => r.CurrencyId == currentPrice.CurrencyId && r.ValidFrom <= date.Date).OrderByDescending(r => r.ValidFrom).FirstOrDefault();
                    decimal currMult = currRate?.Multiplier ?? 1m;
                    decimal currDiv = currRate?.Divider ?? 1m;

                    if (currDiv != 0 && dispMult != 0)
                    {
                        convertedPrice = Math.Round(currentPrice.Price * currMult / currDiv / dispMult * dispDiv, dispDecimals);
                    }
                }

                return new
                {
                    StockId = stock.Id.ToString(),
                    StockCode = stock.Code ?? "",
                    StockName = stock.Name ?? "",
                    StockShortName = stock.ShortName ?? "",
                    StockUnit = defaultUnit?.Name ?? "",
                    StockPrice = convertedPrice,
                    StockPriceCurrencyId = displayCurrency?.Id.ToString() ?? "",
                    StockType = stock.Type ?? "",
                    StockGroup = stock.Group ?? "",
                    StockTags = stock.Tags != null ? string.Join(" ", stock.Tags) : "",
                    Balances = balancesDict
                };
            }).Where(x => x != null).ToList();

            return Results.Ok(result);
        });

        // 4. АГРЕГИРОВАННЫЙ ОТЧЕТ
        group.MapGet("/aggregated", async (HttpRequest req, MermerDbContext db, CancellationToken ct) =>
        {
            return Results.Ok(new
            {
                StartingBalance = 0,
                Income = 0,
                Expense = 0,
                Lines = Array.Empty<object>()
            });
        });

        return app;
    }

    private record StockMovementRecord(Guid WarehouseId, Guid StockId, DateTimeOffset Date, string Type, decimal Income, decimal Expense);
}
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

        // 1. ТЕКУЩИЕ ОСТАТКИ НА СКЛАДАХ (Чистые плоские запросы без спама в логах)
        group.MapGet("/", async (HttpRequest req, MermerDbContext db, CancellationToken ct) =>
        {
            DateTimeOffset date = DateTimeOffset.UtcNow;
            string? dateStr = req.Query["date"].FirstOrDefault();
            if (!string.IsNullOrEmpty(dateStr) && DateTimeOffset.TryParse(dateStr.Replace(" ", "+"), out var pDate))
                date = pDate.ToUniversalTime();

            var whIds = new HashSet<Guid>();
            foreach (var val in req.Query["warehouseId"])
            {
                if (string.IsNullOrWhiteSpace(val)) continue;
                foreach (var part in val.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (Guid.TryParse(part.Trim(), out var parsedWh))
                        whIds.Add(parsedWh);
                }
            }

            var stockIds = new HashSet<Guid>();
            foreach (var val in req.Query["stockId"])
            {
                if (string.IsNullOrWhiteSpace(val)) continue;
                foreach (var part in val.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (Guid.TryParse(part.Trim(), out var parsedSt))
                        stockIds.Add(parsedSt);
                }
            }

            // 1. Накладные (Invoices)
            var invQuery = db.InvoiceLines.AsNoTracking()
                .Where(l => l.StockId.HasValue && l.Invoice != null && l.Invoice.IsCompleted && !l.Invoice.IsDisabled && l.Invoice.WarehouseId.HasValue && l.Invoice.Date <= date);

            if (whIds.Any()) invQuery = invQuery.Where(l => whIds.Contains(l.Invoice.WarehouseId!.Value));
            if (stockIds.Any()) invQuery = invQuery.Where(l => stockIds.Contains(l.StockId!.Value));

            var invLines = await invQuery.Select(l => new
            {
                Wh = l.Invoice.WarehouseId!.Value,
                St = l.StockId!.Value,
                Type = l.Invoice.InvoiceType,
                Qty = l.Quantity
            }).ToListAsync(ct);

            // 2. Складские ордера (StockSlips)
            var slipQuery = db.StockSlipLines.AsNoTracking()
                .Where(l => l.StockId.HasValue && l.StockSlip != null && l.StockSlip.IsCompleted && l.StockSlip.WarehouseId.HasValue && l.StockSlip.Date <= date);

            if (whIds.Any()) slipQuery = slipQuery.Where(l => whIds.Contains(l.StockSlip.WarehouseId!.Value));
            if (stockIds.Any()) slipQuery = slipQuery.Where(l => stockIds.Contains(l.StockId!.Value));

            var slipLines = await slipQuery.Select(l => new
            {
                Wh = l.StockSlip.WarehouseId!.Value,
                St = l.StockId!.Value,
                Type = l.StockSlip.SlipType,
                Qty = l.Quantity
            }).ToListAsync(ct);

            // 3. Перемещения (StockTransfers)
            var trQuery = db.StockTransferLines.AsNoTracking()
                .Where(l => l.StockId.HasValue && l.StockTransfer != null && l.StockTransfer.IsCompleted && !l.StockTransfer.IsDisabled && l.StockTransfer.Date <= date);

            if (whIds.Any())
            {
                trQuery = trQuery.Where(l =>
                    (l.StockTransfer.WarehouseId.HasValue && whIds.Contains(l.StockTransfer.WarehouseId.Value)) ||
                    (l.StockTransfer.DestinationWarehouseId.HasValue && whIds.Contains(l.StockTransfer.DestinationWarehouseId.Value)));
            }
            if (stockIds.Any()) trQuery = trQuery.Where(l => stockIds.Contains(l.StockId!.Value));

            var trLines = await trQuery.Select(l => new
            {
                SrcWh = l.StockTransfer.WarehouseId,
                DstWh = l.StockTransfer.DestinationWarehouseId,
                St = l.StockId!.Value,
                QtySent = l.Quantity,
                QtyRec = l.ReceivedQuantity != 0m ? l.ReceivedQuantity : l.Quantity
            }).ToListAsync(ct);

            // 4. Суммируем остатки в памяти
            var balancesDict = new Dictionary<(Guid Wh, Guid St), BalanceAccumulator>();

            BalanceAccumulator GetOrCreate(Guid wh, Guid st)
            {
                var key = (wh, st);
                if (!balancesDict.TryGetValue(key, out var acc))
                {
                    acc = new BalanceAccumulator();
                    balancesDict[key] = acc;
                }
                return acc;
            }

            foreach (var i in invLines)
            {
                bool isInc = i.Type.Equals("Purchase", StringComparison.OrdinalIgnoreCase) || i.Type.Equals("SalesReturn", StringComparison.OrdinalIgnoreCase);
                var acc = GetOrCreate(i.Wh, i.St);
                if (isInc) acc.Income += i.Qty;
                else acc.Expense += i.Qty;
            }

            foreach (var s in slipLines)
            {
                string t = s.Type ?? "StockOpening";
                bool isInc = t.Equals("StockOpening", StringComparison.OrdinalIgnoreCase) || t.Equals("RevisionExceed", StringComparison.OrdinalIgnoreCase);
                var acc = GetOrCreate(s.Wh, s.St);
                if (isInc) acc.Income += s.Qty;
                else acc.Expense += s.Qty;
            }

            foreach (var tr in trLines)
            {
                if (tr.SrcWh.HasValue && (!whIds.Any() || whIds.Contains(tr.SrcWh.Value)))
                {
                    var acc = GetOrCreate(tr.SrcWh.Value, tr.St);
                    acc.Expense += tr.QtySent;
                }

                if (tr.DstWh.HasValue && (!whIds.Any() || whIds.Contains(tr.DstWh.Value)))
                {
                    var acc = GetOrCreate(tr.DstWh.Value, tr.St);
                    acc.Income += tr.QtyRec;
                }
            }

            var result = balancesDict
                .Select(kvp => new
                {
                    WarehouseId = kvp.Key.Wh.ToString(),
                    StockId = kvp.Key.St.ToString(),
                    Income = kvp.Value.Income,
                    Expense = kvp.Value.Expense,
                    Balance = kvp.Value.Income - kvp.Value.Expense
                })
                .Where(x => x.Balance != 0m || x.Income != 0m || x.Expense != 0m)
                .ToList();

            return Results.Ok(result);
        });


        // 2. ОТЧЕТ ПО ТИПАМ ДОКУМЕНТОВ (STOCK BALANCES BY TYPE)
        group.MapGet("/by-type", async (HttpRequest req, MermerDbContext db, CancellationToken ct) =>
        {
            DateTimeOffset dateFrom = DateTimeOffset.MinValue;
            DateTimeOffset dateTill = DateTimeOffset.MaxValue;
            bool isAllTime = true;

            string? fromStr = req.Query["dateFrom"].FirstOrDefault();
            if (!string.IsNullOrEmpty(fromStr) && DateTimeOffset.TryParse(fromStr.Replace(" ", "+"), out var pFrom))
            {
                if (pFrom.Year > 2000)
                {
                    dateFrom = pFrom.ToUniversalTime();
                    isAllTime = false;
                }
            }

            string? tillStr = req.Query["dateTill"].FirstOrDefault();
            if (!string.IsNullOrEmpty(tillStr) && DateTimeOffset.TryParse(tillStr.Replace(" ", "+"), out var pTill))
            {
                if (pTill.Year < 2099)
                {
                    if (pTill.TimeOfDay == TimeSpan.Zero)
                        dateTill = pTill.Date.AddDays(1).AddTicks(-1).ToUniversalTime();
                    else
                        dateTill = pTill.ToUniversalTime();
                }
            }

            if ((dateTill - dateFrom).TotalDays > 7000)
            {
                isAllTime = true;
            }

            bool aggregate = bool.TryParse(req.Query["aggregate"].FirstOrDefault(), out var agg) && agg;
            string? stockIdStr = req.Query["stockId"].FirstOrDefault();
            Guid? filterStockGuid = Guid.TryParse(stockIdStr, out var sG) ? sG : null;

            // Надежный парсинг Guid складов
            var whIds = new HashSet<Guid>();
            foreach (var val in req.Query["warehouseId"])
            {
                if (string.IsNullOrWhiteSpace(val)) continue;
                foreach (var part in val.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (Guid.TryParse(part.Trim(), out var parsedWh))
                        whIds.Add(parsedWh);
                }
            }

            // 1. Инвойсы
            var invQuery = db.InvoiceLines.AsNoTracking()
                .Where(l => l.StockId.HasValue && l.Invoice != null && !l.Invoice.IsDisabled && l.Invoice.WarehouseId.HasValue);

            if (whIds.Any())
                invQuery = invQuery.Where(l => whIds.Contains(l.Invoice.WarehouseId!.Value));
            if (filterStockGuid.HasValue)
                invQuery = invQuery.Where(l => l.StockId == filterStockGuid.Value);

            var invData = await invQuery.Select(l => new
            {
                WarehouseId = l.Invoice.WarehouseId!.Value,
                StockId = l.StockId!.Value,
                Date = l.Invoice.Date,
                Type = l.Invoice.InvoiceType,
                Qty = l.Quantity
            }).ToListAsync(ct);

            // 2. Складские ордера
            var slipQuery = db.StockSlipLines.AsNoTracking()
                .Where(l => l.StockId.HasValue && l.StockSlip != null && l.StockSlip.WarehouseId.HasValue);

            if (whIds.Any())
                slipQuery = slipQuery.Where(l => whIds.Contains(l.StockSlip.WarehouseId!.Value));
            if (filterStockGuid.HasValue)
                slipQuery = slipQuery.Where(l => l.StockId == filterStockGuid.Value);

            var slipData = await slipQuery.Select(l => new
            {
                WarehouseId = l.StockSlip.WarehouseId!.Value,
                StockId = l.StockId!.Value,
                Date = l.StockSlip.Date,
                Type = l.StockSlip.SlipType,
                Qty = l.Quantity
            }).ToListAsync(ct);

            // 3. Перемещения
            var trQuery = db.StockTransferLines.AsNoTracking()
                .Where(l => l.StockId.HasValue && l.StockTransfer != null && !l.StockTransfer.IsDisabled);

            if (whIds.Any())
            {
                trQuery = trQuery.Where(l =>
                    (l.StockTransfer.WarehouseId.HasValue && whIds.Contains(l.StockTransfer.WarehouseId.Value)) ||
                    (l.StockTransfer.DestinationWarehouseId.HasValue && whIds.Contains(l.StockTransfer.DestinationWarehouseId.Value)));
            }
            if (filterStockGuid.HasValue)
                trQuery = trQuery.Where(l => l.StockId == filterStockGuid.Value);

            var trData = await trQuery.Select(l => new
            {
                SrcWhId = l.StockTransfer.WarehouseId,
                DstWhId = l.StockTransfer.DestinationWarehouseId,
                StockId = l.StockId!.Value,
                Date = l.StockTransfer.Date,
                QtySent = l.Quantity,
                QtyRec = l.ReceivedQuantity != 0m ? l.ReceivedQuantity : l.Quantity
            }).ToListAsync(ct);

            // 4. Формирование движений
            var movements = new List<StockMovementRecord>();

            foreach (var i in invData)
            {
                if (whIds.Any() && !whIds.Contains(i.WarehouseId)) continue;
                string t = i.Type ?? "Sales";
                bool isInc = t.Equals("Purchase", StringComparison.OrdinalIgnoreCase) || t.Equals("SalesReturn", StringComparison.OrdinalIgnoreCase);
                movements.Add(new StockMovementRecord(i.WarehouseId, i.StockId, i.Date.ToUniversalTime(), t, isInc ? i.Qty : 0m, !isInc ? i.Qty : 0m));
            }

            foreach (var s in slipData)
            {
                if (whIds.Any() && !whIds.Contains(s.WarehouseId)) continue;
                string t = s.Type ?? "StockOpening";
                bool isInc = t.Equals("StockOpening", StringComparison.OrdinalIgnoreCase) || t.Equals("RevisionExceed", StringComparison.OrdinalIgnoreCase);
                movements.Add(new StockMovementRecord(s.WarehouseId, s.StockId, s.Date.ToUniversalTime(), t, isInc ? s.Qty : 0m, !isInc ? s.Qty : 0m));
            }

            foreach (var tr in trData)
            {
                // Для выбранных складов фиксируем расход только если склад-отправитель входит в выборку
                if (tr.SrcWhId.HasValue && (!whIds.Any() || whIds.Contains(tr.SrcWhId.Value)))
                    movements.Add(new StockMovementRecord(tr.SrcWhId.Value, tr.StockId, tr.Date.ToUniversalTime(), "StockTransferSource", 0m, tr.QtySent));

                // Фиксируем приход только если склад-получатель входит в выборку
                if (tr.DstWhId.HasValue && (!whIds.Any() || whIds.Contains(tr.DstWhId.Value)))
                    movements.Add(new StockMovementRecord(tr.DstWhId.Value, tr.StockId, tr.Date.ToUniversalTime(), "StockTransferDestination", tr.QtyRec, 0m));
            }

            if (!movements.Any()) return Results.Ok(Array.Empty<object>());

            // 5. Загрузка карточек
            var stocksRaw = await db.Stocks.AsNoTracking()
                .Where(s => !s.IsDisabled)
                .Select(s => new
                {
                    s.Id,
                    s.Code,
                    s.Name,
                    s.ShortName,
                    s.Type,
                    s.Group,
                    s.Tags,
                    UnitName = s.Units.Where(u => u.IsDefault).Select(u => u.Name).FirstOrDefault()
                               ?? s.Units.Select(u => u.Name).FirstOrDefault() ?? "",
                    Price = s.Prices.OrderByDescending(p => p.ValidFrom).Select(p => p.Price).FirstOrDefault(),
                    CurrencyId = s.Prices.OrderByDescending(p => p.ValidFrom).Select(p => p.CurrencyId.ToString()).FirstOrDefault()
                })
                .ToListAsync(ct);

            var stocksDict = stocksRaw.ToDictionary(s => s.Id);
            var result = new List<object>();

            if (aggregate)
            {
                var grouped = movements.GroupBy(m => m.StockId);

                foreach (var g in grouped)
                {
                    if (!stocksDict.TryGetValue(g.Key, out var stock)) continue;

                    decimal starting = 0m;
                    decimal inc = 0m;
                    decimal exp = 0m;
                    List<StockMovementRecord> periodMovs;

                    if (isAllTime)
                    {
                        periodMovs = g.ToList();
                        inc = periodMovs.Sum(m => m.Income);
                        exp = periodMovs.Sum(m => m.Expense);
                    }
                    else
                    {
                        starting = g.Where(m => m.Date < dateFrom).Sum(m => m.Income - m.Expense);
                        periodMovs = g.Where(m => m.Date >= dateFrom && m.Date <= dateTill).ToList();
                        inc = periodMovs.Sum(m => m.Income);
                        exp = periodMovs.Sum(m => m.Expense);
                    }

                    decimal resulting = starting + inc - exp;

                    if (isAllTime)
                    {
                        if (starting == 0m && inc == 0m && exp == 0m && resulting == 0m)
                            continue;
                    }
                    else
                    {
                        // Для периодов дат: отображаем ТОЛЬКО товары с движениями за период
                        if (inc == 0m && exp == 0m)
                            continue;
                    }

                    result.Add(new
                    {
                        WarehouseId = (string?)null,
                        StockId = stock.Id.ToString(),
                        StockCode = stock.Code ?? "",
                        StockName = stock.Name ?? "",
                        StockShortName = stock.ShortName ?? "",
                        StockUnit = stock.UnitName,
                        Unit = stock.UnitName,
                        StockPrice = stock.Price,
                        Price = stock.Price,
                        StockCurrencyId = stock.CurrencyId ?? "",
                        CurrencyId = stock.CurrencyId ?? "",
                        StockType = stock.Type ?? "",
                        StockGroup = stock.Group ?? "",
                        StockTags = stock.Tags ?? Array.Empty<string>(),

                        StartingBalance = starting,
                        Income = inc,
                        Expense = exp,
                        Balance = resulting,
                        ResultingBalance = resulting,

                        StockOpening = periodMovs.Where(m => m.Type.Equals("StockOpening", StringComparison.OrdinalIgnoreCase)).Sum(m => m.Income),
                        RevisionExceed = periodMovs.Where(m => m.Type.Equals("RevisionExceed", StringComparison.OrdinalIgnoreCase)).Sum(m => m.Income),
                        StockTransferDestination = periodMovs.Where(m => m.Type.Equals("StockTransferDestination", StringComparison.OrdinalIgnoreCase)).Sum(m => m.Income),
                        SalesReturn = periodMovs.Where(m => m.Type.Equals("SalesReturn", StringComparison.OrdinalIgnoreCase)).Sum(m => m.Income),
                        Purchase = periodMovs.Where(m => m.Type.Equals("Purchase", StringComparison.OrdinalIgnoreCase)).Sum(m => m.Income),

                        StockSpoilage = periodMovs.Where(m => m.Type.Equals("StockSpoilage", StringComparison.OrdinalIgnoreCase)).Sum(m => m.Expense),
                        StockUsage = periodMovs.Where(m => m.Type.Equals("StockUsage", StringComparison.OrdinalIgnoreCase)).Sum(m => m.Expense),
                        RevisionDeficit = periodMovs.Where(m => m.Type.Equals("RevisionDeficit", StringComparison.OrdinalIgnoreCase)).Sum(m => m.Expense),
                        StockTransferSource = periodMovs.Where(m => m.Type.Equals("StockTransferSource", StringComparison.OrdinalIgnoreCase)).Sum(m => m.Expense),
                        Sales = periodMovs.Where(m => m.Type.Equals("Sales", StringComparison.OrdinalIgnoreCase)).Sum(m => m.Expense),
                        PurchaseReturn = periodMovs.Where(m => m.Type.Equals("PurchaseReturn", StringComparison.OrdinalIgnoreCase)).Sum(m => m.Expense)
                    });
                }
            }
            else
            {
                var grouped = movements.GroupBy(m => new { m.WarehouseId, m.StockId });

                foreach (var g in grouped)
                {
                    if (whIds.Any() && !whIds.Contains(g.Key.WarehouseId)) continue;
                    if (!stocksDict.TryGetValue(g.Key.StockId, out var stock)) continue;

                    decimal starting = 0m;
                    decimal inc = 0m;
                    decimal exp = 0m;
                    List<StockMovementRecord> periodMovs;

                    if (isAllTime)
                    {
                        periodMovs = g.ToList();
                        inc = periodMovs.Sum(m => m.Income);
                        exp = periodMovs.Sum(m => m.Expense);
                    }
                    else
                    {
                        starting = g.Where(m => m.Date < dateFrom).Sum(m => m.Income - m.Expense);
                        periodMovs = g.Where(m => m.Date >= dateFrom && m.Date <= dateTill).ToList();
                        inc = periodMovs.Sum(m => m.Income);
                        exp = periodMovs.Sum(m => m.Expense);
                    }

                    decimal resulting = starting + inc - exp;

                    if (isAllTime)
                    {
                        if (starting == 0m && inc == 0m && exp == 0m && resulting == 0m)
                            continue;
                    }
                    else
                    {
                        if (inc == 0m && exp == 0m)
                            continue;
                    }

                    result.Add(new
                    {
                        WarehouseId = g.Key.WarehouseId.ToString(),
                        StockId = stock.Id.ToString(),
                        StockCode = stock.Code ?? "",
                        StockName = stock.Name ?? "",
                        StockShortName = stock.ShortName ?? "",
                        StockUnit = stock.UnitName,
                        Unit = stock.UnitName,
                        StockPrice = stock.Price,
                        Price = stock.Price,
                        StockCurrencyId = stock.CurrencyId ?? "",
                        CurrencyId = stock.CurrencyId ?? "",
                        StockType = stock.Type ?? "",
                        StockGroup = stock.Group ?? "",
                        StockTags = stock.Tags ?? Array.Empty<string>(),

                        StartingBalance = starting,
                        Income = inc,
                        Expense = exp,
                        Balance = resulting,
                        ResultingBalance = resulting,

                        StockOpening = periodMovs.Where(m => m.Type.Equals("StockOpening", StringComparison.OrdinalIgnoreCase)).Sum(m => m.Income),
                        RevisionExceed = periodMovs.Where(m => m.Type.Equals("RevisionExceed", StringComparison.OrdinalIgnoreCase)).Sum(m => m.Income),
                        StockTransferDestination = periodMovs.Where(m => m.Type.Equals("StockTransferDestination", StringComparison.OrdinalIgnoreCase)).Sum(m => m.Income),
                        SalesReturn = periodMovs.Where(m => m.Type.Equals("SalesReturn", StringComparison.OrdinalIgnoreCase)).Sum(m => m.Income),
                        Purchase = periodMovs.Where(m => m.Type.Equals("Purchase", StringComparison.OrdinalIgnoreCase)).Sum(m => m.Income),

                        StockSpoilage = periodMovs.Where(m => m.Type.Equals("StockSpoilage", StringComparison.OrdinalIgnoreCase)).Sum(m => m.Expense),
                        StockUsage = periodMovs.Where(m => m.Type.Equals("StockUsage", StringComparison.OrdinalIgnoreCase)).Sum(m => m.Expense),
                        RevisionDeficit = periodMovs.Where(m => m.Type.Equals("RevisionDeficit", StringComparison.OrdinalIgnoreCase)).Sum(m => m.Expense),
                        StockTransferSource = periodMovs.Where(m => m.Type.Equals("StockTransferSource", StringComparison.OrdinalIgnoreCase)).Sum(m => m.Expense),
                        Sales = periodMovs.Where(m => m.Type.Equals("Sales", StringComparison.OrdinalIgnoreCase)).Sum(m => m.Expense),
                        PurchaseReturn = periodMovs.Where(m => m.Type.Equals("PurchaseReturn", StringComparison.OrdinalIgnoreCase)).Sum(m => m.Expense)
                    });
                }
            }

            return Results.Ok(result.OrderBy(x => ((dynamic)x).StockName));
        });

        // 3. ОТЧЕТ ПО СКЛАДАМ НА ДАТУ (Легкий плоский запрос)
        group.MapGet("/by-date-warehouses", async (HttpRequest req, MermerDbContext db, CancellationToken ct) =>
        {
            DateTimeOffset date = DateTimeOffset.UtcNow;
            string? dateStr = req.Query["date"].FirstOrDefault();
            if (!string.IsNullOrEmpty(dateStr) && DateTimeOffset.TryParse(dateStr.Replace(" ", "+"), out var pDate))
                date = pDate.ToUniversalTime();

            string? displayCurrencyId = req.Query["displayCurrencyId"].FirstOrDefault();
            Guid? displayCurrGuid = Guid.TryParse(displayCurrencyId, out var dcG) ? dcG : null;

            var whIds = new HashSet<Guid>();
            foreach (var val in req.Query["warehouseId"])
            {
                if (string.IsNullOrWhiteSpace(val)) continue;
                foreach (var part in val.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (Guid.TryParse(part.Trim(), out var parsedWh))
                        whIds.Add(parsedWh);
                }
            }

            var stockIds = new HashSet<Guid>();
            foreach (var val in req.Query["stockId"])
            {
                if (string.IsNullOrWhiteSpace(val)) continue;
                foreach (var part in val.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (Guid.TryParse(part.Trim(), out var parsedSt))
                        stockIds.Add(parsedSt);
                }
            }

            // Читаем движения аналогично методу /
            var invQuery = db.InvoiceLines.AsNoTracking()
                .Where(l => l.StockId.HasValue && l.Invoice != null && l.Invoice.IsCompleted && !l.Invoice.IsDisabled && l.Invoice.WarehouseId.HasValue && l.Invoice.Date <= date);

            if (whIds.Any()) invQuery = invQuery.Where(l => whIds.Contains(l.Invoice.WarehouseId!.Value));
            if (stockIds.Any()) invQuery = invQuery.Where(l => stockIds.Contains(l.StockId!.Value));

            var invLines = await invQuery.Select(l => new
            {
                Wh = l.Invoice.WarehouseId!.Value,
                St = l.StockId!.Value,
                Type = l.Invoice.InvoiceType,
                Qty = l.Quantity
            }).ToListAsync(ct);

            var slipQuery = db.StockSlipLines.AsNoTracking()
                .Where(l => l.StockId.HasValue && l.StockSlip != null && l.StockSlip.IsCompleted && l.StockSlip.WarehouseId.HasValue && l.StockSlip.Date <= date);

            if (whIds.Any()) slipQuery = slipQuery.Where(l => whIds.Contains(l.StockSlip.WarehouseId!.Value));
            if (stockIds.Any()) slipQuery = slipQuery.Where(l => stockIds.Contains(l.StockId!.Value));

            var slipLines = await slipQuery.Select(l => new
            {
                Wh = l.StockSlip.WarehouseId!.Value,
                St = l.StockId!.Value,
                Type = l.StockSlip.SlipType,
                Qty = l.Quantity
            }).ToListAsync(ct);

            var trQuery = db.StockTransferLines.AsNoTracking()
                .Where(l => l.StockId.HasValue && l.StockTransfer != null && l.StockTransfer.IsCompleted && !l.StockTransfer.IsDisabled && l.StockTransfer.Date <= date);

            if (whIds.Any())
            {
                trQuery = trQuery.Where(l =>
                    (l.StockTransfer.WarehouseId.HasValue && whIds.Contains(l.StockTransfer.WarehouseId.Value)) ||
                    (l.StockTransfer.DestinationWarehouseId.HasValue && whIds.Contains(l.StockTransfer.DestinationWarehouseId.Value)));
            }
            if (stockIds.Any()) trQuery = trQuery.Where(l => stockIds.Contains(l.StockId!.Value));

            var trLines = await trQuery.Select(l => new
            {
                SrcWh = l.StockTransfer.WarehouseId,
                DstWh = l.StockTransfer.DestinationWarehouseId,
                St = l.StockId!.Value,
                QtySent = l.Quantity,
                QtyRec = l.ReceivedQuantity != 0m ? l.ReceivedQuantity : l.Quantity
            }).ToListAsync(ct);

            var balancesDict = new Dictionary<(Guid Wh, Guid St), decimal>();

            foreach (var i in invLines)
            {
                bool isInc = i.Type.Equals("Purchase", StringComparison.OrdinalIgnoreCase) || i.Type.Equals("SalesReturn", StringComparison.OrdinalIgnoreCase);
                var key = (i.Wh, i.St);
                balancesDict[key] = (balancesDict.TryGetValue(key, out var b) ? b : 0m) + (isInc ? i.Qty : -i.Qty);
            }

            foreach (var s in slipLines)
            {
                string t = s.Type ?? "StockOpening";
                bool isInc = t.Equals("StockOpening", StringComparison.OrdinalIgnoreCase) || t.Equals("RevisionExceed", StringComparison.OrdinalIgnoreCase);
                var key = (s.Wh, s.St);
                balancesDict[key] = (balancesDict.TryGetValue(key, out var b) ? b : 0m) + (isInc ? s.Qty : -s.Qty);
            }

            foreach (var tr in trLines)
            {
                if (tr.SrcWh.HasValue && (!whIds.Any() || whIds.Contains(tr.SrcWh.Value)))
                {
                    var key = (tr.SrcWh.Value, tr.St);
                    balancesDict[key] = (balancesDict.TryGetValue(key, out var b) ? b : 0m) - tr.QtySent;
                }

                if (tr.DstWh.HasValue && (!whIds.Any() || whIds.Contains(tr.DstWh.Value)))
                {
                    var key = (tr.DstWh.Value, tr.St);
                    balancesDict[key] = (balancesDict.TryGetValue(key, out var b) ? b : 0m) + tr.QtyRec;
                }
            }

            var validStocks = balancesDict.Where(x => x.Value != 0m).Select(x => x.Key.St).Distinct().ToHashSet();
            if (stockIds.Any()) validStocks.UnionWith(stockIds);

            if (!validStocks.Any()) return Results.Ok(Array.Empty<object>());

            var stocks = await db.Stocks.AsNoTracking()
                .Where(s => validStocks.Contains(s.Id) && !s.IsDisabled)
                .Select(s => new
                {
                    s.Id,
                    s.Code,
                    s.Name,
                    s.ShortName,
                    s.Type,
                    s.Group,
                    s.Tags,
                    UnitName = s.Units.Where(u => u.IsDefault).Select(u => u.Name).FirstOrDefault()
                               ?? s.Units.Select(u => u.Name).FirstOrDefault() ?? "",
                    Price = s.Prices.Where(p => p.ValidFrom <= date).OrderByDescending(p => p.ValidFrom).Select(p => p.Price).FirstOrDefault(),
                    CurrencyId = s.Prices.Where(p => p.ValidFrom <= date).OrderByDescending(p => p.ValidFrom).Select(p => (Guid?)p.CurrencyId).FirstOrDefault()
                })
                .ToListAsync(ct);

            var currencies = await db.Currencies.AsNoTracking().ToListAsync(ct);
            var rates = await db.CurrencyRates.AsNoTracking().ToListAsync(ct);

            var displayCurrency = displayCurrGuid.HasValue
                ? currencies.FirstOrDefault(c => c.Id == displayCurrGuid.Value)
                : (currencies.FirstOrDefault(c => c.IsDefault) ?? currencies.FirstOrDefault());

            var dispRate = displayCurrency != null
                ? rates.Where(r => r.CurrencyId == displayCurrency.Id && r.ValidFrom <= date).OrderByDescending(r => r.ValidFrom).FirstOrDefault()
                : null;

            decimal dispMult = dispRate?.Multiplier ?? 1m;
            decimal dispDiv = dispRate?.Divider ?? 1m;
            int dispDecimals = displayCurrency?.Decimals ?? 2;

            var result = stocks.Select(stock =>
            {
                var stockBals = balancesDict
                    .Where(b => b.Key.St == stock.Id && (!whIds.Any() || whIds.Contains(b.Key.Wh)))
                    .ToDictionary(b => b.Key.Wh.ToString(), b => b.Value);

                if (!stockBals.Any() && !stockIds.Contains(stock.Id)) return null;

                decimal convertedPrice = 0m;
                if (stock.CurrencyId.HasValue && stock.Price > 0m)
                {
                    var currRate = rates.Where(r => r.CurrencyId == stock.CurrencyId.Value && r.ValidFrom <= date).OrderByDescending(r => r.ValidFrom).FirstOrDefault();
                    decimal currMult = currRate?.Multiplier ?? 1m;
                    decimal currDiv = currRate?.Divider ?? 1m;

                    if (currDiv != 0 && dispMult != 0)
                    {
                        convertedPrice = Math.Round(stock.Price * currMult / currDiv / dispMult * dispDiv, dispDecimals);
                    }
                }

                return new
                {
                    StockId = stock.Id.ToString(),
                    StockCode = stock.Code ?? "",
                    StockName = stock.Name ?? "",
                    StockShortName = stock.ShortName ?? "",
                    StockUnit = stock.UnitName,
                    StockPrice = convertedPrice,
                    StockPriceCurrencyId = displayCurrency?.Id.ToString() ?? "",
                    StockType = stock.Type ?? "",
                    StockGroup = stock.Group ?? "",
                    StockTags = stock.Tags != null ? string.Join(" ", stock.Tags) : "",
                    Balances = stockBals
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


    private class BalanceAccumulator
    {
        public decimal Income { get; set; }
        public decimal Expense { get; set; }
    }
    private record StockMovementRecord(Guid WarehouseId, Guid StockId, DateTimeOffset Date, string Type, decimal Income, decimal Expense);
}
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

public static class StockRepriceEndpoints
{
    public static IEndpointRouteBuilder MapStockRepriceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/stock-reprice-effects").WithTags("StockReprice");

        // 1. БЫСТРЫЙ ПОДСЧЕТ КОЛИЧЕСТВА СОБЫТИЙ (БЕЗ ВЫГРУЗКИ ВСЕЙ БАЗЫ В ПАМЯТЬ)
        group.MapGet("/count", async (HttpRequest req, MermerDbContext db, CancellationToken ct) =>
        {
            var (fUtc, tUtc) = ParseDates(req);

            var priceEventsCount = await db.StockPrices
                .AsNoTracking()
                .Where(p => p.ValidFrom >= fUtc && p.ValidFrom <= tUtc)
                .Select(p => p.ValidFrom)
                .Distinct()
                .CountAsync(ct);

            return Results.Ok(priceEventsCount);
        });

        // 2. ДАТЫ ПЕРЕОЦЕНОК (ТОЧЕЧНАЯ ВЫБОРКА ИЗ ТАБЛИЦЫ ЦЕН И КУРСОВ)
        group.MapGet("/dates", async (HttpRequest req, MermerDbContext db, CancellationToken ct) =>
        {
            var (fUtc, tUtc) = ParseDates(req);

            var priceDates = await db.StockPrices
                .AsNoTracking()
                .Where(p => p.ValidFrom >= fUtc && p.ValidFrom <= tUtc)
                .Select(p => p.ValidFrom)
                .Distinct()
                .ToListAsync(ct);

            var rateDates = await db.CurrencyRates
                .AsNoTracking()
                .Where(r => r.ValidFrom >= fUtc && r.ValidFrom <= tUtc)
                .Select(r => r.ValidFrom)
                .Distinct()
                .ToListAsync(ct);

            var allDates = priceDates.Union(rateDates)
                .OrderByDescending(d => d)
                .Select(d => DateTime.SpecifyKind(d, DateTimeKind.Utc))
                .ToList();

            return Results.Ok(allDates);
        });

        // 3. ПОЛНЫЙ РАСЧЕТ ЭФФЕКТОВ (С ПАГИНАЦИЕЙ И ЗАЩИТОЙ ПО ПАМЯТИ)
        group.MapGet("/", async (HttpRequest req, MermerDbContext db, CancellationToken ct) =>
        {
            var effects = await CalculateEffectsAsync(req, db, ct);
            return Results.Ok(effects);
        });

        return app;
    }

    private static (DateTime from, DateTime till) ParseDates(HttpRequest req)
    {
        DateTime fUtc = DateTime.UtcNow.AddMonths(-1).Date;
        DateTime tUtc = DateTime.UtcNow.Date;

        string? fromStr = req.Query["from"].FirstOrDefault();
        if (!string.IsNullOrEmpty(fromStr) && DateTime.TryParse(fromStr.Replace(" ", "+"), out var pf))
            fUtc = DateTime.SpecifyKind(pf.Date, DateTimeKind.Utc);

        string? tillStr = req.Query["till"].FirstOrDefault();
        if (!string.IsNullOrEmpty(tillStr) && DateTime.TryParse(tillStr.Replace(" ", "+"), out var pt))
            tUtc = DateTime.SpecifyKind(pt.Date, DateTimeKind.Utc);

        return (fUtc, tUtc);
    }

    private static async Task<List<StockRepriceEffectDto>> CalculateEffectsAsync(HttpRequest req, MermerDbContext db, CancellationToken ct)
    {
        var (fUtc, tUtc) = ParseDates(req);
        int limit = int.TryParse(req.Query["limit"], out var l) ? Math.Clamp(l, 1, 1000) : 300;

        var whIds = req.Query["warehouseId"]
            .Select(x => Guid.TryParse(x, out var g) ? (Guid?)g : null)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .ToList();

        var currencies = await db.Currencies.AsNoTracking().Where(c => !c.IsDisabled).ToListAsync(ct);
        var rates = await db.CurrencyRates.AsNoTracking().ToListAsync(ct);

        decimal GetRateValue(Guid currId, DateTime date)
        {
            var rate = rates.Where(r => r.CurrencyId == currId && r.ValidFrom <= date)
                            .OrderByDescending(r => r.ValidFrom)
                            .FirstOrDefault();
            if (rate == null) return 1m;
            return rate.Divider == 0 ? 0 : rate.Multiplier / rate.Divider;
        }

        var affectedStockIds = new HashSet<Guid>();

        // 1. Прямые изменения цен за период
        var priceChanges = await db.StockPrices
            .Where(p => p.ValidFrom >= fUtc && p.ValidFrom <= tUtc)
            .AsNoTracking()
            .ToListAsync(ct);

        foreach (var p in priceChanges)
            affectedStockIds.Add(p.StockId);

        // 2. Изменения курсов валют
        var rateChanges = rates.Where(r => r.ValidFrom >= fUtc && r.ValidFrom <= tUtc).OrderBy(r => r.ValidFrom).ToList();
        var allPricesForRates = await db.StockPrices.AsNoTracking().ToListAsync(ct);

        var rateChangeEvents = new List<RateChangeEvent>();

        foreach (var rc in rateChanges)
        {
            var prevRate = rates.Where(r => r.CurrencyId == rc.CurrencyId && r.ValidFrom < rc.ValidFrom)
                                .OrderByDescending(r => r.ValidFrom)
                                .FirstOrDefault();
            if (prevRate == null) continue;

            decimal prevDiv = prevRate.Divider == 0 ? 1 : prevRate.Divider;
            decimal currDiv = rc.Divider == 0 ? 1 : rc.Divider;
            decimal diff = (rc.Multiplier / currDiv) - (prevRate.Multiplier / prevDiv);
            if (diff == 0) continue;

            var stocksUsingCurr = allPricesForRates
                .Where(p => p.CurrencyId == rc.CurrencyId && p.ValidFrom <= rc.ValidFrom)
                .GroupBy(p => p.StockId)
                .Select(g => g.OrderByDescending(p => p.ValidFrom).First())
                .ToList();

            foreach (var sp in stocksUsingCurr)
            {
                affectedStockIds.Add(sp.StockId);
                rateChangeEvents.Add(new RateChangeEvent
                {
                    StockId = sp.StockId,
                    Date = rc.ValidFrom,
                    PriceChange = Math.Round(sp.Price * diff, 2)
                });
            }
        }

        var sIds = affectedStockIds.ToList();
        var results = new List<StockRepriceEffectDto>();
        if (!sIds.Any()) return results;

        // 3. Поднимаем историю движений строго для затронутых товаров
        var allMovements = new List<Movement>();

        var invs = await db.InvoiceLines
            .Where(l => l.StockId.HasValue && sIds.Contains(l.StockId.Value) && l.Invoice.IsCompleted && !l.Invoice.IsDisabled && l.Invoice.Date <= tUtc)
            .Select(l => new { Wh = l.Invoice.WarehouseId, St = l.StockId, Dt = l.Invoice.Date, Qty = l.Quantity, Type = l.Invoice.InvoiceType })
            .AsNoTracking()
            .ToListAsync(ct);

        foreach (var i in invs)
        {
            if (!i.Wh.HasValue || !i.St.HasValue) continue;
            bool isInc = i.Type == "Purchase" || i.Type == "SalesReturn";
            allMovements.Add(new Movement { Wh = i.Wh.Value, St = i.St.Value, Dt = i.Dt.UtcDateTime, Qty = isInc ? i.Qty : -i.Qty });
        }

        var slips = await db.StockSlipLines
            .Where(l => l.StockId.HasValue && sIds.Contains(l.StockId.Value) && l.StockSlip.IsCompleted && l.StockSlip.Date <= tUtc)
            .Select(l => new { Wh = l.StockSlip.WarehouseId, St = l.StockId, Dt = l.StockSlip.Date, Qty = l.Quantity, Type = l.StockSlip.SlipType })
            .AsNoTracking()
            .ToListAsync(ct);

        foreach (var s in slips)
        {
            if (!s.Wh.HasValue || !s.St.HasValue) continue;
            bool isInc = s.Type == "StockOpening" || s.Type == "RevisionExceed";
            allMovements.Add(new Movement { Wh = s.Wh.Value, St = s.St.Value, Dt = s.Dt.UtcDateTime, Qty = isInc ? s.Qty : -s.Qty });
        }

        var trs = await db.StockTransferLines
            .Where(l => l.StockId.HasValue && sIds.Contains(l.StockId.Value) && l.StockTransfer.IsCompleted && !l.StockTransfer.IsDisabled && l.StockTransfer.Date <= tUtc)
            .Select(l => new { WhFrom = l.StockTransfer.WarehouseId, WhTo = l.StockTransfer.DestinationWarehouseId, St = l.StockId, Dt = l.StockTransfer.Date, Qty = l.Quantity, RecQty = l.ReceivedQuantity })
            .AsNoTracking()
            .ToListAsync(ct);

        foreach (var tr in trs)
        {
            if (!tr.St.HasValue) continue;
            if (tr.WhFrom.HasValue) allMovements.Add(new Movement { Wh = tr.WhFrom.Value, St = tr.St.Value, Dt = tr.Dt.UtcDateTime, Qty = -tr.Qty });
            if (tr.WhTo.HasValue) allMovements.Add(new Movement { Wh = tr.WhTo.Value, St = tr.St.Value, Dt = tr.Dt.UtcDateTime, Qty = tr.RecQty });
        }

        // Индексация движений в памяти по (StockId, WarehouseId) для устранения O(N*M) фриза
        var movesLookup = allMovements
            .GroupBy(m => (m.St, m.Wh))
            .ToDictionary(g => g.Key, g => g.OrderBy(m => m.Dt).ToList());

        decimal GetBal(Guid stockId, Guid warehouseId, DateTime date)
        {
            if (movesLookup.TryGetValue((stockId, warehouseId), out var moves))
            {
                decimal sum = 0m;
                for (int i = 0; i < moves.Count; i++)
                {
                    if (moves[i].Dt > date) break;
                    sum += moves[i].Qty;
                }
                return sum;
            }
            return 0m;
        }

        var stocksDict = await db.Stocks
            .Where(s => sIds.Contains(s.Id))
            .AsNoTracking()
            .ToDictionaryAsync(s => s.Id, s => s, ct);

        var targetWhIds = whIds.Any() ? whIds : allMovements.Select(m => m.Wh).Distinct().ToList();
        var allPricesList = await db.StockPrices.Where(p => sIds.Contains(p.StockId)).AsNoTracking().ToListAsync(ct);

        // Переоценка цен товаров
        foreach (var nextP in priceChanges)
        {
            var prevP = allPricesList.Where(p => p.StockId == nextP.StockId && p.ValidFrom < nextP.ValidFrom)
                                     .OrderByDescending(p => p.ValidFrom)
                                     .FirstOrDefault();
            if (prevP == null || !prevP.CurrencyId.HasValue || !nextP.CurrencyId.HasValue) continue;

            decimal r1 = GetRateValue(prevP.CurrencyId.Value, nextP.ValidFrom);
            decimal r2 = GetRateValue(nextP.CurrencyId.Value, nextP.ValidFrom);

            decimal diff = Math.Round((nextP.Price * r2) - (prevP.Price * r1), 2);
            if (diff == 0) continue;

            stocksDict.TryGetValue(nextP.StockId, out var stock);

            foreach (var w in targetWhIds)
            {
                decimal bal = GetBal(nextP.StockId, w, nextP.ValidFrom);
                if (bal == 0) continue;

                results.Add(new StockRepriceEffectDto
                {
                    StockId = nextP.StockId.ToString(),
                    StockCode = stock?.Code ?? "",
                    StockName = stock?.Name ?? "",
                    PriceChange = diff,
                    ChangeDate = DateTime.SpecifyKind(nextP.ValidFrom, DateTimeKind.Utc),
                    ChangeReason = 0, // PriceChanged
                    WarehouseId = w.ToString(),
                    Balance = bal
                });
            }
        }

        // Переоценка по изменению курсов валют
        foreach (var rcEvt in rateChangeEvents)
        {
            stocksDict.TryGetValue(rcEvt.StockId, out var stock);

            foreach (var w in targetWhIds)
            {
                decimal bal = GetBal(rcEvt.StockId, w, rcEvt.Date);
                if (bal == 0) continue;

                results.Add(new StockRepriceEffectDto
                {
                    StockId = rcEvt.StockId.ToString(),
                    StockCode = stock?.Code ?? "",
                    StockName = stock?.Name ?? "",
                    PriceChange = rcEvt.PriceChange,
                    ChangeDate = DateTime.SpecifyKind(rcEvt.Date, DateTimeKind.Utc),
                    ChangeReason = 1, // RateChanged
                    WarehouseId = w.ToString(),
                    Balance = bal
                });
            }
        }

        return results
            .OrderByDescending(r => r.ChangeDate)
            .Take(limit)
            .ToList();
    }

    private class Movement
    {
        public Guid Wh { get; set; }
        public Guid St { get; set; }
        public DateTime Dt { get; set; }
        public decimal Qty { get; set; }
    }

    private class RateChangeEvent
    {
        public Guid StockId { get; set; }
        public DateTime Date { get; set; }
        public decimal PriceChange { get; set; }
    }

    public class StockRepriceEffectDto
    {
        public string StockId { get; set; } = string.Empty;
        public string StockCode { get; set; } = string.Empty;
        public string StockName { get; set; } = string.Empty;
        public decimal PriceChange { get; set; }
        public DateTime ChangeDate { get; set; }
        public int ChangeReason { get; set; }
        public string WarehouseId { get; set; } = string.Empty;
        public decimal Balance { get; set; }
    }
}
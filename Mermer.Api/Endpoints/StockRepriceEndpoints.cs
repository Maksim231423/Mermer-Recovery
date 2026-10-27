using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = null };

        group.MapGet("/count", async (HttpRequest req, MermerDbContext db, CancellationToken ct) =>
        {
            var effects = await CalculateEffectsAsync(req, db, ct);
            return Results.Json(effects.Count, jsonOptions);
        });

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
                .Select(d => d.ToLocalTime())
                .ToList();

            return Results.Json(allDates, jsonOptions);
        });

        group.MapGet("/", async (HttpRequest req, MermerDbContext db, CancellationToken ct) =>
        {
            var effects = await CalculateEffectsAsync(req, db, ct);
            return Results.Json(effects, jsonOptions);
        });

        return app;
    }

    private static (DateTime from, DateTime till) ParseDates(HttpRequest req)
    {
        // Безопасные границы для PostgreSQL timestamptz
        DateTime fUtc = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime tUtc = new DateTime(2099, 12, 31, 23, 59, 59, DateTimeKind.Utc);

        string? fromStr = req.Query["from"].FirstOrDefault();
        if (!string.IsNullOrEmpty(fromStr) && DateTime.TryParse(fromStr.Replace(" ", "+"), out var pf))
        {
            if (pf.Year > 2000)
                fUtc = pf.ToUniversalTime();
        }

        string? tillStr = req.Query["till"].FirstOrDefault();
        if (!string.IsNullOrEmpty(tillStr) && DateTime.TryParse(tillStr.Replace(" ", "+"), out var pt))
        {
            if (pt.Year < 2099)
            {
                if (pt.TimeOfDay == TimeSpan.Zero)
                    tUtc = pt.Date.AddDays(1).AddTicks(-1).ToUniversalTime();
                else
                    tUtc = pt.ToUniversalTime();
            }
        }

        return (fUtc, tUtc);
    }

    private static async Task<List<StockRepriceEffectDto>> CalculateEffectsAsync(HttpRequest req, MermerDbContext db, CancellationToken ct)
    {
        var (fUtc, tUtc) = ParseDates(req);

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

        var priceChanges = await db.StockPrices
            .Where(p => p.ValidFrom >= fUtc && p.ValidFrom <= tUtc)
            .AsNoTracking()
            .ToListAsync(ct);

        foreach (var p in priceChanges)
            affectedStockIds.Add(p.StockId);

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

        foreach (var nextP in priceChanges)
        {
            var prevP = allPricesList.Where(p => p.StockId == nextP.StockId && p.ValidFrom < nextP.ValidFrom)
                                     .OrderByDescending(p => p.ValidFrom)
                                     .FirstOrDefault();
            if (prevP == null) continue;

            // Если CurrencyId равен null, считаем валюту базовой USD (курс 1.0)
            decimal r1 = prevP.CurrencyId.HasValue ? GetRateValue(prevP.CurrencyId.Value, nextP.ValidFrom) : 1.0m;
            decimal r2 = nextP.CurrencyId.HasValue ? GetRateValue(nextP.CurrencyId.Value, nextP.ValidFrom) : 1.0m;

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
                    ChangeDate = nextP.ValidFrom,
                    ChangeReason = 0,
                    WarehouseId = w.ToString(),
                    Balance = bal
                });
            }
        }

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
                    ChangeDate = rcEvt.Date, // ВАЖНО: DateTime
                    ChangeReason = 1,
                    WarehouseId = w.ToString(),
                    Balance = bal
                });
            }
        }

        return results
            .OrderByDescending(r => r.ChangeDate)
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
        public DateTime ChangeDate { get; set; } // ВАЖНО: DateTime для совместимости с клиентом!
        public int ChangeReason { get; set; }
        public string WarehouseId { get; set; } = string.Empty;
        public decimal Balance { get; set; }
    }
}
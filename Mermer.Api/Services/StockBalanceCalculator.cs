using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mermer.Data.Postgres;
using Mermer.Data.Postgres.Entities;

namespace Mermer.Api.Services;

public interface IStockBalanceCalculator
{
    Task RecalculateAllBalancesAsync(CancellationToken cancellationToken = default);
}

public class StockBalanceCalculator : IStockBalanceCalculator
{
    private readonly MermerDbContext _dbContext;
    private readonly ILogger<StockBalanceCalculator> _logger;

    public StockBalanceCalculator(MermerDbContext dbContext, ILogger<StockBalanceCalculator> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task RecalculateAllBalancesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        // 1. Агрегация прихода и расхода из накладных (Invoices)
        var invoiceMovements = await _dbContext.InvoiceLines
            .AsNoTracking()
            .Where(l => l.Invoice != null && l.Invoice.WarehouseId.HasValue && l.Invoice.IsCompleted && !l.Invoice.IsDisabled)
            .GroupBy(l => new
            {
                WarehouseId = l.Invoice.WarehouseId!.Value,
                StockId = l.StockId ?? Guid.Empty,
                Type = l.Invoice.InvoiceType
            })
            .Select(g => new
            {
                g.Key.WarehouseId,
                g.Key.StockId,
                g.Key.Type,
                TotalQty = g.Sum(x => x.Quantity)
            })
            .ToListAsync(cancellationToken);

        // 2. Агрегация прихода и расхода из складских актов (StockSlips)
        var slipMovements = await _dbContext.StockSlipLines
            .AsNoTracking()
            .Where(sl => sl.StockSlip != null && sl.StockSlip.WarehouseId.HasValue && sl.StockSlip.IsCompleted)
            .GroupBy(sl => new
            {
                WarehouseId = sl.StockSlip.WarehouseId!.Value,
                StockId = sl.StockId ?? Guid.Empty,
                sl.StockSlip.SlipType
            })
            .Select(g => new
            {
                g.Key.WarehouseId,
                g.Key.StockId,
                g.Key.SlipType,
                TotalQty = g.Sum(x => x.Quantity)
            })
            .ToListAsync(cancellationToken);

        // 3. Объединяем уникальные пары (Склад, Товар)
        var allKeys = invoiceMovements
            .Select(m => (m.WarehouseId, m.StockId))
            .Union(slipMovements.Select(s => (s.WarehouseId, s.StockId)))
            .Where(k => k.StockId != Guid.Empty)
            .Distinct()
            .ToList();

        foreach (var key in allKeys)
        {
            decimal income = 0;
            decimal expense = 0;

            // Расчет по накладным
            var invGroup = invoiceMovements.Where(x => x.WarehouseId == key.WarehouseId && x.StockId == key.StockId);
            foreach (var inv in invGroup)
            {
                if (string.Equals(inv.Type, "Purchase", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(inv.Type, "SalesReturn", StringComparison.OrdinalIgnoreCase))
                {
                    income += inv.TotalQty;
                }
                else if (string.Equals(inv.Type, "Sales", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(inv.Type, "Sale", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(inv.Type, "PurchaseReturn", StringComparison.OrdinalIgnoreCase))
                {
                    expense += inv.TotalQty;
                }
            }

            // Расчет по складским актам
            var slipGroup = slipMovements.Where(x => x.WarehouseId == key.WarehouseId && x.StockId == key.StockId);
            foreach (var slip in slipGroup)
            {
                if (string.Equals(slip.SlipType, "Incoming", StringComparison.OrdinalIgnoreCase))
                {
                    income += slip.TotalQty;
                }
                else
                {
                    expense += slip.TotalQty;
                }
            }

            // Обновляем запись в stock_balances (без указания ClientId, его проставит SaveChangesAsync)
            var balanceEntity = await _dbContext.StockBalances
                .FirstOrDefaultAsync(b => b.WarehouseId == key.WarehouseId && b.StockId == key.StockId, cancellationToken);

            if (balanceEntity == null)
            {
                _dbContext.StockBalances.Add(new StockBalanceEntity
                {
                    WarehouseId = key.WarehouseId,
                    StockId = key.StockId,
                    Income = income,
                    Expense = expense,
                    UpdatedAt = now
                });
            }
            else
            {
                balanceEntity.Income = income;
                balanceEntity.Expense = expense;
                balanceEntity.UpdatedAt = now;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Фактический пересчет остатков завершен: обновлено позиций — {Count}.", allKeys.Count);
    }
}
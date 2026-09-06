using Mermer.Data.Postgres.Entities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mermer.Data.Postgres.Services;

public class StockBalanceCalculator : IStockBalanceCalculator
{
    private readonly MermerDbContext _db;

    public StockBalanceCalculator(MermerDbContext db)
    {
        _db = db;
    }

    public async Task RecalculateAllBalancesAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine("--> [BALANCE CALCULATOR] Начинаем пересчет регистра stock_balances...");

        // 1. Движения по накладным (продажи, закупки, возвраты)
        var invoiceMovements = await _db.InvoiceLines
            .AsNoTracking()
            .Where(l => l.Invoice != null
                     && l.Invoice.IsCompleted
                     && !l.Invoice.IsDisabled
                     && l.Invoice.WarehouseId.HasValue
                     && l.StockId.HasValue)
            .Select(l => new MovementRecord(
                l.Invoice!.WarehouseId!.Value,
                l.StockId!.Value,
                l.Quantity,
                IsIncomeInvoice(l.Invoice.InvoiceType)
            ))
            .ToListAsync(cancellationToken);

        // 2. Движения по складским ордерам (оприходование, инвентаризация, списание)
        var slipMovements = await _db.StockSlipLines
            .AsNoTracking()
            .Where(l => l.StockSlip != null
                     && l.StockSlip.IsCompleted
                     && l.StockSlip.WarehouseId.HasValue
                     && l.StockId.HasValue)
            .Select(l => new MovementRecord(
                l.StockSlip!.WarehouseId!.Value,
                l.StockId!.Value,
                l.Quantity,
                l.StockSlip.IsStockIncome
            ))
            .ToListAsync(cancellationToken);

        // 3. Движения по перемещениям между складами (StockTransfer)
        var transferLines = await _db.StockTransferLines
            .Include(t => t.StockTransfer)
            .AsNoTracking()
            .Where(t => t.StockTransfer != null
                     && t.StockTransfer.IsCompleted
                     && !t.StockTransfer.IsDisabled
                     && t.StockId.HasValue)
            .ToListAsync(cancellationToken);

        var transferMovements = new List<MovementRecord>();
        foreach (var t in transferLines)
        {
            // Склад-источник: расход
            if (t.StockTransfer!.WarehouseId.HasValue)
            {
                transferMovements.Add(new MovementRecord(
                    t.StockTransfer.WarehouseId.Value,
                    t.StockId!.Value,
                    t.Quantity,
                    IsIncome: false
                ));
            }

            // Склад-получатель: приход
            if (t.StockTransfer.DestinationWarehouseId.HasValue)
            {
                transferMovements.Add(new MovementRecord(
                    t.StockTransfer.DestinationWarehouseId.Value,
                    t.StockId!.Value,
                    t.ReceivedQuantity > 0 ? t.ReceivedQuantity : t.Quantity,
                    IsIncome: true
                ));
            }
        }

        // 4. Объединяем все три источника складских движений
        var allMovements = invoiceMovements
            .Concat(slipMovements)
            .Concat(transferMovements);

        // 5. Группируем по паре (Склад + Товар)
        var aggregatedBalances = allMovements
            .GroupBy(m => new { m.WarehouseId, m.StockId })
            .Select(g => new StockBalanceEntity
            {
                WarehouseId = g.Key.WarehouseId,
                StockId = g.Key.StockId,
                Income = g.Where(x => x.IsIncome).Sum(x => x.Amount),
                Expense = g.Where(x => !x.IsIncome).Sum(x => x.Amount),
                UpdatedAt = DateTimeOffset.UtcNow
            })
            .ToList();

        Console.WriteLine($"--> [BALANCE CALCULATOR] Рассчитано пар (Склад + Товар): {aggregatedBalances.Count}");

        // 6. Атомарное сохранение в PostgreSQL
        using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await _db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE stock_balances;", cancellationToken);
            await _db.StockBalances.AddRangeAsync(aggregatedBalances, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            Console.WriteLine("--> [BALANCE CALCULATOR] Регистр stock_balances успешно обновлен!");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            Console.WriteLine($"--> [ERROR] Ошибка при обновлении stock_balances: {ex.Message}");
            throw;
        }
    }

    private static bool IsIncomeInvoice(string invoiceType)
    {
        return invoiceType.Equals("Purchase", StringComparison.OrdinalIgnoreCase) ||
               invoiceType.Equals("SalesReturn", StringComparison.OrdinalIgnoreCase) ||
               invoiceType.Equals("Opening", StringComparison.OrdinalIgnoreCase);
    }

    private record MovementRecord(Guid WarehouseId, Guid StockId, decimal Amount, bool IsIncome);
}
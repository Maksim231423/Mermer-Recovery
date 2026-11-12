using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Mermer.Data.Postgres.Entities;

/// <summary>
/// Stock Balance entity — aggregated balance per warehouse/stock.
/// Maps to: Mermer.StockManagement.Models.StockBalance
/// </summary>
[Table("stock_balances")]
public class StockBalanceEntity
{
    [Column("warehouse_id")]
    public Guid WarehouseId { get; set; }

    [Column("stock_id")]
    public Guid StockId { get; set; }

    /// <summary>NUMERIC(18,4) — total incoming quantity.</summary>
    [Column("income")]
    public decimal Income { get; set; }

    /// <summary>NUMERIC(18,4) — total outgoing quantity.</summary>
    [Column("expense")]
    public decimal Expense { get; set; }

    /// <summary>Computed: Income - Expense</summary>
    [NotMapped]
    public decimal Balance => Income - Expense;

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    // Navigation
    [ForeignKey(nameof(WarehouseId))]
    public WarehouseEntity? Warehouse { get; set; }

    [ForeignKey(nameof(StockId))]
    public StockEntity? Stock { get; set; }
}
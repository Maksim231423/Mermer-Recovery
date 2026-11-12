using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Mermer.Data.Postgres.Entities;

[Table("stock_transfers")]
public class StockTransferEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("code")]
    public string? Code { get; set; }

    [Column("date")]
    public DateTimeOffset Date { get; set; }

    [Column("warehouse_id")]
    public Guid? WarehouseId { get; set; }

    [Column("destination_warehouse_id")]
    public Guid? DestinationWarehouseId { get; set; }

    [Column("display_currency_id")]
    public Guid? DisplayCurrencyId { get; set; }

    [Column("is_completed")]
    public bool IsCompleted { get; set; }

    [Column("is_disabled")]
    public bool IsDisabled { get; set; }

    [Column("user_name")]
    public string? UserName { get; set; }

    [Column("group_name")]
    public string? GroupName { get; set; }

    [Column("description")]
    public string? Description { get; set; }

    [Column("tags")]
    public string[]? Tags { get; set; }

    [Column("action_total")]
    public decimal ActionTotal { get; set; }

    [Column("action_received_total")]
    public decimal ActionReceivedTotal { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    [ForeignKey(nameof(WarehouseId))]
    public WarehouseEntity? Warehouse { get; set; }

    [ForeignKey(nameof(DestinationWarehouseId))]
    public WarehouseEntity? DestinationWarehouse { get; set; }

    [ForeignKey(nameof(DisplayCurrencyId))]
    public CurrencyEntity? DisplayCurrency { get; set; }

    public ICollection<StockTransferLineEntity> Lines { get; set; } = new List<StockTransferLineEntity>();
}
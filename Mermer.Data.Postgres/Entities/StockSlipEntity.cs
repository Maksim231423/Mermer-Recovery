using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Mermer.Data.Postgres.Entities;

[Table("stock_slips")]
public class StockSlipEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("code")]
    public string? Code { get; set; }

    [Column("slip_type")]
    public string SlipType { get; set; } = "StockOpening";

    [Column("is_completed")]
    public bool IsCompleted { get; set; }

    [Column("is_stock_income")]
    public bool IsStockIncome { get; set; }

    [Column("display_total")]
    public decimal DisplayTotal { get; set; }

    [Column("description")]
    public string? Description { get; set; }

    [Column("group_name")]
    public string? GroupName { get; set; }

    [Column("tags")]
    public string[]? Tags { get; set; }

    [Column("date")]
    public DateTimeOffset Date { get; set; }
    public bool IsSynced { get; set; } = false;

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    [Column("user_id")]
    public Guid? UserId { get; set; }

    [Column("warehouse_id")]
    public Guid? WarehouseId { get; set; }

    public UserEntity? User { get; set; }
    public WarehouseEntity? Warehouse { get; set; }

    public ICollection<StockSlipLineEntity> Lines { get; set; } = new List<StockSlipLineEntity>();
}
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Mermer.Data.Postgres.Entities;

[Table("stock_revisions")]
public class StockRevisionEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("code")]
    public string? Code { get; set; }

    [Column("date")]
    public DateTimeOffset Date { get; set; }

    [Column("finish_date")]
    public DateTimeOffset? FinishDate { get; set; }

    [Column("warehouse_id")]
    public Guid? WarehouseId { get; set; }

    [Column("exceed_slip_id")]
    public Guid? ExceedSlipId { get; set; }

    [Column("deficit_slip_id")]
    public Guid? DeficitSlipId { get; set; }

    [Column("user_id")]
    public Guid? UserId { get; set; }

    [Column("user_name")]
    public string? UserName { get; set; }

    [Column("is_completed")]
    public bool IsCompleted { get; set; }

    [Column("is_disabled")]
    public bool IsDisabled { get; set; }

    [Column("group_name")]
    public string? GroupName { get; set; }

    [Column("tags")]
    public string[]? Tags { get; set; }

    [Column("description")]
    public string? Description { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    [ForeignKey(nameof(WarehouseId))]
    public WarehouseEntity? Warehouse { get; set; }

    [ForeignKey(nameof(UserId))]
    public UserEntity? User { get; set; }

    public ICollection<StockRevisionLineEntity> Lines { get; set; } = new List<StockRevisionLineEntity>();
}
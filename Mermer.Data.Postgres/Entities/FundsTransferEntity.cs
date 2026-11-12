using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Mermer.Data.Postgres.Entities;

[Table("funds_transfers")]
public class FundsTransferEntity
{
    [Key][Column("id")] public Guid Id { get; set; }
    [Column("code")] public string? Code { get; set; }
    [Column("date")] public DateTime Date { get; set; }
    [Column("user_id")] public Guid? UserId { get; set; }
    [Column("user_name")] public string? UserName { get; set; }
    [Column("from_depository_id")] public Guid? FromDepositoryId { get; set; }
    [Column("to_depository_id")] public Guid? ToDepositoryId { get; set; }
    [Column("display_currency_id")] public Guid? DisplayCurrencyId { get; set; }
    [Column("is_completed")] public bool IsCompleted { get; set; }
    [Column("is_disabled")] public bool IsDisabled { get; set; }
    [Column("group_name")] public string? Group { get; set; }
    [Column("tags")] public string[]? Tags { get; set; }
    [Column("description")] public string? Description { get; set; }
    [Column("created_at")] public DateTime CreatedAt { get; set; }
    [Column("updated_at")] public DateTime UpdatedAt { get; set; }

    [ForeignKey(nameof(UserId))] public UserEntity? User { get; set; }
    [ForeignKey(nameof(FromDepositoryId))] public DepositoryEntity? FromDepository { get; set; }
    [ForeignKey(nameof(ToDepositoryId))] public DepositoryEntity? ToDepository { get; set; }
    [ForeignKey(nameof(DisplayCurrencyId))] public CurrencyEntity? DisplayCurrency { get; set; }

    public ICollection<FundsTransferLineEntity> Lines { get; set; } = new List<FundsTransferLineEntity>();
}
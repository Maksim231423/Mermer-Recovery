using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Mermer.Data.Postgres.Entities;

[Table("expense_slips")]
public class ExpenseSlipEntity
{
    [Key][Column("id")] public Guid Id { get; set; }
    [Column("code")] public string? Code { get; set; }
    [Column("date")] public DateTime Date { get; set; }
    [Column("user_id")] public Guid? UserId { get; set; }
    [Column("user_name")] public string? UserName { get; set; }
    [Column("office_id")] public Guid? OfficeId { get; set; }
    [Column("depository_id")] public Guid? DepositoryId { get; set; }
    [Column("display_currency_id")] public Guid? DisplayCurrencyId { get; set; }
    [Column("is_completed")] public bool IsCompleted { get; set; }
    [Column("is_disabled")] public bool IsDisabled { get; set; }
    [Column("group_name")] public string? GroupName { get; set; }
    [Column("tags")] public string[]? Tags { get; set; }
    [Column("description")] public string? Description { get; set; }
    [Column("created_at")] public DateTime CreatedAt { get; set; }
    [Column("updated_at")] public DateTime UpdatedAt { get; set; }

    public ICollection<ExpenseSlipLineEntity> Lines { get; set; } = new List<ExpenseSlipLineEntity>();
}

[Table("expense_slip_lines")]
public class ExpenseSlipLineEntity
{
    [Key][Column("id")] public Guid Id { get; set; }
    [Column("expense_slip_id")] public Guid ExpenseSlipId { get; set; }
    [Column("expense_id")] public Guid? ExpenseId { get; set; }
    [Column("amount")] public decimal Amount { get; set; }
    [Column("currency_id")] public Guid? CurrencyId { get; set; }
    [Column("sort_order")] public int SortOrder { get; set; }

    [ForeignKey(nameof(ExpenseSlipId))]
    public ExpenseSlipEntity? ExpenseSlip { get; set; }
}
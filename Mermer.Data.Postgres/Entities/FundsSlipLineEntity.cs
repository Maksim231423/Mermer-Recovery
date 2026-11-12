using Mermer.Data.Postgres.Entities;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

[Table("funds_slip_lines")]
public class FundsSlipLineEntity
{
    [Key][Column("id")] public Guid Id { get; set; }
    [Column("funds_slip_id")] public Guid FundsSlipId { get; set; }
    [Column("amount")] public decimal Amount { get; set; }
    [Column("currency_id")] public Guid? CurrencyId { get; set; }
    [Column("sort_order")] public int SortOrder { get; set; }

    [ForeignKey(nameof(FundsSlipId))] public FundsSlipEntity? FundsSlip { get; set; }
    [ForeignKey(nameof(CurrencyId))] public CurrencyEntity? Currency { get; set; }
}
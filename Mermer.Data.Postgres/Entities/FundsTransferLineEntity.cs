using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Mermer.Data.Postgres.Entities;

[Table("funds_transfer_lines")]
public class FundsTransferLineEntity
{
    [Key][Column("id")] public Guid Id { get; set; }
    [Column("funds_transfer_id")] public Guid FundsTransferId { get; set; }
    [Column("amount")] public decimal Amount { get; set; }
    [Column("received_amount")] public decimal ReceivedAmount { get; set; }
    [Column("currency_id")] public Guid? CurrencyId { get; set; }
    [Column("sort_order")] public int SortOrder { get; set; }

    [ForeignKey(nameof(FundsTransferId))] public FundsTransferEntity? FundsTransfer { get; set; }
    [ForeignKey(nameof(CurrencyId))] public CurrencyEntity? Currency { get; set; }
}
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Mermer.Data.Postgres.Entities;

[Table("partner_transfer_lines")]
public class PartnerTransferLineEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("partner_transfer_id")]
    public Guid PartnerTransferId { get; set; }

    [Column("office_id")]
    public Guid? OfficeId { get; set; }

    [Column("partner_id")]
    public Guid? PartnerId { get; set; }

    [Column("debit_amount")]
    public decimal DebitAmount { get; set; }

    [Column("debit_currency_id")]
    public Guid? DebitCurrencyId { get; set; }

    [Column("credit_amount")]
    public decimal CreditAmount { get; set; }

    [Column("credit_currency_id")]
    public Guid? CreditCurrencyId { get; set; }

    [ForeignKey(nameof(PartnerTransferId))]
    [JsonIgnore]
    public PartnerTransferEntity? PartnerTransfer { get; set; }

    [ForeignKey(nameof(OfficeId))]
    public OfficeEntity? Office { get; set; }

    [ForeignKey(nameof(PartnerId))]
    public PartnerEntity? Partner { get; set; }
}
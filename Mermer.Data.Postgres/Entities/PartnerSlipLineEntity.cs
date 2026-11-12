using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization; // <-- ДОБАВЛЕНО

namespace Mermer.Data.Postgres.Entities;

[Table("partner_slip_lines")]
public class PartnerSlipLineEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("partner_slip_id")]
    public Guid PartnerSlipId { get; set; }

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

    [ForeignKey(nameof(PartnerSlipId))]
    [JsonIgnore]
    public PartnerSlipEntity? PartnerSlip { get; set; }

    [ForeignKey(nameof(PartnerId))]
    public PartnerEntity? Partner { get; set; }

    [ForeignKey(nameof(DebitCurrencyId))]
    public CurrencyEntity? DebitCurrency { get; set; }

    [ForeignKey(nameof(CreditCurrencyId))]
    public CurrencyEntity? CreditCurrency { get; set; }
}
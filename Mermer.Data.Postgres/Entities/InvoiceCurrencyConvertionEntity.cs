using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Mermer.Data.Postgres.Entities;

[Table("invoice_currency_convertions")]
public class InvoiceCurrencyConvertionEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("invoice_id")]
    public Guid InvoiceId { get; set; }

    [Column("currency_id")]
    public Guid CurrencyId { get; set; }

    [Column("multiplier")]
    public decimal Multiplier { get; set; } = 1m;

    [Column("divider")]
    public decimal Divider { get; set; } = 1m;

    // Navigation
    [ForeignKey(nameof(InvoiceId))]
    [JsonIgnore]
    public InvoiceEntity? Invoice { get; set; }

    [ForeignKey(nameof(CurrencyId))]
    public CurrencyEntity? Currency { get; set; }
}
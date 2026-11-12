using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Mermer.Data.Postgres.Entities;

[Table("invoice_payments")]
public class InvoicePaymentEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("invoice_id")]
    public Guid InvoiceId { get; set; }

    [Column("payment_type")]
    public string PaymentType { get; set; } = "Payment";

    [Column("amount")]
    public decimal Amount { get; set; }

    [Column("currency_id")]
    public Guid? CurrencyId { get; set; }

    [Column("sort_order")]
    public int SortOrder { get; set; }

    // Navigation
    [ForeignKey(nameof(InvoiceId))]
    [JsonIgnore]
    public InvoiceEntity? Invoice { get; set; }

    [ForeignKey(nameof(CurrencyId))]
    public CurrencyEntity? Currency { get; set; }
}
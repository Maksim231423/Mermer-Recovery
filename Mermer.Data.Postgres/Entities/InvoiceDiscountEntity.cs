using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Mermer.Data.Postgres.Entities;

[Table("invoice_discounts")]
public class InvoiceDiscountEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("invoice_id")]
    public Guid InvoiceId { get; set; }

    [Column("discount_type")]
    public string DiscountType { get; set; } = "Flat";

    [Column("amount")]
    public decimal Amount { get; set; }

    [Column("description")]
    public string? Description { get; set; }

    [Column("sort_order")]
    public int SortOrder { get; set; }

    // Navigation
    [ForeignKey(nameof(InvoiceId))]
    [JsonIgnore]
    public InvoiceEntity? Invoice { get; set; }
}
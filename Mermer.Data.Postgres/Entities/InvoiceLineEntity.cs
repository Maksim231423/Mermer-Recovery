using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Mermer.Data.Postgres.Entities;

[Table("invoice_lines")]
public class InvoiceLineEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("invoice_id")]
    public Guid InvoiceId { get; set; }

    [Column("source_id")]
    public Guid? SourceId { get; set; }

    [Column("stock_id")]
    public Guid? StockId { get; set; }

    [Column("unit_id")]
    public Guid? UnitId { get; set; }

    [Column("quantity")]
    public decimal Quantity { get; set; }

    [Column("price")]
    public decimal Price { get; set; }

    [Column("currency_id")]
    public Guid? CurrencyId { get; set; }

    [Column("sort_order")]
    public int SortOrder { get; set; }

    // Navigation
    [ForeignKey(nameof(InvoiceId))]
    [JsonIgnore]
    public InvoiceEntity? Invoice { get; set; }

    [ForeignKey(nameof(StockId))]
    public StockEntity? Stock { get; set; }

    [ForeignKey(nameof(UnitId))]
    public StockUnitEntity? Unit { get; set; }

    [ForeignKey(nameof(CurrencyId))]
    public CurrencyEntity? Currency { get; set; }
}
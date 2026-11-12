using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Mermer.Data.Postgres.Entities;

[Table("invoice_stock_unit_convertions")]
public class InvoiceStockUnitConvertionEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("invoice_id")]
    public Guid InvoiceId { get; set; }

    [Column("stock_id")]
    public Guid StockId { get; set; }

    [Column("unit_id")]
    public Guid UnitId { get; set; }

    [Column("multiplier")]
    public decimal Multiplier { get; set; } = 1m;

    [Column("divider")]
    public decimal Divider { get; set; } = 1m;

    // Navigation
    [ForeignKey(nameof(InvoiceId))]
    [JsonIgnore]
    public InvoiceEntity? Invoice { get; set; }

    [ForeignKey(nameof(StockId))]
    public StockEntity? Stock { get; set; }

    [ForeignKey(nameof(UnitId))]
    public StockUnitEntity? Unit { get; set; }
}
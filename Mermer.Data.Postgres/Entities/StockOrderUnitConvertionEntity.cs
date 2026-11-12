using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Mermer.Data.Postgres.Entities;

[Table("stock_order_unit_convertions")]
public class StockOrderUnitConvertionEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("stock_order_id")]
    public Guid StockOrderId { get; set; }

    [Column("stock_id")]
    public Guid? StockId { get; set; }

    [Column("unit_id")]
    public Guid? UnitId { get; set; }

    [Column("multiplier")]
    public decimal Multiplier { get; set; }

    [Column("divider")]
    public decimal Divider { get; set; }

    [ForeignKey(nameof(StockOrderId))]
    [JsonIgnore]
    public StockOrderEntity? StockOrder { get; set; }

    [ForeignKey(nameof(StockId))]
    public StockEntity? Stock { get; set; }

    [ForeignKey(nameof(UnitId))]
    public StockUnitEntity? Unit { get; set; }
}
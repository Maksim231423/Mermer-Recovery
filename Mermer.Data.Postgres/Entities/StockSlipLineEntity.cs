using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Mermer.Data.Postgres.Entities;

[Table("stock_slip_lines")]
public class StockSlipLineEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("stock_slip_id")]
    public Guid StockSlipId { get; set; }

    [Column("stock_id")]
    public Guid? StockId { get; set; }

    [Column("unit_id")]
    public Guid? UnitId { get; set; }

    [Column("quantity")]
    public decimal Quantity { get; set; }

    [Column("action_quantity")]
    public decimal ActionQuantity { get; set; }

    [Column("price")]
    public decimal Price { get; set; }

    [Column("action_total")]
    public decimal ActionTotal { get; set; }

    [Column("sort_order")]
    public int SortOrder { get; set; }

    [ForeignKey(nameof(StockId))]
    public StockEntity? Stock { get; set; }

    [ForeignKey(nameof(UnitId))]
    public StockUnitEntity? Unit { get; set; }

    [ForeignKey(nameof(StockSlipId))]
    [JsonIgnore]
    public StockSlipEntity? StockSlip { get; set; }
}
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Mermer.Data.Postgres.Entities;

[Table("stock_transfer_lines")]
public class StockTransferLineEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("stock_transfer_id")]
    public Guid StockTransferId { get; set; }

    [Column("stock_id")]
    public Guid? StockId { get; set; }

    [Column("unit_id")]
    public Guid? UnitId { get; set; }

    [Column("received_unit_id")]
    public Guid? ReceivedUnitId { get; set; }

    [Column("quantity")]
    public decimal Quantity { get; set; }

    [Column("received_quantity")]
    public decimal ReceivedQuantity { get; set; }

    [Column("price")]
    public decimal Price { get; set; }

    [Column("action_total")]
    public decimal ActionTotal { get; set; }

    [Column("action_received_total")]
    public decimal ActionReceivedTotal { get; set; }

    [Column("sort_order")]
    public int SortOrder { get; set; }

    [ForeignKey(nameof(StockId))]
    public StockEntity? Stock { get; set; }

    [ForeignKey(nameof(UnitId))]
    public StockUnitEntity? Unit { get; set; }

    [ForeignKey(nameof(ReceivedUnitId))]
    public StockUnitEntity? ReceivedUnit { get; set; }

    [ForeignKey(nameof(StockTransferId))]
    [JsonIgnore]
    public StockTransferEntity? StockTransfer { get; set; }
}
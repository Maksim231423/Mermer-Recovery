using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Mermer.Data.Postgres.Entities;

[Table("stock_order_template_lines")]
public class StockOrderTemplateLineEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("stock_order_template_id")]
    public Guid StockOrderTemplateId { get; set; }

    [Column("stock_id")]
    public Guid? StockId { get; set; }

    [ForeignKey(nameof(StockOrderTemplateId))]
    [JsonIgnore]
    public StockOrderTemplateEntity? Template { get; set; }

    [ForeignKey(nameof(StockId))]
    public StockEntity? Stock { get; set; }
}
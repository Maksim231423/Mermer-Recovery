using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Mermer.Data.Postgres.Entities;

[Table("stock_name_composer_values")]
public class StockNameComposerValueEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("composer_id")]
    public Guid ComposerId { get; set; }

    [Column("order")]
    public int Order { get; set; }

    [Column("name")]
    public string? Name { get; set; }

    [Column("short_name")]
    public string? ShortName { get; set; }

    [ForeignKey(nameof(ComposerId))]
    [JsonIgnore]
    public StockNameComposerEntity? Composer { get; set; }
}
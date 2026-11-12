using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Mermer.Data.Postgres.Entities;

/// <summary>
/// Stock Unit entity — unit of measurement for a product (e.g., pcs, kg, box).
/// Maps to: Mermer.StockManagement.Models.StockUnit
/// </summary>
[Table("stock_units")]
public class StockUnitEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("stock_id")]
    public Guid StockId { get; set; }

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>NUMERIC(18,8) — conversion multiplier. Must not be zero.</summary>
    [Column("multiplier")]
    public decimal Multiplier { get; set; } = 1m;

    /// <summary>NUMERIC(18,8) — conversion divider. Must not be zero.</summary>
    [Column("divider")]
    public decimal Divider { get; set; } = 1m;

    [Column("is_default")]
    public bool IsDefault { get; set; }

    // Navigation
    [ForeignKey(nameof(StockId))]
    [JsonIgnore]
    public StockEntity? Stock { get; set; }
}
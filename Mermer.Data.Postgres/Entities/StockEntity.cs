using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NpgsqlTypes;

namespace Mermer.Data.Postgres.Entities;

/// <summary>
/// Stock entity — product/item in inventory.
/// Maps to: Mermer.StockManagement.Models.Stock
/// </summary>
[Table("stocks")]
public class StockEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("code")]
    public string? Code { get; set; }

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("short_name")]
    public string? ShortName { get; set; }

    [Column("type")]
    public string? Type { get; set; }

    [Column("group_name")]
    public string? Group { get; set; }

    [Column("tags")]
    public string[]? Tags { get; set; }

    [Column("barcodes")]
    public string[]? Barcodes { get; set; }

    /// <summary>NUMERIC(18,4) — minimum stock alert level.</summary>
    [Column("limit_min")]
    public decimal? LimitMin { get; set; }

    /// <summary>NUMERIC(18,4) — maximum stock alert level.</summary>
    [Column("limit_max")]
    public decimal? LimitMax { get; set; }

    [Column("description")]
    public string? Description { get; set; }

    [Column("is_disabled")]
    public bool IsDisabled { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Auto-generated TSVECTOR column for full-text search.</summary>
    [Column("search_vector")]
    public NpgsqlTsVector? SearchVector { get; set; }

    // Navigation
    public ICollection<StockUnitEntity> Units { get; set; } = new List<StockUnitEntity>();
    public ICollection<StockPriceEntity> Prices { get; set; } = new List<StockPriceEntity>();
    public ICollection<StockAdditionalPriceEntity> AdditionalPrices { get; set; } = new List<StockAdditionalPriceEntity>();
}
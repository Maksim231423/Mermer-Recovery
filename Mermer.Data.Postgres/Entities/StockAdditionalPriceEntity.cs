using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Mermer.Data.Postgres.Entities;

/// <summary>
/// Stock Additional Price entity — alternate price list entry.
/// </summary>
[Table("stock_additional_prices")]
public class StockAdditionalPriceEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("stock_id")]
    public Guid StockId { get; set; }

    /// <summary>NUMERIC(18,4)</summary>
    [Column("price")]
    public decimal Price { get; set; }

    [Column("currency_id")]
    public Guid? CurrencyId { get; set; }

    [Column("price_group")]
    public string? PriceGroup { get; set; }

    [Column("valid_from")]
    public DateTime ValidFrom { get; set; }

    // Navigation
    [ForeignKey(nameof(StockId))]
    [JsonIgnore]
    public StockEntity? Stock { get; set; }

    [ForeignKey(nameof(CurrencyId))]
    public CurrencyEntity? Currency { get; set; }
}
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Mermer.Data.Postgres.Entities;

/// <summary>
/// Stock Price entity — historical price entry for a product.
/// Maps to: Mermer.StockManagement.Models.StockPrice
/// </summary>
[Table("stock_prices")]
public class StockPriceEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("stock_id")]
    public Guid StockId { get; set; }

    [Column("valid_from")]
    public DateTime ValidFrom { get; set; }

    /// <summary>NUMERIC(18,4) — product price. Never float/double.</summary>
    [Column("price")]
    public decimal Price { get; set; }

    [Column("currency_id")]
    public Guid? CurrencyId { get; set; }

    [Column("price_group")]
    public string? PriceGroup { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    // Navigation
    [ForeignKey(nameof(StockId))]
    [JsonIgnore]
    public StockEntity? Stock { get; set; }

    [ForeignKey(nameof(CurrencyId))]
    public CurrencyEntity? Currency { get; set; }
}
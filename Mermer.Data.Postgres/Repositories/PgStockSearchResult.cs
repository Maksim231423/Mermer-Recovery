using System;

namespace Mermer.Data.Postgres.Repositories;

/// <summary>
/// DTO for stock search results returned by pg_trgm fuzzy search.
/// Maps 1:1 to StockSearchResult from Mermer.StockManagement.Services.
/// </summary>
public class PgStockSearchResult
{
    public string Id { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ShortName { get; set; }
    public string? CodeHtml { get; set; }
    public string? NameHtml { get; set; }
    public decimal Price { get; set; }
    public string? Currency { get; set; }
    public string? CurrencyId { get; set; }

    // ДОБАВИТЬ:
    public decimal? LastPurchasePrice { get; set; }
    public string? LastPurchaseCurrency { get; set; }
    public string? LastPurchaseCurrencyId { get; set; }

    private decimal _balance;
    public decimal Balance
    {
        get => Math.Round(_balance, 2);
        set => _balance = value;
    }
    public string? Unit { get; set; }
    public string? UnitId { get; set; }
    public bool IsDisabled { get; set; }
    public double Similarity { get; set; }
}
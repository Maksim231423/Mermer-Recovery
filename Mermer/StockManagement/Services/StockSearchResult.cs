// Decompiled with JetBrains decompiler
// Type: Mermer.StockManagement.Services.StockSearchResult
// Assembly: Mermer, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: 19F85A6C-D40F-439D-9478-41F01000D67D
// Assembly location: C:\Users\Admin\AppData\Local\Temp\Bofyhol\f9d7aa10a6\lib\net45\Mermer.dll

using System;

#nullable disable
namespace Mermer.StockManagement.Services;

public class StockSearchResult
{
  public string Id { get; set; }

  public string Code { get; set; }

  public string Name { get; set; }

  public string CodeHtml { get; set; }

  public string NameHtml { get; set; }

  public Decimal Price { get; set; }

  public string Currency { get; set; }

  public string CurrencyId { get; set; }

  public Decimal? LastPurchasePrice { get; set; }

  public string LastPurchaseCurrency { get; set; }

  public string LastPurchaseCurrencyId { get; set; }

    private decimal _balance;
    public decimal Balance 
    { 
        get => Math.Round(_balance, 2); 
        set => _balance = value; 
    }

  public string Unit { get; set; }

  public string UnitId { get; set; }

  public bool IsDisabled { get; set; }
}

// Decompiled with JetBrains decompiler
// Type: Mermer.Transactions.Models.TransactionLine
// Assembly: Mermer, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: 19F85A6C-D40F-439D-9478-41F01000D67D
// Assembly location: C:\Users\Admin\AppData\Local\Temp\Bofyhol\f9d7aa10a6\lib\net45\Mermer.dll

using Mermer.Data.Models;
using System;
using System.Linq.Expressions;

#nullable disable
namespace Mermer.Transactions.Models;

public abstract class TransactionLine : BindableObject, IRequestCurrencyConverter
{
  private string _id;
  private string _currencyId;
  private CurrencyConvertion _displayCurrencyConvertion;

  protected TransactionLine()
  {
    this.AutoRaisePropertyChanged(nameof (ActionTotal), nameof (DisplayTotal));
    this.AutoRaisePropertyChanged(nameof (DisplayTotal), nameof (DisplayTotalString));
    this.CurrencyId = this.GetDefaultCurrencyId();
  }

  public string Id
  {
    get => this._id;
    set => this.SetProperty<string>(ref this._id, value, nameof (Id));
  }

  public string CurrencyId
  {
    get => this._currencyId;
    set
    {
      this.SetProperty<string>(ref this._currencyId, value, nameof (CurrencyId), "ActionTotal");
    }
  }

  public abstract Decimal ActionTotal { get; }

  public virtual Decimal DisplayTotal => this.GetDisplayAmount(this.ActionTotal);

  public virtual string DisplayTotalString => this.GetDisplayAmountString(this.DisplayTotal);

    // Добавляем свойство Total для строк
    public virtual Decimal Total => this.DisplayTotal;

    public virtual void UpdateCurrencyConvertion()
    {
        this._displayCurrencyConvertion = null; // Сбрасываем старый кэш валюты
        this.RaisePropertyChanged<Decimal>((Expression<Func<Decimal>>)(() => this.ActionTotal));
        this.RaisePropertyChanged("DisplayTotal");
        this.RaisePropertyChanged("Total");
    }

    public event CurrencyConverter CurrencyConverterRequested;

  protected CurrencyConvertion GetCurrencyConvertion(string currencyId)
  {
    CurrencyConverter converterRequested = this.CurrencyConverterRequested;
    return converterRequested == null ? (CurrencyConvertion) null : converterRequested(currencyId);
  }

  public virtual void UpdateDisplayCurrencyId(bool raiseChangeEvent = false)
  {
    this.RaisePropertyChanged<Decimal>((Expression<Func<Decimal>>) (() => this.DisplayTotal));
  }

    public void UpdateDisplayCurrencyConvertion(CurrencyConvertion convertion, bool raiseChangeEvent = false)
    {
        this._displayCurrencyConvertion = convertion;
        if (!raiseChangeEvent)
            return;

        this.RaisePropertyChanged("DisplayTotal");
        this.RaisePropertyChanged("Total");
    }

    public event Mermer.Transactions.Models.CurrencyId DisplayCurrencyIdRequested;

  protected string GetDisplayCurrencyId()
  {
    Mermer.Transactions.Models.CurrencyId currencyIdRequested = this.DisplayCurrencyIdRequested;
    return currencyIdRequested == null ? (string) null : currencyIdRequested();
  }

  public virtual void UpdateDefaultCurrencyId()
  {
    if (!string.IsNullOrEmpty(this.CurrencyId))
      return;
    this.CurrencyId = this.GetDefaultCurrencyId();
  }

  public event Mermer.Transactions.Models.CurrencyId DefaultCurrencyIdRequested;

  protected string GetDefaultCurrencyId()
  {
    Mermer.Transactions.Models.CurrencyId currencyIdRequested = this.DefaultCurrencyIdRequested;
    return currencyIdRequested == null ? (string) null : currencyIdRequested();
  }

  public event AmountFormatter AmountFormatterRequested;

  protected string GetAmountFormatter(Decimal amount, string currencyId)
  {
    AmountFormatter formatterRequested = this.AmountFormatterRequested;
    return formatterRequested == null ? (string) null : formatterRequested(amount, currencyId);
  }

    public Decimal GetDisplayAmount(Decimal amount)
    {
        if (this._displayCurrencyConvertion == null)
        {
            string displayCurrencyId = this.GetDisplayCurrencyId();
            if (string.IsNullOrEmpty(displayCurrencyId))
                return 0M;
            this._displayCurrencyConvertion = this.GetCurrencyConvertion(displayCurrencyId);
            if (this._displayCurrencyConvertion == null)
                return 0M;
        }
        if (this._displayCurrencyConvertion.Multiplier == 0M)
            return 0M;

        decimal calculated = amount / this._displayCurrencyConvertion.Multiplier * this._displayCurrencyConvertion.Divider;
        return Math.Round(calculated, 2, MidpointRounding.AwayFromZero);
    }

    protected string GetDisplayAmountString(Decimal amount)
  {
    string displayCurrencyId = this.GetDisplayCurrencyId();
    return string.IsNullOrEmpty(displayCurrencyId) ? (string) null : this.GetAmountFormatter(amount, displayCurrencyId);
  }
}

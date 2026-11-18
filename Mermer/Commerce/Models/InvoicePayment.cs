// Decompiled with JetBrains decompiler
// Type: Mermer.Commerce.Models.InvoicePayment
// Assembly: Mermer, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: 19F85A6C-D40F-439D-9478-41F01000D67D
// Assembly location: C:\Users\Admin\AppData\Local\Temp\Bofyhol\f9d7aa10a6\lib\net45\Mermer.dll

using Mermer.Transactions.Models;
using System;
using System.Linq.Expressions;

#nullable disable
namespace Mermer.Commerce.Models;

public class InvoicePayment : RequestCurrencyConverter
{
  private string _id;
  private Decimal _amount;
  private string _currencyId;

  public InvoicePayment()
  {
    this.AutoRaisePropertyChanged(nameof (ActionAmount), nameof (DisplayAmount));
    this.AutoRaisePropertyChanged(nameof (DisplayAmount), nameof (DisplayAmountString));
    this.Id = Guid.NewGuid().ToString();
  }

  public string Id
  {
    get => this._id;
    set => this.SetProperty<string>(ref this._id, value, nameof (Id));
  }

  public virtual Decimal Amount
  {
    get => this._amount;
    set => this.SetProperty<Decimal>(ref this._amount, value, nameof (Amount), "ActionAmount");
  }

  public virtual string CurrencyId
  {
    get => this._currencyId;
    set
    {
      this.SetProperty<string>(ref this._currencyId, value, nameof (CurrencyId), "ActionAmount");
    }
  }

    public Decimal ActionAmount
    {
        get
        {
            if (string.IsNullOrEmpty(this.CurrencyId))
                return 0M;
            CurrencyConvertion currencyConvertion = this.GetCurrencyConvertion(this.CurrencyId);
            if (currencyConvertion == null || currencyConvertion.Divider == 0M)
                return 0M;

            decimal calculated = this.Amount * currencyConvertion.Multiplier / currencyConvertion.Divider;
            return Math.Round(calculated, 2, MidpointRounding.AwayFromZero);
        }
    }

    public Decimal DisplayAmount => this.GetDisplayAmount(this.ActionAmount);

  public string DisplayAmountString => this.GetDisplayAmountString(this.DisplayAmount);

  public override void UpdateCurrencyConvertion()
  {
    this.RaisePropertyChanged<Decimal>((Expression<Func<Decimal>>) (() => this.ActionAmount));
  }

  public override void UpdateDisplayCurrencyId(bool raiseChangeEvent = false)
  {
    this.RaisePropertyChanged<Decimal>((Expression<Func<Decimal>>) (() => this.DisplayAmount));
  }

  public override void UpdateDefaultCurrencyId()
  {
    if (!string.IsNullOrEmpty(this.CurrencyId))
      return;
    this.CurrencyId = this.GetDefaultCurrencyId();
  }
}

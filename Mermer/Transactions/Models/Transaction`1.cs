// Decompiled with JetBrains decompiler
// Type: Mermer.Transactions.Models.Transaction`1
// Assembly: Mermer, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: 19F85A6C-D40F-439D-9478-41F01000D67D
// Assembly location: C:\Users\Admin\AppData\Local\Temp\Bofyhol\f9d7aa10a6\lib\net45\Mermer.dll

using Mermer.Data;
using Mermer.Data.Tools;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

#nullable disable
namespace Mermer.Transactions.Models;

public class Transaction<T> : TransactionModel, IRequestCurrencyConverter where T : TransactionLine
{
  private WatchedObservableCollection<T> _lines;
  private WatchedObservableCollection<CurrencyConvertion> _currencyConvertions;
  private string _displayCurrencyId;

  public virtual WatchedObservableCollection<T> Lines
  {
    get => this._lines;
    set
    {
      if (this._lines != null)
      {
        this._lines.CollectionChanged -= new NotifyCollectionChangedEventHandler(this.Lines_CollectionChanged);
        foreach (T line in (Collection<T>) this._lines)
        {
          line.PropertyChanged -= new PropertyChangedEventHandler(this.LinePropertyChanged);
          line.DisplayCurrencyIdRequested -= new CurrencyId(this.GetDisplayCurrencyId);
          line.DefaultCurrencyIdRequested -= new CurrencyId(this.GetDefaultCurrencyId);
          line.CurrencyConverterRequested -= new Mermer.Transactions.Models.CurrencyConverter(this.CurrencyConverter);
          line.AmountFormatterRequested -= new AmountFormatter(this.GetAmountFormatter);
        }
      }
      this.SetProperty<WatchedObservableCollection<T>>(ref this._lines, value, nameof (Lines));
      if (this._lines != null)
      {
        this._lines.CollectionChanged += new NotifyCollectionChangedEventHandler(this.Lines_CollectionChanged);
        foreach (T line in (Collection<T>) this._lines)
        {
          line.PropertyChanged += new PropertyChangedEventHandler(this.LinePropertyChanged);
          line.DisplayCurrencyIdRequested += new CurrencyId(this.GetDisplayCurrencyId);
          line.DefaultCurrencyIdRequested += new CurrencyId(this.GetDefaultCurrencyId);
          line.CurrencyConverterRequested += new Mermer.Transactions.Models.CurrencyConverter(this.CurrencyConverter);
          line.AmountFormatterRequested += new AmountFormatter(this.GetAmountFormatter);
          line.UpdateCurrencyConvertion();
          line.UpdateDisplayCurrencyId(false);
          line.UpdateDefaultCurrencyId();
        }
      }
      this.RaisePropertyChanged<Decimal>((Expression<Func<Decimal>>) (() => this.ActionTotal));
      this.RaisePropertyChanged<int>((Expression<Func<int>>) (() => this.LinesCount));
    }
  }

  public int LinesCount
  {
    get
    {
      WatchedObservableCollection<T> lines = this.Lines;
      return lines == null ? 0 : lines.Count<T>();
    }
  }

  public virtual WatchedObservableCollection<CurrencyConvertion> CurrencyConvertions
  {
    get => this._currencyConvertions;
    set
    {
      if (this._currencyConvertions != null)
        this._currencyConvertions.Watcher.ItemPropertyChanged -= new ItemPropertyChangedEventHandler(this.CurrencyConvertionPropertyChanged);
      this.SetProperty<WatchedObservableCollection<CurrencyConvertion>>(ref this._currencyConvertions, value, nameof (CurrencyConvertions));
      if (this._currencyConvertions != null)
        this._currencyConvertions.Watcher.ItemPropertyChanged += new ItemPropertyChangedEventHandler(this.CurrencyConvertionPropertyChanged);
      this.RaisePropertyChanged<Decimal>((Expression<Func<Decimal>>) (() => this.ActionTotal));
    }
  }

  public virtual Decimal ActionTotal
  {
    get
    {
      WatchedObservableCollection<T> lines = this.Lines;
      return lines == null ? 0M : lines.Sum<T>((Func<T, Decimal>) (x => x.ActionTotal));
    }
  }

  public bool RaiseChangeEvents { get; set; }

  public string DisplayCurrencyId
  {
    get => this._displayCurrencyId;
    set
    {
      if (!this.SetProperty<string>(ref this._displayCurrencyId, value, nameof (DisplayCurrencyId)))
        return;
      this.UpdateDisplayCurrencyId(this.RaiseChangeEvents);
    }
  }

  public virtual Decimal DisplayTotal => this.GetDisplayAmount(this.ActionTotal);

  public virtual string DisplayTotalString => this.GetDisplayAmountString(this.DisplayTotal);

  protected virtual void Lines_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
  {
    if (e.OldItems != null)
    {
      foreach (T obj in e.OldItems.Cast<T>())
      {
        obj.PropertyChanged -= new PropertyChangedEventHandler(this.LinePropertyChanged);
        obj.DisplayCurrencyIdRequested -= new CurrencyId(this.GetDisplayCurrencyId);
        obj.DefaultCurrencyIdRequested -= new CurrencyId(this.GetDefaultCurrencyId);
        obj.CurrencyConverterRequested -= new Mermer.Transactions.Models.CurrencyConverter(this.CurrencyConverter);
        obj.AmountFormatterRequested -= new AmountFormatter(this.GetAmountFormatter);
      }
    }
    if (e.NewItems != null)
    {
      foreach (T obj in e.NewItems.Cast<T>())
      {
        if (string.IsNullOrEmpty(obj.Id))
          obj.Id = Guid.NewGuid().ToString();
        obj.PropertyChanged += new PropertyChangedEventHandler(this.LinePropertyChanged);
        obj.DisplayCurrencyIdRequested += new CurrencyId(this.GetDisplayCurrencyId);
        obj.DefaultCurrencyIdRequested += new CurrencyId(this.GetDefaultCurrencyId);
        obj.CurrencyConverterRequested += new Mermer.Transactions.Models.CurrencyConverter(this.CurrencyConverter);
        obj.AmountFormatterRequested += new AmountFormatter(this.GetAmountFormatter);
        obj.UpdateCurrencyConvertion();
        obj.UpdateDisplayCurrencyId(false);
        obj.UpdateDefaultCurrencyId();
      }
    }
    this.RaisePropertyChanged<Decimal>((Expression<Func<Decimal>>) (() => this.ActionTotal));
    this.RaisePropertyChanged<int>((Expression<Func<int>>) (() => this.LinesCount));
  }

  protected virtual void LinePropertyChanged(object sender, PropertyChangedEventArgs e)
  {
    if (!(e.PropertyName == "ActionTotal"))
      return;
    this.RaisePropertyChanged<Decimal>((Expression<Func<Decimal>>) (() => this.ActionTotal));
  }

  protected virtual void CurrencyConvertionPropertyChanged(
    object sender,
    PropertyChangedEventArgs e)
  {
    this.UpdateCurrencyConvertion();
  }

  public virtual CurrencyConvertion CurrencyConverter(string currencyId)
  {
    WatchedObservableCollection<CurrencyConvertion> currencyConvertions = this.CurrencyConvertions;
    CurrencyConvertion currencyConvertion = currencyConvertions != null ? currencyConvertions.SingleOrDefault<CurrencyConvertion>((Func<CurrencyConvertion, bool>) (x => x.CurrencyId == currencyId)) : (CurrencyConvertion) null;
    if (currencyConvertion == null)
    {
      currencyConvertion = this.GetCurrencyConvertion(currencyId);
      if (currencyConvertion != null && this.CurrencyConvertions != null)
        this.CurrencyConvertions.Add(currencyConvertion);
    }
    return currencyConvertion;
  }

  public void UpdateCurrencyConvertion()
  {
    if (this.Lines != null)
    {
      foreach (T line in (Collection<T>) this.Lines)
        line.UpdateCurrencyConvertion();
    }
    this.RaisePropertyChanged<Decimal>((Expression<Func<Decimal>>) (() => this.ActionTotal));
  }

  public event Mermer.Transactions.Models.CurrencyConverter CurrencyConverterRequested;

  protected CurrencyConvertion GetCurrencyConvertion(string currencyId)
  {
    Mermer.Transactions.Models.CurrencyConverter converterRequested = this.CurrencyConverterRequested;
    return converterRequested == null ? (CurrencyConvertion) null : converterRequested(currencyId);
  }

    // Добавляем свойство Total, возвращающее пересчитанную сумму
    public virtual Decimal Total => this.DisplayTotal;

    public virtual void UpdateDisplayCurrencyId(bool raiseChangeEvent = false)
    {
        if (this.Lines == null)
            return;

        CurrencyConvertion displayCurrencyConvertion = this.CurrencyConverter(this.DisplayCurrencyId);
        if (displayCurrencyConvertion == null)
            return;

        // Синхронно и безопасно обновляем строки в UI-потоке
        foreach (T line in this.Lines)
        {
            line.UpdateDisplayCurrencyConvertion(displayCurrencyConvertion, true);
        }

        this.RaisePropertyChanged("DisplayTotal");
        this.RaisePropertyChanged("Total");
        this.RaisePropertyChanged("ActionTotal");
        this.RaisePropertyChanged("DisplayTotalString");
    }

    public void UpdateDisplayCurrencyConvertion(CurrencyConvertion convertion, bool raiseChangeEvent = false)
  {
  }

  public event CurrencyId DisplayCurrencyIdRequested;

  public string GetDisplayCurrencyId() => this.DisplayCurrencyId;

  public virtual void UpdateDefaultCurrencyId()
  {
    foreach (T line in (Collection<T>) this.Lines)
      line.UpdateDefaultCurrencyId();
  }

  public event CurrencyId DefaultCurrencyIdRequested;

  public string GetDefaultCurrencyId()
  {
    return this.DefaultCurrencyIdRequested == null ? (string) null : this.DefaultCurrencyIdRequested();
  }

  public event AmountFormatter AmountFormatterRequested;

  protected string GetAmountFormatter(Decimal amount, string currencyId)
  {
    return this.AmountFormatterRequested == null ? (string) null : this.AmountFormatterRequested(amount, currencyId);
  }

    public Decimal GetDisplayAmount(Decimal amount)
    {
        string displayCurrencyId = this.GetDisplayCurrencyId();
        if (string.IsNullOrEmpty(displayCurrencyId))
            return 0M;
        CurrencyConvertion currencyConvertion = this.CurrencyConverter(displayCurrencyId);
        if (currencyConvertion == null || currencyConvertion.Multiplier == 0M)
            return 0M;

        decimal calculated = amount / currencyConvertion.Multiplier * currencyConvertion.Divider;
        return Math.Round(calculated, 2, MidpointRounding.AwayFromZero);
    }

    protected string GetDisplayAmountString(Decimal amount)
  {
    string displayCurrencyId = this.GetDisplayCurrencyId();
    return string.IsNullOrEmpty(displayCurrencyId) ? (string) null : this.GetAmountFormatter(amount, displayCurrencyId);
  }
}

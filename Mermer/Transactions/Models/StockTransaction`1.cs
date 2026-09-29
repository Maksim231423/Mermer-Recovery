// Decompiled with JetBrains decompiler
// Type: Mermer.Transactions.Models.StockTransaction`1
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

#nullable disable
namespace Mermer.Transactions.Models;

public abstract class StockTransaction<T> : Transaction<T>, IRequestStockUnitConverter where T : StockTransactionLine
{
  private string _warehouseId;
  private WatchedObservableCollection<StockTransactionOverhead> _overheads;
  private WatchedObservableCollection<StockUnitConvertion> _stockUnitConvertions;

  public StockTransaction() => this.AutoRaisePropertyChanged("ActionTotal", "DisplayTotal");

  public virtual string WarehouseId
  {
    get => this._warehouseId;
    set => this.SetProperty<string>(ref this._warehouseId, value, nameof (WarehouseId));
  }

  public abstract bool IsStockIncome { get; }

  public override WatchedObservableCollection<T> Lines
  {
    get => base.Lines;
    set
    {
      base.Lines?.ForEach((Action<T>) (x => x.StockUnitConverterRequested -= new Mermer.Transactions.Models.StockUnitConverter(this.StockUnitConverter)));
      base.Lines = value;
      base.Lines?.ForEach((Action<T>) (item =>
      {
        item.StockUnitConverterRequested += new Mermer.Transactions.Models.StockUnitConverter(this.StockUnitConverter);
        item.UpdateStockUnitConvertion();
      }));
      this.RaisePropertyChanged<Decimal>((Expression<Func<Decimal>>) (() => this.LineQuantitiesSum));
    }
  }

  public Decimal LineQuantitiesSum
  {
    get
    {
      WatchedObservableCollection<T> lines = this.Lines;
      return lines == null ? 0M : lines.Sum<T>((Func<T, Decimal>) (x => x.Quantity));
    }
  }

  public virtual WatchedObservableCollection<StockTransactionOverhead> Overheads
  {
    get => this._overheads;
    set
    {
      if (this._overheads != null)
      {
        this._overheads.CollectionChanged -= new NotifyCollectionChangedEventHandler(this.Overheads_CollectionChanged);
        this._overheads.ForEach((Action<StockTransactionOverhead>) (item =>
        {
          item.PropertyChanged -= new PropertyChangedEventHandler(this.OverheadPropertyChanged);
          item.CurrencyConverterRequested -= new Mermer.Transactions.Models.CurrencyConverter(((Transaction<T>) this).CurrencyConverter);
          item.DisplayCurrencyIdRequested -= new CurrencyId(((Transaction<T>) this).GetDisplayCurrencyId);
          item.DefaultCurrencyIdRequested -= new CurrencyId(((Transaction<T>) this).GetDefaultCurrencyId);
        }));
      }
      this.SetProperty<WatchedObservableCollection<StockTransactionOverhead>>(ref this._overheads, value, nameof (Overheads));
      if (this._overheads == null)
        return;
      this._overheads.ForEach((Action<StockTransactionOverhead>) (item =>
      {
        item.PropertyChanged += new PropertyChangedEventHandler(this.OverheadPropertyChanged);
        item.CurrencyConverterRequested += new Mermer.Transactions.Models.CurrencyConverter(((Transaction<T>) this).CurrencyConverter);
        item.DisplayCurrencyIdRequested += new CurrencyId(((Transaction<T>) this).GetDisplayCurrencyId);
        item.DefaultCurrencyIdRequested += new CurrencyId(((Transaction<T>) this).GetDefaultCurrencyId);
        item.UpdateCurrencyConvertion();
        item.UpdateDefaultCurrencyId();
      }));
      this._overheads.CollectionChanged += new NotifyCollectionChangedEventHandler(this.Overheads_CollectionChanged);
    }
  }

  public virtual Decimal ActionOverheadTotal
  {
    get
    {
      WatchedObservableCollection<StockTransactionOverhead> overheads = this.Overheads;
      return overheads == null ? 0M : overheads.Sum<StockTransactionOverhead>((Func<StockTransactionOverhead, Decimal>) (x => x.ActionAmount));
    }
  }

  public virtual WatchedObservableCollection<StockUnitConvertion> StockUnitConvertions
  {
    get => this._stockUnitConvertions;
    set
    {
      if (this._stockUnitConvertions != null)
        this._stockUnitConvertions.Watcher.ItemPropertyChanged -= new ItemPropertyChangedEventHandler(this.StockUnitConvertionPropertyChanged);
      this.SetProperty<WatchedObservableCollection<StockUnitConvertion>>(ref this._stockUnitConvertions, value, nameof (StockUnitConvertions));
      if (this._stockUnitConvertions != null)
        this._stockUnitConvertions.Watcher.ItemPropertyChanged += new ItemPropertyChangedEventHandler(this.StockUnitConvertionPropertyChanged);
      this.RaisePropertyChanged<Decimal>((Expression<Func<Decimal>>) (() => this.ActionTotal));
    }
  }

  protected override void Lines_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
  {
    if (e.OldItems != null)
    {
      foreach (T obj in e.OldItems.Cast<T>())
        obj.StockUnitConverterRequested -= new Mermer.Transactions.Models.StockUnitConverter(this.StockUnitConverter);
    }
    if (e.NewItems != null)
    {
      foreach (T obj in e.NewItems.Cast<T>())
      {
        obj.StockUnitConverterRequested += new Mermer.Transactions.Models.StockUnitConverter(this.StockUnitConverter);
        obj.UpdateStockUnitConvertion();
      }
    }
    base.Lines_CollectionChanged(sender, e);
    this.RaisePropertyChanged<Decimal>((Expression<Func<Decimal>>) (() => this.LineQuantitiesSum));
  }

  protected override void LinePropertyChanged(object sender, PropertyChangedEventArgs e)
  {
    base.LinePropertyChanged(sender, e);
    if (!(e.PropertyName == "Quantity"))
      return;
    this.RaisePropertyChanged<Decimal>((Expression<Func<Decimal>>) (() => this.LineQuantitiesSum));
  }

  protected virtual void Overheads_CollectionChanged(
    object sender,
    NotifyCollectionChangedEventArgs e)
  {
    if (e.OldItems != null)
    {
      foreach (StockTransactionOverhead transactionOverhead in e.OldItems.Cast<StockTransactionOverhead>())
      {
        transactionOverhead.PropertyChanged -= new PropertyChangedEventHandler(this.OverheadPropertyChanged);
        transactionOverhead.CurrencyConverterRequested -= new Mermer.Transactions.Models.CurrencyConverter(((Transaction<T>) this).CurrencyConverter);
        transactionOverhead.DisplayCurrencyIdRequested -= new CurrencyId(((Transaction<T>) this).GetDisplayCurrencyId);
        transactionOverhead.DefaultCurrencyIdRequested -= new CurrencyId(((Transaction<T>) this).GetDefaultCurrencyId);
      }
    }
    if (e.NewItems != null)
    {
      foreach (StockTransactionOverhead transactionOverhead in e.NewItems.Cast<StockTransactionOverhead>())
      {
        transactionOverhead.PropertyChanged += new PropertyChangedEventHandler(this.OverheadPropertyChanged);
        transactionOverhead.CurrencyConverterRequested += new Mermer.Transactions.Models.CurrencyConverter(((Transaction<T>) this).CurrencyConverter);
        transactionOverhead.DisplayCurrencyIdRequested += new CurrencyId(((Transaction<T>) this).GetDisplayCurrencyId);
        transactionOverhead.DefaultCurrencyIdRequested += new CurrencyId(((Transaction<T>) this).GetDefaultCurrencyId);
        transactionOverhead.UpdateCurrencyConvertion();
        transactionOverhead.UpdateDefaultCurrencyId();
      }
    }
    this.RaisePropertyChanged<Decimal>((Expression<Func<Decimal>>) (() => this.ActionOverheadTotal));
  }

  protected virtual void OverheadPropertyChanged(object sender, PropertyChangedEventArgs e)
  {
    if (!(e.PropertyName == "ActionAmount"))
      return;
    this.RaisePropertyChanged<Decimal>((Expression<Func<Decimal>>) (() => this.ActionOverheadTotal));
  }

  protected virtual void StockUnitConvertionPropertyChanged(
    object sender,
    PropertyChangedEventArgs e)
  {
    foreach (T line in (Collection<T>) this.Lines)
      line.UpdateStockUnitConvertion();
  }

  protected override void CurrencyConvertionPropertyChanged(
    object sender,
    PropertyChangedEventArgs e)
  {
    foreach (RequestCurrencyConverter overhead in (Collection<StockTransactionOverhead>) this.Overheads)
      overhead.UpdateCurrencyConvertion();
  }

  public void UpdateStockUnitConvertion()
  {
    this.Lines?.ForEach((Action<T>) (x => x.UpdateStockUnitConvertion()));
    this.RaisePropertyChanged<Decimal>((Expression<Func<Decimal>>) (() => this.ActionTotal));
  }

  public event Mermer.Transactions.Models.StockUnitConverter StockUnitConverterRequested;

  protected StockUnitConvertion GetStockUnitConvertion(string stockId, string unitId)
  {
    return this.StockUnitConverterRequested == null ? (StockUnitConvertion) null : this.StockUnitConverterRequested(stockId, unitId);
  }

  protected virtual StockUnitConvertion StockUnitConverter(string stockId, string unitId)
  {
    WatchedObservableCollection<StockUnitConvertion> stockUnitConvertions = this.StockUnitConvertions;
    StockUnitConvertion stockUnitConvertion = stockUnitConvertions != null ? stockUnitConvertions.FirstOrDefault<StockUnitConvertion>((Func<StockUnitConvertion, bool>) (x => x.StockId == stockId && x.UnitId == unitId)) : (StockUnitConvertion) null;
    if (stockUnitConvertion == null)
    {
      stockUnitConvertion = this.GetStockUnitConvertion(stockId, unitId);
      if (stockUnitConvertion != null && this.StockUnitConvertions != null)
        this.StockUnitConvertions.Add(stockUnitConvertion);
    }
    return stockUnitConvertion;
  }
}

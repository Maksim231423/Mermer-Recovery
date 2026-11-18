// Decompiled with JetBrains decompiler
// Type: Mermer.Ui.Core.ViewModels.StockManagement.StockDetailsViewModel
// Assembly: Mermer.Ui.Core, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: DC92D011-8413-44AC-9F10-F866D891CF66
// Assembly location: C:\Users\Admin\AppData\Local\Temp\Bofyhol\f9d7aa10a6\lib\net45\Mermer.Ui.Core.dll

using MvvmCross.Core.Navigation;
using MvvmCross.Core.ViewModels;
using Mermer.FundsManagement.Models;
using Mermer.StockManagement.Models;
using Mermer.StockManagement.Services;
using Mermer.Ui.Core.Helpers;
using Mermer.Data;
using Mermer.Data.Authorizers;
using Mermer.Data.Storage;
using Mermer.Data.Tools;
using Mermer.Mvvm.Services;
using Mermer.Mvvm.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

#nullable disable
namespace Mermer.Ui.Core.ViewModels.StockManagement;

public class StockDetailsViewModel : DetailsViewModel<Stock>
{
  private readonly IStockCodeGenerationService _codeGenerationService;
  private string[] _unitNames;
  private string[] _typeNames;
  private string[] _groupNames;
  private string[] _tagNames;
  private string[] _priceGroupNames;
  private StockPrice _selectedPrice;
  private StockAdditionalPrice _selectedAdditionalPrice;
  private StockUnit _selectedUnit;

  public StockDetailsViewModel(
    IRepositoryWithFacets<Stock> repository,
    Reference<Currency> currencies,
    IListAuthorizer<Stock> authorizer,
    IMvxNavigationService navigationService,
    IUserInteractionService userInteractionService,
    IStockCodeGenerationService codeGenerationService)
    : base((IRepository<Stock>) repository, authorizer, navigationService, userInteractionService)
  {
    this._codeGenerationService = codeGenerationService;
    this.Currencies = currencies;
  }

  public virtual string[] UnitNames
  {
    get => this._unitNames;
    set => this.SetProperty<string[]>(ref this._unitNames, value, nameof (UnitNames));
  }

  public virtual string[] TypeNames
  {
    get => this._typeNames;
    set => this.SetProperty<string[]>(ref this._typeNames, value, nameof (TypeNames));
  }

  public virtual string[] GroupNames
  {
    get => this._groupNames;
    set => this.SetProperty<string[]>(ref this._groupNames, value, nameof (GroupNames));
  }

  public virtual string[] TagNames
  {
    get => this._tagNames;
    set => this.SetProperty<string[]>(ref this._tagNames, value, nameof (TagNames));
  }

  public virtual string[] PriceGroupNames
  {
    get => this._priceGroupNames;
    set => this.SetProperty<string[]>(ref this._priceGroupNames, value, nameof (PriceGroupNames));
  }

  protected override Task PreLoad()
  {
    return Task.WhenAll(base.PreLoad(), this.LoadFacetsAsync(), this.Currencies.Initialize());
  }

  protected virtual async Task LoadFacetsAsync()
  {
    StockDetailsViewModel detailsViewModel = this;
    Dictionary<string, Dictionary<string, int>> facets = await ((IRepositoryWithFacets<Stock>) detailsViewModel.Repository).GetFacets("UnitNames", "TypeNames", "GroupNames", "TagNames", "PriceGroupNames");
    detailsViewModel.UnitNames = facets["UnitNames"].Select<KeyValuePair<string, int>, string>((Func<KeyValuePair<string, int>, string>) (x => x.Key)).ToArray<string>();
    detailsViewModel.TypeNames = facets["TypeNames"].Select<KeyValuePair<string, int>, string>((Func<KeyValuePair<string, int>, string>) (x => x.Key)).ToArray<string>();
    detailsViewModel.GroupNames = facets["GroupNames"].Select<KeyValuePair<string, int>, string>((Func<KeyValuePair<string, int>, string>) (x => x.Key)).ToArray<string>();
    detailsViewModel.TagNames = facets["TagNames"].Select<KeyValuePair<string, int>, string>((Func<KeyValuePair<string, int>, string>) (x => x.Key)).ToArray<string>();
    detailsViewModel.PriceGroupNames = facets["PriceGroupNames"].Select<KeyValuePair<string, int>, string>((Func<KeyValuePair<string, int>, string>) (x => x.Key)).ToArray<string>();
  }

    protected override async Task PostLoad()
    {
        await base.PostLoad();

        if (string.IsNullOrEmpty(ItemId))
        {
            Details.Code = await _codeGenerationService.GetNextCode();
        }

        if (Details.Prices == null)
            Details.Prices = new WatchedObservableCollection<StockPrice>();

        Details.Prices.Watcher.ItemsChanged += () =>
        {
            if (!string.IsNullOrEmpty(Details.CurrencyId))
                return;

            Details.CurrencyId = Currencies?.List?.SingleOrDefault(x => x.IsDefault)?.Id;
        };

        if (Details.AdditionalPrices == null)
            Details.AdditionalPrices = new WatchedObservableCollection<StockAdditionalPrice>();

        Details.AdditionalPrices.Watcher.ItemPropertyChanged += (s, e) =>
        {
            if (s is StockAdditionalPrice stockAdditionalPrice)
            {
                if (string.IsNullOrEmpty(stockAdditionalPrice.CurrencyId))
                {
                    stockAdditionalPrice.CurrencyId = Currencies?.List?.SingleOrDefault(x => x.IsDefault)?.Id;
                }
                if (stockAdditionalPrice.ValidFrom == default(DateTime))
                {
                    stockAdditionalPrice.ValidFrom = DateTime.Today;
                }
            }
        };

        if (Details.Units == null)
            Details.Units = new ObservableCollection<StockUnit>();

        Details.Units.CollectionChanged += (s, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
            {
                foreach (StockUnit stockUnit in e.NewItems.Cast<StockUnit>())
                    stockUnit.Id = Guid.NewGuid().ToString();
            }
        };

        IEnumerable<string> usedCurrencyIds = Details.Prices.Select(x => x.CurrencyId);
        Currencies.Filter = x => !x.IsDisabled || usedCurrencyIds.Contains(x.Id);
    }

    public Reference<Currency> Currencies { get; }

  public virtual StockPrice SelectedPrice
  {
    get => this._selectedPrice;
    set
    {
      if (this._selectedPrice != null)
        this._selectedPrice.PropertyChanged -= new PropertyChangedEventHandler(this.RaisePriceChangedNotification);
      this.SetProperty<StockPrice>(ref this._selectedPrice, value, nameof (SelectedPrice));
      if (this._selectedPrice == null)
        return;
      this._selectedPrice.PropertyChanged += new PropertyChangedEventHandler(this.RaisePriceChangedNotification);
    }
  }

  private void RaisePriceChangedNotification(object sender, PropertyChangedEventArgs e)
  {
    this.Details.RaisePropertyChanged("Price");
    this.Details.RaisePropertyChanged("CurrencyId");
  }

  public ICommand SelectPriceCurrencyCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.OnSelectPriceCurrencyAsync), (Func<bool>) (() => !this.IsBusy && this.SelectedPrice != null));
    }
  }

  private async Task OnSelectPriceCurrencyAsync()
  {
    StockDetailsViewModel detailsViewModel = this;
    StockPrice stockPrice = detailsViewModel.SelectedPrice;
    stockPrice.CurrencyId = await detailsViewModel.NavigationService.Navigate<ListViewModel<Currency>, string, string>(detailsViewModel.SelectedPrice.CurrencyId);
    stockPrice = (StockPrice) null;
  }

  public ICommand SelectCurrencyCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.OnSelectCurrencyAsync), (Func<bool>) (() => !this.IsBusy));
    }
  }

  private async Task OnSelectCurrencyAsync()
  {
    StockDetailsViewModel detailsViewModel = this;
    Stock stock = detailsViewModel.Details;
    stock.CurrencyId = await detailsViewModel.NavigationService.Navigate<ListViewModel<Currency>, string, string>(detailsViewModel.Details.CurrencyId);
    stock = (Stock) null;
  }

  public ICommand RemoveSelectedPriceCommand
  {
    get
    {
      return (ICommand) new MvxCommand(new Action(this.OnRemoveSelectedPrice), (Func<bool>) (() => !this.IsBusy && this.SelectedPrice != null));
    }
  }

  private void OnRemoveSelectedPrice()
  {
    StockPrice selectedPrice = this.SelectedPrice;
    this.SelectedPrice = (StockPrice) null;
    this.Details.Prices.Remove(selectedPrice);
  }

  public virtual StockAdditionalPrice SelectedAdditionalPrice
  {
    get => this._selectedAdditionalPrice;
    set
    {
      this.SetProperty<StockAdditionalPrice>(ref this._selectedAdditionalPrice, value, nameof (SelectedAdditionalPrice));
    }
  }

  public ICommand SelectAdditionalPriceCurrencyCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.OnSelectAdditionalPriceCurrencyAsync), (Func<bool>) (() => !this.IsBusy && this.SelectedAdditionalPrice != null));
    }
  }

  private async Task OnSelectAdditionalPriceCurrencyAsync()
  {
    StockDetailsViewModel detailsViewModel = this;
    StockAdditionalPrice stockAdditionalPrice = detailsViewModel.SelectedAdditionalPrice;
    stockAdditionalPrice.CurrencyId = await detailsViewModel.NavigationService.Navigate<ListViewModel<Currency>, string, string>(detailsViewModel.SelectedAdditionalPrice.CurrencyId);
    stockAdditionalPrice = (StockAdditionalPrice) null;
  }

  public ICommand RemoveSelectedAdditionalPriceCommand
  {
    get
    {
      return (ICommand) new MvxCommand(new Action(this.OnRemoveSelectedAdditionalPrice), (Func<bool>) (() => !this.IsBusy && this.SelectedAdditionalPrice != null));
    }
  }

  private void OnRemoveSelectedAdditionalPrice()
  {
    StockAdditionalPrice selectedAdditionalPrice = this.SelectedAdditionalPrice;
    this.SelectedAdditionalPrice = (StockAdditionalPrice) null;
    this.Details.AdditionalPrices.Remove(selectedAdditionalPrice);
  }

  public virtual StockUnit SelectedUnit
  {
    get => this._selectedUnit;
    set
    {
      if (this._selectedUnit != null)
        this._selectedUnit.PropertyChanged -= new PropertyChangedEventHandler(this.RaiseUnitChangedNotification);
      this.SetProperty<StockUnit>(ref this._selectedUnit, value, nameof (SelectedUnit));
      if (this._selectedUnit == null)
        return;
      this._selectedUnit.PropertyChanged += new PropertyChangedEventHandler(this.RaiseUnitChangedNotification);
    }
  }

  private void RaiseUnitChangedNotification(object sender, PropertyChangedEventArgs e)
  {
    this.Details.RaisePropertyChanged("Unit");
  }

  public ICommand RemoveSelectedUnitCommand
  {
    get
    {
      return (ICommand) new MvxCommand(new Action(this.OnRemoveSelectedUnit), (Func<bool>) (() => !this.IsBusy && this.SelectedUnit != null));
    }
  }

  private void OnRemoveSelectedUnit()
  {
    StockUnit selectedUnit = this.SelectedUnit;
    this.SelectedUnit = (StockUnit) null;
    this.Details.Units.Remove(selectedUnit);
  }

  public ICommand OpenNameComposer
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.OnOpenNameComposerAsync), (Func<bool>) (() => !this.IsBusy));
    }
  }

  private async Task OnOpenNameComposerAsync()
  {
    StockDetailsViewModel detailsViewModel = this;
    SncParams sncParams1 = new SncParams()
    {
      Name = detailsViewModel.Details.Name,
      ShortName = detailsViewModel.Details.ShortName
    };
    SncParams sncParams2 = await detailsViewModel.NavigationService.Navigate<StockNameComposerDialogViewModel, SncParams, SncParams>(sncParams1);
    if (sncParams2 == null)
      return;
    detailsViewModel.Details.Name = sncParams2.Name;
    detailsViewModel.Details.ShortName = sncParams2.ShortName;
  }

  public ICommand PrintBarcodesCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.OnPrintBarcodesAsync), (Func<bool>) (() => !this.IsBusy && !this.IsDirty));
    }
  }

  public virtual Task OnPrintBarcodesAsync()
  {
    IEnumerable<string> barcodes = this.Details.Barcodes;
    List<string> stringList = (barcodes != null ? barcodes.ToList<string>() : (List<string>) null) ?? new List<string>();
    stringList.Add(this.Details.Code);
    Currency currency = this.Currencies.List.Single<Currency>((Func<Currency, bool>) (x => x.Id == this.Details.CurrencyId));
    return this.NavigationService.Navigate<StockBarcodesPrinterViewModel, StockBarcodesPrinterParams>(new StockBarcodesPrinterParams()
    {
      Name = this.Details.Name,
      Price = $"{this.Details.Price:N} {currency.Name}",
      Barcodes = (IEnumerable<string>) stringList
    });
  }

    protected override async Task<bool> OnSaveAsync()
    {
        try
        {
            // 1. Проверка названия товара
            if (string.IsNullOrWhiteSpace(Details.Name))
            {
                throw new Exception(this["Field '{0}' is required", this["Name"]]);
            }

            // 2. Проверка кода товара
            if (string.IsNullOrWhiteSpace(Details.Code))
            {
                throw new Exception(this["Field '{0}' is required", this["Code"]]);
            }

            // 3. Единица измерения (Unit)
            if (string.IsNullOrWhiteSpace(Details.Unit))
            {
                throw new Exception(this["Field '{0}' is required", this["Unit"]]);
            }

            // 4. ПРОВЕРКА ЦЕНЫ: Обязательное поле, строго больше нуля
            bool hasValidPrice = Details.Price > 0m
                || (Details.Prices != null && Details.Prices.Any(p => p.Price > 0m));

            if (!hasValidPrice)
            {
                throw new Exception(this["Field '{0}' is required and must be greater than zero", this["Price"]]);
            }

            // 5. Валюта обязательна
            string effectiveCurrencyId = Details.CurrencyId;
            if (string.IsNullOrEmpty(effectiveCurrencyId) && Details.Prices != null)
            {
                effectiveCurrencyId = Details.Prices.FirstOrDefault(p => p.Price > 0m)?.CurrencyId;
            }

            if (string.IsNullOrEmpty(effectiveCurrencyId))
            {
                throw new Exception(this["Field '{0}' is required", this["Currency"]]);
            }
        }
        catch (Exception ex)
        {
            // Показываем сообщение об ошибке пользователю и прерываем сохранение
            UserInteractionService.ShowExceptionMessage(ex);
            return false;
        }

        return await base.OnSaveAsync();
    }
}

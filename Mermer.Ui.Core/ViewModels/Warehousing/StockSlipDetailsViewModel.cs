using Mermer.Authorization.Services;
using Mermer.Data.Authorizers;
using Mermer.Data.Storage;
using Mermer.Enterprise.Models;
using Mermer.FundsManagement.Models;
using Mermer.Mvvm.Services;
using Mermer.Services;
using Mermer.StockManagement.Models;
using Mermer.StockManagement.Models.Extenders;
using Mermer.StockManagement.Services;
using Mermer.Transactions.Services;
using Mermer.Ui.Core.Helpers;
using Mermer.Ui.Core.Services;
using Mermer.Ui.Core.ViewModels.Common;
using Mermer.Ui.Core.ViewModels.Transactions;
using Mermer.Warehousing.Models;
using MvvmCross.Core.Navigation;
using MvvmCross.Core.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

#nullable disable
namespace Mermer.Ui.Core.ViewModels.Warehousing;

public class StockSlipDetailsViewModel :
  StockTransactionDetailsViewModel<StockSlip, StockSlipLine, StockSlipType>,
  IMvxViewModel<StockSlipType>,
  IMvxViewModel
{
    private StockSlipType _newSlipType = StockSlipType.StockUsage;
    private readonly IPrintingService _printingService;

    public StockSlipDetailsViewModel(
      CopyCreate copyCreate,
      IConfigurator configurator,
      ILoginService loginService,
      StockSearcher stockSearcher,
      Reference<Currency> currencies,
      Reference<Warehouse> warehouses,
      IPrintingService printingService,
      IRepository<StockSlip> repository,
      IListAuthorizer<StockSlip> authorizer,
      IStocksRepository stocksRepository,
      IMvxNavigationService navigationService,
      ITransactionCodeGenerationService codegentor,
      IUserInteractionService userInteractionService)
      : base(copyCreate, repository, authorizer, configurator, loginService, stockSearcher, currencies, warehouses, stocksRepository, navigationService, codegentor, userInteractionService)
    {
        this._printingService = printingService;
    }

    public void Prepare(StockSlipType parameter) => this._newSlipType = parameter;

    protected override async Task PreLoad()
    {
        await base.PreLoad();

        if (this.Types == null || !this.Types.Any())
        {
            this.Types = Enum.GetValues(typeof(StockSlipType))
                .Cast<StockSlipType>()
                .Select(x => new ListHelper<StockSlipType>
                {
                    Text = this[x.ToString()],
                    Value = x
                }).ToArray();

            if (string.IsNullOrEmpty(this.ItemId))
            {
                this.CanChangeType = true;
            }
        }

        await Task.WhenAll(
            Currencies.Initialize(),
            Warehouses.Initialize()
        );
    }

    protected override async Task PostLoad()
    {
        await base.PostLoad();

        if (string.IsNullOrEmpty(ItemId))
        {
            Details.SlipType = _newSlipType;

            if (Currencies?.List != null && string.IsNullOrEmpty(Details.DisplayCurrencyId))
            {
                var active = Currencies.List.Where(c => !c.IsDisabled).ToList();
                Details.DisplayCurrencyId = active.FirstOrDefault(c => c.IsDefault)?.Id ?? active.FirstOrDefault()?.Id;
            }
        }

        // Передаем текущую валюту шапки в поисковик товаров
        if (StockSearcher != null && !string.IsNullOrEmpty(Details.DisplayCurrencyId))
        {
            var prop = StockSearcher.GetType().GetProperty("CurrencyId");
            prop?.SetValue(StockSearcher, Details.DisplayCurrencyId);
        }

        SubscribeLines();
    }

    private void SubscribeLines()
    {
        if (Details?.Lines != null)
        {
            Details.Lines.CollectionChanged -= Lines_CollectionChanged;
            Details.Lines.CollectionChanged += Lines_CollectionChanged;

            // Если открыт новый документ, в пустых строках проставляем валюту шапки
            if (string.IsNullOrEmpty(ItemId) && !string.IsNullOrEmpty(Details.DisplayCurrencyId))
            {
                foreach (var line in Details.Lines)
                {
                    if (string.IsNullOrEmpty(line.CurrencyId) || line.Price == 0m)
                    {
                        line.CurrencyId = Details.DisplayCurrencyId;
                        line.RaisePropertyChanged("CurrencyId");
                    }
                }
            }
        }
    }

    private void Lines_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is StockSlipLine line && !string.IsNullOrEmpty(Details?.DisplayCurrencyId))
                {
                    // Новая строка всегда наследует валюту шапки
                    line.CurrencyId = Details.DisplayCurrencyId;
                    line.RaisePropertyChanged("CurrencyId");
                }
            }
        }
    }

    protected override async Task<bool> OnSaveAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(Details.WarehouseId))
            {
                throw new Exception(this["Field '{0}' is required", this["Warehouse"]]);
            }

            if (Details.Lines == null || !Details.Lines.Any())
            {
                throw new Exception(this["Document cannot be empty"]);
            }

            foreach (var line in Details.Lines)
            {
                if (line.Quantity <= 0)
                    throw new Exception(this["Quantity must be greater than zero"]);

                if (Details.IsPriceEditable && line.Price < 0)
                    throw new Exception(this["Price cannot be negative"]);

                if (string.IsNullOrEmpty(line.CurrencyId))
                    throw new Exception(this["Field '{0}' is required", this["Currency"]]);
            }
        }
        catch (Exception ex)
        {
            UserInteractionService.ShowExceptionMessage(ex);
            return false;
        }

        _newSlipType = Details.SlipType;
        if (!await base.OnSaveAsync())
            return false;

        await _printingService.PrintStockSlip(Details);
        return true;
    }

    protected override void Details_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        base.Details_PropertyChanged(sender, e);

        if (e.PropertyName == "Lines")
        {
            SubscribeLines();
        }

        if (e.PropertyName == "DisplayCurrencyId")
        {
            var newCurrencyId = this.Details.DisplayCurrencyId;

            if (StockSearcher != null && !string.IsNullOrEmpty(newCurrencyId))
            {
                var prop = StockSearcher.GetType().GetProperty("CurrencyId");
                prop?.SetValue(StockSearcher, newCurrencyId);
            }

            // При смене валюты в шапке меняем валюту незаполненных строк
            if (this.Details.Lines != null)
            {
                foreach (var line in this.Details.Lines)
                {
                    if (string.IsNullOrEmpty(ItemId) && (line.Price == 0m || string.IsNullOrEmpty(line.CurrencyId)))
                    {
                        line.CurrencyId = newCurrencyId;
                        line.RaisePropertyChanged("CurrencyId");
                    }
                    line.RaisePropertyChanged("DisplayTotal");
                }
            }

            this.Details.RaisePropertyChanged("DisplayTotal");
            this.Details.RaisePropertyChanged("Lines");
        }

        if (!(e.PropertyName == "IsPriceEditable") || this.Details.IsPriceEditable)
            return;

        foreach (StockSlipLine line in (System.Collections.ObjectModel.Collection<StockSlipLine>)this.Details.Lines)
        {
            Stock fromStocksCache = this.GetFromStocksCache(line.StockId);
            Decimal num = line.ActionQuantity / line.Quantity;
            DateTime? date = new DateTime?(this.Details.Date);
            StockPrice price = fromStocksCache.GetPrice(date);
            line.Price = price.Price * num;
            line.CurrencyId = price.CurrencyId;
        }
    }

    protected override async Task OnSelectedLineEditAsync()
    {
        StockSlipDetailsViewModel detailsViewModel = this;
        Stock stocksCacheAsync = await detailsViewModel.GetFromStocksCacheAsync(detailsViewModel.SelectedLine.StockId);
        IMvxNavigationService navigationService = detailsViewModel.NavigationService;
        StockSlipDetailsLineEditViewModel.Params @params = new StockSlipDetailsLineEditViewModel.Params();
        @params.StockCode = stocksCacheAsync.Code;
        @params.StockName = stocksCacheAsync.Name;
        @params.Quantity = detailsViewModel.SelectedLine.Quantity;
        @params.UnitId = detailsViewModel.SelectedLine.UnitId;
        @params.Units = (IEnumerable<StockUnit>)stocksCacheAsync.Units;
        @params.Price = detailsViewModel.SelectedLine.Price;
        @params.CurrencyId = !string.IsNullOrEmpty(detailsViewModel.SelectedLine.CurrencyId)
            ? detailsViewModel.SelectedLine.CurrencyId
            : detailsViewModel.Details.DisplayCurrencyId;
        @params.Currencies = detailsViewModel.Currencies.List;
        @params.IsPriceEditable = detailsViewModel.Details.IsPriceEditable;
        CancellationToken cancellationToken = new CancellationToken();
        StockTransactionDetailsLineEditViewModel.Result result = await navigationService.Navigate<StockSlipDetailsLineEditViewModel, StockSlipDetailsLineEditViewModel.Params, StockTransactionDetailsLineEditViewModel.Result>(@params, cancellationToken: cancellationToken);
        if (result == null)
            return;
        detailsViewModel.SelectedLine.Quantity = result.Quantity;
        detailsViewModel.SelectedLine.UnitId = result.UnitId;
        if (!detailsViewModel.Details.IsPriceEditable)
            return;
        detailsViewModel.SelectedLine.Price = result.Price;
        detailsViewModel.SelectedLine.CurrencyId = result.CurrencyId;
    }

    public ICommand PrintCommand
    {
        get
        {
            return (ICommand)new MvxAsyncCommand(new Func<Task>(this.OnPrintCommandAsync), (Func<bool>)(() => !this.IsBusy && !this.IsDirty));
        }
    }

    protected virtual async Task OnPrintCommandAsync()
    {
        StockSlipDetailsViewModel detailsViewModel = this;
        await detailsViewModel._printingService.PrintStockSlip(detailsViewModel.Details, true);
    }
}
using Mermer.Authorization.Enums;
using Mermer.Authorization.Services;
using Mermer.Common.Settings;
using Mermer.Data;
using Mermer.Data.Authorizers;
using Mermer.Data.Extenders;
using Mermer.Data.Storage;
using Mermer.Enterprise.Models;
using Mermer.FundsManagement.Models;
using Mermer.Mvvm.Services;
using Mermer.Mvvm.ViewModels;
using Mermer.Services;
using Mermer.StockManagement.Models;
using Mermer.StockManagement.Services;
using Mermer.Transactions.Models;
using Mermer.Transactions.Services;
using Mermer.Ui.Core.Helpers;
using Mermer.Ui.Core.ViewModels.Common;
using Mermer.Ui.Core.ViewModels.StockManagement;
using Mermer.Ui.Core.ViewModels.Warehousing;
using MvvmCross.Core.Navigation;
using MvvmCross.Core.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

#nullable disable
namespace Mermer.Ui.Core.ViewModels.Transactions;

public class StockTransactionDetailsViewModel<T, TLine> :
  TransactionDetailsViewModel<T, TLine>,
  IMvxViewModel<IEnumerable<CopyCreateLine>>,
  IMvxViewModel
  where T : StockTransaction<TLine>
  where TLine : StockTransactionLine
{
    private readonly IStocksRepository _stocksRepository;
    private Decimal _addQuantity = 1M;
    private ObservableCollection<Stock> _stocksCache;
    private bool _allowReporting;
    private IEnumerable<CopyCreateLine> _stockLineCopies;
    private IMvxAsyncCommand _forceCloseCommand;

    public StockTransactionDetailsViewModel(
        CopyCreate copyCreate,
        IRepository<T> repository,
        IListAuthorizer<T> authorizer,
        IConfigurator configurator,
        ILoginService loginService,
        StockSearcher stockSearcher,
        Reference<Currency> currencies,
        Reference<Warehouse> warehouses,
        IStocksRepository stocksRepository,
        IMvxNavigationService navigationService,
        ITransactionCodeGenerationService codegentor,
        IUserInteractionService userInteractionService)
        : base(configurator, repository, authorizer, loginService, currencies, navigationService, codegentor, userInteractionService)
    {
        this._stocksRepository = stocksRepository;
        this.Warehouses = warehouses;
        this.CopyCreate = copyCreate;
        this.CopyCreate.GetLines = () => this.Details.Lines.Select(x => new CopyCreateLine
        {
            StockId = x.StockId,
            Quantity = new Decimal?(x.Quantity),
            UnitId = x.UnitId,
            Price = new Decimal?(x.Price),
            CurrencyId = x.CurrencyId
        });
        this.StockSearcher = stockSearcher;
        this.StockSearcher.ResultSelected += this.StockSearcher_ResultSelected;
    }

    public CopyCreate CopyCreate { get; }
    public StockSearcher StockSearcher { get; }
    public Reference<Warehouse> Warehouses { get; }

    public virtual Decimal AddQuantity
    {
        get => this._addQuantity;
        set => this.SetProperty<Decimal>(ref this._addQuantity, value, nameof(AddQuantity));
    }

    public ObservableCollection<Stock> StocksCache
    {
        get => this._stocksCache;
        set => this.SetProperty<ObservableCollection<Stock>>(ref this._stocksCache, value, nameof(StocksCache));
    }

    public virtual bool AllowReporting
    {
        get => this._allowReporting;
        set => this.SetProperty<bool>(ref this._allowReporting, value, nameof(AllowReporting));
    }

    public void Prepare(IEnumerable<CopyCreateLine> parameter) => this._stockLineCopies = parameter;

    protected override Task PreLoad()
    {
        this.StocksCache = new ObservableCollection<Stock>();

        _ = Task.Run(async () =>
        {
            try
            {
                await this.StockSearcher.Initialize();
            }
            catch { }
        });

        return Task.WhenAll(base.PreLoad(), this.Warehouses.Initialize());
    }

    protected override async Task OnLoad()
    {
        await base.OnLoad();
        ConnectionSettings configAsync = await Configurator.GetConfigAsync<ConnectionSettings>();
        AllowReporting = configAsync.AllowReporting;

        if (!string.IsNullOrEmpty(ItemId) || !string.IsNullOrEmpty(Details.WarehouseId))
            return;

        // Если склад по умолчанию запрещен для операций — выбираем первый разрешенный для операций
        string defaultWh = AppSettings.DefaultWarehouseId;
        if (IsWarehouseAllowed(defaultWh))
        {
            Details.WarehouseId = defaultWh;
        }
        else
        {
            var firstAllowed = Warehouses?.List?.FirstOrDefault(w => !w.IsDisabled && IsWarehouseAllowed(w.Id));
            Details.WarehouseId = firstAllowed?.Id;
        }
    }

    private bool IsWarehouseAllowed(string warehouseId)
    {
        return AccountSecurityHelper.CanOperate(LoginService, warehouseId);
    }

    protected override async Task PostLoad()
    {
        await base.PostLoad();

        Details.StockUnitConverterRequested += StockUnitConverter;

        if (Details.StockUnitConvertions == null)
            Details.StockUnitConvertions = new WatchedObservableCollection<StockUnitConvertion>();

        if (Details.Overheads == null)
            Details.Overheads = new WatchedObservableCollection<StockTransactionOverhead>();

        if (string.IsNullOrEmpty(ItemId) && _stockLineCopies != null)
        {
            await UpdateStocksCacheAsync(_stockLineCopies.Select(x => x.StockId).ToArray());
            foreach (CopyCreateLine stockLineCopy in _stockLineCopies)
            {
                TLine newLineAsync = await CreateNewLineAsync(stockLineCopy.StockId, stockLineCopy.Quantity, stockLineCopy.UnitId, stockLineCopy.Price, stockLineCopy.CurrencyId);
                Details.Lines.Add(newLineAsync);
            }
        }

        StockSearcher.WarehouseId = Details.WarehouseId;
        StockSearcher.CurrencyId = Details.DisplayCurrencyId;
        StockSearcher.ShowLastPurchasePrice = AppSettings.ShowLastPurchasePriceOnSearch;

        await LoadStocksCache();
        Details.RaisePropertyChanged("LineQuantitiesSum");

        // При создании/редактировании выпадающий список содержит только склады с правом Operate
        // Для существующего документа разрешаем отображение его склада, если есть хотя бы право Read
        Warehouses.Filter = w => !w.IsDisabled && (AccountSecurityHelper.CanOperate(LoginService, w.Id) || (!string.IsNullOrEmpty(ItemId) && w.Id == Details.WarehouseId && AccountSecurityHelper.CanRead(LoginService, w.Id)));
        Warehouses?.RaisePropertyChanged("List");
    }

    public override async Task Initialize()
    {
        await base.Initialize();

        if (_stockLineCopies == null)
            return;

        _stockLineCopies = null;
        IsDirty = true;
    }

    protected override void Details_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "WarehouseId")
            this.StockSearcher.WarehouseId = this.Details.WarehouseId;
        else if (e.PropertyName == "DisplayCurrencyId")
            this.StockSearcher.CurrencyId = this.Details.DisplayCurrencyId;
        base.Details_PropertyChanged(sender, e);
    }

    private StockUnitConvertion StockUnitConverter(string stockId, string unitId)
    {
        Stock fromStocksCache = this.GetFromStocksCache(stockId);
        StockUnit stockUnit2 = fromStocksCache?.Units?.SingleOrDefault(x => x.Id == unitId);
        if (stockUnit2 == null)
            return null;
        return new StockUnitConvertion
        {
            StockId = fromStocksCache.Id,
            UnitId = stockUnit2.Id,
            Multiplier = stockUnit2.Multiplier,
            Divider = stockUnit2.Divider
        };
    }

    private async Task LoadStocksCache()
    {
        await UpdateStocksCacheAsync(Details.Lines.Select(x => x.StockId).ToArray());
        RaisePropertyChanged(() => StocksCache);
    }

    protected Stock GetFromStocksCache(string stockId)
    {
        if (string.IsNullOrEmpty(stockId)) return null;
        return this.StocksCache?.FirstOrDefault(x => x.Id == stockId);
    }

    protected async Task<Stock> GetFromStocksCacheAsync(string stockId)
    {
        if (string.IsNullOrEmpty(stockId)) return null;
        Stock cached = this.StocksCache?.FirstOrDefault(x => x.Id == stockId);
        if (cached == null)
        {
            cached = await this._stocksRepository.GetAsync(stockId);
            if (cached != null) this.StocksCache.Add(cached);
        }
        return cached;
    }

    protected async Task UpdateStocksCacheAsync(params string[] stockIds)
    {
        string[] array = stockIds.Distinct().Where(id => !string.IsNullOrEmpty(id) && !StocksCache.Any(sc => sc.Id == id)).ToArray();

        if (!array.Any())
            return;

        foreach (Stock stock in await _stocksRepository.GetListAsync(array))
        {
            if (stock != null) StocksCache.Add(stock);
        }
    }

    protected async Task UpdateStocksCacheByCodeAsync(params string[] stockCodes)
    {
        string[] stockCodesToAdd = stockCodes.Distinct().Where(code => !StocksCache.Any(sc => sc.Code == code)).ToArray();

        if (!stockCodesToAdd.Any())
            return;

        for (int i = 0; i < stockCodesToAdd.Length; i += 100)
        {
            string[] stockCodesToAddPartial = stockCodesToAdd.Skip(i).Take(100).ToArray();
            var expressionArray = new Expression<Func<Stock, bool>>[]
            {
                x => stockCodesToAddPartial.Contains(x.Code)
            };

            foreach (Stock stock in await _stocksRepository.GetAsync(expressionArray))
            {
                if (stock != null) StocksCache.Add(stock);
            }
        }
    }

    protected async Task<Stock> GetFromStocksCacheByCodeAsync(string stockCode)
    {
        Stock cacheByCodeAsync = this.StocksCache.FirstOrDefault(x => x.Code == stockCode);
        if (cacheByCodeAsync == null)
        {
            cacheByCodeAsync = (await this._stocksRepository.GetAsync(x => x.Code == stockCode)).FirstOrDefault();
            if (cacheByCodeAsync != null) this.StocksCache.Add(cacheByCodeAsync);
        }
        return cacheByCodeAsync;
    }

    protected virtual async void StockSearcher_ResultSelected(StockSearcher searcher, StockSearchResult result)
    {
        if (result == null) return;
        TLine newLineAsync = await CreateNewLineAsync(result.Id, new Decimal?(AddQuantity), result.UnitId, new Decimal?(result.Price), result.CurrencyId);

        Details.Lines.Add(newLineAsync);
        SelectedLine = newLineAsync;
        AddQuantity = 1M;

        await OnSelectedLineEditAsync();
    }

    protected virtual async Task<TLine> CreateNewLineAsync(string stockId, Decimal? quantity = null, string unitId = null, Decimal? price = null, string currencyId = null)
    {
        return this.CreateNewLine(await this.GetFromStocksCacheAsync(stockId), quantity, unitId, price, currencyId);
    }

    protected virtual TLine CreateNewLine(Stock stock, Decimal? quantity = null, string unitId = null, Decimal? price = null, string currencyId = null)
    {
        if (price.HasValue && price.Value == 0M)
            price = null;

        TLine instance = Activator.CreateInstance<TLine>();
        instance.Id = Guid.NewGuid().ToString();
        instance.StockId = stock?.Id;
        instance.Quantity = quantity.GetValueOrDefault();
        instance.UnitId = unitId ?? stock?.UnitId;

        if (!price.HasValue || string.IsNullOrEmpty(currencyId))
        {
            decimal rawStockPrice = stock != null ? stock.Price : 0m;
            string targetCurrencyId = !string.IsNullOrEmpty(this.Details?.DisplayCurrencyId)
                ? this.Details.DisplayCurrencyId
                : (stock?.CurrencyId ?? "");

            decimal multiplier = 1m;
            decimal divider = 1m;

            if (this.Details?.CurrencyConvertions != null && !string.IsNullOrEmpty(stock?.CurrencyId))
            {
                var conv = this.Details.CurrencyConvertions.FirstOrDefault(x => x.CurrencyId == stock.CurrencyId);
                if (conv != null)
                {
                    multiplier = conv.Multiplier != 0 ? conv.Multiplier : 1m;
                    divider = conv.Divider != 0 ? conv.Divider : 1m;
                }
            }

            price = this.Details != null
                ? this.Details.GetDisplayAmount(rawStockPrice * multiplier / divider)
                : rawStockPrice;

            currencyId = targetCurrencyId;
        }

        int decimals = 2;
        if (this.Currencies?.List != null && !string.IsNullOrEmpty(currencyId))
        {
            var matchedCurrency = this.Currencies.List.FirstOrDefault(x => x.Id == currencyId);
            if (matchedCurrency != null)
                decimals = matchedCurrency.Decimals;
        }

        instance.Price = Math.Round(price.GetValueOrDefault(), decimals);
        instance.CurrencyId = currencyId;
        return instance;
    }

    public bool CheckCanClose()
    {
        if (!this.IsDirty) return true;
        var result = this.UserInteractionService.ShowMessage("Warning", "Are you sure you want to close? Changes will be lost.", UserInteractionType.YesNoCancel);
        return result == true;
    }

    public ICommand ForceCloseCommand
    {
        get
        {
            return _forceCloseCommand ??= new MvxAsyncCommand(async () =>
            {
                if (this.IsDirty)
                {
                    var result = this.UserInteractionService.ShowMessage("Warning", "Are you sure you want to close? Changes will be lost.", UserInteractionType.YesNoCancel);
                    if (result != true) return;
                }
                await this.NavigationService.Close(this);
            });
        }
    }

    public ICommand SelectedLineMinusOneCommand => new MvxCommand(OnSelectedLineMinusOne, () => !IsBusy && HasSaveAccess && IsLineSelected);
    private void OnSelectedLineMinusOne()
    {
        SelectedLine.Quantity -= 1M;
        if (SelectedLine.Quantity == 0M) SelectedLineDeleteCommand.Execute(null);
    }

    public ICommand SelectedLinePlusOneCommand => new MvxCommand(() => SelectedLine.Quantity += 1M, () => !IsBusy && HasSaveAccess && IsLineSelected);

    public ICommand SelectedLineEditCommand => new MvxAsyncCommand(OnSelectedLineEditAsync, () => !IsBusy && HasSaveAccess && IsLineSelected);

    protected virtual async Task OnSelectedLineEditAsync()
    {
        if (SelectedLine == null) return;
        Stock stocksCacheAsync = await GetFromStocksCacheAsync(SelectedLine.StockId);
        StockTransactionDetailsLineEditViewModel.Params @params = new()
        {
            StockCode = stocksCacheAsync?.Code,
            StockName = stocksCacheAsync?.Name,
            Quantity = SelectedLine.Quantity,
            UnitId = SelectedLine.UnitId,
            Units = stocksCacheAsync?.Units,
            Price = SelectedLine.Price,
            CurrencyId = SelectedLine.CurrencyId,
            Currencies = Currencies.List,
            ActionDate = Details?.Date
        };

        var result = await NavigationService.Navigate<StockTransactionDetailsLineEditViewModel, StockTransactionDetailsLineEditViewModel.Params, StockTransactionDetailsLineEditViewModel.Result>(@params);
        if (result == null) return;

        SelectedLine.Quantity = result.Quantity;
        SelectedLine.UnitId = result.UnitId;
        SelectedLine.Price = result.Price;
        SelectedLine.CurrencyId = result.CurrencyId;
    }

    public ICommand SelectedLineDeleteCommand => new MvxCommand(() => SelectedLine = Details.Lines.RemoveWithSelection(SelectedLine), () => !IsBusy && HasSaveAccess && IsLineSelected);

    public ICommand SelectWarehouseCommand => new MvxAsyncCommand(async () => Details.WarehouseId = await NavigationService.Navigate<ListViewModel<Warehouse>, string, string>(Details.WarehouseId ?? Guid.Empty.ToString()), () => !IsBusy && HasSaveAccess);

    public ICommand ShowStockTrackinsList => new MvxAsyncCommand(() => NavigationService.Navigate<StokTrackingsListViewModel, (string, string)>((Details.Id, Details.Code)), () => !IsBusy && !IsDirty && AllowReporting && AllowStockTracking());

    protected virtual bool AllowStockTracking() => Details != null && Details.IsStockIncome;

    public ICommand ImportCommand => new MvxAsyncCommand(OnImportCommandAsync, () => !IsBusy && HasSaveAccess);

    protected virtual async Task OnImportCommandAsync()
    {
        var source = await NavigationService.Navigate<DataImportViewModel, Type, IEnumerable<object>>(typeof(LineImport));
        int i = 0;
        IsBusy = true;
        SuspendLoading = true;

        try
        {
            var list = source?.Cast<LineImport>().ToArray();
            if (list != null)
            {
                int itemsCount = list.Length;
                await UpdateStocksCacheByCodeAsync(list.Select(x => x.StockCode).ToArray());

                await Task.Run(() =>
                {
                    var collection = new List<TLine>();
                    foreach (LineImport item in list)
                    {
                        i++;
                        InvokeOnMainThread(() => Status = this[$"Importing {i} of {itemsCount} lines"]);

                        Stock stock = StocksCache.FirstOrDefault(x => x.Code == item.StockCode);
                        string id1 = stock?.Units?.FirstOrDefault(x => x.Name == item.Unit)?.Id;
                        string id2 = Currencies.List.FirstOrDefault(x => x.Name == item.Currency)?.Id;

                        TLine newLine = CreateNewLine(stock, item.Quantity, id1, item.Price, id2);
                        collection.Add(newLine);
                    }

                    InvokeOnMainThread(() => Details.Lines = new WatchedObservableCollection<TLine>(collection));
                });
            }
        }
        catch (Exception ex)
        {
            UserInteractionService.ShowExceptionMessage(ex);
        }

        Status = null;
        SuspendLoading = false;
        IsBusy = false;
    }

    protected override async Task<bool> OnSaveAsync()
    {
        if (!string.IsNullOrEmpty(Details?.WarehouseId) && !AccountSecurityHelper.CanOperate(LoginService, Details.WarehouseId))
        {
            UserInteractionService.ShowMessage(this["Access Denied"], this["You do not have permission to operate with the selected warehouse."]);
            return false;
        }

        return await base.OnSaveAsync();
    }

    public class LineImport
    {
        public string StockCode { get; set; }
        public Decimal Quantity { get; set; }
        public string Unit { get; set; }
        public Decimal Price { get; set; }
        public string Currency { get; set; }
    }
}
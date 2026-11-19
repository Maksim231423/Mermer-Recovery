using Mermer.Authorization.Enums;
using Mermer.Authorization.Services;
using Mermer.Common.Models;
using Mermer.Common.Settings;
using Mermer.CRM.Models;
using Mermer.Data;
using Mermer.Data.Authorizers;
using Mermer.Data.Extenders;
using Mermer.Data.Storage;
using Mermer.Enterprise.Models;
using Mermer.Mvvm.Services;
using Mermer.Mvvm.ViewModels;
using Mermer.Services;
using Mermer.StockManagement.Models;
using Mermer.StockManagement.Services;
using Mermer.Transactions.Models.Authorizers;
using Mermer.Transactions.Services;
using Mermer.Ui.Core.Helpers;
using Mermer.Ui.Core.ViewModels.Common;
using Mermer.Warehousing.Ordering.Models;
using Mermer.Warehousing.Ordering.Services;
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
namespace Mermer.Ui.Core.ViewModels.Warehousing.Ordering;

public class AggregatedStockOrderDetailsViewModel :
  TransactionDetailsViewModel<AggregatedStockOrder>,
  IMvxViewModel<AggregatedStockOrderDetailsViewModel.Params>,
  IMvxViewModel
{
    private readonly IConfigurator _configurator;
    private readonly IStockBalancesRepository _stockBalancesRepository;
    private readonly IStockOrderActionsRepository _orderActionsRepository;
    private readonly IRepository<Stock> _stocksRepository;
    private ObservableCollection<Stock> _stocksCache;
    private ObservableCollection<ListHelper<string, Decimal>> _stocksBalances;
    private ColumnDescription[] _columns;
    private AggregatedStockOrderLine _selectedLine;
    private string[] _groupNames;
    private string[] _tagNames;
    private AggregatedStockOrderDetailsViewModel.Params _paramenter;
    private bool _canChangeDate;

    public AggregatedStockOrderDetailsViewModel(
      IConfigurator configurator,
      ILoginService loginService,
      StockSearcher stockSearcher,
      Reference<Warehouse> warehouses,
      Reference<Partner> partners,
      IRepository<Stock> stocksRepository,
      IMvxNavigationService navigationService,
      ITransactionCodeGenerationService codegentor,
      IRepository<AggregatedStockOrder> repository,
      IListAuthorizer<AggregatedStockOrder> authorizer,
      IUserInteractionService userInteractionService,
      IStockBalancesRepository stockBalancesRepository,
      IStockOrderActionsRepository orderActionsRepository)
      : base(codegentor, repository, authorizer, loginService, navigationService, userInteractionService)
    {
        this._configurator = configurator;
        this._stocksRepository = stocksRepository;
        this._stockBalancesRepository = stockBalancesRepository;
        this._orderActionsRepository = orderActionsRepository;
        this.Warehouses = warehouses;
        this.Partners = partners;
        this.StockSearcher = stockSearcher;
        this.StockSearcher.ResultSelected += new SearchResultSelected(this.StockSearcher_ResultSelected);
    }

    public bool CanChangeDate
    {
        get => this.HasSaveAccess && this._canChangeDate;
        set => this.SetProperty(ref this._canChangeDate, value, nameof(CanChangeDate));
    }

    protected override MvxInpcInterceptionResult InterceptRaisePropertyChanged(PropertyChangedEventArgs changedArgs)
    {
        if (changedArgs.PropertyName == "HasSaveAccess")
            this.RaisePropertyChanged(() => this.CanChangeDate);
        return base.InterceptRaisePropertyChanged(changedArgs);
    }

    public StockSearcher StockSearcher { get; }
    public Reference<Warehouse> Warehouses { get; }
    public Reference<Partner> Partners { get; set; }

    public ICommand SelectPartnerCommand => new MvxAsyncCommand(SelectPartnerAsync, () => !this.IsBusy && this.HasSaveAccess);

    private async Task SelectPartnerAsync()
    {
        if (Details != null)
        {
            Details.PartnerId = await NavigationService.Navigate<ListViewModel<Partner>, string, string>(Details.PartnerId);
        }
    }

    public ObservableCollection<Stock> StocksCache
    {
        get => this._stocksCache;
        set => this.SetProperty(ref this._stocksCache, value, nameof(StocksCache));
    }

    public ObservableCollection<ListHelper<string, Decimal>> StocksBalances
    {
        get => this._stocksBalances;
        set => this.SetProperty(ref this._stocksBalances, value, nameof(StocksBalances));
    }

    public ColumnDescription[] Columns
    {
        get => this._columns;
        set => this.SetProperty(ref this._columns, value, nameof(Columns));
    }

    public ICommand SelectWarehouseCommand => new MvxAsyncCommand(SelectWarehouseAsync, () => !this.IsBusy && this.HasSaveAccess);

    private async Task SelectWarehouseAsync()
    {
        if (Details != null)
        {
            Details.WarehouseId = await NavigationService.Navigate<ListViewModel<Warehouse>, string, string>(Details.WarehouseId);
        }
    }

    public override AggregatedStockOrder Details
    {
        get => base.Details;
        set
        {
            if (base.Details != null)
                base.Details.PropertyChanged -= Details_PropertyChanged;
            base.Details = value;
            if (base.Details == null)
                return;
            base.Details.PropertyChanged += Details_PropertyChanged;
        }
    }

    public AggregatedStockOrderLine SelectedLine
    {
        get => this._selectedLine;
        set
        {
            this.SetProperty(ref this._selectedLine, value, nameof(SelectedLine));
            this.RaisePropertyChanged(() => this.IsLineSelected);
        }
    }

    public bool IsLineSelected => this.SelectedLine != null;

    public virtual string[] GroupNames
    {
        get => this._groupNames;
        set => this.SetProperty(ref this._groupNames, value, nameof(GroupNames));
    }

    public virtual string[] TagNames
    {
        get => this._tagNames;
        set => this.SetProperty(ref this._tagNames, value, nameof(TagNames));
    }

    protected virtual async Task LoadFacetsAsync()
    {
        var facets = await ((IRepositoryWithFacets<AggregatedStockOrder>)this.Repository).GetFacets("GroupNames", "TagNames");
        if (facets != null)
        {
            if (facets.ContainsKey("GroupNames"))
                this.GroupNames = facets["GroupNames"].Select(x => x.Key).ToArray();

            if (facets.ContainsKey("TagNames"))
                this.TagNames = facets["TagNames"].Select(x => x.Key).ToArray();
        }
    }

    private async void Details_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "WarehouseId" && this.Details != null)
        {
            await this.UpdateStockBalances();
            this.StockSearcher.WarehouseId = this.Details.WarehouseId;
        }
    }

    public void Prepare(AggregatedStockOrderDetailsViewModel.Params parameter)
    {
        this._paramenter = parameter;
    }

    protected override Task PreLoad()
    {
        // ── ПРОВЕРКА ПРАВ НА ИЗМЕНЕНИЕ ДАТЫ ──
        if (this.LoginService?.Session != null && this.LoginService.Session.IsAdmin)
        {
            this.CanChangeDate = true;
        }
        else if (this.Authorizer is ITransactionAuthorizer<AggregatedStockOrder> txAuth)
        {
            this.CanChangeDate = txAuth.CanChangeDate();
        }
        else
        {
            this.CanChangeDate = false;
        }

        this.StocksCache = new ObservableCollection<Stock>();
        this.StocksBalances = new ObservableCollection<ListHelper<string, Decimal>>();
        return Task.WhenAll(base.PreLoad(), this.LoadFacetsAsync(), this.Warehouses.Initialize(), this.Partners.Initialize(), this.StockSearcher.Initialize());
    }

    protected override async Task PostLoad()
    {
        await base.PostLoad();

        if (Details == null) return;

        if (Details.Lines == null)
            Details.Lines = new WatchedObservableCollection<AggregatedStockOrderLine>();

        // 1. Фильтрация складов по правам сессии (скрываем склады со статусом No Access / 0)
        if (LoginService?.Session != null && !LoginService.Session.IsAdmin && LoginService.Session.Accounts != null)
        {
            var accounts = LoginService.Session.Accounts;

            Warehouses.Filter = w =>
                !w.IsDisabled &&
                (!accounts.TryGetValue(w.Id, out var level) ||
                 (level != AccountAccessLevel.None && (level.HasFlag(AccountAccessLevel.Operate) || level.HasFlag(AccountAccessLevel.Read))));
        }
        else
        {
            Warehouses.Filter = w => !w.IsDisabled;
        }

        Warehouses?.RaisePropertyChanged("List");

        // 2. Инициализация нового документа
        if (string.IsNullOrEmpty(ItemId))
        {
            if (_paramenter != null)
            {
                Details.WarehouseId = IsWarehouseAllowed(_paramenter.WarehouseId) ? _paramenter.WarehouseId : null;

                foreach (string stockId in _paramenter.StockIds)
                {
                    AggregatedStockOrderLine line = await CreateLine(stockId);
                    Details.Lines.Add(line);
                }
            }

            if (string.IsNullOrEmpty(Details.WarehouseId))
            {
                AppSettings configAsync = await _configurator.GetConfigAsync<AppSettings>();
                string defaultWhId = configAsync.DefaultWarehouseId;

                // Если склад по умолчанию запрещен — подставляем первый разрешенный
                Details.WarehouseId = IsWarehouseAllowed(defaultWhId)
                    ? defaultWhId
                    : Warehouses?.List?.FirstOrDefault(w => Warehouses.Filter(w))?.Id;
            }
        }
        else
        {
            if (!IsWarehouseAllowed(Details.WarehouseId))
            {
                Details.WarehouseId = Warehouses?.List?.FirstOrDefault(w => Warehouses.Filter(w))?.Id;
            }
        }

        StockSearcher.WarehouseId = Details.WarehouseId;
        await LoadStocksCache();

        Partners.Filter = p => !p.IsDisabled || p.Id == Details.PartnerId;

        GenerateColumns();
        this.RaisePropertyChanged(() => this.CanChangeDate);
    }

    private bool IsWarehouseAllowed(string warehouseId)
    {
        if (string.IsNullOrEmpty(warehouseId) || LoginService?.Session == null || LoginService.Session.IsAdmin)
            return true;

        var accounts = LoginService.Session.Accounts;
        if (accounts == null || accounts.Count == 0)
            return true;

        if (accounts.TryGetValue(warehouseId, out var level))
        {
            return level != AccountAccessLevel.None &&
                   (level.HasFlag(AccountAccessLevel.Operate) || level.HasFlag(AccountAccessLevel.Read));
        }

        return true;
    }

    private void GenerateColumns()
    {
        this.Columns = this.Warehouses.List.Select(x => new ColumnDescription(x.Id, x.Name)).ToArray();
    }

    private async Task LoadStocksCache()
    {
        if (Details?.Lines == null) return;
        foreach (AggregatedStockOrderLine line in Details.Lines)
        {
            await GetFromStocksCacheAsync(line.StockId);
        }
        RaisePropertyChanged(() => StocksCache);
    }

    protected Stock GetFromStocksCache(string stockId)
    {
        return this.GetFromStocksCacheAsync(stockId).GetAwaiter().GetResult();
    }

    protected async Task<Stock> GetFromStocksCacheAsync(string stockId)
    {
        Stock stock = this.StocksCache.SingleOrDefault(x => x.Id == stockId);
        if (stock == null)
        {
            stock = await this._stocksRepository.GetAsync(stockId);
            if (stock != null) this.StocksCache.Add(stock);
        }
        await this.UpdateStockBalance(stockId);
        return stock;
    }

    protected async Task UpdateStockBalances()
    {
        foreach (Model model in this.StocksCache)
            await this.UpdateStockBalance(model.Id);
    }

    protected async Task UpdateStockBalance(string stockId)
    {
        if (this.Details == null || string.IsNullOrEmpty(this.Details.WarehouseId)) return;

        Decimal num = (await this._stockBalancesRepository.GetAsync(stockId, DateTime.Now, this.Details.WarehouseId)).Sum(x => x.Balance);
        ListHelper<string, Decimal> listHelper = this.StocksBalances.SingleOrDefault(x => x.Key == stockId);
        if (listHelper == null)
        {
            listHelper = new ListHelper<string, Decimal>() { Key = stockId };
            this.StocksBalances.Add(listHelper);
        }
        listHelper.Value = num;
    }

    private async void StockSearcher_ResultSelected(StockSearcher searcher, StockSearchResult result)
    {
        if (this.Details == null || this.Details.Lines.Any(x => x.StockId == result.Id))
            return;

        AggregatedStockOrderLine line = await this.CreateLine(result.Id);
        this.Details.Lines.Add(line);
        this.SelectedLine = line;
    }

    private async Task<AggregatedStockOrderLine> CreateLine(string stockId)
    {
        Stock stock = await this.GetFromStocksCacheAsync(stockId);
        if (stock == null) return new AggregatedStockOrderLine { StockId = stockId };

        AggregatedStockOrderLine line = new AggregatedStockOrderLine()
        {
            StockId = stock.Id,
            UnitId = stock.UnitId
        };

        var orderActions = await this._orderActionsRepository.GetAsync(stock.Id);
        if (orderActions != null)
        {
            line.Orders = new WatchedDictionary<string, Decimal>(
                orderActions.GroupBy(x => x.WarehouseId)
                            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity))
            );
        }
        else
        {
            line.Orders = new WatchedDictionary<string, decimal>();
        }

        return line;
    }

    public ICommand SelectedLineDeleteCommand => new MvxCommand(this.OnSelectedLineDelete, () => !this.IsBusy && this.HasSaveAccess && this.IsLineSelected);

    private void OnSelectedLineDelete()
    {
        if (this.Details != null && this.Details.Lines != null)
        {
            this.SelectedLine = this.Details.Lines.RemoveWithSelection(this.SelectedLine);
        }
    }

    public ICommand NewTransferCommand => new MvxAsyncCommand(
        this.OnNewTransferCommandAsync,
        () => !this.IsBusy && !this.IsDirty && this.Details != null && this.Details.IsCompleted && !string.IsNullOrEmpty(this.Details.WarehouseId)
    );

    protected virtual async Task OnNewTransferCommandAsync()
    {
        if (this.Details == null) return;

        NewStockTransferDialogViewModel.Params @params = new NewStockTransferDialogViewModel.Params();
        @params.Warehouses = this.Warehouses.List.Where(x => x.Id != this.Details.WarehouseId);
        @params.SourceWarehouse = this.Warehouses.List.SingleOrDefault(x => x.Id == this.Details.WarehouseId);
        CancellationToken cancellationToken = new CancellationToken();

        string destinationWarehouseId = await this.NavigationService.Navigate<NewStockTransferDialogViewModel, NewStockTransferDialogViewModel.Params, string>(@params, cancellationToken: cancellationToken);

        if (!string.IsNullOrEmpty(destinationWarehouseId))
        {
            CopyCreateLine[] array = this.Details.Lines.SelectMany(
                x => x.Orders.Where(i => i.Key == destinationWarehouseId && i.Value > 0M),
                (x, order) => new CopyCreateLine()
                {
                    StockId = x.StockId,
                    Quantity = new Decimal?(order.Value),
                    UnitId = x.UnitId
                }).ToArray();

            if (!array.Any())
            {
                this.UserInteractionService.ShowMessage(this["Exception"], this["There are no orders for selected warehouse!"]);
            }
            else
            {
                await this.NavigationService.Navigate<StockTransferDetailsViewModel, StockTransferDetailsViewModel.Params>(new StockTransferDetailsViewModel.Params()
                {
                    SourceWarehouseId = this.Details.WarehouseId,
                    DestinationWarehouseId = destinationWarehouseId,
                    Lines = array
                });
            }
        }
    }

    public ICommand ImportCommand => new MvxAsyncCommand(this.OnImportCommandAsync, () => !this.IsBusy && this.HasSaveAccess);

    protected virtual async Task OnImportCommandAsync()
    {
        if (this.Details == null) return;

        IEnumerable<object> source = await this.NavigationService.Navigate<DataImportViewModel, Type, IEnumerable<object>>(typeof(AggregatedStockOrderDetailsViewModel.LineImport));
        int i = 0;
        this.IsBusy = true;
        this.SuspendLoading = true;
        try
        {
            var array = source?.Cast<AggregatedStockOrderDetailsViewModel.LineImport>().ToArray();
            if (array != null)
            {
                int itemsCount = array.Length;
                foreach (var item in array)
                {
                    ++i;
                    this.Status = this["Importing {0} of {1} lines", i, itemsCount];
                    Stock stock = this.StocksCache.SingleOrDefault(x => x.Code != item.StockCode);
                    if (stock == null)
                    {
                        stock = (await this._stocksRepository.GetAsync(x => x.Code == item.StockCode)).FirstOrDefault();
                        if (stock != null) this.StocksCache.Add(stock);
                    }
                    if (stock != null)
                    {
                        this.Details.Lines.Add(new AggregatedStockOrderLine { StockId = stock.Id });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            this.UserInteractionService.ShowExceptionMessage(ex);
        }
        this.Status = null;
        this.SuspendLoading = false;
        this.IsBusy = false;
    }

    protected override async Task<bool> OnSaveAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(Details?.WarehouseId))
            {
                throw new Exception(this["Field '{0}' is required", this["Warehouse"]]);
            }

            if (!IsWarehouseAllowed(Details.WarehouseId))
            {
                throw new Exception("You do not have permission to use the selected warehouse.");
            }

            if (Details.Lines == null || !Details.Lines.Any())
            {
                throw new Exception(this["Document cannot be empty"]);
            }
        }
        catch (Exception ex)
        {
            UserInteractionService.ShowExceptionMessage(ex);
            return false;
        }

        return await base.OnSaveAsync();
    }

    public class Params
    {
        public string WarehouseId { get; set; }
        public IEnumerable<string> StockIds { get; set; }
    }

    public class LineImport
    {
        public string StockCode { get; internal set; }
    }
}
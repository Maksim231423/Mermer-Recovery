using MvvmCross.Core.Navigation;
using MvvmCross.Core.ViewModels;
using MvvmCross.Plugins.Messenger;
using Mermer.Common.Settings;
using Mermer.Enterprise.Models;
using Mermer.StockManagement.Models;
using Mermer.StockManagement.Services;
using Mermer.Ui.Core.Helpers;
using Mermer.Ui.Core.ViewModels.Common;
using Mermer.Mvvm.Services;
using Mermer.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using System.Windows.Input;

#nullable disable
namespace Mermer.Ui.Core.ViewModels.StockManagement;

public class StockBalancesListViewModel :
  ListViewModelBaseWithFilterDate<StockBalanceByTypeWithBalanceAndData>
{
    private readonly IConfigurator _configurator;
    private readonly IStockBalancesRepository _repository;
    private System.Collections.Generic.List<object> _selectedWarehouseIds;
    private bool _aggregateWarehouses = true;
    private string _stockId;
    private string _selectedStockMessage;
    private bool _initialized;

    public StockBalancesListViewModel(
        IMvxMessenger messenger,
        IConfigurator configurator,
        StockSearcher stockSearcher,
        Reference<Warehouse> warehouses,
        IStockBalancesRepository repository,
        IMvxNavigationService navigationService,
        IUserInteractionService userInteractionService)
        : base(messenger, navigationService, userInteractionService)
    {
        this._repository = repository;
        this._configurator = configurator;
        this.Warehouses = warehouses;
        this.StockSearcher = stockSearcher;
        this.StockSearcher.ResultSelected += new SearchResultSelected(this.StockSearcher_ResultSelected);
    }

    public StockSearcher StockSearcher { get; }

    public Reference<Warehouse> Warehouses { get; }

    public System.Collections.Generic.List<object> SelectedWarehouseIds
    {
        get => this._selectedWarehouseIds;
        set
        {
            if (this._selectedWarehouseIds != null && value != null && this._selectedWarehouseIds.SequenceEqual(value))
                return;

            if (!this.SetProperty(ref this._selectedWarehouseIds, value ?? new List<object>(), nameof(SelectedWarehouseIds)))
                return;

            if (!this.IsBusy)
                this.Initialize();
        }
    }

    public string[] WarehouseIds
    {
        get
        {
            System.Collections.Generic.List<object> selectedWarehouseIds = this.SelectedWarehouseIds;
            return (selectedWarehouseIds != null ? selectedWarehouseIds.Cast<string>().ToArray<string>() : (string[])null) ?? Array.Empty<string>();
        }
    }

    public virtual bool AggregateWarehouses
    {
        get => this._aggregateWarehouses;
        set
        {
            if (!this.SetProperty<bool>(ref this._aggregateWarehouses, value, nameof(AggregateWarehouses)) || this.IsBusy)
                return;
            this.Initialize();
        }
    }

    public virtual string StockId
    {
        get => this._stockId;
        set
        {
            if (!this.SetProperty<string>(ref this._stockId, value, nameof(StockId)) || this.IsBusy)
                return;
            this.RaisePropertyChanged<bool>((Expression<Func<bool>>)(() => this.StockIdSelected));
            this.Initialize();
        }
    }

    public bool StockIdSelected => !string.IsNullOrEmpty(this.StockId);

    public virtual string SelectedStockMessage
    {
        get => this._selectedStockMessage;
        set
        {
            this.SetProperty<string>(ref this._selectedStockMessage, value, nameof(SelectedStockMessage));
        }
    }

    private void StockSearcher_ResultSelected(StockSearcher searcher, StockSearchResult result)
    {
        this.SelectedStockMessage = this["Showing balances for stock: {0} | {1}", new object[2]
        {
            (object) result.Code,
            (object) result.Name
        }];
        this.StockId = result.Id;
    }

    protected override async Task PreLoad()
    {
        if (!_initialized)
        {
            await Task.WhenAll(
                base.PreLoad(),
                Warehouses.Initialize(),
                StockSearcher.Initialize()
            );

            SelectedWarehouseIds = new List<object>();
            _initialized = true;
        }
        else
        {
            await base.PreLoad();
        }

        if (string.IsNullOrEmpty(StockId))
            SelectedStockMessage = this["Showing balances for all stocks"];
    }

    protected override Task<IEnumerable<StockBalanceByTypeWithBalanceAndData>> GetFilteredListByDateAsync(
        DateTime from,
        DateTime till)
    {
        return this._repository.GetByTypeAsync(this.WarehouseIds, this.StockId, from, till, this.AggregateWarehouses);
    }

    protected override Task<IEnumerable<StockBalanceByTypeWithBalanceAndData>> GetFilteredListAsync(
        ListFilter filter)
    {
        return this._repository.GetByTypeAsync(this.WarehouseIds, this.StockId, DateTime.MinValue, DateTime.MaxValue, this.AggregateWarehouses);
    }

    public ICommand RemoveSelectedStockId
    {
        get
        {
            return (ICommand)new MvxCommand(new Action(this.OnRemoveSelectedStockId), (Func<bool>)(() => !this.IsBusy));
        }
    }

    private void OnRemoveSelectedStockId() => this.StockId = (string)null;

    public ICommand SelectOrViewDetailsCommand
    {
        get
        {
            return (ICommand)new MvxAsyncCommand(new Func<Task>(this.OnSelectOrViewDetailsAsync), (Func<bool>)(() => !this.IsBusy && this.SelectedItem != null));
        }
    }

    private Task OnSelectOrViewDetailsAsync()
    {
        IMvxNavigationService navigationService = this.NavigationService;
        StockActionsFilter stockActionsFilter1 = new StockActionsFilter();
        StockActionsFilter stockActionsFilter2 = stockActionsFilter1;
        string[] strArray;
        if (this.AggregateWarehouses || string.IsNullOrEmpty(this.SelectedItem.WarehouseId))
            strArray = this.WarehouseIds;
        else
            strArray = new string[1]
            {
                this.SelectedItem.WarehouseId
            };
        stockActionsFilter2.WarehouseIds = strArray;
        stockActionsFilter1.StockId = this.SelectedItem.StockId;
        stockActionsFilter1.DateFrom = this.DateFilterFrom;
        stockActionsFilter1.DateTill = this.DateFilterTill;
        StockActionsFilter stockActionsFilter3 = stockActionsFilter1;
        return navigationService.Navigate<StockActionsListViewModel, StockActionsFilter>(stockActionsFilter3);
    }

    public ICommand ShowActionsCommand
    {
        get
        {
            return (ICommand)new MvxAsyncCommand(new Func<Task>(this.OnShowActionsAsync), (Func<bool>)(() => !this.IsBusy));
        }
    }

    private Task OnShowActionsAsync()
    {
        return this.NavigationService.Navigate<StockActionsListViewModel, StockActionsFilter>(new StockActionsFilter()
        {
            WarehouseIds = this.WarehouseIds,
            StockId = this.StockId,
            DateFrom = this.DateFilterFrom,
            DateTill = this.DateFilterTill
        });
    }

    // ОПТИМИЗАЦИЯ: Возвращаем 0 или реальное число без повторного похода в сеть
    protected override Task<int> CountFilteredListByDateAsync(DateTime from, DateTime till)
    {
        return Task.FromResult(0);
    }

    protected override Task<int> CountFilteredListAsync(ListFilter filter)
    {
        return Task.FromResult(0);
    }

    protected override Task<int> CountListAsync(params Expression<Func<StockBalanceByTypeWithBalanceAndData, bool>>[] predicates)
    {
        return Task.FromResult(0);
    }

    protected override Expression<Func<StockBalanceByTypeWithBalanceAndData, bool>> GetDateFilter(
        DateTime from,
        DateTime till)
    {
        return x => true;
    }

    protected override Task<IEnumerable<StockBalanceByTypeWithBalanceAndData>> GetListAsync(
        params Expression<Func<StockBalanceByTypeWithBalanceAndData, bool>>[] predicates)
    {
        return this._repository.GetByTypeAsync(this.WarehouseIds, this.StockId, DateTime.MinValue, DateTime.MaxValue, this.AggregateWarehouses);
    }
}
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
    private List<object> _selectedWarehouseIds;
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
        this.StockSearcher.ResultSelected += this.StockSearcher_ResultSelected;
    }

    public StockSearcher StockSearcher { get; }

    public Reference<Warehouse> Warehouses { get; }

    public List<object> SelectedWarehouseIds
    {
        get => this._selectedWarehouseIds;
        set
        {
            if (this._selectedWarehouseIds != null && value != null && this._selectedWarehouseIds.SequenceEqual(value))
                return;

            if (!this.SetProperty(ref this._selectedWarehouseIds, value ?? new List<object>(), nameof(SelectedWarehouseIds)))
                return;

            this.RaisePropertyChanged(() => this.WarehouseIds);

            if (!this.IsBusy)
            {
                this.Initialize();
                this.UpdateFilters();
            }
        }
    }

    public string[] WarehouseIds
    {
        get
        {
            if (this.SelectedWarehouseIds == null || this.SelectedWarehouseIds.Count == 0)
                return Array.Empty<string>();

            return this.SelectedWarehouseIds
                .Select(x => x?.ToString())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public virtual bool AggregateWarehouses
    {
        get => this._aggregateWarehouses;
        set
        {
            if (!this.SetProperty(ref this._aggregateWarehouses, value, nameof(AggregateWarehouses)))
                return;

            if (!this.IsBusy)
            {
                this.Initialize();
                this.UpdateFilters();
            }
        }
    }

    public virtual string StockId
    {
        get => this._stockId;
        set
        {
            if (!this.SetProperty(ref this._stockId, value, nameof(StockId)) || this.IsBusy)
                return;

            this.RaisePropertyChanged(() => this.StockIdSelected);
            this.Initialize();
            this.UpdateFilters();
        }
    }

    public bool StockIdSelected => !string.IsNullOrEmpty(this.StockId);

    public virtual string SelectedStockMessage
    {
        get => this._selectedStockMessage;
        set => this.SetProperty(ref this._selectedStockMessage, value, nameof(SelectedStockMessage));
    }

    private void StockSearcher_ResultSelected(StockSearcher searcher, StockSearchResult result)
    {
        this.SelectedStockMessage = this["Showing balances for stock: {0} | {1}", new object[2]
        {
            result.Code,
            result.Name
        }];
        this.StockId = result.Id;
    }

    protected override async Task PreLoad()
    {
        if (!_initialized)
        {
            await Task.WhenAll(
                base.PreLoad(),
                this.Warehouses.Initialize(),
                this.StockSearcher.Initialize()
            );

            this.SelectedWarehouseIds = new List<object>();
            this._initialized = true;
        }
        else
        {
            await base.PreLoad();
        }

        if (string.IsNullOrEmpty(this.StockId))
            this.SelectedStockMessage = this["Showing balances for all stocks"];
    }

    private void UpdateFilters()
    {
        try
        {
            // Уведомляем UI об изменении фильтров для пересчета значений счетчиков
            this.RaisePropertyChanged(() => this.Filters);
        }
        catch { }
    }

    // ── ЗАГРУЗКА ДАННЫХ В ТАБЛИЦУ ──

    // Плитка "All Records"
    protected override Task<IEnumerable<StockBalanceByTypeWithBalanceAndData>> GetFilteredListAsync(ListFilter filter)
    {
        return this._repository.GetByTypeAsync(this.WarehouseIds, this.StockId, DateTime.MinValue, DateTime.MaxValue, this.AggregateWarehouses);
    }

    // Фильтры по периодам дат
    protected override Task<IEnumerable<StockBalanceByTypeWithBalanceAndData>> GetFilteredListByDateAsync(DateTime from, DateTime till)
    {
        return this._repository.GetByTypeAsync(this.WarehouseIds, this.StockId, from, till, this.AggregateWarehouses);
    }

    protected override Task<IEnumerable<StockBalanceByTypeWithBalanceAndData>> GetListAsync(
        params Expression<Func<StockBalanceByTypeWithBalanceAndData, bool>>[] predicates)
    {
        return this._repository.GetByTypeAsync(this.WarehouseIds, this.StockId, DateTime.MinValue, DateTime.MaxValue, this.AggregateWarehouses);
    }

    // ── ПОДСЧЁТ СЧЁТЧИКОВ НА ПЛИТКАХ ──

    // Счётчик плитки "All Records"
    protected override async Task<int> CountFilteredListAsync(ListFilter filter)
    {
        var items = await this._repository.GetByTypeAsync(this.WarehouseIds, this.StockId, DateTime.MinValue, DateTime.MaxValue, this.AggregateWarehouses);
        return items?.Count() ?? 0;
    }

    // Счётчик для плиток дат (#Today, #This Week, #This Year и т.д.)
    protected override async Task<int> CountFilteredListByDateAsync(DateTime from, DateTime till)
    {
        var items = await this._repository.GetByTypeAsync(this.WarehouseIds, this.StockId, from, till, this.AggregateWarehouses);
        return items?.Count() ?? 0;
    }

    protected override Task<int> CountListAsync(params Expression<Func<StockBalanceByTypeWithBalanceAndData, bool>>[] predicates)
    {
        return Task.FromResult(0);
    }

    protected override Expression<Func<StockBalanceByTypeWithBalanceAndData, bool>> GetDateFilter(DateTime from, DateTime till)
    {
        return x => true;
    }

    // ── КОМАНДЫ ──

    public ICommand RemoveSelectedStockId => new MvxCommand(this.OnRemoveSelectedStockId, () => !this.IsBusy);

    private void OnRemoveSelectedStockId() => this.StockId = null;

    public ICommand SelectOrViewDetailsCommand =>
        new MvxAsyncCommand(this.OnSelectOrViewDetailsAsync, () => !this.IsBusy && this.SelectedItem != null);

    private Task OnSelectOrViewDetailsAsync()
    {
        string[] strArray;
        if (this.AggregateWarehouses || string.IsNullOrEmpty(this.SelectedItem.WarehouseId))
            strArray = this.WarehouseIds;
        else
            strArray = new[] { this.SelectedItem.WarehouseId };

        var filter = new StockActionsFilter
        {
            WarehouseIds = strArray,
            StockId = this.SelectedItem.StockId,
            DateFrom = this.DateFilterFrom,
            DateTill = this.DateFilterTill
        };

        return this.NavigationService.Navigate<StockActionsListViewModel, StockActionsFilter>(filter);
    }

    public ICommand ShowActionsCommand =>
        new MvxAsyncCommand(this.OnShowActionsAsync, () => !this.IsBusy);

    private Task OnShowActionsAsync()
    {
        var filter = new StockActionsFilter
        {
            WarehouseIds = this.WarehouseIds,
            StockId = this.StockId,
            DateFrom = this.DateFilterFrom,
            DateTill = this.DateFilterTill
        };

        return this.NavigationService.Navigate<StockActionsListViewModel, StockActionsFilter>(filter);
    }
}
using MvvmCross.Core.Navigation;
using MvvmCross.Core.ViewModels;
using Mermer.Enterprise.Models;
using Mermer.FundsManagement.Models;
using Mermer.StockManagement.Models;
using Mermer.StockManagement.Services;
using Mermer.Ui.Core.Helpers;
using Mermer.Data.Extenders;
using Mermer.Mvvm.Services;
using Mermer.Mvvm.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using System.Windows.Input;

#nullable disable
namespace Mermer.Ui.Core.ViewModels.StockManagement;

public class StockBalancesByDateAndWarehousesListViewModel : BaseViewModel
{
    private readonly IStockBalancesRepository _balancesRepository;
    private DateTime _date = DateTime.Today;
    private System.Collections.Generic.List<object> _selectedWarehouseIds;
    private string _displayCurrencyId;
    private ColumnDescription[] _columns;
    private ObservableCollection<string> _selectedStockIds;
    private bool _showAllStocks;
    private ObservableCollection<StockBalanceByWarehouses> _list;
    private StockBalanceByWarehouses _selectedItem;
    private bool _loaded;

    public System.Windows.Input.ICommand SelectOrViewDetailsCommand => new MvvmCross.Core.ViewModels.MvxCommand(() =>
    {
        if (SelectedItem != null)
        {
        }
    });

    public StockBalancesByDateAndWarehousesListViewModel(
      StockSearcher stockSearcher,
      Reference<Currency> currencies,
      Reference<Warehouse> warehouses,
      IMvxNavigationService navigationService,
      IStockBalancesRepository balancesRepository,
      IUserInteractionService userInteractionService)
      : base(navigationService, userInteractionService)
    {
        this._balancesRepository = balancesRepository;
        this.Currencies = currencies;
        this.Warehouses = warehouses;
        this.StockSearcher = stockSearcher;
        this.StockSearcher.ResultSelected += new SearchResultSelected(this.OnStockSearcherOnResultSelected);
        this.SelectedStockIds = new ObservableCollection<string>();
    }

    public StockSearcher StockSearcher { get; }
    public Reference<Currency> Currencies { get; }
    public Reference<Warehouse> Warehouses { get; }

    public virtual DateTime Date
    {
        get => this._date;
        set
        {
            if (!this.SetProperty<DateTime>(ref this._date, value, nameof(Date)) || this.IsBusy)
                return;
            this.Initialize();
        }
    }

    public DateTime DateFilterInclusive => this.Date.AddDays(1.0).Date;

    public System.Collections.Generic.List<object> SelectedWarehouseIds
    {
        get => this._selectedWarehouseIds;
        set
        {
            if (this._selectedWarehouseIds != null && value != null && this._selectedWarehouseIds.SequenceEqual(value))
                return;
            this.SetProperty(ref this._selectedWarehouseIds, value, nameof(SelectedWarehouseIds));

            // Генерируем колонки под выбранные склады
            this.GenerateColumns();

            if (this.IsBusy) return;
            this.Initialize();
        }
    }

    public IEnumerable<string> WarehouseIds
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

    public virtual string DisplayCurrencyId
    {
        get => this._displayCurrencyId;
        set
        {
            if (this.SetProperty<string>(ref this._displayCurrencyId, value, nameof(DisplayCurrencyId)) && !this.IsBusy)
                this.Initialize();
            this.StockSearcher.CurrencyId = this._displayCurrencyId;
        }
    }

    public ColumnDescription[] Columns
    {
        get => this._columns;
        set => this.SetProperty<ColumnDescription[]>(ref this._columns, value, nameof(Columns));
    }

    public ObservableCollection<string> SelectedStockIds
    {
        get => this._selectedStockIds;
        set
        {
            if (this._selectedStockIds != null)
                this._selectedStockIds.CollectionChanged -= SelectedStockIds_CollectionChanged;
            this.SetProperty(ref this._selectedStockIds, value, nameof(SelectedStockIds));
            if (this._selectedStockIds == null) return;
            this._selectedStockIds.CollectionChanged += SelectedStockIds_CollectionChanged;
        }
    }

    public bool ShowAllStocks
    {
        get => this._showAllStocks;
        set => this.SetProperty<bool>(ref this._showAllStocks, value, nameof(ShowAllStocks));
    }

    public virtual ObservableCollection<StockBalanceByWarehouses> List
    {
        get => this._list;
        set => this.SetProperty(ref this._list, value, nameof(List));
    }

    public StockBalanceByWarehouses SelectedItem
    {
        get => this._selectedItem;
        set
        {
            if (!this.SetProperty<StockBalanceByWarehouses>(ref this._selectedItem, value, nameof(SelectedItem))) return;
            this.RaisePropertyChanged(() => this.IsItemSelected);
        }
    }

    public bool IsItemSelected => this.SelectedItem != null;

    protected override async Task PreLoad()
    {
        // ИСПРАВЛЕНИЕ: Загружаем справочники только один раз, чтобы не сбрасывался выбор склада!
        if (!_loaded)
        {
            await Task.WhenAll(base.PreLoad(), this.Currencies.Initialize(), this.Warehouses.Initialize(), this.StockSearcher.Initialize());
            if (string.IsNullOrEmpty(this.DisplayCurrencyId))
            {
                var defaultCurrency = this.Currencies.List.FirstOrDefault(x => x.IsDefault);
                if (defaultCurrency != null) this.DisplayCurrencyId = defaultCurrency.Id;
            }
            this._loaded = true;
        }
        else
        {
            await base.PreLoad();
        }
    }

    protected override async Task OnLoad()
    {
        this.List = new ObservableCollection<StockBalanceByWarehouses>(await this._balancesRepository.GetByDateAndWarehousesAsync(this.DateFilterInclusive, this.WarehouseIds, this.DisplayCurrencyId, this.ShowAllStocks ? null : this.SelectedStockIds));
    }

    private async Task AddLineAsync(string stockId)
    {
        try
        {
            var balances = await this._balancesRepository.GetByDateAndWarehousesAsync(
                this.DateFilterInclusive,
                this.WarehouseIds,
                this.DisplayCurrencyId,
                new[] { stockId });

            var item = balances?.FirstOrDefault();
            if (item != null)
            {
                // Защита от NRE в CustomUnboundColumnData
                if (item.Balances == null)
                {
                    item.Balances = new Dictionary<string, decimal>();
                }

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (this.List == null)
                        this.List = new ObservableCollection<StockBalanceByWarehouses>();

                    var existing = this.List.FirstOrDefault(x => x.StockId == item.StockId);
                    if (existing != null)
                    {
                        this.List.Remove(existing);
                    }

                    this.List.Add(item);
                    this.SelectedItem = item;
                });
            }
        }
        catch (Exception ex)
        {
            this.UserInteractionService?.ShowExceptionMessage(ex);
        }
    }

    public ICommand AddAllStocksCommand => new MvxAsyncCommand(this.OnAddAllStocksCommandAsync, () => !this.IsBusy);

    private async Task OnAddAllStocksCommandAsync()
    {
        this.IsBusy = true;
        try
        {
            this.SelectedStockIds.Clear();
            this.ShowAllStocks = true;
            var balances = await this._balancesRepository.GetByDateAndWarehousesAsync(this.DateFilterInclusive, this.WarehouseIds, this.DisplayCurrencyId);
            this.List = new ObservableCollection<StockBalanceByWarehouses>(balances);
            this.SelectedStockIds = new ObservableCollection<string>(this.List.Select(x => x.StockId));
            this.SubCaption = this["All Records"];
        }
        catch (Exception ex)
        {
            this.UserInteractionService.ShowExceptionMessage(ex);
        }
        this.IsBusy = false;
    }

    public ICommand SelectedLineDeleteCommand => new MvxCommand(this.OnSelectedLineDeleteCommand, () => !this.IsBusy && this.SelectedItem != null);

    private void OnSelectedLineDeleteCommand()
    {
        if (this.SelectedStockIds.Contains(this.SelectedItem.StockId))
            this.SelectedStockIds.Remove(this.SelectedItem.StockId);
        this.SelectedItem = this.List.RemoveWithSelection(this.SelectedItem);
    }

    public ICommand ReloadCommand => new MvxAsyncCommand(this.OnReloadAsync, () => !this.IsBusy);

    private Task OnReloadAsync() => this.Initialize();

    private void OnStockSearcherOnResultSelected(StockSearcher searcher, StockSearchResult result)
    {
        if (result == null || string.IsNullOrEmpty(result.Id)) return;

        if (!this.SelectedStockIds.Contains(result.Id))
        {
            this.SelectedStockIds.Add(result.Id);
        }
        else
        {
            // Если товар уже был в списке, принудительно подгружаем/обновляем строку
            _ = this.AddLineAsync(result.Id);
        }

        // Очищаем строку поиска после выбора товара
        this.StockSearcher.SearchText = string.Empty;
    }

    private async void SelectedStockIds_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            this.ShowAllStocks = false;
            foreach (string stockId in e.NewItems.Cast<string>())
            {
                await this.AddLineAsync(stockId);
            }
        }
        this.SubCaption = this["By Selected Stocks"];
    }

    private void GenerateColumns()
    {
        this.Columns = this.Warehouses.List.Where(x => this.WarehouseIds.Contains(x.Id)).Select(x => new ColumnDescription(x.Id, x.Name)).ToArray();
    }
}
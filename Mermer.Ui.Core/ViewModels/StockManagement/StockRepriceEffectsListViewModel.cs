using MvvmCross.Core.Navigation;
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

#nullable disable
namespace Mermer.Ui.Core.ViewModels.StockManagement;

public class StockRepriceEffectsListViewModel : ListViewModelBaseWithFilterDate<StockRepriceEffect>
{
    private readonly IConfigurator _configurator;
    private readonly IStockRepriceEffectsRepository _repository;
    private IEnumerable<ListHelper<StockPriceChangeReason, string>> _priceChangeReasons;
    private System.Collections.Generic.List<object> _selectedWarehouseIds;
    private bool _initialized;

    public StockRepriceEffectsListViewModel(
      IMvxMessenger messenger,
      IConfigurator configurator,
      Reference<Warehouse> warehouses,
      IStockRepriceEffectsRepository repository,
      IMvxNavigationService navigationService,
      IUserInteractionService userInteractionService)
      : base(messenger, navigationService, userInteractionService)
    {
        this._configurator = configurator;
        this._repository = repository;
        this.Warehouses = warehouses;
        this.PriceChangeReasons = (IEnumerable<ListHelper<StockPriceChangeReason, string>>)new ListHelper<StockPriceChangeReason, string>[2]
        {
            new ListHelper<StockPriceChangeReason, string>(StockPriceChangeReason.PriceChanged, this["PriceChange", Array.Empty<object>()]),
            new ListHelper<StockPriceChangeReason, string>(StockPriceChangeReason.RateChanged, this["RateChange", Array.Empty<object>()])
        };
    }

    public IEnumerable<ListHelper<StockPriceChangeReason, string>> PriceChangeReasons
    {
        get => this._priceChangeReasons;
        set
        {
            this.SetProperty<IEnumerable<ListHelper<StockPriceChangeReason, string>>>(ref this._priceChangeReasons, value, nameof(PriceChangeReasons));
        }
    }

    public Reference<Warehouse> Warehouses { get; }

    public System.Collections.Generic.List<object> SelectedWarehouseIds
    {
        get => this._selectedWarehouseIds;
        set
        {
            if (this._selectedWarehouseIds != null && value != null && this._selectedWarehouseIds.SequenceEqual<object>((IEnumerable<object>)value))
                return;

            if (!this.SetProperty<System.Collections.Generic.List<object>>(ref this._selectedWarehouseIds, value, nameof(SelectedWarehouseIds)))
                return;

            this.RaisePropertyChanged(() => this.WarehouseIds);

            if (!this.IsBusy && this._initialized)
            {
                // Принудительно перезагружаем список и обновляем плитки
                Task.Run(async () =>
                {
                    await this.ReloadWithCountersAsync();
                });
            }
        }
    }

    private async Task ReloadWithCountersAsync()
    {
        this.IsBusy = true;
        try
        {
            await this.Initialize();

            // Оповещаем плитки о необходимости запросить Counter заново
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                this.RaisePropertyChanged(() => this.Filters);
                if (this.Filters != null)
                {
                    foreach (var filter in this.Filters)
                    {
                        filter.RaisePropertyChanged("Count");
                    }
                }
            });
        }
        catch (Exception ex)
        {
            this.UserInteractionService?.ShowExceptionMessage(ex);
        }
        finally
        {
            this.IsBusy = false;
        }
    }

    public System.Windows.Input.ICommand SelectOrViewDetailsCommand => new MvvmCross.Core.ViewModels.MvxCommand(() =>
    {
        if (SelectedItem != null)
        {
            
        }
    });

    public string[] WarehouseIds
    {
        get
        {
            var selectedWarehouseIds = this.SelectedWarehouseIds;

            if (selectedWarehouseIds == null || selectedWarehouseIds.Count == 0)
            {
                return null;
            }

            return selectedWarehouseIds.Select(x => x?.ToString()).ToArray();
        }
    }

    protected override async Task PreLoad()
    {
        // ИСПРАВЛЕНИЕ: Загружаем справочник складов и выставляем дефолтный склад ТОЛЬКО ОДИН РАЗ при открытии окна!
        if (!this._initialized)
        {
            await Task.WhenAll(base.PreLoad(), this.Warehouses.Initialize());

            var defaultWhId = this._configurator.GetConfig<AppSettings>()?.DefaultWarehouseId;
            if (!string.IsNullOrEmpty(defaultWhId))
            {
                this.SelectedWarehouseIds = new System.Collections.Generic.List<object> { defaultWhId };
            }

            this._initialized = true;
        }
        else
        {
            await base.PreLoad();
        }
    }

    protected override Task<int> CountFilteredListAsync(ListFilter filter)
    {
        return this._repository.CountAsync(DateTime.MinValue, DateTime.MaxValue, this.WarehouseIds);
    }

    protected override Task<int> CountFilteredListByDateAsync(DateTime from, DateTime till)
    {
        return this._repository.CountAsync(from, till, this.WarehouseIds);
    }

    protected override Task<IEnumerable<StockRepriceEffect>> GetFilteredListAsync(ListFilter filter)
    {
        return this._repository.GetAsync(DateTime.MinValue, DateTime.MaxValue, this.WarehouseIds);
    }

    protected override Task<IEnumerable<StockRepriceEffect>> GetFilteredListByDateAsync(
      DateTime from,
      DateTime till)
    {
        return this._repository.GetAsync(from, till, this.WarehouseIds);
    }

    protected override Task<int> CountListAsync(params Expression<Func<StockRepriceEffect, bool>>[] predicates)
    {
        return this._repository.CountAsync(DateTime.MinValue, DateTime.MaxValue, this.WarehouseIds);
    }

    protected override Task<IEnumerable<StockRepriceEffect>> GetListAsync(params Expression<Func<StockRepriceEffect, bool>>[] predicates)
    {
        return this._repository.GetAsync(DateTime.MinValue, DateTime.MaxValue, this.WarehouseIds);
    }

    protected override Expression<Func<StockRepriceEffect, bool>> GetDateFilter(DateTime from, DateTime till)
    {
        return effect => effect.ChangeDate >= from && effect.ChangeDate <= till;
    }
}
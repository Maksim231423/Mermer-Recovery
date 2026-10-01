using Humanizer;
using Mermer.Common.Settings;
using Mermer.Data.Tools.Expressions;
using Mermer.Enterprise.Models;
using Mermer.FundsManagement.Models;
using Mermer.FundsManagement.Models.Extenders;
using Mermer.Mvvm.Messages;
using Mermer.Mvvm.Services;
using Mermer.Services;
using Mermer.StockManagement.Models;
using Mermer.StockManagement.Services;
using Mermer.Ui.Core.Helpers;
using Mermer.Ui.Core.ViewModels.Common;
using MvvmCross.Core.Navigation;
using MvvmCross.Core.ViewModels;
using MvvmCross.Plugins.Messenger;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using System.Windows.Input;

#nullable disable
namespace Mermer.Ui.Core.ViewModels.StockManagement;

public class StockBalancesByStatusesListViewModel : ListViewModelBaseWithFilter<StockBalanceWithData>
{
    private readonly IConfigurator _configurator;
    private readonly IStocksRepository _stocksRepository;
    private readonly IStockBalancesRepository _balancesRepository;
    private readonly MvxSubscriptionToken _messageToken;
    private string _caption;
    private List<object> _selectedWarehouseIds;
    private string _displayCurrencyId;
    private bool _loaded;
    private IEnumerable<StockBalanceWithData> _balances = Enumerable.Empty<StockBalanceWithData>();

    public StockBalancesByStatusesListViewModel(
        IMvxMessenger messenger,
        IConfigurator configurator,
        Reference<Currency> currencies,
        Reference<Warehouse> warehouses,
        IStocksRepository stocksRepository,
        IStockBalancesRepository balancesRepository,
        IMvxNavigationService navigationService,
        IUserInteractionService userInteractionService)
        : base(messenger, navigationService, userInteractionService)
    {
        _configurator = configurator;
        _stocksRepository = stocksRepository;
        _balancesRepository = balancesRepository;
        _messageToken = messenger.Subscribe<DocumentModified<StockBalance>>(async m => await this.ReloadDataAsync(), MvxReference.Strong);
        Currencies = currencies;
        Warehouses = warehouses;

        Filters = new[]
        {
            new ListFilter
            {
                Title = this["Existing"],
                Tag = "Existing",
                CanLoad = x => !IsBusy,
                Loader = x => LoadByFilterAsync(x),
                Counter = CountByFilterAsync
            },
            new ListFilter
            {
                Title = this["Finished"],
                Tag = "Finished",
                CanLoad = x => !IsBusy,
                Loader = x => LoadByFilterAsync(x),
                Counter = CountByFilterAsync
            },
            new ListFilter
            {
                Title = this["Small Amount"],
                Tag = "Min",
                CanLoad = x => !IsBusy,
                Loader = x => LoadByFilterAsync(x),
                Counter = CountByFilterAsync
            },
            new ListFilter
            {
                Title = this["Over Limit"],
                Tag = "Max",
                CanLoad = x => !IsBusy,
                Loader = x => LoadByFilterAsync(x),
                Counter = CountByFilterAsync
            },
            new ListFilter
            {
                Title = this["All Records"],
                Tag = "All",
                CanLoad = x => !IsBusy,
                Loader = x => LoadByFilterAsync(x),
                Counter = CountByFilterAsync
            }
        };
    }

    public override string Caption
    {
        get => this._caption ?? this["StockBalance".Pluralize(), Array.Empty<object>()];
        set => this._caption = value;
    }

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

            if (!this.IsBusy && this._loaded)
            {
                this.ReloadDataCommand.Execute(null);
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

    public virtual string DisplayCurrencyId
    {
        get => this._displayCurrencyId;
        set
        {
            if (!this.SetProperty(ref this._displayCurrencyId, value, nameof(DisplayCurrencyId)))
                return;

            if (!this.IsBusy && this._loaded)
            {
                this.ReloadDataCommand.Execute(null);
            }
        }
    }

    public Reference<Currency> Currencies { get; }

    public Reference<Warehouse> Warehouses { get; }

    private ICommand _reloadDataCommand;
    public ICommand ReloadDataCommand => _reloadDataCommand ??= new MvxAsyncCommand(ReloadDataAsync);

    private async Task ReloadDataAsync()
    {
        this.IsBusy = true;
        try
        {
            await this.PreLoadBalances();
            await this.Initialize();
            this.RaisePropertyChanged(() => this.Filters);
        }
        finally
        {
            this.IsBusy = false;
        }
    }

    protected override async Task PreLoad()
    {
        if (!_loaded)
        {
            await Task.WhenAll(Currencies.Initialize(), Warehouses.Initialize());

            AppSettings configAsync = await _configurator.GetConfigAsync<AppSettings>();
            if (!string.IsNullOrEmpty(configAsync?.DefaultWarehouseId))
            {
                this._selectedWarehouseIds = new List<object> { configAsync.DefaultWarehouseId };
            }
            else
            {
                this._selectedWarehouseIds = new List<object>();
            }

            var defCurr = Currencies.List.FirstOrDefault(x => x.IsDefault)
                          ?? Currencies.List.FirstOrDefault(x => string.Equals(x.Name, "USD", StringComparison.OrdinalIgnoreCase))
                          ?? Currencies.List.FirstOrDefault();

            this._displayCurrencyId = defCurr?.Id;
            _loaded = true;
        }

        await PreLoadBalances();
        await base.PreLoad();
    }

    private async Task PreLoadBalances()
    {
        string[] whFilter = this.WarehouseIds.Length > 0 ? this.WarehouseIds : null;

        var stocks = await _stocksRepository.GetAsync();
        var balancesList = await _balancesRepository.GetAsync(null, DateTime.UtcNow, whFilter);

        var inner = (balancesList ?? Enumerable.Empty<StockBalance>())
            .GroupBy(x => x.StockId)
            .Select(g => new
            {
                StockId = g.Key,
                Income = g.Sum(x => x.Income),
                Expense = g.Sum(x => x.Expense)
            }).ToList();

        var displayCurrency = Currencies.List.FirstOrDefault(x => x.Id == DisplayCurrencyId)
                              ?? Currencies.List.FirstOrDefault(x => x.IsDefault)
                              ?? Currencies.List.FirstOrDefault();

        var displayCurrencyRate = displayCurrency?.GetRate() ?? new CurrencyRate { Multiplier = 1m, Divider = 1m };
        int displayCurrencyDecimals = displayCurrency?.Decimals ?? 2;

        decimal dispMult = displayCurrencyRate.Multiplier != 0m ? displayCurrencyRate.Multiplier : 1m;
        decimal dispDiv = displayCurrencyRate.Divider != 0m ? displayCurrencyRate.Divider : 1m;

        var query =
            from s in stocks
            join c in Currencies.List on s.CurrencyId equals c.Id into cGroup
            from curr in cGroup.DefaultIfEmpty()
            join b in inner on s.Id equals b.StockId into bGroup
            from sb in bGroup.DefaultIfEmpty()
            let cRate = curr?.GetRate() ?? new CurrencyRate { Multiplier = 1m, Divider = 1m }
            let priceConverted = curr != null && dispMult != 0m
                ? Math.Round(s.Price * (cRate.Multiplier != 0m ? cRate.Multiplier : 1m) / (cRate.Divider != 0m ? cRate.Divider : 1m) / dispMult * dispDiv, displayCurrencyDecimals)
                : s.Price
            let currentBalance = (sb != null ? sb.Income - sb.Expense : 0M)
            select new StockBalanceWithData
            {
                StockId = s.Id,
                StockCode = s.Code ?? "",
                StockName = s.Name ?? "",
                StockUnitId = s.UnitId,
                StockUnit = s.Unit ?? "",
                StockPrice = priceConverted,
                StockGroup = s.Group ?? string.Empty,
                StockType = s.Type ?? string.Empty,
                StockTags = s.Tags ?? Array.Empty<string>(),
                Income = sb?.Income ?? 0M,
                Expense = sb?.Expense ?? 0M,
                IsExisting = currentBalance > 0M,
                IsFinished = currentBalance <= 0M,
                IsOverUsed = currentBalance < 0M,
                IsFinishing = s.LimitMin.HasValue && currentBalance < s.LimitMin.Value,
                IsOverLimit = s.LimitMax.HasValue && currentBalance > s.LimitMax.Value
            };

        _balances = query.ToList();
    }

    protected override PredicateBuilder<StockBalanceWithData> GetPredicateBuilder(ListFilter filter)
    {
        PredicateBuilder<StockBalanceWithData> predicateBuilder = base.GetPredicateBuilder(filter);
        if (filter.Tag is string tag)
        {
            switch (tag)
            {
                case "Existing":
                    predicateBuilder.Add(x => x.IsExisting);
                    break;
                case "Finished":
                    predicateBuilder.Add(x => x.IsFinished);
                    break;
                case "Min":
                    predicateBuilder.Add(x => x.IsFinishing);
                    break;
                case "Max":
                    predicateBuilder.Add(x => x.IsOverLimit);
                    break;
            }
        }
        return predicateBuilder;
    }

    protected override Task<int> CountListAsync(params Expression<Func<StockBalanceWithData, bool>>[] predicates)
    {
        IEnumerable<StockBalanceWithData> current = this._balances ?? Enumerable.Empty<StockBalanceWithData>();
        if (predicates != null)
        {
            foreach (var predicate in predicates.Where(p => p != null))
            {
                current = current.Where(predicate.Compile());
            }
        }
        return Task.FromResult(current.Count());
    }

    protected override Task<IEnumerable<StockBalanceWithData>> GetListAsync(params Expression<Func<StockBalanceWithData, bool>>[] predicates)
    {
        IEnumerable<StockBalanceWithData> current = this._balances ?? Enumerable.Empty<StockBalanceWithData>();
        if (predicates != null)
        {
            foreach (var predicate in predicates.Where(p => p != null))
            {
                current = current.Where(predicate.Compile());
            }
        }
        return Task.FromResult(current);
    }

    public override void Dispose()
    {
        base.Dispose();
        this._messageToken?.Dispose();
    }

    public ICommand SelectOrViewDetailsCommand => new MvxAsyncCommand(OnSelectOrViewDetailsCommandAsync, () => !IsBusy);

    protected virtual Task OnSelectOrViewDetailsCommandAsync()
    {
        try
        {
            var type = this.GetType();
            var editCmd = type.GetProperty("EditCommand")?.GetValue(this) as ICommand;
            var selectCmd = type.GetProperty("SelectCommand")?.GetValue(this) as ICommand;

            if (selectCmd != null && selectCmd.CanExecute(null))
            {
                selectCmd.Execute(null);
            }
            else if (editCmd != null && editCmd.CanExecute(null))
            {
                editCmd.Execute(null);
            }
        }
        catch { }

        return Task.CompletedTask;
    }
}
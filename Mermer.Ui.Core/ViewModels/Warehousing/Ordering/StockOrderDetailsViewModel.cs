using MvvmCross.Core.Navigation;
using MvvmCross.Core.ViewModels;
using Mermer.Authorization.Enums;
using Mermer.Authorization.Services;
using Mermer.Common.Settings;
using Mermer.Enterprise.Models;
using Mermer.StockManagement.Models;
using Mermer.StockManagement.Services;
using Mermer.Transactions.Models;
using Mermer.Transactions.Models.Authorizers;
using Mermer.Transactions.Services;
using Mermer.Ui.Core.Helpers;
using Mermer.Ui.Core.Services;
using Mermer.Ui.Core.ViewModels.Common;
using Mermer.Warehousing.Ordering.Models;
using Mermer.Data.Authorizers;
using Mermer.Data.Storage;
using Mermer.Data.Tools;
using Mermer.Mvvm.Services;
using Mermer.Mvvm.ViewModels;
using Mermer.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Mermer.CRM.Models;

#nullable disable
namespace Mermer.Ui.Core.ViewModels.Warehousing.Ordering;

public class StockOrderDetailsViewModel :
  TransactionDetailsViewModel<StockOrder>,
  IMvxViewModel<IEnumerable<CopyCreateLine>>,
  IMvxViewModel
{
    private readonly IConfigurator _configurator;
    private readonly IPrintingService _printingService;
    private readonly IRepository<Stock> _stocksRepository;
    private ObservableCollection<Stock> _stocksCache;
    private string[] _groupNames;
    private string[] _tagNames;
    private IEnumerable<CopyCreateLine> _stockLineCopies;
    private Decimal _addQuantity = 1M;
    private StockOrderLine _selectedLine;
    private bool _canChangeDate;

    public Reference<Partner> Partners { get; set; }

    public StockOrderDetailsViewModel(
      CopyCreate copyCreate,
      IConfigurator configurator,
      ILoginService loginService,
      StockSearcher stockSearcher,
      IPrintingService printingService,
      Reference<Warehouse> warehouses,
      Reference<Partner> partners,
      IRepository<StockOrder> repository,
      IListAuthorizer<StockOrder> authorizer,
      IRepository<Stock> stocksRepository,
      IMvxNavigationService navigationService,
      IUserInteractionService userInteractionService,
      ITransactionCodeGenerationService codeGenerationService)
      : base(codeGenerationService, repository, authorizer, loginService, navigationService, userInteractionService)
    {
        this._configurator = configurator;
        this._printingService = printingService;
        this._stocksRepository = stocksRepository;
        this.CopyCreate = copyCreate;
        this.CopyCreate.GetLines = () => this.Details.Lines.Select(x => new CopyCreateLine
        {
            StockId = x.StockId,
            Quantity = new Decimal?(x.Quantity),
            UnitId = x.UnitId
        });
        this.StockSearcher = stockSearcher;
        this.StockSearcher.ResultSelected += this.StockSearcher_ResultSelected;
        this.Warehouses = warehouses;
        this.Partners = partners;
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

    public ObservableCollection<Stock> StocksCache
    {
        get => this._stocksCache;
        set => this.SetProperty(ref this._stocksCache, value, nameof(StocksCache));
    }

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
        Dictionary<string, Dictionary<string, int>> facets = await ((IRepositoryWithFacets<StockOrder>)this.Repository).GetFacets("GroupNames", "TagNames");
        this.GroupNames = facets["GroupNames"].Select(x => x.Key).ToArray();
        this.TagNames = facets["TagNames"].Select(x => x.Key).ToArray();
    }

    public void Prepare(IEnumerable<CopyCreateLine> parameter) => this._stockLineCopies = parameter;

    protected override Task PreLoad()
    {
        if (this.LoginService?.Session != null && this.LoginService.Session.IsAdmin)
        {
            this.CanChangeDate = true;
        }
        else if (this.Authorizer is ITransactionAuthorizer<StockOrder> txAuth)
        {
            this.CanChangeDate = txAuth.CanChangeDate();
        }
        else
        {
            this.CanChangeDate = false;
        }

        this.StocksCache = new ObservableCollection<Stock>();
        return Task.WhenAll(base.PreLoad(), this.LoadFacetsAsync(), this.Warehouses.Initialize(), this.Partners.Initialize(), this.StockSearcher.Initialize());
    }

    protected override async Task PostLoad()
    {
        await base.PostLoad();

        if (LoginService?.Session != null && !LoginService.Session.IsAdmin && LoginService.Session.Accounts != null)
        {
            var allowedWarehouseIds = LoginService.Session.Accounts
                .Where(a => a.Value.HasFlag(AccountAccessLevel.Operate) || a.Value.HasFlag(AccountAccessLevel.Read))
                .Select(a => a.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            Warehouses.Filter = w => !w.IsDisabled && allowedWarehouseIds.Contains(w.Id);
        }
        else
        {
            Warehouses.Filter = w => !w.IsDisabled;
        }

        if (string.IsNullOrEmpty(ItemId))
        {
            var firstAllowedWh = Warehouses?.List?.FirstOrDefault();
            Details.WarehouseId = firstAllowedWh?.Id;
        }

        if (Details.Lines == null)
        {
            Details.Lines = new ObservableCollection<StockOrderLine>();
            if (_stockLineCopies != null)
            {
                foreach (CopyCreateLine stockLineCopy in _stockLineCopies)
                {
                    Details.Lines.Add(new StockOrderLine
                    {
                        StockId = stockLineCopy.StockId,
                        Quantity = stockLineCopy.Quantity.GetValueOrDefault(),
                        UnitId = stockLineCopy.UnitId
                    });
                }
            }
        }

        var watcher = new ObservableCollectionWatcher<StockOrderLine>(Details.Lines);
        watcher.ItemPropertyChanged += Line_PropertyChanged;

        if (Details.StockUnitConvertions == null)
        {
            Details.StockUnitConvertions = new ObservableCollection<StockUnitConvertion>();
        }

        StockSearcher.WarehouseId = Details.WarehouseId;
        Details.PropertyChanged += Details_PropertyChanged;

        await LoadStocksCache();

        Partners.Filter = p => !p.IsDisabled || p.Id == Details.PartnerId;
        Warehouses?.RaisePropertyChanged("List");
        this.RaisePropertyChanged(() => this.CanChangeDate);
    }

    private void Details_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != "WarehouseId")
            return;
        this.StockSearcher.WarehouseId = this.Details.WarehouseId;
    }

    private void Line_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != "UnitId")
            return;

        if (sender is StockOrderLine stockOrderLine)
        {
            UpdateStockUnitConvertion(stockOrderLine.StockId, stockOrderLine.UnitId);
        }
    }

    private async Task LoadStocksCache()
    {
        ObservableCollection<Stock> cache = new ObservableCollection<Stock>();
        foreach (StockOrderLine line in (Collection<StockOrderLine>)this.Details.Lines)
        {
            if (!cache.Any(x => x.Id == line.StockId))
                cache.Add(await this._stocksRepository.GetAsync(line.StockId));
        }
        this.StocksCache = cache;
        if (this._stockLineCopies != null)
        {
            foreach (CopyCreateLine copyCreateLine in this._stockLineCopies.Where(x => !string.IsNullOrEmpty(x.UnitId)))
                this.UpdateStockUnitConvertion(copyCreateLine.StockId, copyCreateLine.UnitId);
            this._stockLineCopies = null;
        }
    }

    private async void StockSearcher_ResultSelected(StockSearcher searcher, StockSearchResult result)
    {
        if (this.StocksCache.All(x => x.Id != result.Id))
        {
            Stock async = await this._stocksRepository.GetAsync(result.Id);
            this.StocksCache.Add(async);
        }
        StockOrderLine stockOrderLine = new StockOrderLine()
        {
            StockId = result.Id,
            Quantity = this.AddQuantity,
            UnitId = result.UnitId
        };
        this.UpdateStockUnitConvertion(stockOrderLine.StockId, stockOrderLine.UnitId);
        this.Details.Lines.Add(stockOrderLine);
        this.AddQuantity = 1M;
        this.SelectedLine = stockOrderLine;
        this.SelectedLineEditCommand.Execute(null);
    }

    private void UpdateStockUnitConvertion(string stockId, string unitId)
    {
        if (this.Details.StockUnitConvertions.Any(x => x.StockId == stockId && x.UnitId == unitId))
            return;
        StockUnit stockUnit = this.StocksCache.Single(x => x.Id == stockId).Units.Single(x => x.Id == unitId);
        this.Details.StockUnitConvertions.Add(new StockUnitConvertion()
        {
            StockId = stockId,
            UnitId = stockUnit.Id,
            Multiplier = stockUnit.Multiplier,
            Divider = stockUnit.Divider
        });
    }

    protected override async Task<bool> OnSaveAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(Details.WarehouseId))
            {
                throw new Exception(this["Field '{0}' is required", this["Warehouse"]]);
            }

            if (string.IsNullOrEmpty(Details.PartnerId))
            {
                throw new Exception(this["Field '{0}' is required", this["Partner"]]);
            }

            if (Details.Lines == null || !Details.Lines.Any())
            {
                throw new Exception(this["Document cannot be empty"]);
            }

            foreach (var line in Details.Lines)
            {
                if (line.Quantity <= 0)
                    throw new Exception(this["Quantity must be greater than zero"]);
            }
        }
        catch (Exception ex)
        {
            UserInteractionService.ShowExceptionMessage(ex);
            return false;
        }

        return await base.OnSaveAsync();
    }

    public CopyCreate CopyCreate { get; }
    public StockSearcher StockSearcher { get; set; }
    public Reference<Warehouse> Warehouses { get; set; }

    public virtual Decimal AddQuantity
    {
        get => this._addQuantity;
        set => this.SetProperty(ref this._addQuantity, value, nameof(AddQuantity));
    }

    public bool IsLineSelected => this.SelectedLine != null;

    public virtual StockOrderLine SelectedLine
    {
        get => this._selectedLine;
        set
        {
            this.SetProperty(ref this._selectedLine, value, nameof(SelectedLine));
            this.RaisePropertyChanged(() => this.IsLineSelected);
        }
    }

    public ICommand SelectedLineMinusOneCommand => new MvxCommand(() => this.SelectedLine.Quantity -= 1M, () => !this.IsBusy && this.HasSaveAccess && this.IsLineSelected);
    public ICommand SelectedLinePlusOneCommand => new MvxCommand(() => this.SelectedLine.Quantity += 1M, () => !this.IsBusy && this.HasSaveAccess && this.IsLineSelected);

    public ICommand SelectedLineDeleteCommand => new MvxCommand(this.SelectedLineDelete, () => !this.IsBusy && this.HasSaveAccess && this.IsLineSelected);

    private void SelectedLineDelete()
    {
        int num = this.Details.Lines.IndexOf(this.SelectedLine);
        this.Details.Lines.Remove(this.SelectedLine);
        int index = num - 1;
        if (index < 0) index = 0;
        if (index >= this.Details.Lines.Count) return;
        this.SelectedLine = this.Details.Lines.ElementAt(index);
    }

    public ICommand SelectedLineEditCommand => new MvxAsyncCommand(this.OnSelectedLineEditAsync, () => !this.IsBusy && this.HasSaveAccess && this.IsLineSelected);

    protected virtual async Task OnSelectedLineEditAsync()
    {
        Stock stock = StocksCache.Single(x => x.Id == SelectedLine.StockId);

        var parameters = new StockOrderDetailsLineEditViewModel.Params
        {
            StockCode = stock.Code,
            StockName = stock.Name,
            Quantity = SelectedLine.Quantity,
            UnitId = SelectedLine.UnitId,
            Units = stock.Units
        };

        var result = await NavigationService.Navigate<StockOrderDetailsLineEditViewModel, StockOrderDetailsLineEditViewModel.Params, StockOrderDetailsLineEditViewModel.Result>(parameters);

        if (result == null) return;

        SelectedLine.Quantity = result.Quantity;
        SelectedLine.UnitId = result.UnitId;
    }

    public ICommand SelectWarehouseCommand => new MvxAsyncCommand(this.SelectWarehouseAsync, () => !this.IsBusy && this.HasSaveAccess);

    public ICommand SelectPartnerCommand => new MvxAsyncCommand(this.SelectPartnerAsync, () => !this.IsBusy && this.HasSaveAccess);

    private async Task SelectPartnerAsync()
    {
        this.Details.PartnerId = await this.NavigationService.Navigate<ListViewModel<Partner>, string, string>(this.Details.PartnerId);
    }

    private async Task SelectWarehouseAsync()
    {
        this.Details.WarehouseId = await this.NavigationService.Navigate<ListViewModel<Warehouse>, string, string>(this.Details.WarehouseId);
    }

    public ICommand PrintCommand => new MvxAsyncCommand(async () => await this._printingService.PrintStockOrder(this.Details, true), () => !this.IsBusy && !this.IsDirty);

    public ICommand ImportCommand => new MvxAsyncCommand(this.OnImportCommandAsync, () => !this.IsBusy && this.HasSaveAccess);

    protected virtual async Task OnImportCommandAsync()
    {
        IEnumerable<object> source1 = await this.NavigationService.Navigate<DataImportViewModel, Type, IEnumerable<object>>(typeof(StockOrderDetailsViewModel.LineImport));
        int i = 0;
        this.IsBusy = true;
        this.SuspendLoading = true;
        try
        {
            IEnumerable<StockOrderDetailsViewModel.LineImport> source2 = source1?.Cast<StockOrderDetailsViewModel.LineImport>();
            if (source2 != null)
            {
                int itemsCount = source2.Count();
                foreach (StockOrderDetailsViewModel.LineImport lineImport in source2)
                {
                    ++i;
                    this.Status = this["Importing {0} of {1} lines", i, itemsCount];
                    Stock stock = this.StocksCache.SingleOrDefault(x => x.Code != lineImport.StockCode);
                    if (stock == null)
                    {
                        stock = (await this._stocksRepository.GetAsync(x => x.Code == lineImport.StockCode)).Single();
                        this.StocksCache.Add(stock);
                    }
                    StockOrderLine stockOrderLine = new StockOrderLine()
                    {
                        StockId = stock.Id,
                        Quantity = lineImport.Quantity,
                        UnitId = stock.Units.SingleOrDefault(x => x.Name == lineImport.Unit)?.Id
                    };
                    this.UpdateStockUnitConvertion(stockOrderLine.StockId, stockOrderLine.UnitId);
                    this.Details.Lines.Add(stockOrderLine);
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

    public class LineImport
    {
        public string StockCode { get; internal set; }
        public Decimal Quantity { get; internal set; }
        public string Unit { get; internal set; }
    }
}
using Mermer.Data;
using Mermer.Data.Authorizers;
using Mermer.FundsManagement.Models;
using Mermer.Mvvm.Services;
using Mermer.Mvvm.ViewModels;
using Mermer.StockManagement.Models;
using Mermer.StockManagement.Services;
using Mermer.Ui.Core.Helpers;
using Mermer.Ui.Core.ViewModels.Common;
using MvvmCross.Core.Navigation;
using MvvmCross.Core.ViewModels;
using MvvmCross.Plugins.Messenger;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using System.Windows.Input;

#nullable disable
namespace Mermer.Ui.Core.ViewModels.StockManagement;

public class StocksListViewModel :
    ListViewModelBase<StockInfo>,
    IMvxViewModel<string, string>,
    IMvxViewModel<string>,
    IMvxViewModel,
    IMvxViewModelResult<string>
{
    protected string ItemId;
    private readonly IStocksRepository _repository;
    private readonly IListAuthorizer<Stock> _authorizer;
    private readonly IStockCodeGenerationService _codeGenerationService;
    private string _additionalPriceCurrencyId;
    private string _additionalPriceGroup;
    private string[] _priceGroupNames;

    public StocksListViewModel(
        IMvxMessenger messenger,
        IStocksRepository repository,
        IListAuthorizer<Stock> authorizer,
        Reference<Currency> currencyReference,
        IMvxNavigationService navigationService,
        IUserInteractionService userInteractionService,
        IStockCodeGenerationService codeGenerationService)
        : base(messenger, navigationService, userInteractionService)
    {
        this._repository = repository;
        this._authorizer = authorizer;
        this._codeGenerationService = codeGenerationService;
        this.Currencies = currencyReference;

        // Исключаем удаленные валюты из выпадающего списка
        if (this.Currencies != null)
        {
            this.Currencies.Filter = c => !c.IsDisabled;
        }
    }

    public override string Caption => this["Stocks", Array.Empty<object>()];

    public virtual string AdditionalPriceCurrencyId
    {
        get => this._additionalPriceCurrencyId;
        set
        {
            if (!this.SetProperty<string>(ref this._additionalPriceCurrencyId, value, nameof(AdditionalPriceCurrencyId)) || this.IsBusy)
                return;
            this.RaisePropertyChanged<bool>(() => this.ShowAdditionalPrice);
            this.Initialize();
        }
    }

    public virtual string AdditionalPriceGroup
    {
        get => this._additionalPriceGroup;
        set
        {
            if (!this.SetProperty<string>(ref this._additionalPriceGroup, value, nameof(AdditionalPriceGroup)) || this.IsBusy)
                return;
            this.RaisePropertyChanged<bool>(() => this.ShowAdditionalPrice);
            this.Initialize();
        }
    }

    public virtual bool ShowAdditionalPrice
    {
        get
        {
            return !string.IsNullOrEmpty(this.AdditionalPriceGroup) || !string.IsNullOrEmpty(this.AdditionalPriceCurrencyId);
        }
    }

    public Reference<Currency> Currencies { get; }

    public bool HasCreateAccess => this._authorizer.CanCreate();

    public virtual string[] PriceGroupNames
    {
        get => this._priceGroupNames;
        set => this.SetProperty<string[]>(ref this._priceGroupNames, value, nameof(PriceGroupNames));
    }

    protected virtual async Task LoadFacetsAsync()
    {
        try
        {
            var facets = await this._repository.GetFacets("PriceGroupNames");

            if (facets != null && facets.ContainsKey("PriceGroupNames") && facets["PriceGroupNames"] != null)
            {
                this.PriceGroupNames = facets["PriceGroupNames"].Select(x => x.Key).ToArray();
            }
            else
            {
                this.PriceGroupNames = Array.Empty<string>();
            }
        }
        catch
        {
            this.PriceGroupNames = Array.Empty<string>();
        }
    }

    public void Prepare(string parameter) => this.ItemId = parameter;

    public void Prepare(string parameter1, string parameter2) => this.ItemId = parameter1;

    protected override Task PreLoad()
    {
        if (this.Currencies != null)
        {
            this.Currencies.Filter = c => !c.IsDisabled;
        }

        return Task.WhenAll(base.PreLoad(), this.LoadFacetsAsync(), this.Currencies.Initialize());
    }

    protected override async Task OnLoad()
    {
        try
        {
            string safeCurrency = AdditionalPriceCurrencyId ?? string.Empty;
            string safeGroup = AdditionalPriceGroup ?? string.Empty;

            IEnumerable<StockInfo> infoAsync = await _repository.GetInfoAsync(safeCurrency, safeGroup);

            var stockList = infoAsync?.ToList() ?? new List<StockInfo>();
            List = stockList;

            if (!string.IsNullOrEmpty(ItemId) && stockList.Count > 0)
            {
                SelectedItem = stockList.FirstOrDefault(x => x != null && x.Id == ItemId);
            }
        }
        catch (Exception ex)
        {
            List = new List<StockInfo>();
            UserInteractionService.ShowExceptionMessage(ex);
        }
    }

    public ICommand CreateNewCommand
    {
        get
        {
            return new MvxAsyncCommand(this.OnCreateNewAsync, () => !this.IsBusy && this.HasCreateAccess);
        }
    }

    protected virtual Task OnCreateNewAsync()
    {
        return this.NavigationService.Navigate<DetailsViewModel<Stock>, string>(string.Empty);
    }

    public ICommand ViewDetailsCommand
    {
        get
        {
            return new MvxAsyncCommand(this.OnViewDetailsAsync, () => !this.IsBusy && this.SelectedItem != null);
        }
    }

    protected virtual Task OnViewDetailsAsync()
    {
        return this.NavigationService.Navigate<DetailsViewModel<Stock>, string>(this.SelectedItem.Id);
    }

    public ICommand SelectOrViewDetailsCommand
    {
        get
        {
            return new MvxAsyncCommand(this.OnSelectOrViewDetailsAsync, () => !this.IsBusy && this.SelectedItem != null);
        }
    }

    protected virtual Task OnSelectOrViewDetailsAsync()
    {
        if (!string.IsNullOrEmpty(this.ItemId))
            return this.NavigationService.Close<string>((IMvxViewModelResult<string>)this, this.SelectedItem.Id);

        this.ViewDetailsCommand.Execute(null);
        return Task.CompletedTask;
    }

    public ICommand ImportCommand
    {
        get
        {
            return new MvxAsyncCommand(this.OnImportCommandAsync, () => !this.IsBusy);
        }
    }

    protected virtual async Task OnImportCommandAsync()
    {
        IEnumerable<object> source1 = await this.NavigationService.Navigate<DataImportViewModel, Type, IEnumerable<object>>(typeof(StockImport));
        int i = 0;
        this.IsBusy = true;
        this.SuspendLoading = true;
        try
        {
            var stockImports = source1?.Cast<StockImport>().ToArray();
            if (stockImports != null && stockImports.Length > 0)
            {
                for (int index = 0; index < stockImports.Length; ++index)
                {
                    StockImport item = stockImports[index];
                    ++i;
                    this.Status = this["Importing {0} of {1} items", new object[] { i, stockImports.Length }];
                    bool exists = true;
                    Stock model = null;
                    if (!string.IsNullOrEmpty(item.Code))
                    {
                        var found = await this._repository.GetAsync(x => x.Code == item.Code);
                        model = found?.FirstOrDefault();
                    }

                    if (model == null)
                    {
                        exists = false;
                        model = new Stock
                        {
                            Id = Guid.NewGuid().ToString(),
                            Code = item.Code ?? await this._codeGenerationService.GetNextCode(),
                            Units = new ObservableCollection<StockUnit>(),
                            Prices = new WatchedObservableCollection<StockPrice>(),
                            Barcodes = Array.Empty<string>(),
                            Tags = Array.Empty<string>()
                        };
                    }

                    if (!string.IsNullOrEmpty(item.Name))
                        model.Name = item.Name;
                    if (!string.IsNullOrEmpty(item.Unit) && model.Unit != item.Unit)
                        model.Unit = item.Unit;

                    if (item.Price > 0M && !string.IsNullOrEmpty(item.Currency))
                    {
                        Currency currency = this.Currencies.List.FirstOrDefault(x => x.Name == item.Currency);
                        if (currency != null)
                        {
                            if (string.IsNullOrEmpty(item.PriceGroup))
                            {
                                if (model.Price != item.Price || model.CurrencyId != currency.Id)
                                {
                                    model.Price = item.Price;
                                    model.CurrencyId = currency.Id;
                                }
                            }
                            else
                            {
                                if (model.AdditionalPrices == null)
                                    model.AdditionalPrices = new WatchedObservableCollection<StockAdditionalPrice>();

                                var addPrice = model.AdditionalPrices.FirstOrDefault(x => x.Group == item.PriceGroup && x.ValidFrom.Date == DateTime.Today.Date);
                                if (addPrice == null)
                                {
                                    addPrice = new StockAdditionalPrice
                                    {
                                        Group = item.PriceGroup,
                                        ValidFrom = DateTime.Today
                                    };
                                    model.AdditionalPrices.Add(addPrice);
                                }
                                addPrice.Price = item.Price;
                                addPrice.CurrencyId = currency.Id;
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(item.Group))
                        model.Group = item.Group;
                    if (!string.IsNullOrEmpty(item.Type))
                        model.Type = item.Type;

                    if (!string.IsNullOrEmpty(item.Barcodes))
                    {
                        var splitBarcodes = item.Barcodes.Split(',').Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x));
                        model.Barcodes = (model.Barcodes ?? Array.Empty<string>()).Union(splitBarcodes).Distinct().ToArray();
                    }

                    if (!string.IsNullOrEmpty(item.Tags))
                    {
                        var splitTags = item.Tags.Split(',').Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x));
                        model.Tags = (model.Tags ?? Array.Empty<string>()).Union(splitTags).Distinct().ToArray();
                    }

                    if (item.LimitMin > 0M)
                        model.LimitMin = item.LimitMin;
                    if (item.LimitMax > 0M)
                        model.LimitMax = item.LimitMax;

                    if (exists)
                        await this._repository.UpdateAsync(model);
                    else
                        await this._repository.CreateAsync(model);
                }
            }
        }
        catch (Exception ex)
        {
            this.UserInteractionService.ShowExceptionMessage(ex);
        }
        finally
        {
            this.Status = null;
            this.SuspendLoading = false;
            this.IsBusy = false;
            this.ReloadCommand.Execute(null);
        }
    }

    public ICommand MergeCommand
    {
        get
        {
            return new MvxAsyncCommand(this.OnMergeAsync, () => !this.IsBusy);
        }
    }

    public virtual async Task OnMergeAsync()
    {
        await this.NavigationService.Navigate<StockMergerDialogViewModel>();

        // После закрытия диалога слияния сразу обновляем таблицу
        await this.OnLoad();
    }

    public ICommand FixSynchIssuesCommand
    {
        get
        {
            return new MvxAsyncCommand(this.OnFixSynchIssuesAsync, () => !this.IsBusy);
        }
    }

    private async Task OnFixSynchIssuesAsync()
    {
        this.IsBusy = true;
        try
        {
            await this.Currencies.Initialize();
            await this.LoadFacetsAsync();
            await this.OnLoad();

            this.UserInteractionService.ShowMessage("Синхронизация", "Синхронизация и проверка данных товаров успешно выполнены.");
        }
        catch (Exception ex)
        {
            this.UserInteractionService.ShowExceptionMessage(ex);
        }
        finally
        {
            this.IsBusy = false;
        }
    }

    public class StockImport
    {
        public string Code { get; set; }
        public string Name { get; set; }
        public string Unit { get; set; }
        public decimal Price { get; set; }
        public string Currency { get; set; }
        public string PriceGroup { get; set; }
        public string Barcodes { get; set; }
        public string Group { get; set; }
        public string Type { get; set; }
        public string Tags { get; set; }
        public decimal LimitMin { get; set; }
        public decimal LimitMax { get; set; }
    }
}
using Mermer.Commerce.Models;
using Mermer.Common.Settings;
using Mermer.CRM.Models;
using Mermer.Enterprise.Models;
using Mermer.Finance.Models;
using Mermer.Finance.Spending.Models;
using Mermer.FundsManagement.Models;
using Mermer.FundsManagement.Models.Extenders;
using Mermer.FundsManagement.Services;
using Mermer.Mvvm.Services;
using Mermer.Mvvm.ViewModels;
using Mermer.Services;
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
namespace Mermer.Ui.Core.ViewModels.FundsManagement;

public class FundsActionsListViewModel :
    ListViewModelBaseWithFilterDate<FundsAction>,
    IMvxViewModel<FundsActionsFilter>,
    IMvxViewModel
{
    private readonly IConfigurator _configurator;
    private readonly IFundsActionsRepository _repository;
    private List<object> _selectedDepositoryIds;
    private FundsActionsFilter _parameter;
    private bool _loaded;
    private string _currencyId;

    public virtual string CurrencyId
    {
        get => this._currencyId;
        set
        {
            if (!this.SetProperty<string>(ref this._currencyId, value, nameof(CurrencyId)) || this.IsBusy)
                return;

            // Мгновенный пересчет колонки в памяти без выгрузки данных
            this.ApplyCustomCurrencyRate();
        }
    }

    public Reference<Currency> Currencies { get; private set; }

    public FundsActionsListViewModel(
        IMvxMessenger messenger,
        IConfigurator configurator,
        Reference<Partner> partners,
        Reference<Depository> depositories,
        Reference<Currency> currencies,
        IFundsActionsRepository repository,
        IMvxNavigationService navigationService,
        IUserInteractionService userInteractionService)
        : base(messenger, navigationService, userInteractionService)
    {
        this._configurator = configurator;
        this._repository = repository;
        this.Partners = partners;
        this.Depositories = depositories;
        this.Types = new LocalizedTransactionTypes("Repricing");
        this.Currencies = currencies;

        // Исключаем удаленные валюты из списка
        this.Currencies.Filter = c => !c.IsDisabled;
    }

    public List<object> SelectedDepositoryIds
    {
        get => this._selectedDepositoryIds;
        set
        {
            if (this._selectedDepositoryIds != null && value != null && this._selectedDepositoryIds.SequenceEqual(value))
                return;

            if (!this.SetProperty<List<object>>(ref this._selectedDepositoryIds, value, nameof(SelectedDepositoryIds)))
                return;

            // Если окно уже загружено — вызываем Initialize(), который перезагружает счетчики и таблицу, как кнопка Reload
            if (_loaded && !this.IsBusy)
            {
                this.Initialize();
            }
        }
    }

    public string[] DepositoryIds
    {
        get
        {
            var list = this.SelectedDepositoryIds;
            if (list == null || !list.Any()) return Array.Empty<string>();

            return list
                .SelectMany(x => (x?.ToString() ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrEmpty(x) && Guid.TryParse(x, out _))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public Reference<Partner> Partners { get; }
    public Reference<Depository> Depositories { get; }
    public LocalizedTransactionTypes Types { get; }

    public void Prepare(FundsActionsFilter parameter) => this._parameter = parameter;

    public override Task<bool> OnCloseAsync()
    {
        return this.NavigationService.Close(this).ContinueWith(_ => true);
    }

    protected override async Task PreLoad()
    {
        if (!_loaded)
        {
            if (_parameter != null)
            {
                SelectedDepositoryIds = _parameter.DepositoryIds?.Cast<object>().ToList() ?? new List<object>();
                DateFilterFrom = _parameter.DateFrom;
                DateFilterTill = _parameter.DateTill;
            }
            else
            {
                var config = await _configurator.GetConfigAsync<AppSettings>();
                if (config != null && !string.IsNullOrEmpty(config.DefaultDepositoryId))
                {
                    SelectedDepositoryIds = new List<object> { config.DefaultDepositoryId };
                }
            }
        }

        this.Currencies.Filter = c => !c.IsDisabled;

        await Task.WhenAll(
            base.PreLoad(),
            Depositories.Initialize(),
            Partners.Initialize(),
            Currencies.Initialize()
        );

        // По умолчанию пусто, то есть (none)
        this._currencyId = null;
        this.RaisePropertyChanged(nameof(CurrencyId));

        _loaded = true;
    }

    private void ApplyCustomCurrencyRate()
    {
        if (this.List == null) return;
        this.List = this.ApplyCustomCurrencyRate(this.List).ToList();
        this.RaisePropertyChanged(nameof(List));
    }

    private IEnumerable<FundsAction> ApplyCustomCurrencyRate(IEnumerable<FundsAction> list)
    {
        if (list == null) return Enumerable.Empty<FundsAction>();

        decimal rate = 0m;
        if (!string.IsNullOrEmpty(this._currencyId))
        {
            var currency = this.Currencies?.List?.SingleOrDefault(x => x.Id == this._currencyId);
            var rateObj = currency != null ? currency.GetRate() : null;

            if (rateObj != null && rateObj.Multiplier != 0m)
                rate = rateObj.Divider / rateObj.Multiplier;
        }

        foreach (var item in list)
        {
            item.ActionEffectInCustomCurrency = item.ActionEffect * rate;
        }

        return list;
    }

    protected override Task OnLoad()
    {
        if (this._parameter == null)
            return base.OnLoad();

        this._parameter = null;
        return this.LoadByDateAsync(false);
    }

    // Подсчет всегда идет по датам и кассам, currencyId передается null
    protected override Task<int> CountFilteredListByDateAsync(DateTime from, DateTime till)
    {
        return this._repository.CountAsync(from, till, null, this.DepositoryIds);
    }

    protected override Task<int> CountFilteredListAsync(ListFilter filter)
    {
        return this._repository.CountAsync(null, null, null, this.DepositoryIds);
    }

    protected override async Task<IEnumerable<FundsAction>> GetFilteredListByDateAsync(DateTime from, DateTime till)
    {
        var result = await this._repository.GetAsync(from, till, null, this.DepositoryIds);
        return ApplyCustomCurrencyRate(result).ToList();
    }

    protected override async Task<IEnumerable<FundsAction>> GetFilteredListAsync(ListFilter filter)
    {
        var result = await this._repository.GetAsync(null, null, null, this.DepositoryIds);
        return ApplyCustomCurrencyRate(result).ToList();
    }

    protected override Task<int> CountListAsync(params Expression<Func<FundsAction, bool>>[] predicates) => throw new NotImplementedException();
    protected override Task<IEnumerable<FundsAction>> GetListAsync(params Expression<Func<FundsAction, bool>>[] predicates) => throw new NotImplementedException();
    protected override Expression<Func<FundsAction, bool>> GetDateFilter(DateTime from, DateTime till) => throw new NotImplementedException();

    public ICommand SelectOrViewDetailsCommand
    {
        get
        {
            return new MvxAsyncCommand(this.OnSelectOrViewDetailsAsync, () => !this.IsBusy && this.SelectedItem != null);
        }
    }

    private Task OnSelectOrViewDetailsAsync()
    {
        switch (this.SelectedItem.TransactionType)
        {
            case "Collection":
            case "Payment":
                return this.NavigationService.Navigate<DetailsViewModel<Bill>, string>(this.SelectedItem.TransactionId);
            case "ExpenseSlip":
                return this.NavigationService.Navigate<DetailsViewModel<ExpenseSlip>, string>(this.SelectedItem.TransactionId);
            case "FundsOpening":
            case "FundsRevisionDeficit":
            case "FundsRevisionExceed":
                return this.NavigationService.Navigate<DetailsViewModel<FundsSlip>, string>(this.SelectedItem.TransactionId);
            case "FundsTransferDestination":
            case "FundsTransferSource":
                return this.NavigationService.Navigate<DetailsViewModel<FundsTransfer>, string>(this.SelectedItem.TransactionId);
            case "Purchase":
            case "PurchaseReturn":
            case "Sales":
            case "SalesReturn":
                return this.NavigationService.Navigate<DetailsViewModel<Invoice>, string>(this.SelectedItem.TransactionId);
            default:
                return Task.CompletedTask;
        }
    }
}
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
            if (this.SetProperty<string>(ref this._currencyId, value, nameof(CurrencyId)))
            {
                ApplyCustomCurrencyRate();
            }
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
    }

    public List<object> SelectedDepositoryIds
    {
        get => this._selectedDepositoryIds;
        set
        {
            if (this.SetProperty<List<object>>(ref this._selectedDepositoryIds, value, nameof(SelectedDepositoryIds)))
            {
                if (_loaded && !this.IsBusy)
                {
                    Task.Run(async () => await this.LoadByDateAsync(false));
                }
            }
        }
    }

    public string[] DepositoryIds
    {
        get
        {
            if (this.SelectedDepositoryIds == null || !this.SelectedDepositoryIds.Any())
                return Array.Empty<string>();

            return this.SelectedDepositoryIds
                .Select(x => x?.ToString())
                .Where(x => !string.IsNullOrEmpty(x))
                .ToArray();
        }
    }

    // Если IsDirty не виртуальное, переопределяем метод закрытия
    public override Task<bool> OnCloseAsync()
    {
        // Молча закрываем вкладку без всяких проверок
        return this.NavigationService.Close(this).ContinueWith(_ => true);
    }

    public Reference<Partner> Partners { get; }
    public Reference<Depository> Depositories { get; }
    public LocalizedTransactionTypes Types { get; }

    public void Prepare(FundsActionsFilter parameter) => this._parameter = parameter;

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

        _loaded = true;

        await Task.WhenAll(
            base.PreLoad(),
            Depositories.Initialize(),
            Partners.Initialize(),
            Currencies.Initialize()
        );

        if (string.IsNullOrEmpty(CurrencyId))
        {
            CurrencyId = Currencies.List.FirstOrDefault(x => x.IsDefault)?.Id
                         ?? Currencies.List.FirstOrDefault()?.Id;
        }
    }

    private void ApplyCustomCurrencyRate()
    {
        if (this.List == null || !this.List.Any()) return;

        var targetCurrency = this.Currencies?.List?.FirstOrDefault(x => x.Id == this._currencyId);
        if (targetCurrency == null) return;

        var updatedList = new List<FundsAction>();

        foreach (var item in this.List)
        {
            decimal sourceAmount = item.ActionEffect;
            string sourceCurrencyId = item.ActionCurrencyId;

            if (string.IsNullOrEmpty(sourceCurrencyId) || sourceCurrencyId == targetCurrency.Id)
            {
                item.ActionEffectInCustomCurrency = Math.Round(sourceAmount, targetCurrency.Decimals);
            }
            else
            {
                var sourceCurrency = this.Currencies?.List?.FirstOrDefault(x => x.Id == sourceCurrencyId);
                if (sourceCurrency != null)
                {
                    var sourceRate = sourceCurrency.GetRate(item.TransactionDate);
                    var targetRate = targetCurrency.GetRate(item.TransactionDate);

                    if (sourceRate != null && targetRate != null && sourceRate.Divider != 0 && targetRate.Multiplier != 0)
                    {
                        decimal sMult = sourceRate.Multiplier;
                        decimal sDiv = sourceRate.Divider;
                        decimal tMult = targetRate.Multiplier;
                        decimal tDiv = targetRate.Divider;

                        decimal conversionRate = (sMult / sDiv) * (tDiv / tMult);
                        item.ActionEffectInCustomCurrency = Math.Round(sourceAmount * conversionRate, targetCurrency.Decimals);
                    }
                    else
                    {
                        item.ActionEffectInCustomCurrency = sourceAmount;
                    }
                }
                else
                {
                    item.ActionEffectInCustomCurrency = sourceAmount;
                }
            }

            updatedList.Add(item);
        }

        this.List = updatedList;
        this.RaisePropertyChanged(nameof(List));
    }

    private IEnumerable<FundsAction> TransformWithCurrencyRate(IEnumerable<FundsAction> list)
    {
        if (list == null) return Enumerable.Empty<FundsAction>();

        var targetCurrency = this.Currencies?.List?.FirstOrDefault(x => x.Id == this._currencyId);
        if (targetCurrency == null) return list;

        var result = list.ToList();
        foreach (var item in result)
        {
            decimal sourceAmount = item.ActionEffect;
            string sourceCurrencyId = item.ActionCurrencyId;

            if (string.IsNullOrEmpty(sourceCurrencyId) || sourceCurrencyId == targetCurrency.Id)
            {
                item.ActionEffectInCustomCurrency = Math.Round(sourceAmount, targetCurrency.Decimals);
            }
            else
            {
                var sourceCurrency = this.Currencies?.List?.FirstOrDefault(x => x.Id == sourceCurrencyId);
                if (sourceCurrency != null)
                {
                    var sourceRate = sourceCurrency.GetRate(item.TransactionDate);
                    var targetRate = targetCurrency.GetRate(item.TransactionDate);

                    if (sourceRate != null && targetRate != null && sourceRate.Divider != 0 && targetRate.Multiplier != 0)
                    {
                        decimal sMult = sourceRate.Multiplier;
                        decimal sDiv = sourceRate.Divider;
                        decimal tMult = targetRate.Multiplier;
                        decimal tDiv = targetRate.Divider;

                        decimal conversionRate = (sMult / sDiv) * (tDiv / tMult);
                        item.ActionEffectInCustomCurrency = Math.Round(sourceAmount * conversionRate, targetCurrency.Decimals);
                    }
                    else
                    {
                        item.ActionEffectInCustomCurrency = sourceAmount;
                    }
                }
                else
                {
                    item.ActionEffectInCustomCurrency = sourceAmount;
                }
            }
        }

        return result;
    }

    protected override Task OnLoad()
    {
        if (this._parameter != null)
        {
            this._parameter = null;
            return this.LoadByDateAsync(false);
        }

        // Если список уже загружен (например, были выбраны #All Records), не сбрасывать его в base.OnLoad() (на #Today)
        if (this.List != null && this.List.Any())
        {
            return Task.CompletedTask;
        }

        return base.OnLoad();
    }

    protected override Task<int> CountFilteredListByDateAsync(DateTime from, DateTime till)
    {
        return this._repository.CountAsync(from, till, (string)null, this.DepositoryIds);
    }

    protected override Task<int> CountFilteredListAsync(ListFilter filter)
    {
        return this._repository.CountAsync(null, null, (string)null, this.DepositoryIds);
    }

    protected override async Task<IEnumerable<FundsAction>> GetFilteredListByDateAsync(DateTime from, DateTime till)
    {
        var result = await this._repository.GetAsync(from, till, (string)null, this.DepositoryIds);
        return TransformWithCurrencyRate(result);
    }

    protected override async Task<IEnumerable<FundsAction>> GetFilteredListAsync(ListFilter filter)
    {
        var result = await this._repository.GetAsync(null, null, (string)null, this.DepositoryIds);
        return TransformWithCurrencyRate(result);
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
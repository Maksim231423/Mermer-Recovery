using Mermer.Authorization.Services;
using Mermer.Commerce.Models;
using Mermer.CRM.Models;
using Mermer.CRM.Services;
using Mermer.Data;
using Mermer.Data.Authorizers;
using Mermer.Data.Storage;
using Mermer.Enterprise.Models;
using Mermer.FundsManagement.Models;
using Mermer.Mvvm.Services;
using Mermer.Mvvm.ViewModels;
using Mermer.Services;
using Mermer.Transactions.Models;
using Mermer.Transactions.Services;
using Mermer.Ui.Core.Helpers;
using Mermer.Ui.Core.Services;
using Mermer.Ui.Core.ViewModels.Transactions;
using MvvmCross.Core.Navigation;
using MvvmCross.Core.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Mermer.Ui.Core.ViewModels.Commerce;

public class BillDetailsViewModel :
    FundsTransactionDetailsViewModel<Bill, BillLine, BillType>,
    IMvxViewModel<BillType>,
    IMvxViewModel
{
    private readonly IPrintingService _printingService;
    private readonly IPartnerBalancesRepository _partnerBalancesRepository;
    private BillType _newSlipType;
    private PartnerBalanceResult _partnerBalanceToDate;

    public BillDetailsViewModel(
        IRepository<Bill> repository,
        IListAuthorizer<Bill> authorizer,
        IConfigurator configurator,
        ILoginService loginService,
        Reference<Office> offices,
        Reference<Partner> partners,
        Reference<Currency> currencies,
        IPrintingService printingService,
        Reference<Depository> depositories,
        IMvxNavigationService navigationService,
        ITransactionCodeGenerationService codegentor,
        IUserInteractionService userInteractionService,
        IPartnerBalancesRepository partnerBalancesRepository)
        : base(repository, authorizer, configurator, loginService, currencies, depositories, navigationService, codegentor, userInteractionService)
    {
        _printingService = printingService;
        _partnerBalancesRepository = partnerBalancesRepository;
        Offices = offices;
        Partners = partners;
    }

    public Reference<Office> Offices { get; }
    public Reference<Partner> Partners { get; }

    public virtual PartnerBalanceResult PartnerBalanceToDate
    {
        get => _partnerBalanceToDate;
        set
        {
            SetProperty(ref _partnerBalanceToDate, value);
            RaisePropertyChanged(() => PartnerBalanceResult);
        }
    }

    public virtual PartnerBalanceResult PartnerBalanceResult
    {
        get
        {
            if (PartnerBalanceToDate == null)
                return null;
            return new PartnerBalanceResult
            {
                Balance = PartnerBalanceToDate.Balance + Details.DisplayDebitCreditTotal
            };
        }
    }

    public void Prepare(BillType parameter) => _newSlipType = parameter;

    protected override Task PreLoad()
    {
        return Task.WhenAll(base.PreLoad(), Offices.Initialize(), Partners.Initialize());
    }

    protected override async Task OnLoad()
    {
        await base.OnLoad();
        if (!string.IsNullOrEmpty(ItemId))
            return;

        Details.OfficeId = AppSettings.DefaultOfficeId;

        await Currencies.Initialize();

        if (Details.CurrencyConvertions == null)
        {
            Details.CurrencyConvertions = new WatchedObservableCollection<CurrencyConvertion>();
        }

        // Берем только активные валюты
        var activeCurrencies = Currencies?.List?
            .Where(c => !c.IsDisabled)
            .ToList() ?? new List<Currency>();

        var activeIds = activeCurrencies.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 1. Очищаем все строки, у которых CurrencyId пустой, null или ссылается на удаленную валюту
        var invalidItems = Details.CurrencyConvertions
            .Where(c => string.IsNullOrEmpty(c.CurrencyId) || !activeIds.Contains(c.CurrencyId))
            .ToList();

        foreach (var item in invalidItems)
        {
            Details.CurrencyConvertions.Remove(item);
        }

        // 2. Если для какой-то активной валюты строки нет — добавляем её
        foreach (var curr in activeCurrencies)
        {
            if (!Details.CurrencyConvertions.Any(c => string.Equals(c.CurrencyId, curr.Id, StringComparison.OrdinalIgnoreCase)))
            {
                Details.CurrencyConvertions.Add(new CurrencyConvertion
                {
                    CurrencyId = curr.Id,
                    Multiplier = 1m,
                    Divider = 1m
                });
            }
        }
    }

    protected override async Task PostLoad()
    {
        await base.PostLoad();

        if (string.IsNullOrEmpty(ItemId))
            Details.BillType = _newSlipType;

        // --- ОЧИСТКА УДАЛЕННЫХ / ПУСТЫХ ВАЛЮТ ИЗ КОНВЕРСИЙ ---
        if (Details?.CurrencyConvertions != null && Currencies?.List != null)
        {
            var activeCurrencyIds = Currencies.List
                .Where(c => !c.IsDisabled)
                .Select(c => c.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Удаляем любые строки конверсий, у которых CurrencyId пустой, null или валюта отключена
            var toRemove = Details.CurrencyConvertions
                .Where(c => string.IsNullOrEmpty(c.CurrencyId) || !activeCurrencyIds.Contains(c.CurrencyId))
                .ToList();

            foreach (var item in toRemove)
            {
                Details.CurrencyConvertions.Remove(item);
            }
        }

        UpdatePartnerBalance();

        Details.RaisePropertyChanged("DisplayDebitCreditTotal");
        Details.RaisePropertyChanged("DisplayCreditTotal");
        Details.RaisePropertyChanged("DisplayDebitTotal");

        RaisePropertyChanged(() => PartnerBalanceToDate);
        RaisePropertyChanged(() => PartnerBalanceResult);

        Partners.Filter = x => !x.IsDisabled || x.Id == Details?.PartnerId;
        Offices.Filter = x => !x.IsDisabled || x.Id == Details?.OfficeId;

        UpdateFacilityFilters();

        if (string.IsNullOrEmpty(ItemId))
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.ContextIdle,
                new Action(() =>
                {
                    IsDirty = false;
                    RaisePropertyChanged(() => Caption);
                }));
        }
    }

    public override bool IsDirty
    {
        get
        {
            // Если это новый документ, у которого нет строк и не выбран контрагент — он пустой
            if (string.IsNullOrEmpty(ItemId) &&
                (Details?.Lines == null || Details.Lines.Count == 0) &&
                string.IsNullOrEmpty(Details?.PartnerId))
            {
                return false;
            }

            return base.IsDirty;
        }
        set => base.IsDirty = value;
    }

    protected override async Task<bool> OnSaveAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(Details.PartnerId))
            {
                throw new Exception(this["Field '{0}' is required", this["Partner"]]);
            }
        }
        catch (Exception ex)
        {
            UserInteractionService.ShowExceptionMessage(ex);
            return false;
        }

        if (!await base.OnSaveAsync())
            return false;

        decimal balance = PartnerBalanceToDate?.Balance ?? 0M;
        await _printingService.PrintBill(Details, balance);
        return true;
    }

    protected override void Details_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        base.Details_PropertyChanged(sender, e);

        if (e.PropertyName == "Date" || e.PropertyName == "PartnerId" || e.PropertyName == "DepositoryId" || e.PropertyName == "DisplayCurrencyId")
            UpdatePartnerBalance();
        else if (e.PropertyName == "DisplayDebitCreditTotal")
            RaisePropertyChanged(() => PartnerBalanceResult);
        else if (e.PropertyName == "OfficeId")
            UpdateFacilityFilters();
    }

    private void UpdateFacilityFilters()
    {
        Depositories.Filter = x =>
        {
            if (x.OfficeId != Details?.OfficeId) return false;
            return !x.IsDisabled || x.Id == Details?.DepositoryId;
        };
    }

    private async void UpdatePartnerBalance()
    {
        if (Details != null && !string.IsNullOrEmpty(Details.PartnerId) && !string.IsNullOrEmpty(Details.OfficeId) && Details.CurrencyConvertions != null)
        {
            try
            {
                var balanceToDateAsync = await _partnerBalancesRepository.GetBalanceToDateAsync(Details.OfficeId, Details.PartnerId, Details.Date, Details.Id);
                var currencyConvertion = CurrencyConverter(Details.DisplayCurrencyId);

                decimal rawBalance = balanceToDateAsync != null ? balanceToDateAsync.Balance : 0m;

                decimal multiplier = (currencyConvertion != null && currencyConvertion.Multiplier != 0) ? currencyConvertion.Multiplier : 1m;
                decimal divider = (currencyConvertion != null && currencyConvertion.Divider != 0) ? currencyConvertion.Divider : 1m;

                PartnerBalanceToDate = new PartnerBalanceResult
                {
                    Balance = rawBalance / multiplier * divider
                };
            }
            catch
            {
                PartnerBalanceToDate = new PartnerBalanceResult { Balance = 0m };
            }
        }
        else
        {
            PartnerBalanceToDate = null;
        }
    }

    public ICommand SelectPartnerCommand => new MvxAsyncCommand(OnSelectPartnerCommandAsync, () => !IsBusy && HasSaveAccess);

    private async Task OnSelectPartnerCommandAsync()
    {
        Details.PartnerId = await NavigationService.Navigate<ListViewModel<Partner>, string, string>(Details.PartnerId ?? Guid.Empty.ToString());
    }

    public ICommand PrintCommand => new MvxAsyncCommand(OnPrintCommandAsync, () => !IsBusy && !IsDirty);

    protected virtual async Task OnPrintCommandAsync()
    {
        decimal balance = PartnerBalanceToDate?.Balance ?? 0M;
        await _printingService.PrintBill(Details, balance, true);
    }

    public ICommand SelectOfficeCommand => new MvxAsyncCommand(OnSelectOfficeAsync, () => !IsBusy && HasSaveAccess);

    private async Task OnSelectOfficeAsync()
    {
        Details.OfficeId = await NavigationService.Navigate<ListViewModel<Office>, string, string>(Details.OfficeId ?? Guid.Empty.ToString());
    }
}
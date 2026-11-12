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
using System.Collections.Specialized;
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
        return Task.WhenAll(
            base.PreLoad(),
            Offices.Initialize(),
            Partners.Initialize(),
            Depositories.Initialize()
        );
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

        // 1. Гарантируем наличие актуальных курсов валют для пересчета
        if (Details != null)
        {
            if (Details.CurrencyConvertions == null)
                Details.CurrencyConvertions = new WatchedObservableCollection<CurrencyConvertion>();

            var activeCurrencies = Currencies?.List?.Where(c => !c.IsDisabled).ToList() ?? new List<Currency>();
            var activeIds = activeCurrencies.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var toRemove = Details.CurrencyConvertions
                .Where(c => string.IsNullOrEmpty(c.CurrencyId) || !activeIds.Contains(c.CurrencyId))
                .ToList();

            foreach (var item in toRemove)
                Details.CurrencyConvertions.Remove(item);

            var docDate = Details.Date != default ? Details.Date.Date : DateTime.Today;

            foreach (var curr in activeCurrencies)
            {
                CurrencyRate matchedRate = null;
                if (curr.Rates != null && curr.Rates.Any())
                {
                    matchedRate = curr.Rates
                        .Where(r => r.ValidFrom.Date <= docDate)
                        .OrderByDescending(r => r.ValidFrom)
                        .FirstOrDefault()
                        ?? curr.Rates.OrderByDescending(r => r.ValidFrom).FirstOrDefault();
                }

                decimal mult = (matchedRate != null && matchedRate.Multiplier != 0m) ? matchedRate.Multiplier : 1m;
                decimal div = (matchedRate != null && matchedRate.Divider != 0m) ? matchedRate.Divider : 1m;

                var existingConv = Details.CurrencyConvertions.FirstOrDefault(c =>
                    string.Equals(c.CurrencyId, curr.Id, StringComparison.OrdinalIgnoreCase));

                if (existingConv == null)
                {
                    Details.CurrencyConvertions.Add(new CurrencyConvertion
                    {
                        CurrencyId = curr.Id,
                        Multiplier = mult,
                        Divider = div
                    });
                }
                else if (existingConv.Multiplier == 1m && existingConv.Divider == 1m && (mult != 1m || div != 1m))
                {
                    existingConv.Multiplier = mult;
                    existingConv.Divider = div;
                }
            }

            // Обновляем привязки конвертеров внутри строк
            Details.UpdateCurrencyConvertion();
            if (Details.Lines != null)
            {
                foreach (var line in Details.Lines)
                {
                    line.UpdateCurrencyConvertion();
                }
            }

            // ПОДПИСЫВАЕМСЯ НА ДОБАВЛЕНИЕ НОВЫХ СТРОК В ТАБЛИЦУ
            SubscribeLines();
        }

        UpdatePartnerBalance();

        Details.RaisePropertyChanged("ActionTotal");
        Details.RaisePropertyChanged("ActionDebitCreditTotal");
        Details.RaisePropertyChanged("ActionDebitTotal");
        Details.RaisePropertyChanged("ActionCreditTotal");
        Details.RaisePropertyChanged("DisplayTotal");
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

    private void SubscribeLines()
    {
        if (Details?.Lines != null)
        {
            Details.Lines.CollectionChanged -= Lines_CollectionChanged;
            Details.Lines.CollectionChanged += Lines_CollectionChanged;

            // Если документ новый и в таблице уже создана пустая строка — ставим ей валюту из шапки
            if (string.IsNullOrEmpty(ItemId) && !string.IsNullOrEmpty(Details.DisplayCurrencyId))
            {
                foreach (var line in Details.Lines)
                {
                    line.CurrencyId = Details.DisplayCurrencyId;
                    line.UpdateCurrencyConvertion();
                }
            }
        }
    }

    private void Lines_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is BillLine line && !string.IsNullOrEmpty(Details?.DisplayCurrencyId))
                {
                    // Новая строка всегда наследует валюту, выбранную в шапке документа
                    line.CurrencyId = Details.DisplayCurrencyId;
                    line.UpdateCurrencyConvertion();
                }
            }
        }
    }

    public override bool IsDirty
    {
        get
        {
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

        if (e.PropertyName == "Lines")
        {
            SubscribeLines();
        }

        if (e.PropertyName == "DisplayCurrencyId")
        {
            // При смене валюты в шапке нового документа автоматически переключаем валюту всех строк
            if (Details.Lines != null)
            {
                foreach (var line in Details.Lines)
                {
                    if (string.IsNullOrEmpty(ItemId))
                    {
                        line.CurrencyId = Details.DisplayCurrencyId;
                    }
                    line.UpdateCurrencyConvertion();
                }
            }

            Details.UpdateCurrencyConvertion();

            Details.RaisePropertyChanged("ActionTotal");
            Details.RaisePropertyChanged("DisplayTotal");
            Details.RaisePropertyChanged("DisplayDebitCreditTotal");
            Details.RaisePropertyChanged("DisplayDebitTotal");
            Details.RaisePropertyChanged("DisplayCreditTotal");
            RaisePropertyChanged(() => PartnerBalanceResult);
        }

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
            if (x.IsDisabled && !string.Equals(x.Id, Details?.DepositoryId, StringComparison.OrdinalIgnoreCase))
                return false;

            if (string.IsNullOrWhiteSpace(Details?.OfficeId))
                return true;

            if (string.IsNullOrWhiteSpace(x.OfficeId))
                return true;

            return string.Equals(x.OfficeId, Details.OfficeId, StringComparison.OrdinalIgnoreCase);
        };

        Depositories?.RaisePropertyChanged("List");
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
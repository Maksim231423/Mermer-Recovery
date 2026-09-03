using MvvmCross.Core.Navigation;
using MvvmCross.Core.ViewModels;
using Mermer.Authorization.Services;
using Mermer.Commerce.Models;
using Mermer.CRM.Models;
using Mermer.CRM.Services;
using Mermer.Enterprise.Models;
using Mermer.FundsManagement.Models;
using Mermer.StockManagement.Models;
using Mermer.StockManagement.Services;
using Mermer.Transactions.Models;
using Mermer.Transactions.Models.Authorizers;
using Mermer.Transactions.Services;
using Mermer.Ui.Core.Helpers;
using Mermer.Ui.Core.Services;
using Mermer.Ui.Core.ViewModels.Common;
using Mermer.Ui.Core.ViewModels.StockManagement;
using Mermer.Ui.Core.ViewModels.Transactions;
using Mermer.Ui.Core.ViewModels.Warehousing.Ordering;
using Mermer.Data;
using Mermer.Data.Authorizers;
using Mermer.Data.Storage;
using Mermer.Mvvm.Services;
using Mermer.Mvvm.ViewModels;
using Mermer.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Mermer.Ui.Core.ViewModels.Commerce;

public class InvoiceDetailsViewModel :
    StockTransactionDetailsViewModel<Invoice, InvoiceLine, InvoiceType>,
    IMvxViewModel<InvoiceType>,
    IMvxViewModel
{
    private readonly IPrintingService _printingService;
    private readonly IStocksRepository _stocksRepository;
    private readonly IPartnerBalancesRepository _partnerBalancesRepository;
    private InvoiceType _newInvoiceType = InvoiceType.Sales;
    private PartnerBalanceResult _partnerBalanceToDate;
    private string[] _priceGroupNames;

    public InvoiceDetailsViewModel(
        CopyCreate copyCreate,
        IRepository<Invoice> repository,
        ITransactionAuthorizer<Invoice> authorizer,
        IConfigurator configurator,
        ILoginService loginService,
        StockSearcher stockSearcher,
        Reference<Office> offices,
        Reference<Partner> partners,
        Reference<Currency> currencies,
        Reference<Warehouse> warehouses,
        IPrintingService printingService,
        Reference<Depository> depositories,
        IStocksRepository stocksRepository,
        IMvxNavigationService navigationService,
        ITransactionCodeGenerationService codegentor,
        IUserInteractionService userInteractionService,
        IPartnerBalancesRepository partnerBalancesRepository)
        : base(copyCreate, repository, authorizer, configurator, loginService, stockSearcher, currencies, warehouses, stocksRepository, navigationService, codegentor, userInteractionService)
    {
        _printingService = printingService;
        _stocksRepository = stocksRepository;
        _partnerBalancesRepository = partnerBalancesRepository;
        Offices = offices;
        Partners = partners;
        Depositories = depositories;

        DiscountTypes = Enum.GetValues(typeof(InvoiceDiscountType)).Cast<InvoiceDiscountType>().Select(x => new ListHelper<InvoiceDiscountType>
        {
            Text = this[x.ToString()],
            Value = x
        }).ToArray();
    }

    public Reference<Office> Offices { get; }
    public Reference<Partner> Partners { get; }
    public Reference<Depository> Depositories { get; }
    public ListHelper<InvoiceDiscountType>[] DiscountTypes { get; set; }

    public override Invoice Details
    {
        get => base.Details;
        set
        {
            base.Details = value;
            UpdatePartnerBalance();
        }
    }

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
            if (PartnerBalanceToDate == null) return null;
            return new PartnerBalanceResult
            {
                Balance = PartnerBalanceToDate.Balance + Details.DisplayDebitCreditTotal
            };
        }
    }

    public virtual string[] PriceGroupNames
    {
        get => _priceGroupNames;
        set => SetProperty(ref _priceGroupNames, value);
    }

    public void Prepare(InvoiceType parameter) => _newInvoiceType = parameter;

    protected override async Task LoadFacetsAsync()
    {
        await base.LoadFacetsAsync();
        var facets = await _stocksRepository.GetFacets("PriceGroupNames");
        PriceGroupNames = facets["PriceGroupNames"].Select(x => x.Key).ToArray();
    }

    protected override Task PreLoad()
    {
        return Task.WhenAll(
            base.PreLoad(),
            Offices.Initialize(),
            Partners.Initialize(),
            Depositories.Initialize(),
            Warehouses.Initialize() 
        );
    }

    protected override async Task OnLoad()
    {
        await base.OnLoad();
        if (!string.IsNullOrEmpty(ItemId)) return;

        Details.InvoiceType = _newInvoiceType;
        Details.OfficeId = AppSettings.DefaultOfficeId;
        Details.DepositoryId = AppSettings.DefaultDepositoryId;
        Details.StockPriceGroup = AppSettings.DefaultStockPriceGroup;
        Details.DueDate = DateTime.Now.AddDays(AppSettings.DefaultDueDateInDays).Date;
    }

    protected override async Task PostLoad()
    {
        await base.PostLoad();

        if (Details.Payments == null) Details.Payments = new WatchedObservableCollection<InvoicePayment>();
        if (Details.Changes == null) Details.Changes = new WatchedObservableCollection<InvoicePayment>();
        if (Details.Discounts == null) Details.Discounts = new WatchedObservableCollection<InvoiceDiscount>();

        RaisePropertyChanged(() => CanCreateAggregatedStockOrder);
        RaisePropertyChanged(() => CanShowNewPrices);

        Details.RaisePropertyChanged("DisplayDiscountsTotal");
        Details.RaisePropertyChanged("DisplayGrandTotal");
        Details.RaisePropertyChanged("DisplayPaymentsTotal");
        Details.RaisePropertyChanged("DisplayChangesTotal");
        Details.RaisePropertyChanged("DisplayLeftTotal");
        Details.RaisePropertyChanged("DisplayDebitCreditTotal");
        Details.RaisePropertyChanged("DisplayCreditTotal");
        Details.RaisePropertyChanged("DisplayDebitTotal");

        RaisePropertyChanged(() => PartnerBalanceToDate);
        RaisePropertyChanged(() => PartnerBalanceResult);

        StockSearcher.PriceGroup = Details.StockPriceGroup;

        Partners.Filter = x => !x.IsDisabled || x.Id == Details?.PartnerId;
        Offices.Filter = x => !x.IsDisabled || x.Id == Details?.OfficeId;

        UpdateFacilityFilters();

        // Если это новый документ и пользователь ещё ничего не добавлял в строки
        if (string.IsNullOrEmpty(ItemId) && (Details.Lines == null || Details.Lines.Count == 0))
        {
            this.IsDirty = false;
        }
    }

    protected override void Details_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        base.Details_PropertyChanged(sender, e);

        // Передаем бэкенду правильный системный ID валюты
        if (e.PropertyName == "DisplayCurrencyId")
        {
            StockSearcher.CurrencyId = Details.DisplayCurrencyId;

            if (!string.IsNullOrEmpty(StockSearcher.SearchText))
            {
                var temp = StockSearcher.SearchText;
                StockSearcher.SearchText = "";
                StockSearcher.SearchText = temp;
            }
        }

        if (e.PropertyName == "Date" || e.PropertyName == "PartnerId" || e.PropertyName == "WarehouseId" || e.PropertyName == "DisplayCurrencyId")
            UpdatePartnerBalance();
        else if (e.PropertyName == "DisplayDebitCreditTotal")
            RaisePropertyChanged(() => PartnerBalanceResult);
        else if (e.PropertyName == "InvoiceType")
        {
            RaisePropertyChanged(() => CanSelectSource);
            RaisePropertyChanged(() => CanCreateAggregatedStockOrder);
            RaisePropertyChanged(() => CanShowNewPrices);
        }
        else if (e.PropertyName == "OfficeId")
            UpdateFacilityFilters();
        else if (e.PropertyName == "StockPriceGroup")
            StockSearcher.PriceGroup = Details.StockPriceGroup;

        RaisePropertyChanged(() => CanSelectSource);
    }

    private void UpdateFacilityFilters()
    {
        Warehouses.Filter = x =>
        {
            // Безопасное сравнение GUID без учета регистра
            if (!string.Equals(x.OfficeId, Details?.OfficeId, StringComparison.OrdinalIgnoreCase)) return false;
            return !x.IsDisabled || string.Equals(x.Id, Details?.WarehouseId, StringComparison.OrdinalIgnoreCase);
        };

        Depositories.Filter = x =>
        {
            if (!string.Equals(x.OfficeId, Details?.OfficeId, StringComparison.OrdinalIgnoreCase)) return false;
            return !x.IsDisabled || string.Equals(x.Id, Details?.DepositoryId, StringComparison.OrdinalIgnoreCase);
        };
    }

    private async void UpdatePartnerBalance()
    {
        if (Details != null && !string.IsNullOrEmpty(Details.PartnerId) && !string.IsNullOrEmpty(Details.OfficeId))
        {
            try
            {
                var balanceToDateAsync = _partnerBalancesRepository != null
                    ? await _partnerBalancesRepository.GetBalanceToDateAsync(Details.OfficeId, Details.PartnerId, Details.Date, Details.Id)
                    : null;

                decimal rawBalance = balanceToDateAsync?.Balance ?? 0m;

                // Безопасное получение курса: если конвертации нет, берем 1:1
                decimal multiplier = 1m;
                decimal divider = 1m;

                if (!string.IsNullOrEmpty(Details.DisplayCurrencyId) && Details.CurrencyConvertions != null)
                {
                    var conversion = Details.CurrencyConvertions.FirstOrDefault(x => x.CurrencyId == Details.DisplayCurrencyId);
                    if (conversion != null)
                    {
                        multiplier = conversion.Multiplier != 0 ? conversion.Multiplier : 1m;
                        divider = conversion.Divider != 0 ? conversion.Divider : 1m;
                    }
                }

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

    protected override async Task<bool> OnSaveAsync()
    {
        try
        {
            // --- НОВАЯ ПРОВЕРКА НА ОПЛАТУ ---
            // Если есть неоплаченный остаток (Left) и не стоит галочка "На запись"
            if (Details.DisplayLeftTotal > 0 && !Details.DebitCreditLeftAmount)
            {
                throw new Exception("Оплата не прийнята повністю! Внесіть суму оплати або поставте галочку 'На запис' (Debit/Credit Left Amount).");
            }

            if (!string.IsNullOrEmpty(Details.PartnerId))
            {
                if (Details.IsDebitCredit)
                {
                    Partner partner = Partners.List.SingleOrDefault(x => x.Id == Details.PartnerId);
                    if (partner != null && partner.CreditLimit.HasValue)
                    {
                        decimal credit = PartnerBalanceResult?.Credit ?? 0M;
                        if (partner.CreditLimit.Value < credit)
                        {
                            string caption = this["Partner credit limit reached"];
                            string message = this[$"{{0}} partner has a credit limit at: {{1:#,##0.00}}{Environment.NewLine}Are you sure you want to continue?", partner.Fullname, partner.CreditLimit.Value];

                            if (!UserInteractionService.ShowMessage(caption, message, UserInteractionType.YesNo).GetValueOrDefault())
                                return false;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            UserInteractionService.ShowExceptionMessage(ex);

            // Блокируем сохранность!
            return false;
        }

        if (!await base.OnSaveAsync()) return false;

        decimal balance = PartnerBalanceToDate?.Balance ?? 0M;
        await _printingService.PrintInvoice(Details, balance);
        return true;
    }

    public ICommand SelectedLineAlternativeCommand => new MvxAsyncCommand(OnSelectedLineAlternativeCommandAsync, () => !IsBusy && CanEditSelectedLine);

    protected virtual async Task OnSelectedLineAlternativeCommandAsync()
    {
        string stockId = await NavigationService.Navigate<SelectStockAlternativeViewModel, Tuple<string, string>, string>(new Tuple<string, string>(SelectedLine.StockId, Details.WarehouseId));
        if (string.IsNullOrEmpty(stockId) || SelectedLine.StockId == stockId) return;

        Stock stocksCacheAsync = await GetFromStocksCacheAsync(stockId);
        SelectedLine.StockId = stocksCacheAsync.Id;
        SelectedLine.UnitId = stocksCacheAsync.UnitId;

        var currencyConvertion = Details.CurrencyConverter(stocksCacheAsync.CurrencyId);
        SelectedLine.Price = Details.GetDisplayAmount(stocksCacheAsync.Price * currencyConvertion.Multiplier / currencyConvertion.Divider);
        SelectedLine.CurrencyId = Details.DisplayCurrencyId;
    }

    public ICommand UpdatePaymentCommand => new MvxAsyncCommand(OnUpdatePaymentCommandAsync, () => !IsBusy && HasSaveAccess);

    private async Task OnUpdatePaymentCommandAsync()
    {
        try
        {
            var parameters = new IpdParams
            {
                SubTotal = Details.DisplayTotal,
                DiscountsTotal = Details.DisplayDiscountsTotal,
                PaymentsTotal = Details.DisplayPaymentsTotal,
                ChangesTotal = Details.DisplayChangesTotal,
                CanDebitCredit = Details.CanDebitCredit,
                DebitCreditLeftAmount = Details.DebitCreditLeftAmount
            };

            var result = await NavigationService.Navigate<InvoicePaymentDialogViewModel, IpdParams, IpdParams>(parameters);
            if (result == null) return;

            // 1. Обновляем Скидки
            if (Details.DisplayDiscountsTotal != result.DiscountsTotal)
            {
                Details.Discounts.Clear();
                if (result.DiscountsTotal > 0M)
                {
                    var currencyConvertion = Details.CurrencyConverter(Details.DisplayCurrencyId);

                    decimal baseDiscountAmount = result.DiscountsTotal * currencyConvertion.Multiplier / currencyConvertion.Divider;

                    Details.Discounts.Add(new InvoiceDiscount
                    {
                        Amount = baseDiscountAmount,
                        Type = InvoiceDiscountType.Flat
                    });
                }
            }

            // 2. Обновляем Оплату (без умножения на rate!)
            if (Details.DisplayPaymentsTotal != result.PaymentsTotal)
            {
                Details.Payments.Clear();
                if (result.PaymentsTotal > 0M)
                {
                    
                    Details.Payments.Add(new InvoicePayment { Amount = result.PaymentsTotal, CurrencyId = Details.DisplayCurrencyId });
                }
            }

            // 3. Обновляем остаток (без умножения на rate!)
            if (Details.DisplayChangesTotal != result.ChangesTotal)
            {
                Details.Changes.Clear();
                if (result.ChangesTotal > 0M)
                {
                    Details.Changes.Add(new InvoicePayment { Amount = result.ChangesTotal, CurrencyId = Details.DisplayCurrencyId });
                }
            }

            Details.DebitCreditLeftAmount = result.DebitCreditLeftAmount;
        }
        catch (Exception ex)
        {
            UserInteractionService.ShowExceptionMessage(ex);
        }
    }

    public ICommand RemovePartnerCommand => new MvxCommand(OnRemovePartnerCommand, () => !IsBusy && HasSaveAccess);

    private void OnRemovePartnerCommand()
    {
        Details.DebitCreditLeftAmount = false;
        Details.PartnerId = null;
    }

    public ICommand SelectPartnerCommand => new MvxAsyncCommand(OnSelectPartnerCommandAsync, () => !IsBusy && HasSaveAccess);

    private async Task OnSelectPartnerCommandAsync()
    {
        Details.PartnerId = await NavigationService.Navigate<ListViewModel<Partner>, string, string>(Details.PartnerId ?? Guid.Empty.ToString());
    }

    public ICommand SelectDepositoryCommand => new MvxAsyncCommand(OnSelectDepositoryCommandAsync, () => !IsBusy && HasSaveAccess);

    private async Task OnSelectDepositoryCommandAsync()
    {
        Details.DepositoryId = await NavigationService.Navigate<ListViewModel<Depository>, string, string>(Details.DepositoryId ?? Guid.Empty.ToString());
    }

    public bool CanSelectSource => HasSaveAccess && Details != null && Details.InvoiceType == InvoiceType.SalesReturn;

    public ICommand SelectSource => new MvxAsyncCommand(OnSelectSourceAsync, () => !IsBusy && CanSelectSource);

    private async Task OnSelectSourceAsync()
    {
        var stockActions = await NavigationService.Navigate<SelectSourceInvoiceViewModel, IEnumerable<StockAction>>();
        if (stockActions == null) return;

        var displayCurrencyConvertion = CurrencyConverter(Details.DisplayCurrencyId);

        foreach (var source in stockActions)
        {
            Stock stocksCacheAsync = await GetFromStocksCacheAsync(source.ActionStockId);
            decimal num = source.ActionPrice / displayCurrencyConvertion.Multiplier * displayCurrencyConvertion.Divider;

            InvoiceLine newLine = CreateNewLine(stocksCacheAsync, source.ActionExpense, stocksCacheAsync.UnitId, num, Details.DisplayCurrencyId);
            newLine.SourceId = source.ActionId;

            Details.Lines.Add(newLine);
            SelectedLine = newLine;
        }
    }

    public ICommand PrintCommand => new MvxAsyncCommand(OnPrintCommandAsync, () => !IsBusy && !IsDirty);

    protected virtual async Task OnPrintCommandAsync()
    {
        if (Details?.Lines == null || Details.Lines.Count == 0)
        {
            UserInteractionService.ShowMessage("Error", "Cannot print an empty invoice!");
            return;
        }

        decimal balance = PartnerBalanceToDate?.Balance ?? 0M;
        await _printingService.PrintInvoice(Details, balance, true);
    }

    public bool CanCreateAggregatedStockOrder => Details != null && Details.InvoiceType == InvoiceType.Purchase;

    public ICommand ToAggregatedStockOrderCommand => new MvxAsyncCommand(OnToAggregatedStockOrderCommandAsync, () => !IsBusy && !IsDirty && CanCreateAggregatedStockOrder);

    protected virtual Task OnToAggregatedStockOrderCommandAsync()
    {
        return NavigationService.Navigate<AggregatedStockOrderDetailsViewModel, AggregatedStockOrderDetailsViewModel.Params>(new AggregatedStockOrderDetailsViewModel.Params
        {
            WarehouseId = Details.WarehouseId,
            StockIds = Details.Lines.Select(x => x.StockId)
        });
    }

    public bool CanShowNewPrices => Details != null && Details.InvoiceType == InvoiceType.Purchase;

    public ICommand ShowNewPricesCommand => new MvxAsyncCommand(OnShowNewPricesCommandAsync, () => !IsBusy && !IsDirty && CanShowNewPrices);

    private Task OnShowNewPricesCommandAsync()
    {
        return NavigationService.Navigate<StockRepriceDialogViewModel, IEnumerable<StockRepriceRequest>>(Details.Lines.Select(x => new StockRepriceRequest
        {
            StockId = x.StockId,
            ReferencePrice = x.Price,
            ReferencePriceCurrencyId = x.CurrencyId
        }));
    }

    public ICommand SelectOfficeCommand => new MvxAsyncCommand(OnSelectOfficeAsync, () => !IsBusy && HasSaveAccess);

    private async Task OnSelectOfficeAsync()
    {
        Details.OfficeId = await NavigationService.Navigate<ListViewModel<Office>, string, string>(Details.OfficeId ?? Guid.Empty.ToString());
    }

    public new ICommand CloseCommand
    {
        get
        {
            return new MvxAsyncCommand(async () =>
            {
                // Если накладная новая и не добавлено ни одной строки с товаром
                if (string.IsNullOrEmpty(ItemId) && (Details?.Lines == null || Details.Lines.Count == 0))
                {
                    this.IsDirty = false;
                    await NavigationService.Close(this);
                    return;
                }

                // Иначе вызываем оригинальную команду закрытия с диалогом подтверждения
                base.CloseCommand?.Execute(null);
            }, () => !IsBusy);
        }
    }

    public override async Task<bool> OnCloseAsync()
    {
        // Если это новый документ и в накладной нет ни одной добавленной строки с товаром
        if (string.IsNullOrEmpty(ItemId) && (Details?.Lines == null || Details.Lines.Count == 0))
        {
            this.IsDirty = false;
            await this.NavigationService.Close(this);
            BaseViewModel.RequestComponentCloseAction?.Invoke(this);
            return true;
        }

        // Если строки добавлены или документ уже сохранен — штатная проверка с диалогом
        return await base.OnCloseAsync();
    }
}
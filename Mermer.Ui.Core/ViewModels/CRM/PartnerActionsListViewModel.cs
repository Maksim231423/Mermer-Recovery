using Mermer.Authorization.Services;
using Mermer.Commerce.Models;
using Mermer.Common.Settings;
using Mermer.CRM.Models;
using Mermer.CRM.Services;
using Mermer.Enterprise.Models;
using Mermer.FundsManagement.Models;
using Mermer.FundsManagement.Models.Extenders;
using Mermer.Mvvm.Services;
using Mermer.Mvvm.ViewModels;
using Mermer.Services;
using Mermer.Ui.Core.Helpers;
using Mermer.Ui.Core.ViewModels.Common;
using MvvmCross.Core.Navigation;
using MvvmCross.Core.ViewModels;
using MvvmCross.Platform;
using MvvmCross.Plugins.Messenger;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using System.Windows.Input;

#nullable disable
namespace Mermer.Ui.Core.ViewModels.CRM;

public class PartnerActionsListViewModel :
  ListViewModelBaseWithFilterDate<PartnerAction>,
  IMvxViewModel<PartnerActionsFilter>,
  IMvxViewModel
{
    private readonly IConfigurator _configurator;
    private readonly IPartnerActionsRepository _repository;
    private readonly ILoginService _loginService;
    private List<object> _selectedOfficeIds;
    private string _partnerId;
    private PartnerActionsFilter _parameter;
    private bool _loaded;
    private string _currencyId;

    public decimal ActionEffectInCustomCurrency { get; set; }

    public PartnerActionsListViewModel(
        IMvxMessenger messenger,
        IConfigurator configurator,
        Reference<Office> offices,
        Reference<Partner> partners,
        Reference<Currency> currencies,
        IPartnerActionsRepository repository,
        IMvxNavigationService navigationService,
        IUserInteractionService userInteractionService)
        : base(messenger, navigationService, userInteractionService)
    {
        this._configurator = configurator;
        this._repository = repository;
        this.Offices = offices;
        this.Partners = partners;
        this.Types = new LocalizedTransactionTypes("Repricing");
        this.Currencies = currencies;

        try { this._loginService = Mvx.Resolve<ILoginService>(); } catch { }
    }

    public virtual string CurrencyId
    {
        get => this._currencyId;
        set
        {
            if (!this.SetProperty<string>(ref this._currencyId, value, nameof(CurrencyId)) || this.IsBusy)
                return;
            this.ApplyCustomCurrencyRate();
        }
    }

    public Reference<Currency> Currencies { get; private set; }

    public List<object> SelectedOfficeIds
    {
        get => this._selectedOfficeIds;
        set
        {
            if (this._selectedOfficeIds != null && value != null && this._selectedOfficeIds.SequenceEqual<object>((IEnumerable<object>)value) || !this.SetProperty<List<object>>(ref this._selectedOfficeIds, value, nameof(SelectedOfficeIds)) || this.IsBusy)
                return;
            this.Initialize();
        }
    }

    public string[] OfficeIds
    {
        get
        {
            List<object> selectedOfficeIds = this.SelectedOfficeIds;
            var list = (selectedOfficeIds != null ? selectedOfficeIds.Cast<string>().ToArray<string>() : (string[])null) ?? Array.Empty<string>();
            return list.Where(id => AccountSecurityHelper.CanRead(this._loginService, id)).ToArray();
        }
    }

    public virtual string PartnerId
    {
        get => this._partnerId;
        set
        {
            if (!this.SetProperty<string>(ref this._partnerId, value, nameof(PartnerId)) || this.IsBusy)
                return;
            this.Initialize();
        }
    }

    public Reference<Office> Offices { get; }
    public Reference<Partner> Partners { get; }
    public LocalizedTransactionTypes Types { get; }

    public void Prepare(PartnerActionsFilter parameter) => this._parameter = parameter;

    protected override async Task PreLoad()
    {
        if (this.Partners != null)
        {
            this.Partners.Filter = x => !x.IsDisabled;
        }

        if (this.Offices != null)
        {
            this.Offices.Filter = o => !o.IsDisabled && AccountSecurityHelper.CanRead(this._loginService, o.Id);
        }

        await Task.WhenAll(
            base.PreLoad(),
            Offices.Initialize(),
            Partners.Initialize(),
            Currencies.Initialize()
        );

        if (!_loaded && !OfficeIds.Any())
        {
            if (_parameter != null)
            {
                SelectedOfficeIds = _parameter.OfficeIds.Where(id => AccountSecurityHelper.CanRead(this._loginService, id)).Cast<object>().ToList();
                PartnerId = _parameter.PartnerId;
                DateFilterFrom = _parameter.DateFrom;
                DateFilterTill = _parameter.DateTill;
            }
            else
            {
                AppSettings configAsync = await _configurator.GetConfigAsync<AppSettings>();
                string defOffice = configAsync?.DefaultOfficeId;
                if (!string.IsNullOrEmpty(defOffice) && AccountSecurityHelper.CanRead(this._loginService, defOffice))
                {
                    SelectedOfficeIds = new List<object> { defOffice };
                }
                else
                {
                    var firstAllowed = Offices?.List?.FirstOrDefault();
                    SelectedOfficeIds = firstAllowed != null ? new List<object> { firstAllowed.Id } : new List<object>();
                }
            }
        }

        _loaded = true;
        CurrencyId = null;
    }

    protected override Task OnLoad()
    {
        if (this._parameter == null)
            return base.OnLoad();
        this._parameter = (PartnerActionsFilter)null;
        return this.LoadByDateAsync(false);
    }

    protected override async Task<int> CountFilteredListByDateAsync(DateTime from, DateTime till)
    {
        if (this._loginService?.Session == null || this._loginService.Session.IsAdmin || this._loginService.Session.Accounts == null)
            return await this._repository.CountAsync(new DateTime?(from), new DateTime?(till), this.PartnerId, this.OfficeIds);

        var items = await this.GetFilteredListByDateAsync(from, till);
        return items?.Count() ?? 0;
    }

    protected override async Task<int> CountFilteredListAsync(ListFilter filter)
    {
        if (this._loginService?.Session == null || this._loginService.Session.IsAdmin || this._loginService.Session.Accounts == null)
            return await this._repository.CountAsync(new DateTime?(), new DateTime?(), this.PartnerId, this.OfficeIds);

        var items = await this.GetFilteredListAsync(filter);
        return items?.Count() ?? 0;
    }

    protected override async Task<IEnumerable<PartnerAction>> GetFilteredListByDateAsync(DateTime from, DateTime till)
    {
        var result = await this._repository.GetAsync(from, till, this.PartnerId, this.OfficeIds);
        result = result?.Where(x => AccountSecurityHelper.CanRead(this._loginService, x.ActionOfficeId));
        return ApplyCustomCurrencyRate(result);
    }

    protected override async Task<IEnumerable<PartnerAction>> GetFilteredListAsync(ListFilter filter)
    {
        var result = await this._repository.GetAsync(default(DateTime?), default(DateTime?), this.PartnerId, this.OfficeIds);
        result = result?.Where(x => AccountSecurityHelper.CanRead(this._loginService, x.ActionOfficeId));
        return ApplyCustomCurrencyRate(result);
    }

    protected override Expression<Func<PartnerAction, bool>> GetDateFilter(DateTime from, DateTime till)
    {
        return x => x.TransactionDate >= from && x.TransactionDate <= till;
    }

    protected override Task<int> CountListAsync(params Expression<Func<PartnerAction, bool>>[] predicates)
    {
        return this._repository.CountAsync(null, null, this.PartnerId, this.OfficeIds);
    }

    protected override async Task<IEnumerable<PartnerAction>> GetListAsync(params Expression<Func<PartnerAction, bool>>[] predicates)
    {
        var result = await this._repository.GetAsync(null, null, this.PartnerId, this.OfficeIds);
        result = result?.Where(x => AccountSecurityHelper.CanRead(this._loginService, x.ActionOfficeId));
        return ApplyCustomCurrencyRate(result);
    }

    public ICommand SelectOrViewDetailsCommand => new MvxAsyncCommand(this.OnSelectOrViewDetailsAsync, () => !this.IsBusy && this.SelectedItem != null);

    private Task OnSelectOrViewDetailsAsync()
    {
        switch (this.SelectedItem.TransactionType)
        {
            case "Collection":
            case "Payment":
                return this.NavigationService.Navigate<DetailsViewModel<Bill>, string>(this.SelectedItem.TransactionId);
            case "PartnerBalanceRevision":
            case "PartnerOpeningBalance":
                return this.NavigationService.Navigate<DetailsViewModel<PartnerSlip>, string>(this.SelectedItem.TransactionId);
            case "PartnerTransfer":
                return this.NavigationService.Navigate<DetailsViewModel<PartnerTransfer>, string>(this.SelectedItem.TransactionId);
            case "Purchase":
            case "PurchaseReturn":
            case "Sales":
            case "SalesReturn":
                return this.NavigationService.Navigate<DetailsViewModel<Invoice>, string>(this.SelectedItem.TransactionId);
            default:
                return Task.CompletedTask;
        }
    }

    private void ApplyCustomCurrencyRate() => this.List = this.ApplyCustomCurrencyRate(this.List);

    private IEnumerable<PartnerAction> ApplyCustomCurrencyRate(IEnumerable<PartnerAction> list)
    {
        if (list == null) return list;

        decimal rate = 0M;

        if (!string.IsNullOrEmpty(this._currencyId))
        {
            Currency currency = this.Currencies?.List?.SingleOrDefault(x => x.Id == this._currencyId);
            CurrencyRate rate1 = currency?.GetRate();

            if (rate1 != null && rate1.Multiplier != 0M)
            {
                rate = rate1.Divider / rate1.Multiplier;
            }
        }

        return list.Select(item =>
        {
            item.ActionEffectInCustomCurrency = item.ActionEffect * rate;
            return item;
        });
    }
}
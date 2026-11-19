using Mermer.Authorization.Services;
using Mermer.Common.Settings;
using Mermer.Enterprise.Models;
using Mermer.FundsManagement.Models;
using Mermer.FundsManagement.Models.Extenders;
using Mermer.FundsManagement.Services;
using Mermer.Mvvm.Services;
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
namespace Mermer.Ui.Core.ViewModels.FundsManagement;

public class FundsBalancesListViewModel :
  ListViewModelBaseWithFilterDate<FundsBalanceByTypeWithBalance>
{
    private readonly IConfigurator _configurator;
    private readonly IFundsBalancesRepository _repository;
    private readonly ILoginService _loginService;
    private List<object> _selectedDepositoryIds;
    private bool _loaded;
    private string _currencyId;

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

    public FundsBalancesListViewModel(
        IMvxMessenger messenger,
        IConfigurator configurator,
        Reference<Depository> depositories,
        Reference<Currency> currencies,
        IFundsBalancesRepository repository,
        IMvxNavigationService navigationService,
        IUserInteractionService userInteractionService)
        : base(messenger, navigationService, userInteractionService)
    {
        this._configurator = configurator;
        this._repository = repository;
        this.Depositories = depositories;
        this.Currencies = currencies;

        try { this._loginService = Mvx.Resolve<ILoginService>(); } catch { }

        if (this.Currencies != null)
        {
            this.Currencies.Filter = c => !c.IsDisabled;
        }
    }

    public List<object> SelectedDepositoryIds
    {
        get => this._selectedDepositoryIds;
        set
        {
            if (this._selectedDepositoryIds != null && value != null && this._selectedDepositoryIds.SequenceEqual(value))
                return;

            if (!this.SetProperty(ref this._selectedDepositoryIds, value ?? new List<object>(), nameof(SelectedDepositoryIds)))
                return;

            this.RaisePropertyChanged(() => this.DepositoryIds);

            if (this._loaded && !this.IsBusy)
            {
                this.Initialize();
            }
        }
    }

    public string[] DepositoryIds
    {
        get
        {
            if (this.SelectedDepositoryIds == null || !this.SelectedDepositoryIds.Any())
            {
                return this.Depositories?.List?
                    .Where(d => AccountSecurityHelper.CanRead(this._loginService, d.Id))
                    .Select(d => d.Id)
                    .ToArray() ?? Array.Empty<string>();
            }

            return this.SelectedDepositoryIds
                .Select(x => x?.ToString())
                .Where(x => !string.IsNullOrWhiteSpace(x) && AccountSecurityHelper.CanRead(this._loginService, x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public Reference<Depository> Depositories { get; }

    protected override async Task PreLoad()
    {
        if (this.Currencies != null)
        {
            this.Currencies.Filter = c => !c.IsDisabled;
        }

        if (this.Depositories != null)
        {
            this.Depositories.Filter = d => !d.IsDisabled && AccountSecurityHelper.CanRead(this._loginService, d.Id);
        }

        await Task.WhenAll(
            base.PreLoad(),
            this.Depositories.Initialize(),
            this.Currencies.Initialize()
        );

        if (!this._loaded && (this.SelectedDepositoryIds == null || !this.SelectedDepositoryIds.Any()))
        {
            string defDep = (await this._configurator.GetConfigAsync<AppSettings>())?.DefaultDepositoryId;
            if (!string.IsNullOrEmpty(defDep) && AccountSecurityHelper.CanRead(this._loginService, defDep))
            {
                this._selectedDepositoryIds = new List<object> { defDep };
            }
            else
            {
                var firstAllowed = this.Depositories?.List?.FirstOrDefault();
                this._selectedDepositoryIds = firstAllowed != null ? new List<object> { firstAllowed.Id } : new List<object>();
            }
            this.RaisePropertyChanged(() => this.SelectedDepositoryIds);
            this.RaisePropertyChanged(() => this.DepositoryIds);
            this._loaded = true;
        }

        this.CurrencyId = null;
    }

    private void ApplyCustomCurrencyRate() => this.List = this.ApplyCustomCurrencyRate(this.List);

    private IEnumerable<FundsBalanceByTypeWithBalance> ApplyCustomCurrencyRate(IEnumerable<FundsBalanceByTypeWithBalance> list)
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
            item.ResultingBalanceInCustomCurrency = item.ResultingBalance * rate;
            return item;
        });
    }

    protected override async Task<IEnumerable<FundsBalanceByTypeWithBalance>> GetFilteredListByDateAsync(DateTime from, DateTime till)
    {
        var targetIds = this.DepositoryIds;
        if (targetIds == null || targetIds.Length == 0)
            return Enumerable.Empty<FundsBalanceByTypeWithBalance>();

        string targetDepId = targetIds.Length == 1 ? targetIds[0] : null;

        var result = await this._repository.GetByTypeAsync(targetDepId, new DateTime?(from), new DateTime?(till));
        var allowedSet = targetIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        result = result?.Where(x => allowedSet.Contains(x.DepositoryId) && AccountSecurityHelper.CanRead(this._loginService, x.DepositoryId));
        return ApplyCustomCurrencyRate(result);
    }

    protected override async Task<IEnumerable<FundsBalanceByTypeWithBalance>> GetFilteredListAsync(ListFilter filter)
    {
        var targetIds = this.DepositoryIds;
        if (targetIds == null || targetIds.Length == 0)
            return Enumerable.Empty<FundsBalanceByTypeWithBalance>();

        string targetDepId = targetIds.Length == 1 ? targetIds[0] : null;

        var result = await this._repository.GetByTypeAsync(targetDepId, new DateTime?(), new DateTime?());
        var allowedSet = targetIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        result = result?.Where(x => allowedSet.Contains(x.DepositoryId) && AccountSecurityHelper.CanRead(this._loginService, x.DepositoryId));
        return ApplyCustomCurrencyRate(result);
    }

    protected override async Task<IEnumerable<FundsBalanceByTypeWithBalance>> GetListAsync(params Expression<Func<FundsBalanceByTypeWithBalance, bool>>[] predicates)
    {
        var targetIds = this.DepositoryIds;
        if (targetIds == null || targetIds.Length == 0)
            return Enumerable.Empty<FundsBalanceByTypeWithBalance>();

        string targetDepId = targetIds.Length == 1 ? targetIds[0] : null;

        var result = await this._repository.GetByTypeAsync(targetDepId, null, null);
        var allowedSet = targetIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        result = result?.Where(x => allowedSet.Contains(x.DepositoryId) && AccountSecurityHelper.CanRead(this._loginService, x.DepositoryId));
        return ApplyCustomCurrencyRate(result);
    }

    public ICommand SelectOrViewDetailsCommand => new MvxAsyncCommand(this.OnSelectOrViewDetailsAsync, () => !this.IsBusy && this.SelectedItem != null);

    private Task OnSelectOrViewDetailsAsync()
    {
        return this.NavigationService.Navigate<FundsActionsListViewModel, FundsActionsFilter>(new FundsActionsFilter()
        {
            DepositoryIds = new string[1]
            {
                this.SelectedItem.DepositoryId
            },
            DateFrom = this.DateFilterFrom,
            DateTill = this.DateFilterTill
        });
    }

    public ICommand ShowActionsCommand => new MvxAsyncCommand(this.OnShowActionsAsync, () => !this.IsBusy);

    private Task OnShowActionsAsync()
    {
        return this.NavigationService.Navigate<FundsActionsListViewModel, FundsActionsFilter>(new FundsActionsFilter()
        {
            DepositoryIds = this.DepositoryIds,
            DateFrom = this.DateFilterFrom,
            DateTill = this.DateFilterTill
        });
    }

    protected override Expression<Func<FundsBalanceByTypeWithBalance, bool>> GetDateFilter(DateTime from, DateTime till) => x => true;

    protected override async Task<int> CountListAsync(params Expression<Func<FundsBalanceByTypeWithBalance, bool>>[] predicates)
    {
        var items = await this.GetListAsync(predicates);
        return items?.Count() ?? 0;
    }

    protected override async Task<int> CountFilteredListAsync(ListFilter filter)
    {
        var items = await this.GetFilteredListAsync(filter);
        return items?.Count() ?? 0;
    }

    protected override async Task<int> CountFilteredListByDateAsync(DateTime from, DateTime till)
    {
        var items = await this.GetFilteredListByDateAsync(from, till);
        return items?.Count() ?? 0;
    }
}
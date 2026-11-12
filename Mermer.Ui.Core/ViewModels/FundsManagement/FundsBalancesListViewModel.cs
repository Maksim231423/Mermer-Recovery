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
    private string _depositoryId;
    private bool _loaded;
    private string _currencyId;

    public virtual string CurrencyId
    {
        get => this._currencyId;
        set
        {
            if (!this.SetProperty<string>(ref this._currencyId, value, nameof(CurrencyId)) || this.IsBusy)
                return;
            this.ApplyCustomCurrencyRate(); // Пересчет при изменении
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

        // Фильтруем удаленные валюты сразу при создании ViewModel
        if (this.Currencies != null)
        {
            this.Currencies.Filter = c => !c.IsDisabled;
        }
    }

    public virtual string DepositoryId
    {
        get => this._depositoryId;
        set
        {
            if (!this.SetProperty<string>(ref this._depositoryId, value, nameof(DepositoryId)) || this.IsBusy)
                return;
            this.Initialize();
        }
    }

    public Reference<Depository> Depositories { get; }

    protected override async Task PreLoad()
    {
        // Гарантируем фильтрацию активных валют перед загрузкой
        if (this.Currencies != null)
        {
            this.Currencies.Filter = c => !c.IsDisabled;
        }

        if (!this._loaded && string.IsNullOrEmpty(this.DepositoryId))
            this.DepositoryId = (await this._configurator.GetConfigAsync<AppSettings>()).DefaultDepositoryId;

        this._loaded = true;

        await Task.WhenAll(
            base.PreLoad(),
            this.Depositories.Initialize(),
            this.Currencies.Initialize()
        );

        // ИСПРАВЛЕНО: Валюта по умолчанию НЕ устанавливается (всегда null),
        // даже если в настройках программы задан DefaultCurrencyId.
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
        var result = await this._repository.GetByTypeAsync(this.DepositoryId, new DateTime?(from), new DateTime?(till));
        return ApplyCustomCurrencyRate(result);
    }

    protected override async Task<IEnumerable<FundsBalanceByTypeWithBalance>> GetFilteredListAsync(ListFilter filter)
    {
        var result = await this._repository.GetByTypeAsync(this.DepositoryId, new DateTime?(), new DateTime?());
        return ApplyCustomCurrencyRate(result);
    }

    protected override async Task<IEnumerable<FundsBalanceByTypeWithBalance>> GetListAsync(params Expression<Func<FundsBalanceByTypeWithBalance, bool>>[] predicates)
    {
        var result = await this._repository.GetByTypeAsync(this.DepositoryId, null, null);
        return ApplyCustomCurrencyRate(result);
    }

    public ICommand SelectOrViewDetailsCommand
    {
        get
        {
            return (ICommand)new MvxAsyncCommand(new Func<Task>(this.OnSelectOrViewDetailsAsync), (Func<bool>)(() => !this.IsBusy && this.SelectedItem != null));
        }
    }

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

    public ICommand ShowActionsCommand
    {
        get
        {
            return (ICommand)new MvxAsyncCommand(new Func<Task>(this.OnShowActionsAsync), (Func<bool>)(() => !this.IsBusy));
        }
    }

    private Task OnShowActionsAsync()
    {
        return this.NavigationService.Navigate<FundsActionsListViewModel, FundsActionsFilter>(new FundsActionsFilter()
        {
            DepositoryIds = new string[1] { this.DepositoryId },
            DateFrom = this.DateFilterFrom,
            DateTill = this.DateFilterTill
        });
    }

    protected override Expression<Func<FundsBalanceByTypeWithBalance, bool>> GetDateFilter(
      DateTime from,
      DateTime till)
    {
        return x => true;
    }

    protected override async Task<int> CountListAsync(params Expression<Func<FundsBalanceByTypeWithBalance, bool>>[] predicates)
    {
        var depId = string.IsNullOrEmpty(this.DepositoryId) ? null : this.DepositoryId;
        var result = await this._repository.GetByTypeAsync(depId, null, null);
        return result != null ? result.Count() : 0;
    }

    protected override async Task<int> CountFilteredListAsync(ListFilter filter)
    {
        var depId = string.IsNullOrEmpty(this.DepositoryId) ? null : this.DepositoryId;
        var result = await this._repository.GetByTypeAsync(depId, null, null);
        return result != null ? result.Count() : 0;
    }

    protected override async Task<int> CountFilteredListByDateAsync(DateTime from, DateTime till)
    {
        var depId = string.IsNullOrEmpty(this.DepositoryId) ? null : this.DepositoryId;
        var result = await this._repository.GetByTypeAsync(depId, from, till);
        return result != null ? result.Count() : 0;
    }
}
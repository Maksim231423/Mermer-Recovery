using Mermer.Authorization.Enums;
using Mermer.Authorization.Services;
using Mermer.Data.Authorizers;
using Mermer.Data.Storage;
using Mermer.Enterprise.Models;
using Mermer.Mvvm.Services;
using Mermer.Mvvm.ViewModels;
using Mermer.Transactions.Models;
using Mermer.Ui.Core.Helpers;
using MvvmCross.Core.Navigation;
using MvvmCross.Core.ViewModels;
using MvvmCross.Plugins.Messenger;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Input;

#nullable disable
namespace Mermer.Ui.Core.ViewModels.Common;

public abstract class TransactionsListViewModel<T> : ListViewModel<T> where T : class, ITransactionModel, INotifyPropertyChanged
{
    private IEnumerable<ListFilter> _filters;
    private ListFilter _selectedFilter;

    private DateTime _dateFilterFrom = DateTime.Today;
    private DateTime _dateFilterTill = DateTime.Today;

    protected TransactionsListViewModel(
      IRepository<T> repository,
      IListAuthorizer<T> authorizer,
      IMvxMessenger messenger,
      IMvxNavigationService navigationService,
      IUserInteractionService userInteractionService)
      : base(repository, authorizer, messenger, navigationService, userInteractionService)
    {
        this.InitFilters();
        this.Types = new LocalizedTransactionTypes("Repricing");
    }

    public IEnumerable<ListFilter> Filters
    {
        get => this._filters;
        set => this.SetProperty<IEnumerable<ListFilter>>(ref this._filters, value, nameof(Filters));
    }

    public virtual ListFilter SelectedFilter
    {
        get => this._selectedFilter;
        set => this.SetProperty<ListFilter>(ref this._selectedFilter, value, nameof(SelectedFilter));
    }

    public DateTime DateFilterFrom
    {
        get => this._dateFilterFrom;
        set => this.SetProperty<DateTime>(ref this._dateFilterFrom, value, nameof(DateFilterFrom));
    }

    public DateTime DateFilterTill
    {
        get => this._dateFilterTill;
        set => this.SetProperty<DateTime>(ref this._dateFilterTill, value, nameof(DateFilterTill));
    }

    public DateTime DateFilterTillInclusive
    {
        get
        {
            DateTime dateTime = this.DateFilterTill;
            dateTime = dateTime.AddDays(1.0);
            return dateTime.Date;
        }
    }

    public LocalizedTransactionTypes Types { get; set; }

    protected ILoginService GetLoginService()
    {
        var type = this.GetType();
        var prop = type.GetProperty("LoginService", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (prop?.GetValue(this) is ILoginService lsProp) return lsProp;

        var field = type.GetField("_loginService", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (field?.GetValue(this) is ILoginService lsField) return lsField;

        try
        {
            return MvvmCross.Platform.Mvx.Resolve<ILoginService>();
        }
        catch
        {
            return null;
        }
    }

    protected virtual IEnumerable<T> ApplySecurityFilter(IEnumerable<T> items)
    {
        if (items == null) return items;
        var loginService = GetLoginService();
        if (loginService?.Session == null || loginService.Session.IsAdmin || loginService.Session.Accounts == null)
            return items;

        return items.Where(x => AccountSecurityHelper.CanReadTransaction(loginService, x)).ToList();
    }

    public ICommand FilterByDateCommand
    {
        get
        {
            return new MvxAsyncCommand(this.OnFilterByDateCommandAsync, () => !this.IsBusy && this.DateFilterFrom <= this.DateFilterTill);
        }
    }

    protected virtual async Task OnFilterByDateCommandAsync()
    {
        this.IsBusy = true;
        try
        {
            this.SelectedFilter = null;
            IEnumerable<T> filteredListByDateAsync = await this.GetFilteredListByDateAsync(this.DateFilterFrom, this.DateFilterTillInclusive);
            this.List = this.ApplySecurityFilter(filteredListByDateAsync);
            this.SubCaption = $"{this.DateFilterFrom:MMM d} - {this.DateFilterTill:MMM d}";
        }
        catch (Exception ex)
        {
            this.UserInteractionService.ShowExceptionMessage(ex);
        }
        this.IsBusy = false;
    }

    protected virtual void InitFilters()
    {
        ListFilter[] listFilterArray = new ListFilter[8];
        ListFilterByDate listFilterByDate1 = new ListFilterByDate();
        listFilterByDate1.Title = this["Today"];
        listFilterByDate1.CanLoad = x => !this.IsBusy;
        listFilterByDate1.Loader = x => this.LoadByFilterAsync(x);
        listFilterByDate1.Counter = this.CountByFilterAsync;
        listFilterByDate1.From = DateTime.Today;
        listFilterByDate1.Till = DateTime.Today;
        listFilterArray[0] = listFilterByDate1;

        ListFilterByDate listFilterByDate2 = new ListFilterByDate();
        listFilterByDate2.Title = this["Yesturday"];
        listFilterByDate2.CanLoad = x => !this.IsBusy;
        listFilterByDate2.Loader = x => this.LoadByFilterAsync(x);
        listFilterByDate2.Counter = this.CountByFilterAsync;
        listFilterByDate2.From = DateTime.Today.AddDays(-1.0);
        listFilterByDate2.Till = DateTime.Today.AddDays(-1.0);
        listFilterArray[1] = listFilterByDate2;

        ListFilterByDate listFilterByDate3 = new ListFilterByDate();
        listFilterByDate3.Title = this["This Week"];
        listFilterByDate3.CanLoad = x => !this.IsBusy;
        listFilterByDate3.Loader = x => this.LoadByFilterAsync(x);
        listFilterByDate3.Counter = this.CountByFilterAsync;
        listFilterByDate3.From = DateTime.Today.StartOfWeek();
        listFilterByDate3.Till = DateTime.Today.EndOfWeek();
        listFilterArray[2] = listFilterByDate3;

        ListFilterByDate listFilterByDate4 = new ListFilterByDate();
        listFilterByDate4.Title = this["Past Week"];
        listFilterByDate4.CanLoad = x => !this.IsBusy;
        listFilterByDate4.Loader = x => this.LoadByFilterAsync(x);
        listFilterByDate4.Counter = this.CountByFilterAsync;
        listFilterByDate4.From = DateTime.Today.AddDays(-7.0).StartOfWeek();
        listFilterByDate4.Till = DateTime.Today.AddDays(-7.0).EndOfWeek();
        listFilterArray[3] = listFilterByDate4;

        ListFilterByDate listFilterByDate5 = new ListFilterByDate();
        listFilterByDate5.Title = this["This Month"];
        listFilterByDate5.CanLoad = x => !this.IsBusy;
        listFilterByDate5.Loader = x => this.LoadByFilterAsync(x);
        listFilterByDate5.Counter = this.CountByFilterAsync;
        listFilterByDate5.From = DateTime.Today.AddDays(1 - DateTime.Today.Day);
        DateTime dateTime1 = DateTime.Today.AddMonths(1);
        listFilterByDate5.Till = dateTime1.AddDays(-DateTime.Today.Day);
        listFilterArray[4] = listFilterByDate5;

        ListFilterByDate listFilterByDate6 = new ListFilterByDate();
        listFilterByDate6.Title = this["Past Month"];
        listFilterByDate6.CanLoad = x => !this.IsBusy;
        listFilterByDate6.Loader = x => this.LoadByFilterAsync(x);
        listFilterByDate6.Counter = this.CountByFilterAsync;
        DateTime dateTime2 = DateTime.Today.AddDays(1 - DateTime.Today.Day);
        listFilterByDate6.From = dateTime2.AddMonths(-1);
        listFilterByDate6.Till = DateTime.Today.AddDays(-DateTime.Today.Day);
        listFilterArray[5] = listFilterByDate6;

        ListFilterByDate listFilterByDate7 = new ListFilterByDate();
        listFilterByDate7.Title = this["This Year"];
        listFilterByDate7.CanLoad = x => !this.IsBusy;
        listFilterByDate7.Loader = x => this.LoadByFilterAsync(x);
        listFilterByDate7.Counter = this.CountByFilterAsync;
        listFilterByDate7.From = DateTime.Today.AddDays(1 - DateTime.Today.DayOfYear);
        DateTime dateTime3 = DateTime.Today.AddYears(1);
        listFilterByDate7.Till = dateTime3.AddDays(-DateTime.Today.DayOfYear);
        listFilterArray[6] = listFilterByDate7;

        listFilterArray[7] = new ListFilter
        {
            Title = this["All Records"],
            CanLoad = x => !this.IsBusy,
            Loader = x => this.LoadByFilterAsync(x),
            Counter = this.CountByFilterAsync
        };
        this.Filters = listFilterArray;
    }

    protected override Task PreLoad()
    {
        return Task.WhenAll(this.Filters.Select(x => x.Initialize()));
    }

    protected override async Task PostLoad()
    {
        await base.PostLoad();

        var loginService = GetLoginService();
        if (loginService?.Session != null && !loginService.Session.IsAdmin && loginService.Session.Accounts != null)
        {
            var whProp = this.GetType().GetProperty("Warehouses");
            if (whProp?.GetValue(this) is Reference<Warehouse> whRef)
            {
                whRef.Filter = w => !w.IsDisabled && AccountSecurityHelper.CanRead(loginService, w.Id);
                whRef.RaisePropertyChanged("List");
            }

            var depProp = this.GetType().GetProperty("Depositories");
            if (depProp?.GetValue(this) is Reference<Depository> depRef)
            {
                depRef.Filter = d => !d.IsDisabled && AccountSecurityHelper.CanRead(loginService, d.Id);
                depRef.RaisePropertyChanged("List");
            }

            var offProp = this.GetType().GetProperty("Offices");
            if (offProp?.GetValue(this) is Reference<Office> offRef)
            {
                offRef.Filter = o => !o.IsDisabled && AccountSecurityHelper.CanRead(loginService, o.Id);
                offRef.RaisePropertyChanged("List");
            }
        }
    }

    protected override Task OnLoad()
    {
        ListFilter listFilter = this.SelectedFilter;
        if (listFilter == null)
        {
            listFilter = this.Filters?.FirstOrDefault();
        }
        this.SelectedFilter = listFilter;
        return this.SelectedFilter == null ? Task.CompletedTask : this.LoadByFilterAsync(this.SelectedFilter, false);
    }

    protected virtual async Task LoadByFilterAsync(ListFilter filter, bool setBusiness = true)
    {
        if (setBusiness)
            this.IsBusy = true;
        try
        {
            if (filter is ListFilterByDate listFilterByDate)
            {
                this.DateFilterFrom = listFilterByDate.From;
                this.DateFilterTill = listFilterByDate.Till;
                IEnumerable<T> filteredListByDateAsync = await this.GetFilteredListByDateAsync(this.DateFilterFrom, this.DateFilterTillInclusive);
                this.List = this.ApplySecurityFilter(filteredListByDateAsync);
            }
            else
            {
                IEnumerable<T> filteredListAsync = await this.GetFilteredListAsync(filter);
                this.List = this.ApplySecurityFilter(filteredListAsync);
            }
            this.SubCaption = filter.Title;
        }
        catch (Exception ex)
        {
            this.UserInteractionService.ShowExceptionMessage(ex);
        }
        if (!setBusiness)
            return;
        this.IsBusy = false;
    }

    protected virtual Task<int> CountByFilterAsync(ListFilter filter)
    {
        if (filter == null)
            return Task.FromResult(0);
        return filter is ListFilterByDate listFilterByDate ? this.CountFilteredListByDateAsync(listFilterByDate.From, listFilterByDate.Till.AddDays(1.0)) : this.CountFilteredListAsync(filter);
    }

    protected virtual async Task<int> CountFilteredListAsync(ListFilter filter)
    {
        var loginService = GetLoginService();
        if (loginService?.Session == null || loginService.Session.IsAdmin || loginService.Session.Accounts == null)
        {
            return await this.Repository.CountAsync();
        }

        // Для не-админа загружаем записи и считаем только разрешенные
        var items = await this.GetFilteredListAsync(filter);
        return this.ApplySecurityFilter(items)?.Count() ?? 0;
    }

    protected virtual async Task<int> CountFilteredListByDateAsync(DateTime from, DateTime till)
    {
        var loginService = GetLoginService();
        if (loginService?.Session == null || loginService.Session.IsAdmin || loginService.Session.Accounts == null)
        {
            return await this.Repository.CountAsync((Expression<Func<T, bool>>)(x => x.Date >= from && x.Date < till));
        }

        // Для не-админа отсекаем MERKEZ и возвращаем точное число видимых строк
        var items = await this.GetFilteredListByDateAsync(from, till);
        return this.ApplySecurityFilter(items)?.Count() ?? 0;
    }

    protected virtual Task<IEnumerable<T>> GetFilteredListAsync(ListFilter filter)
    {
        return this.Repository.GetAsync();
    }

    protected virtual Task<IEnumerable<T>> GetFilteredListByDateAsync(DateTime from, DateTime till)
    {
        return this.Repository.GetAsync((Expression<Func<T, bool>>)(x => x.Date >= from && x.Date < till));
    }
}
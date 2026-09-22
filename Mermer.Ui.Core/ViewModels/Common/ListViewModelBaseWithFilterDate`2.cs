// Decompiled with JetBrains decompiler
// Type: Mermer.Ui.Core.ViewModels.Common.ListViewModelBaseWithFilterDate`2
// Assembly: Mermer.Ui.Core, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: DC92D011-8413-44AC-9F10-F866D891CF66
// Assembly location: C:\Users\Admin\AppData\Local\Temp\Bofyhol\f9d7aa10a6\lib\net45\Mermer.Ui.Core.dll

using Mermer.Data.Tools.Expressions;
using Mermer.Mvvm.Services;
using Mermer.Ui.Core.Helpers;
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
namespace Mermer.Ui.Core.ViewModels.Common;

public abstract class ListViewModelBaseWithFilterDate<TList, TFilter> : 
  ListViewModelBaseWithFilter<TList, TFilter>
{
    private DateTime _dateFilterFrom = DateTime.Today;
    private DateTime _dateFilterTill = DateTime.Today;
    private bool _isCustomDateFilter;

    protected ListViewModelBaseWithFilterDate(
      IMvxMessenger messenger,
      IMvxNavigationService navigationService,
      IUserInteractionService userInteractionService)
      : base(messenger, navigationService, userInteractionService)
    {
        var today = DateTime.Today;

        Filters = new ListFilter[]
        {
        new ListFilterByDate
        {
            Title = this["Today"],
            CanLoad = x => !IsBusy,
            Loader = x => LoadByFilterAsync(x),
            Counter = CountByFilterAsync,
            From = today,
            Till = today
        },
        new ListFilterByDate
        {
            Title = this["Yesterday"],
            CanLoad = x => !IsBusy,
            Loader = x => LoadByFilterAsync(x),
            Counter = CountByFilterAsync,
            From = today.AddDays(-1),
            Till = today.AddDays(-1)
        },
        new ListFilterByDate
        {
            Title = this["This Week"],
            CanLoad = x => !IsBusy,
            Loader = x => LoadByFilterAsync(x),
            Counter = CountByFilterAsync,
            From = today.StartOfWeek(),
            Till = today.EndOfWeek()
        },
        new ListFilterByDate
        {
            Title = this["Past Week"],
            CanLoad = x => !IsBusy,
            Loader = x => LoadByFilterAsync(x),
            Counter = CountByFilterAsync,
            From = today.AddDays(-7).StartOfWeek(),
            Till = today.AddDays(-7).EndOfWeek()
        },
        new ListFilterByDate
        {
            Title = this["This Month"],
            CanLoad = x => !IsBusy,
            Loader = x => LoadByFilterAsync(x),
            Counter = CountByFilterAsync,
            From = today.AddDays(1 - today.Day),
            Till = today.AddMonths(1).AddDays(-today.Day)
        },
        new ListFilterByDate
        {
            Title = this["Past Month"],
            CanLoad = x => !IsBusy,
            Loader = x => LoadByFilterAsync(x),
            Counter = CountByFilterAsync,
            From = today.AddDays(1 - today.Day).AddMonths(-1),
            Till = today.AddDays(-today.Day)
        },
        new ListFilterByDate
        {
            Title = this["This Year"],
            CanLoad = x => !IsBusy,
            Loader = x => LoadByFilterAsync(x),
            Counter = CountByFilterAsync,
            From = today.AddDays(1 - today.DayOfYear),
            Till = today.AddYears(1).AddDays(-today.DayOfYear)
        },
        new ListFilter
        {
            Title = this["All Records"],
            CanLoad = x => !IsBusy,
            Loader = x => LoadByFilterAsync(x),
            Counter = CountByFilterAsync
        }
        };
    }

    public DateTime DateFilterFrom
  {
    get => this._dateFilterFrom;
    set => this.SetProperty<DateTime>(ref this._dateFilterFrom, value, nameof (DateFilterFrom));
  }

  public DateTime DateFilterTill
  {
    get => this._dateFilterTill;
    set => this.SetProperty<DateTime>(ref this._dateFilterTill, value, nameof (DateFilterTill));
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

    protected override Task OnLoad()
    {
        if (this.SelectedFilter == null)
        {
            // Используем публичное свойство SelectedFilter вместо закрытого поля _selectedFilter
            this.SelectedFilter = this.Filters?.FirstOrDefault();
        }

        return this.SelectedFilter == null
            ? Task.CompletedTask
            : this.LoadByFilterAsync(this.SelectedFilter, false);
    }

    protected override async Task LoadByFilterAsync(ListFilter filter, bool setBusiness = true)
  {
    ListViewModelBaseWithFilterDate<TList, TFilter> baseWithFilterDate = this;
    if (setBusiness)
      baseWithFilterDate.IsBusy = true;
    try
    {
      if (filter is ListFilterByDate listFilterByDate)
      {
        baseWithFilterDate.DateFilterFrom = listFilterByDate.From;
        baseWithFilterDate.DateFilterTill = listFilterByDate.Till;
        IEnumerable<TList> filteredListByDateAsync = await baseWithFilterDate.GetFilteredListByDateAsync(baseWithFilterDate.DateFilterFrom, baseWithFilterDate.DateFilterTillInclusive);
        baseWithFilterDate.List = filteredListByDateAsync;
      }
      else
      {
        IEnumerable<TList> filteredListAsync = await baseWithFilterDate.GetFilteredListAsync(filter);
        baseWithFilterDate.List = filteredListAsync;
      }
      baseWithFilterDate.SubCaption = filter.Title;
      baseWithFilterDate._isCustomDateFilter = false;
    }
    catch (Exception ex)
    {
      baseWithFilterDate.UserInteractionService.ShowExceptionMessage(ex);
    }
    if (!setBusiness)
      return;
    baseWithFilterDate.IsBusy = false;
  }

  protected override Task<int> CountByFilterAsync(ListFilter filter)
  {
    if (filter == null)
      return Task.FromResult<int>(0);
    return filter is ListFilterByDate listFilterByDate ? this.CountFilteredListByDateAsync(listFilterByDate.From, listFilterByDate.Till.AddDays(1.0)) : this.CountFilteredListAsync(filter);
  }

  protected abstract Expression<Func<TFilter, bool>> GetDateFilter(DateTime from, DateTime till);

  protected virtual Task<int> CountFilteredListByDateAsync(DateTime from, DateTime till)
  {
    PredicateBuilder<TFilter> predicateBuilder = this.GetPredicateBuilder((ListFilter) null);
    predicateBuilder.Add(this.GetDateFilter(from, till));
    return this.CountListAsync(predicateBuilder.Expressions);
  }

  protected virtual Task<IEnumerable<TList>> GetFilteredListByDateAsync(
    DateTime from,
    DateTime till)
  {
    PredicateBuilder<TFilter> predicateBuilder = this.GetPredicateBuilder((ListFilter) null);
    predicateBuilder.Add(this.GetDateFilter(from, till));
    return this.GetListAsync(predicateBuilder.Expressions);
  }

  public ICommand FilterByDateCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.OnFilterByDateCommandAsync), (Func<bool>) (() => !this.IsBusy && this.DateFilterFrom <= this.DateFilterTill));
    }
  }

  protected virtual async Task OnFilterByDateCommandAsync() => await this.LoadByDateAsync();

    protected virtual async Task LoadByDateAsync(bool setBusiness = true)
    {
        if (setBusiness)
            IsBusy = true;

        try
        {
            SelectedFilter = null;
            List = await GetFilteredListByDateAsync(DateFilterFrom, DateFilterTillInclusive);
            SubCaption = $"{DateFilterFrom:MMM d} - {DateFilterTill:MMM d}";
            _isCustomDateFilter = true;
        }
        catch (Exception ex)
        {
            UserInteractionService.ShowExceptionMessage(ex);
        }

        if (setBusiness)
            IsBusy = false;
    }
}

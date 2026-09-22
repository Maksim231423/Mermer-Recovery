// Decompiled with JetBrains decompiler
// Type: Mermer.Ui.Core.ViewModels.Common.ListViewModelBaseWithFilter`2
// Assembly: Mermer.Ui.Core, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: DC92D011-8413-44AC-9F10-F866D891CF66
// Assembly location: C:\Users\Admin\AppData\Local\Temp\Bofyhol\f9d7aa10a6\lib\net45\Mermer.Ui.Core.dll

using MvvmCross.Core.Navigation;
using MvvmCross.Plugins.Messenger;
using Mermer.Ui.Core.Helpers;
using Mermer.Data.Tools.Expressions;
using Mermer.Mvvm.Services;
using Mermer.Mvvm.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

#nullable disable
namespace Mermer.Ui.Core.ViewModels.Common;

public abstract class ListViewModelBaseWithFilter<TList, TFilter> : ListViewModelBase<TList>
{
  protected const string FilterAllString = "All Records";
  private IEnumerable<ListFilter> _filters;
  private ListFilter _selectedFilter;

  protected ListViewModelBaseWithFilter(
    IMvxMessenger messenger,
    IMvxNavigationService navigationService,
    IUserInteractionService userInteractionService)
    : base(messenger, navigationService, userInteractionService)
  {
    this.Filters = (IEnumerable<ListFilter>) new ListFilter[1]
    {
      new ListFilter()
      {
        Title = this["All Records", Array.Empty<object>()],
        CanLoad = (Func<ListFilter, bool>) (x => !this.IsBusy),
        Loader = (Func<ListFilter, Task>) (x => this.LoadByFilterAsync(x)),
        Counter = new Func<ListFilter, Task<int>>(this.CountByFilterAsync)
      }
    };
  }

  public IEnumerable<ListFilter> Filters
  {
    get => this._filters;
    set => this.SetProperty<IEnumerable<ListFilter>>(ref this._filters, value, nameof (Filters));
  }

  public virtual ListFilter SelectedFilter
  {
    get => this._selectedFilter;
    set => this.SetProperty<ListFilter>(ref this._selectedFilter, value, nameof (SelectedFilter));
  }

    protected override Task PreLoad()
    {
        // Запускаем подсчет счетчиков в фоне, окно открывается мгновенно
        _ = Task.Run(async () =>
        {
            try
            {
                if (this.Filters != null)
                {
                    await Task.WhenAll(this.Filters.Select(x => x.Initialize()));
                }
            }
            catch { }
        });

        return Task.CompletedTask;
    }

    protected override Task OnLoad()
  {
    ListFilter listFilter = this.SelectedFilter;
    if (listFilter == null)
    {
      IEnumerable<ListFilter> filters = this.Filters;
      listFilter = filters != null ? filters.FirstOrDefault<ListFilter>() : (ListFilter) null;
    }
    this.SelectedFilter = listFilter;
    return this.SelectedFilter == null ? Task.CompletedTask : this.LoadByFilterAsync(this.SelectedFilter, false);
  }

    protected virtual async Task LoadByFilterAsync(ListFilter filter, bool setBusiness = true)
    {
        if (setBusiness)
            IsBusy = true;

        try
        {
            List = await GetFilteredListAsync(filter);
            SubCaption = filter.Title;
        }
        catch (Exception ex)
        {
            UserInteractionService.ShowExceptionMessage(ex);
        }

        if (setBusiness)
            IsBusy = false;
    }

    protected virtual Task<int> CountByFilterAsync(ListFilter filter)
  {
    return filter != null ? this.CountFilteredListAsync(filter) : Task.FromResult<int>(0);
  }

  protected virtual Task<int> CountFilteredListAsync(ListFilter filter)
  {
    return this.CountListAsync(this.GetPredicateBuilder(filter).Expressions);
  }

  protected virtual Task<IEnumerable<TList>> GetFilteredListAsync(ListFilter filter)
  {
    return this.GetListAsync(this.GetPredicateBuilder(filter).Expressions);
  }

  protected virtual PredicateBuilder<TFilter> GetPredicateBuilder(ListFilter filter)
  {
    return new PredicateBuilder<TFilter>();
  }

  protected abstract Task<int> CountListAsync(
    params Expression<Func<TFilter, bool>>[] predicates);

  protected abstract Task<IEnumerable<TList>> GetListAsync(
    params Expression<Func<TFilter, bool>>[] predicates);
}

// Decompiled with JetBrains decompiler
// Type: Mermer.Ui.Core.ViewModels.Warehousing.Ordering.StockOrdersListViewModel
// Assembly: Mermer.Ui.Core, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: DC92D011-8413-44AC-9F10-F866D891CF66
// Assembly location: C:\Users\Admin\AppData\Local\Temp\Bofyhol\f9d7aa10a6\lib\net45\Mermer.Ui.Core.dll

using Mermer.Authorization.Enums;
using Mermer.Authorization.Services;
using Mermer.CRM.Models;
using Mermer.Data.Authorizers;
using Mermer.Data.Storage;
using Mermer.Enterprise.Models;
using Mermer.Mvvm.Services;
using Mermer.Ui.Core.Helpers;
using Mermer.Ui.Core.ViewModels.Common;
using Mermer.Warehousing.Ordering.Models;
using MvvmCross.Core.Navigation;
using MvvmCross.Plugins.Messenger;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

#nullable disable
namespace Mermer.Ui.Core.ViewModels.Warehousing.Ordering;

public class StockOrdersListViewModel : TransactionsListViewModel<StockOrder>
{
  private readonly ILoginService _loginService;
  private const string FilterOwnString = "My Orders";
  private const string FilterOpenString = "Open";
  private const string FilterCompletedString = "Completed";
  private const string FilterDeletedString = "Deleted";
    public Reference<Partner> Partners { get; }
    public StockOrdersListViewModel(
      ILoginService loginService,
      Reference<Warehouse> warehouses,
      IMvxMessenger messenger,
      IRepository<StockOrder> repository,
      Reference<Partner> partners,
      IListAuthorizer<StockOrder> authorizer,
      IMvxNavigationService navigationService,
      IUserInteractionService userInteractionService)
      : base(repository, authorizer, messenger, navigationService, userInteractionService)
    {
        _loginService = loginService;
        Warehouses = warehouses;
        this.Partners = partners;
        Filters = new[]
        {
        new ListFilter
        {
            Title = this["My Orders"],
            Tag = "My Orders",
            CanLoad = x => !IsBusy,
            Loader = x => LoadByFilterAsync(x),
            Counter = CountByFilterAsync
        },
        new ListFilter
        {
            Title = this["Open"],
            Tag = "Open",
            CanLoad = x => !IsBusy,
            Loader = x => LoadByFilterAsync(x),
            Counter = CountByFilterAsync
        },
        new ListFilter
        {
            Title = this["Completed"],
            Tag = "Completed",
            CanLoad = x => !IsBusy,
            Loader = x => LoadByFilterAsync(x),
            Counter = CountByFilterAsync
        },
        new ListFilter
        {
            Title = this["Deleted"],
            Tag = "Deleted",
            CanLoad = x => !IsBusy,
            Loader = x => LoadByFilterAsync(x),
            Counter = CountByFilterAsync
        }
    };
    }

    public Reference<Warehouse> Warehouses { get; }

  protected override Task PreLoad() => Task.WhenAll(base.PreLoad(), this.Warehouses.Initialize(), this.Partners.Initialize());

    protected override Task<int> CountFilteredListAsync(ListFilter filter)
    {
        if (filter?.Tag == null) return base.CountFilteredListAsync(filter);

        switch (filter.Tag.ToString())
        {
            case "My Orders":
                string userId = this._loginService.Session?.UserId;
                if (string.IsNullOrEmpty(userId)) return Task.FromResult(0);
                return this.Repository.CountAsync((Expression<Func<StockOrder, bool>>)(x => x.UserId == userId));
            case "Open":
                return this.Repository.CountAsync((Expression<Func<StockOrder, bool>>)(x => !x.IsCompleted && !x.IsDisabled));
            case "Completed":
                return this.Repository.CountAsync((Expression<Func<StockOrder, bool>>)(x => x.IsCompleted && !x.IsDisabled));
            case "Deleted":
                return this.Repository.CountAsync((Expression<Func<StockOrder, bool>>)(x => x.IsDisabled));
            default:
                return base.CountFilteredListAsync(filter);
        }
    }

    protected override async Task PostLoad()
    {
        await base.PostLoad();

        if (this._loginService?.Session != null && !this._loginService.Session.IsAdmin && this._loginService.Session.Accounts != null)
        {
            var allowedIds = this._loginService.Session.Accounts
                .Where(a => a.Value.HasFlag(AccountAccessLevel.Operate) || a.Value.HasFlag(AccountAccessLevel.Read))
                .Select(a => a.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            this.Warehouses.Filter = w => !w.IsDisabled && allowedIds.Contains(w.Id);
        }
        else
        {
            this.Warehouses.Filter = w => !w.IsDisabled;
        }

        this.Warehouses?.RaisePropertyChanged("List");
    }

    protected override Task<IEnumerable<StockOrder>> GetFilteredListAsync(ListFilter filter)
    {
        if (filter?.Tag == null) return base.GetFilteredListAsync(filter);

        switch (filter.Tag.ToString())
        {
            case "My Orders":
                string userId = this._loginService.Session?.UserId;
                if (string.IsNullOrEmpty(userId)) return Task.FromResult(Enumerable.Empty<StockOrder>());
                return this.Repository.GetAsync((Expression<Func<StockOrder, bool>>)(x => x.UserId == userId));
            case "Open":
                return this.Repository.GetAsync((Expression<Func<StockOrder, bool>>)(x => !x.IsCompleted && !x.IsDisabled));
            case "Completed":
                return this.Repository.GetAsync((Expression<Func<StockOrder, bool>>)(x => x.IsCompleted && !x.IsDisabled));
            case "Deleted":
                return this.Repository.GetAsync((Expression<Func<StockOrder, bool>>)(x => x.IsDisabled));
            default:
                return base.GetFilteredListAsync(filter);
        }
    }
}

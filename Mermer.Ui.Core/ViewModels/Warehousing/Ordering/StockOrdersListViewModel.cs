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

    public Reference<Partner> Partners { get; }
    public Reference<Warehouse> Warehouses { get; }

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
        Partners = partners;

        Filters = new[]
        {
            new ListFilter { Title = this["My Orders"], Tag = "My Orders", CanLoad = x => !IsBusy, Loader = x => LoadByFilterAsync(x), Counter = CountByFilterAsync },
            new ListFilter { Title = this["Open"], Tag = "Open", CanLoad = x => !IsBusy, Loader = x => LoadByFilterAsync(x), Counter = CountByFilterAsync },
            new ListFilter { Title = this["Completed"], Tag = "Completed", CanLoad = x => !IsBusy, Loader = x => LoadByFilterAsync(x), Counter = CountByFilterAsync },
            new ListFilter { Title = this["Deleted"], Tag = "Deleted", CanLoad = x => !IsBusy, Loader = x => LoadByFilterAsync(x), Counter = CountByFilterAsync }
        };
    }

    protected override async Task PreLoad()
    {
        await Task.WhenAll(base.PreLoad(), this.Warehouses.Initialize(), this.Partners.Initialize());

        if (this.Warehouses != null)
        {
            this.Warehouses.Filter = w => !w.IsDisabled && AccountSecurityHelper.CanRead(this._loginService, w.Id);
            this.Warehouses.RaisePropertyChanged("List");
        }
    }

    protected override async Task PostLoad()
    {
        await base.PostLoad();

        if (this.Warehouses != null)
        {
            this.Warehouses.Filter = w => !w.IsDisabled && AccountSecurityHelper.CanRead(this._loginService, w.Id);
            this.Warehouses.RaisePropertyChanged("List");
        }
    }

    protected override async Task<int> CountFilteredListAsync(ListFilter filter)
    {
        if (this._loginService?.Session == null || this._loginService.Session.IsAdmin || this._loginService.Session.Accounts == null)
        {
            switch (filter?.Tag?.ToString())
            {
                case "My Orders":
                    string uId = this._loginService.Session?.UserId;
                    return string.IsNullOrEmpty(uId) ? 0 : await this.Repository.CountAsync(x => x.UserId == uId);
                case "Open":
                    return await this.Repository.CountAsync(x => !x.IsCompleted && !x.IsDisabled);
                case "Completed":
                    return await this.Repository.CountAsync(x => x.IsCompleted && !x.IsDisabled);
                case "Deleted":
                    return await this.Repository.CountAsync(x => x.IsDisabled);
                default:
                    return await base.CountFilteredListAsync(filter);
            }
        }

        var items = await this.GetFilteredListAsync(filter);
        return items?.Count() ?? 0;
    }

    protected override async Task<IEnumerable<StockOrder>> GetFilteredListAsync(ListFilter filter)
    {
        IEnumerable<StockOrder> raw;
        switch (filter?.Tag?.ToString())
        {
            case "My Orders":
                string userId = this._loginService.Session?.UserId;
                raw = string.IsNullOrEmpty(userId)
                    ? Enumerable.Empty<StockOrder>()
                    : await this.Repository.GetAsync(x => x.UserId == userId);
                break;
            case "Open":
                raw = await this.Repository.GetAsync(x => !x.IsCompleted && !x.IsDisabled);
                break;
            case "Completed":
                raw = await this.Repository.GetAsync(x => x.IsCompleted && !x.IsDisabled);
                break;
            case "Deleted":
                raw = await this.Repository.GetAsync(x => x.IsDisabled);
                break;
            default:
                raw = await base.GetFilteredListAsync(filter);
                break;
        }

        return ApplySecurityFilter(raw);
    }
}
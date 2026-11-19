using MvvmCross.Core.Navigation;
using MvvmCross.Plugins.Messenger;
using Mermer.Enterprise.Models;
using Mermer.Ui.Core.Helpers;
using Mermer.Ui.Core.ViewModels.Common;
using Mermer.Ui.Core.ViewModels.Transactions;
using Mermer.Warehousing.Models;
using Mermer.Data.Authorizers;
using Mermer.Data.Storage;
using Mermer.Mvvm.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Mermer.Ui.Core.ViewModels.Warehousing;

public class StockTransfersListViewModel :
    StockTransactionsListViewModel<StockTransfer, StockTransferLine>
{
    private bool _initialized;

    public StockTransfersListViewModel(
        IMvxMessenger messenger,
        Reference<Warehouse> warehouses,
        IRepository<StockTransfer> repository,
        IListAuthorizer<StockTransfer> authorizer,
        IMvxNavigationService navigationService,
        IUserInteractionService userInteractionService)
        : base(messenger, repository, authorizer, warehouses, navigationService, userInteractionService)
    {
        var today = DateTime.Today;

        Filters = new ListFilter[]
        {
            new ListFilter
            {
                Title = this["Conflicted"],
                CanLoad = x => !IsBusy,
                Loader = x => LoadByFilterAsync(x),
                Counter = CountByFilterAsync,
                Tag = "Conflicted"
            },
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
                Title = this["This Week"],
                CanLoad = x => !IsBusy,
                Loader = x => LoadByFilterAsync(x),
                Counter = CountByFilterAsync,
                From = today.StartOfWeek(),
                Till = today.EndOfWeek()
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
                Counter = CountByFilterAsync,
                Tag = "All"
            }
        };
    }

    protected override async Task PreLoad()
    {
        await base.PreLoad();
        if (this.Warehouses != null)
        {
            var ls = this.GetLoginService();
            this.Warehouses.Filter = w => !w.IsDisabled && AccountSecurityHelper.CanRead(ls, w.Id);
            this.Warehouses.RaisePropertyChanged("List");
        }
    }

    protected override Task OnLoad()
    {
        if (!_initialized)
        {
            SelectedFilter = Filters.ElementAt(2); // This Week
            _initialized = true;
        }
        return base.OnLoad();
    }

    protected override async Task<int> CountByFilterAsync(ListFilter filter)
    {
        if (filter.Tag?.ToString() == "Conflicted")
        {
            var items = await this.GetFilteredListAsync(filter);
            return items?.Count() ?? 0;
        }

        return await base.CountByFilterAsync(filter);
    }

    protected override async Task<IEnumerable<StockTransfer>> GetFilteredListAsync(ListFilter filter)
    {
        if (filter.Tag?.ToString() == "Conflicted")
        {
            var raw = await Repository.GetAsync(x => x.IsConflicted);
            return ApplySecurityFilter(raw);
        }

        return await base.GetFilteredListAsync(filter);
    }
}
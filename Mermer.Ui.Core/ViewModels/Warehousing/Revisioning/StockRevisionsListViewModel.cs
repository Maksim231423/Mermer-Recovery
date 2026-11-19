using MvvmCross.Core.Navigation;
using MvvmCross.Plugins.Messenger;
using Mermer.Authorization.Services;
using Mermer.Enterprise.Models;
using Mermer.Transactions.Services;
using Mermer.Ui.Core.Helpers;
using Mermer.Ui.Core.ViewModels.Common;
using Mermer.Warehousing.Revisioning.Models;
using Mermer.Warehousing.Revisioning.Services;
using Mermer.Data.Authorizers;
using Mermer.Data.Storage;
using Mermer.Mvvm.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

#nullable disable
namespace Mermer.Ui.Core.ViewModels.Warehousing.Revisioning;

public class StockRevisionsListViewModel : TransactionsListViewModel<StockRevision>
{
    private readonly ILoginService _loginService;
    private readonly ITransactionCodeGenerationService _codeGentor;

    public StockRevisionsListViewModel(
      IMvxMessenger messenger,
      ILoginService loginService,
      Reference<Warehouse> warehouses,
      IStockRevisionsRepository repository,
      IListAuthorizer<StockRevision> authorizer,
      IMvxNavigationService navigationService,
      ITransactionCodeGenerationService codeGentor,
      IUserInteractionService userInteractionService)
      : base(repository, authorizer, messenger, navigationService, userInteractionService)
    {
        _loginService = loginService;
        _codeGentor = codeGentor;
        Warehouses = warehouses;

        this.DateFilterFrom = DateTime.Today;
        this.DateFilterTill = DateTime.Today;

        Filters = new[]
        {
            new ListFilter
            {
                Title = this["Active"],
                Tag = "Active",
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
            },
            new ListFilter
            {
                Title = this["All Records"],
                Tag = "All Records",
                CanLoad = x => !IsBusy,
                Loader = x => LoadByFilterAsync(x),
                Counter = CountByFilterAsync
            }
        };
    }

    public Reference<Warehouse> Warehouses { get; }

    protected override async Task PreLoad()
    {
        await base.PreLoad();
        await this.Warehouses.Initialize();
        ApplySecurityFilters();
    }

    protected override async Task PostLoad()
    {
        await base.PostLoad();
        ApplySecurityFilters();
    }

    private void ApplySecurityFilters()
    {
        if (this.Warehouses != null)
        {
            this.Warehouses.Filter = w => !w.IsDisabled && AccountSecurityHelper.CanRead(this._loginService, w.Id);
            this.Warehouses.RaisePropertyChanged("List");
        }
    }

    protected override async Task OnCreateNewAsync()
    {
        try
        {
            ApplySecurityFilters();

            // Передаем в мастер создания только склады с правом совершения операций (Operate)
            var operateWarehouses = this.Warehouses.List
                .Where(w => !w.IsDisabled && AccountSecurityHelper.CanOperate(this._loginService, w.Id))
                .ToList();

            var result = await this.NavigationService.Navigate<StockRevisionsListNewRevisionViewModel, IEnumerable<Warehouse>, StockRevisionsListNewRevisionViewModel.Result>(operateWarehouses);
            if (result == null)
                return;

            var stockRevision = new StockRevision
            {
                Id = Guid.NewGuid().ToString(),
                Code = await this._codeGentor.GetNextCode(),
                Date = result.StartDate,
                WarehouseId = result.WarehouseId,
                UserId = this._loginService.Session?.UserId,
                UserName = this._loginService.Session?.Username
            };

            await this.Repository.CreateAsync(stockRevision);
            await this.NavigationService.Navigate<StockRevisionDetailsViewModel, string>(stockRevision.Id);
        }
        catch (Exception ex)
        {
            this.UserInteractionService.ShowExceptionMessage(ex);
        }
    }

    protected override async Task<int> CountFilteredListAsync(ListFilter filter)
    {
        if (this._loginService?.Session == null || this._loginService.Session.IsAdmin || this._loginService.Session.Accounts == null)
        {
            switch (filter?.Tag?.ToString())
            {
                case "Active":
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

    protected override async Task<IEnumerable<StockRevision>> GetFilteredListAsync(ListFilter filter)
    {
        IEnumerable<StockRevision> raw;
        switch (filter?.Tag?.ToString())
        {
            case "Active":
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
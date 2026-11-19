using MvvmCross.Core.Navigation;
using MvvmCross.Plugins.Messenger;
using Mermer.Authorization.Enums;
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
    private const string FilterActiveString = "Active";
    private const string FilterCompletedString = "Completed";
    private const string FilterDeletedString = "Deleted";
    private const string FilterAllString = "All Records";
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
        if (this._loginService?.Session != null && !this._loginService.Session.IsAdmin && this._loginService.Session.Accounts != null)
        {
            var accounts = this._loginService.Session.Accounts;

            // Скрываем склады с явным запретом (None / 0)
            this.Warehouses.Filter = w =>
                !w.IsDisabled &&
                (!accounts.TryGetValue(w.Id, out var level) ||
                 (level != AccountAccessLevel.None && (level.HasFlag(AccountAccessLevel.Operate) || level.HasFlag(AccountAccessLevel.Read))));
        }
        else
        {
            this.Warehouses.Filter = w => !w.IsDisabled;
        }

        this.Warehouses.RaisePropertyChanged("List");
    }

    protected override async Task OnCreateNewAsync()
    {
        try
        {
            ApplySecurityFilters();

            // Передаем в мастер создания только отфильтрованные (разрешенные) склады
            var allowedWarehouses = this.Warehouses.List.ToList();

            StockRevisionsListNewRevisionViewModel.Result result = await this.NavigationService.Navigate<StockRevisionsListNewRevisionViewModel, IEnumerable<Warehouse>, StockRevisionsListNewRevisionViewModel.Result>(allowedWarehouses);
            if (result == null)
                return;

            StockRevision stockRevision = new StockRevision
            {
                Id = Guid.NewGuid().ToString(),
                Code = await this._codeGentor.GetNextCode(),
                Date = result.StartDate,
                WarehouseId = result.WarehouseId,
                UserId = this._loginService.Session.UserId,
                UserName = this._loginService.Session.Username
            };

            await this.Repository.CreateAsync(stockRevision);
            await this.NavigationService.Navigate<StockRevisionDetailsViewModel, string>(stockRevision.Id);
        }
        catch (Exception ex)
        {
            this.UserInteractionService.ShowExceptionMessage(ex);
        }
    }

    protected override Task<int> CountFilteredListAsync(ListFilter filter)
    {
        switch (filter.Tag.ToString())
        {
            case "Active":
                return this.Repository.CountAsync((Expression<Func<StockRevision, bool>>)(x => !x.IsCompleted && !x.IsDisabled));
            case "Completed":
                return this.Repository.CountAsync((Expression<Func<StockRevision, bool>>)(x => x.IsCompleted && !x.IsDisabled));
            case "Deleted":
                return this.Repository.CountAsync((Expression<Func<StockRevision, bool>>)(x => x.IsDisabled));
            default:
                return base.CountFilteredListAsync(filter);
        }
    }

    protected override Task<IEnumerable<StockRevision>> GetFilteredListAsync(ListFilter filter)
    {
        switch (filter.Tag.ToString())
        {
            case "Active":
                return this.Repository.GetAsync((Expression<Func<StockRevision, bool>>)(x => !x.IsCompleted && !x.IsDisabled));
            case "Completed":
                return this.Repository.GetAsync((Expression<Func<StockRevision, bool>>)(x => x.IsCompleted && !x.IsDisabled));
            case "Deleted":
                return this.Repository.GetAsync((Expression<Func<StockRevision, bool>>)(x => x.IsDisabled));
            default:
                return base.GetFilteredListAsync(filter);
        }
    }
}
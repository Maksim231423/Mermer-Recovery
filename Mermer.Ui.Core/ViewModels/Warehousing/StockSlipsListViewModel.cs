using MvvmCross.Core.Navigation;
using MvvmCross.Plugins.Messenger;
using Mermer.Authorization.Enums;
using Mermer.Authorization.Services;
using Mermer.Enterprise.Models;
using Mermer.Ui.Core.Helpers;
using Mermer.Ui.Core.ViewModels.Transactions;
using Mermer.Warehousing.Models;
using Mermer.Data.Authorizers;
using Mermer.Data.Storage;
using Mermer.Mvvm.Services;
using System;
using System.Linq;
using System.Threading.Tasks;

#nullable disable
namespace Mermer.Ui.Core.ViewModels.Warehousing;

public class StockSlipsListViewModel : StockTransactionsListViewModel<StockSlip, StockSlipLine>
{
    private readonly ILoginService _loginService;

    public StockSlipsListViewModel(
        IMvxMessenger messenger,
        Reference<Warehouse> warehouses,
        IRepository<StockSlip> repository,
        IListAuthorizer<StockSlip> authorizer,
        IMvxNavigationService navigationService,
        IUserInteractionService userInteractionService,
        ILoginService loginService)
        : base(messenger, repository, authorizer, warehouses, navigationService, userInteractionService)
    {
        this._loginService = loginService;
    }

    protected override async Task PostLoad()
    {
        await base.PostLoad();

        if (this._loginService?.Session != null && !this._loginService.Session.IsAdmin && this._loginService.Session.Accounts != null)
        {
            var allowedWarehouseIds = this._loginService.Session.Accounts
                .Where(a => a.Value.HasFlag(AccountAccessLevel.Operate) || a.Value.HasFlag(AccountAccessLevel.Read))
                .Select(a => a.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            this.Warehouses.Filter = w => !w.IsDisabled && allowedWarehouseIds.Contains(w.Id);
        }
        else
        {
            this.Warehouses.Filter = w => !w.IsDisabled;
        }

        this.Warehouses?.RaisePropertyChanged("List");
    }
}
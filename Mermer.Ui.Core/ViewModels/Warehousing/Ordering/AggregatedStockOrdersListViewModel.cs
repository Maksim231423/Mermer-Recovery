using MvvmCross.Core.Navigation;
using MvvmCross.Plugins.Messenger;
using Mermer.Authorization.Services;
using Mermer.Enterprise.Models;
using Mermer.Ui.Core.Helpers;
using Mermer.Ui.Core.ViewModels.Common;
using Mermer.Warehousing.Ordering.Models;
using Mermer.Data.Authorizers;
using Mermer.Data.Storage;
using Mermer.Mvvm.Services;
using System.Threading.Tasks;

#nullable disable
namespace Mermer.Ui.Core.ViewModels.Warehousing.Ordering;

public class AggregatedStockOrdersListViewModel : TransactionsListViewModel<AggregatedStockOrder>
{
    private readonly ILoginService _loginService;

    public AggregatedStockOrdersListViewModel(
      IMvxMessenger messenger,
      Reference<Warehouse> warehouses,
      IMvxNavigationService navigationService,
      IRepository<AggregatedStockOrder> repository,
      IListAuthorizer<AggregatedStockOrder> authorizer,
      IUserInteractionService userInteractionService,
      ILoginService loginService)
      : base(repository, authorizer, messenger, navigationService, userInteractionService)
    {
        this.Warehouses = warehouses;
        this._loginService = loginService;
    }

    public Reference<Warehouse> Warehouses { get; }

    protected override async Task PreLoad()
    {
        await Task.WhenAll(base.PreLoad(), this.Warehouses.Initialize());

        if (this.Warehouses != null)
        {
            this.Warehouses.Filter = w => !w.IsDisabled && AccountSecurityHelper.CanRead(this._loginService, w.Id);
            this.Warehouses.RaisePropertyChanged("List");
        }
    }
}
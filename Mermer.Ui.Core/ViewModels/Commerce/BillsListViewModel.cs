using MvvmCross.Core.Navigation;
using MvvmCross.Plugins.Messenger;
using Mermer.Authorization.Enums;
using Mermer.Authorization.Services;
using Mermer.Commerce.Models;
using Mermer.CRM.Models;
using Mermer.Enterprise.Models;
using Mermer.Ui.Core.Helpers;
using Mermer.Ui.Core.ViewModels.Transactions;
using Mermer.Data.Authorizers;
using Mermer.Data.Storage;
using Mermer.Mvvm.Services;
using System.Threading.Tasks;

#nullable disable
namespace Mermer.Ui.Core.ViewModels.Commerce;

public class BillsListViewModel : FundsTransactionsListViewModel<Bill, BillLine>
{
    private readonly ILoginService _loginService;

    public BillsListViewModel(
      IMvxMessenger messenger,
      IRepository<Bill> repository,
      IListAuthorizer<Bill> authorizer,
      Reference<Partner> partners,
      Reference<Depository> depositories,
      IMvxNavigationService navigationService,
      IUserInteractionService userInteractionService,
      ILoginService loginService)
      : base(messenger, repository, authorizer, depositories, navigationService, userInteractionService)
    {
        this.Partners = partners;
        this._loginService = loginService;
    }

    public Reference<Partner> Partners { get; }

    protected override async Task PreLoad()
    {
        await Task.WhenAll(base.PreLoad(), this.Partners.Initialize());

        this.Depositories.Filter = d => !d.IsDisabled && AccountSecurityHelper.CanRead(this._loginService, d.Id);
        this.Depositories?.RaisePropertyChanged("List");
    }
}
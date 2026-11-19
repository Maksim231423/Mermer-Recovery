using MvvmCross.Core.Navigation;
using MvvmCross.Plugins.Messenger;
using Mermer.CRM.Models;
using Mermer.Enterprise.Models;
using Mermer.Ui.Core.Helpers;
using Mermer.Ui.Core.ViewModels.Common;
using Mermer.Data.Authorizers;
using Mermer.Data.Storage;
using Mermer.Mvvm.Services;
using System.Threading.Tasks;

#nullable disable
namespace Mermer.Ui.Core.ViewModels.CRM;

public class PartnerTransfersListViewModel : TransactionsListViewModel<PartnerTransfer>
{
    public PartnerTransfersListViewModel(
      IMvxMessenger messenger,
      Reference<Office> offices,
      Reference<Partner> partners,
      IRepository<PartnerTransfer> repository,
      IListAuthorizer<PartnerTransfer> authorizer,
      IMvxNavigationService navigationService,
      IUserInteractionService userInteractionService)
      : base(repository, authorizer, messenger, navigationService, userInteractionService)
    {
        this.Offices = offices;
        this.Partners = partners;
    }

    public Reference<Partner> Partners { get; set; }
    public Reference<Office> Offices { get; }

    protected override async Task PreLoad()
    {
        await Task.WhenAll(base.PreLoad(), this.Partners.Initialize(), this.Offices.Initialize());

        var loginService = this.GetLoginService();
        this.Offices.Filter = o => !o.IsDisabled && AccountSecurityHelper.CanRead(loginService, o.Id);
        this.Offices?.RaisePropertyChanged("List");
    }
}
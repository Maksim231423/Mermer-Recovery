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

public class PartnerSlipsListViewModel : TransactionsListViewModel<PartnerSlip>
{
    public PartnerSlipsListViewModel(
      Reference<Office> offices,
      IRepository<PartnerSlip> repository,
      IListAuthorizer<PartnerSlip> authorizer,
      IMvxMessenger messenger,
      IMvxNavigationService navigationService,
      IUserInteractionService userInteractionService)
      : base(repository, authorizer, messenger, navigationService, userInteractionService)
    {
        this.Offices = offices;
    }

    public Reference<Office> Offices { get; }

    protected override async Task PreLoad()
    {
        await Task.WhenAll(base.PreLoad(), this.Offices.Initialize());

        var loginService = this.GetLoginService();
        this.Offices.Filter = o => !o.IsDisabled && AccountSecurityHelper.CanRead(loginService, o.Id);
        this.Offices?.RaisePropertyChanged("List");
    }
}
using MvvmCross.Core.Navigation;
using MvvmCross.Plugins.Messenger;
using Mermer.Enterprise.Models;
using Mermer.Finance.Spending.Models;
using Mermer.Ui.Core.Helpers;
using Mermer.Ui.Core.ViewModels.Transactions;
using Mermer.Data.Authorizers;
using Mermer.Data.Storage;
using Mermer.Mvvm.Services;
using System.Threading.Tasks;

#nullable disable
namespace Mermer.Ui.Core.ViewModels.Finance.Spending;

public class ExpenseSlipsListViewModel : FundsTransactionsListViewModel<ExpenseSlip, ExpenseSlipLine>
{
    public ExpenseSlipsListViewModel(
        IMvxMessenger messenger,
        IRepository<ExpenseSlip> repository,
        IListAuthorizer<ExpenseSlip> authorizer,
        Reference<Depository> depositories,
        IMvxNavigationService navigationService,
        IUserInteractionService userInteractionService)
        : base(messenger, repository, authorizer, depositories, navigationService, userInteractionService)
    {
    }

    protected override async Task PreLoad()
    {
        await base.PreLoad();
        if (this.Depositories != null)
        {
            var ls = this.GetLoginService();
            this.Depositories.Filter = d => !d.IsDisabled && AccountSecurityHelper.CanRead(ls, d.Id);
            this.Depositories.RaisePropertyChanged("List");
        }
    }
}
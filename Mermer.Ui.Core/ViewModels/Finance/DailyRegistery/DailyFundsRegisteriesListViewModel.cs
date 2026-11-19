using MvvmCross.Core.Navigation;
using MvvmCross.Plugins.Messenger;
using Mermer.Authorization.Enums;
using Mermer.Authorization.Services;
using Mermer.Enterprise.Models;
using Mermer.Finance.DailyRegistery.Models;
using Mermer.Finance.DailyRegistery.Services;
using Mermer.FundsManagement.Models;
using Mermer.Ui.Core.Helpers;
using Mermer.Ui.Core.ViewModels.Transactions;
using Mermer.Data.Authorizers;
using Mermer.Data.Storage;
using Mermer.Mvvm.Services;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;

#nullable disable
namespace Mermer.Ui.Core.ViewModels.Finance.DailyRegistery;

public class DailyFundsRegisteriesListViewModel :
  FundsTransactionsListViewModel<DailyFundsRegistery, DailyFundsRegisteryLine>
{
    private readonly IReadOnlyListAuthorizer<FundsBalance> _balanceAuthorizer;
    private readonly ILoginService _loginService;
    private bool _canViewBalance;

    public DailyFundsRegisteriesListViewModel(
      IMvxMessenger messenger,
      Reference<Depository> depositories,
      IDailyFundsRegisteriesRepository repository,
      IListAuthorizer<DailyFundsRegistery> authorizer,
      IReadOnlyListAuthorizer<FundsBalance> balanceAuthorizer,
      IMvxNavigationService navigationService,
      IUserInteractionService userInteractionService,
      ILoginService loginService)
      : base(messenger, (IRepository<DailyFundsRegistery>)repository, authorizer, depositories, navigationService, userInteractionService)
    {
        this._balanceAuthorizer = balanceAuthorizer;
        this._loginService = loginService;
    }

    public bool CanViewBalance
    {
        get => this._canViewBalance;
        set => this.SetProperty<bool>(ref this._canViewBalance, value, nameof(CanViewBalance));
    }

    protected override Task PreLoad()
    {
        try
        {
            this.CanViewBalance = false;
            this._balanceAuthorizer.Authorize();
            this.CanViewBalance = true;
        }
        catch (Exception ex)
        {
        }
        return base.PreLoad();
    }

    protected override async Task PostLoad()
    {
        await base.PostLoad();

        if (this._loginService?.Session != null && !this._loginService.Session.IsAdmin && this._loginService.Session.Accounts != null)
        {
            var accounts = this._loginService.Session.Accounts;

            this.Depositories.Filter = d =>
                !d.IsDisabled &&
                (!accounts.TryGetValue(d.Id, out var level) ||
                 (level != AccountAccessLevel.None && (level.HasFlag(AccountAccessLevel.Operate) || level.HasFlag(AccountAccessLevel.Read))));
        }
        else
        {
            this.Depositories.Filter = d => !d.IsDisabled;
        }

        this.Depositories?.RaisePropertyChanged("List");
    }

    protected override async Task<IEnumerable<DailyFundsRegistery>> GetFilteredListAsync(
      ListFilter filter)
    {
        return (IEnumerable<DailyFundsRegistery>)await ((IDailyFundsRegisteriesRepository)this.Repository).GetAsync(Array.Empty<Expression<Func<DailyFundsRegistery, bool>>>());
    }

    protected override async Task<IEnumerable<DailyFundsRegistery>> GetFilteredListByDateAsync(
      DateTime from,
      DateTime till)
    {
        return (IEnumerable<DailyFundsRegistery>)await ((IDailyFundsRegisteriesRepository)this.Repository).GetAsync((Expression<Func<DailyFundsRegistery, bool>>)(x => x.Date >= from && x.Date < till));
    }
}
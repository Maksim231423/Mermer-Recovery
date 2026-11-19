using MvvmCross.Core.Navigation;
using MvvmCross.Core.ViewModels;
using Mermer.Authorization.Enums;
using Mermer.Authorization.Services;
using Mermer.Enterprise.Models;
using Mermer.FundsManagement.Models;
using Mermer.Transactions.Models;
using Mermer.Transactions.Services;
using Mermer.Ui.Core.Helpers;
using Mermer.Data.Authorizers;
using Mermer.Data.Extenders;
using Mermer.Data.Storage;
using Mermer.Mvvm.Services;
using Mermer.Mvvm.ViewModels;
using Mermer.Services;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

#nullable disable
namespace Mermer.Ui.Core.ViewModels.Transactions;

public class FundsTransactionDetailsViewModel<T, TLine> : TransactionDetailsViewModel<T, TLine>
  where T : FundsTransaction<TLine>
  where TLine : FundsTransactionLine
{
    protected FundsTransactionDetailsViewModel(
      IRepository<T> repository,
      IListAuthorizer<T> authorizer,
      IConfigurator configurator,
      ILoginService loginService,
      Reference<Currency> currencies,
      Reference<Depository> depositories,
      IMvxNavigationService navigationService,
      ITransactionCodeGenerationService codegentor,
      IUserInteractionService userInteractionService)
      : base(configurator, repository, authorizer, loginService, currencies, navigationService, codegentor, userInteractionService)
    {
        this.Depositories = depositories;
    }

    public Reference<Depository> Depositories { get; }

    protected override Task PreLoad() => Task.WhenAll(base.PreLoad(), this.Depositories.Initialize());

    protected override async Task OnLoad()
    {
        await base.OnLoad();
        if (!string.IsNullOrEmpty(ItemId))
            return;

        // Если касса по умолчанию не разрешена для операций — подставляем первую разрешенную
        string defaultDep = AppSettings.DefaultDepositoryId;
        if (AccountSecurityHelper.CanOperate(LoginService, defaultDep))
        {
            Details.DepositoryId = defaultDep;
        }
        else
        {
            var firstAllowed = Depositories?.List?.FirstOrDefault(d => !d.IsDisabled && AccountSecurityHelper.CanOperate(LoginService, d.Id));
            Details.DepositoryId = firstAllowed?.Id;
        }
    }

    protected override async Task PostLoad()
    {
        await base.PostLoad();

        // Для новых документов в выпадающем списке только кассы с правом Operate.
        // Для существующего документа оставляем его кассу в списке, если есть хотя бы право Read.
        Depositories.Filter = x => !x.IsDisabled && (AccountSecurityHelper.CanOperate(LoginService, x.Id) || (!string.IsNullOrEmpty(ItemId) && x.Id == Details.DepositoryId && AccountSecurityHelper.CanRead(LoginService, x.Id)));
        Depositories?.RaisePropertyChanged("List");
    }

    protected override async Task<bool> OnSaveAsync()
    {
        if (!string.IsNullOrEmpty(Details?.DepositoryId) && !AccountSecurityHelper.CanOperate(LoginService, Details.DepositoryId))
        {
            UserInteractionService.ShowMessage(this["Access Denied"], this["You do not have permission to operate with the selected depository."]);
            return false;
        }

        return await base.OnSaveAsync();
    }

    public ICommand SelectedLineDeleteCommand
    {
        get
        {
            return new MvxCommand(this.OnSelectedLineDelete, () => !this.IsBusy && this.HasSaveAccess && this.IsLineSelected);
        }
    }

    private void OnSelectedLineDelete()
    {
        this.SelectedLine = this.Details.Lines.RemoveWithSelection<TLine>(this.SelectedLine);
    }

    public ICommand SelectDepositoryCommand
    {
        get
        {
            return new MvxAsyncCommand(this.OnSelectDepositoryCommandAsync, () => !this.IsBusy && this.HasSaveAccess);
        }
    }

    private async Task OnSelectDepositoryCommandAsync()
    {
        Details.DepositoryId = await this.NavigationService.Navigate<ListViewModel<Depository>, string, string>(this.Details.DepositoryId ?? Guid.Empty.ToString());
    }
}
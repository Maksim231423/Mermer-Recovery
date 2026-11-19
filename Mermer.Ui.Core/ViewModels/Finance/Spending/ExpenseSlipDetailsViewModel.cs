using MvvmCross.Core.Navigation;
using MvvmCross.Core.ViewModels;
using Mermer.Authorization.Enums;
using Mermer.Authorization.Services;
using Mermer.Enterprise.Models;
using Mermer.Finance.Spending.Models;
using Mermer.FundsManagement.Models;
using Mermer.Transactions.Services;
using Mermer.Ui.Core.Helpers;
using Mermer.Ui.Core.Services;
using Mermer.Ui.Core.ViewModels.Transactions;
using Mermer.Data.Authorizers;
using Mermer.Data.Storage;
using Mermer.Mvvm.Services;
using Mermer.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

#nullable disable
namespace Mermer.Ui.Core.ViewModels.Finance.Spending;

public class ExpenseSlipDetailsViewModel :
  FundsTransactionDetailsViewModel<ExpenseSlip, ExpenseSlipLine>
{
    private readonly IPrintingService _printingService;

    public ExpenseSlipDetailsViewModel(
      IConfigurator configurator,
      ILoginService loginService,
      Reference<Expense> expenses,
      Reference<Currency> currencies,
      IPrintingService printingService,
      Reference<Depository> depositories,
      IRepository<ExpenseSlip> repository,
      IListAuthorizer<ExpenseSlip> authorizer,
      IMvxNavigationService navigationService,
      ITransactionCodeGenerationService codegentor,
      IUserInteractionService userInteractionService)
      : base(repository, authorizer, configurator, loginService, currencies, depositories, navigationService, codegentor, userInteractionService)
    {
        this._printingService = printingService;
        this.Expenses = expenses;
    }

    public Reference<Expense> Expenses { get; }

    protected override Task PreLoad() => Task.WhenAll(base.PreLoad(), this.Expenses.Initialize());

    protected override async Task PostLoad()
    {
        await base.PostLoad();

        // 1. Фильтрация касс (скрываем кассы со статусом No Access / 0)
        if (this.LoginService?.Session != null && !this.LoginService.Session.IsAdmin && this.LoginService.Session.Accounts != null)
        {
            var accounts = this.LoginService.Session.Accounts;

            this.Depositories.Filter = d =>
                !d.IsDisabled &&
                (!accounts.TryGetValue(d.Id, out var level) ||
                 (level != AccountAccessLevel.None && (level.HasFlag(AccountAccessLevel.Operate) || level.HasFlag(AccountAccessLevel.Read))));
        }
        else
        {
            this.Depositories.Filter = d => !d.IsDisabled;
        }

        // 2. Для нового документа подставляем первую РАЗРЕШЕННУЮ кассу
        if (string.IsNullOrEmpty(ItemId))
        {
            if (string.IsNullOrEmpty(Details.DepositoryId) || !IsDepositoryAllowed(Details.DepositoryId))
            {
                var firstAllowed = this.Depositories?.List?.FirstOrDefault();
                Details.DepositoryId = firstAllowed?.Id;
            }
        }

        IEnumerable<string> usedExpenseIds = Details.Lines.Select(x => x.ExpenseId).Distinct();
        Expenses.Filter = x => !x.IsDisabled || usedExpenseIds.Contains(x.Id);

        this.Depositories?.RaisePropertyChanged("List");
        Details.RaisePropertyChanged("DisplayTotal");
    }

    private bool IsDepositoryAllowed(string depositoryId)
    {
        if (string.IsNullOrEmpty(depositoryId) || this.LoginService?.Session == null || this.LoginService.Session.IsAdmin)
            return true;

        var accounts = this.LoginService.Session.Accounts;
        if (accounts == null || accounts.Count == 0)
            return true;

        if (accounts.TryGetValue(depositoryId, out var level))
        {
            return level != AccountAccessLevel.None &&
                   (level.HasFlag(AccountAccessLevel.Operate) || level.HasFlag(AccountAccessLevel.Read));
        }

        return true;
    }

    protected override async Task<bool> OnSaveAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(Details.DepositoryId))
            {
                throw new Exception(this["Field '{0}' is required", this["Depository"]]);
            }

            if (!IsDepositoryAllowed(Details.DepositoryId))
            {
                throw new Exception("You do not have permission to use the selected depository.");
            }

            if (Details.Lines == null || !Details.Lines.Any())
            {
                throw new Exception(this["Document cannot be empty"]);
            }

            foreach (var line in Details.Lines)
            {
                if (string.IsNullOrEmpty(line.ExpenseId))
                    throw new Exception(this["Field '{0}' is required", this["Expense"]]);

                if (line.Amount <= 0)
                    throw new Exception(this["Amount must be greater than zero"]);

                if (string.IsNullOrEmpty(line.CurrencyId))
                    throw new Exception(this["Field '{0}' is required", this["Currency"]]);
            }
        }
        catch (Exception ex)
        {
            UserInteractionService.ShowExceptionMessage(ex);
            return false;
        }

        if (!await base.OnSaveAsync())
            return false;

        await _printingService.PrintExpenseSlip(Details);
        return true;
    }

    public ICommand PrintCommand
    {
        get
        {
            return (ICommand)new MvxAsyncCommand(new Func<Task>(this.OnPrintCommandAsync), (Func<bool>)(() => !this.IsBusy && !this.IsDirty));
        }
    }

    protected virtual async Task OnPrintCommandAsync()
    {
        await this._printingService.PrintExpenseSlip(this.Details, true);
    }
}
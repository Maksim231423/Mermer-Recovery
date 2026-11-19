using Mermer.Authorization.Enums;
using Mermer.Authorization.Services;
using Mermer.Data.Authorizers;
using Mermer.Data.Storage;
using Mermer.Enterprise.Models;
using Mermer.Finance.DailyRegistery.Models;
using Mermer.FundsManagement.Models;
using Mermer.Mvvm.Services;
using Mermer.Services;
using Mermer.Transactions.Services;
using Mermer.Ui.Core.Helpers;
using Mermer.Ui.Core.ViewModels.Transactions;
using MvvmCross.Core.Navigation;
using System;
using System.Linq;
using System.Threading.Tasks;

#nullable disable
namespace Mermer.Ui.Core.ViewModels.Finance.DailyRegistery;

public class DailyFundsRegisteryDetailsViewModel :
  FundsTransactionDetailsViewModel<DailyFundsRegistery, DailyFundsRegisteryLine>
{
    private readonly ILoginService _loginService;

    public DailyFundsRegisteryDetailsViewModel(
        IRepository<DailyFundsRegistery> repository,
        IListAuthorizer<DailyFundsRegistery> authorizer,
        IConfigurator configurator,
        ILoginService loginService,
        Reference<Currency> currencies,
        Reference<Depository> depositories,
        IMvxNavigationService navigationService,
        ITransactionCodeGenerationService codegentor,
        IUserInteractionService userInteractionService)
        : base(repository, authorizer, configurator, loginService, currencies, depositories, navigationService, codegentor, userInteractionService)
    {
        this._loginService = loginService;
    }

    protected override async Task PostLoad()
    {
        await base.PostLoad();

        // 1. Фильтрация касс (скрываем кассы со статусом No Access / 0)
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

        // 2. Для новой записи подставляем первую РАЗРЕШЁННУЮ кассу
        if (string.IsNullOrEmpty(ItemId))
        {
            if (string.IsNullOrEmpty(Details.DepositoryId) || !IsDepositoryAllowed(Details.DepositoryId))
            {
                var firstAllowed = this.Depositories?.List?.FirstOrDefault();
                Details.DepositoryId = firstAllowed?.Id;
            }
        }

        this.Depositories?.RaisePropertyChanged("List");
    }

    private bool IsDepositoryAllowed(string depositoryId)
    {
        if (string.IsNullOrEmpty(depositoryId) || this._loginService?.Session == null || this._loginService.Session.IsAdmin)
            return true;

        var accounts = this._loginService.Session.Accounts;
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
            // 1. Обязательно должна быть указана Касса (Depository)
            if (string.IsNullOrEmpty(Details.DepositoryId))
            {
                throw new Exception(this["Field '{0}' is required", this["Depository"]]);
            }

            // 2. Проверка прав на выбранную кассу
            if (!IsDepositoryAllowed(Details.DepositoryId))
            {
                throw new Exception("You do not have permission to use the selected depository.");
            }

            // 3. Документ реестра не может быть пустым
            if (Details.Lines == null || !Details.Lines.Any())
            {
                throw new Exception(this["Document cannot be empty"]);
            }

            // 4. Проверка каждой строки в таблице
            foreach (var line in Details.Lines)
            {
                if (string.IsNullOrEmpty(line.CurrencyId))
                {
                    throw new Exception(this["Field '{0}' is required", this["Currency"]]);
                }
            }
        }
        catch (Exception ex)
        {
            UserInteractionService.ShowExceptionMessage(ex);
            return false;
        }

        return await base.OnSaveAsync();
    }
}
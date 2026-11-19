using Mermer.Authorization.Enums;
using Mermer.Authorization.Services;
using Mermer.Data.Authorizers;
using Mermer.Data.Storage;
using Mermer.Enterprise.Models;
using Mermer.Finance.Models;
using Mermer.FundsManagement.Models;
using Mermer.FundsManagement.Models.Extenders;
using Mermer.Mvvm.Services;
using Mermer.Mvvm.ViewModels;
using Mermer.Services;
using Mermer.Transactions.Services;
using Mermer.Ui.Core.Helpers;
using Mermer.Ui.Core.ViewModels.Transactions;
using MvvmCross.Core.Navigation;
using MvvmCross.Core.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

#nullable disable
namespace Mermer.Ui.Core.ViewModels.Finance;

public class FundsTransferDetailsViewModel : FundsTransactionDetailsViewModel<FundsTransfer, FundsTransferLine>
{
    private readonly ILoginService _loginService;

    public FundsTransferDetailsViewModel(
        IConfigurator configurator,
        ILoginService loginService,
        Reference<Currency> currencies,
        Reference<Depository> depositories,
        IRepository<FundsTransfer> repository,
        IListAuthorizer<FundsTransfer> authorizer,
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

        // 1. Фильтрация касс по правам сессии (скрываем кассы с No Access)
        if (this._loginService?.Session != null && !this._loginService.Session.IsAdmin && this._loginService.Session.Accounts != null)
        {
            var accounts = this._loginService.Session.Accounts;

            Depositories.Filter = d =>
                !d.IsDisabled &&
                (!accounts.TryGetValue(d.Id, out var level) ||
                 (level != AccountAccessLevel.None && (level.HasFlag(AccountAccessLevel.Operate) || level.HasFlag(AccountAccessLevel.Read))));
        }
        else
        {
            Depositories.Filter = d => !d.IsDisabled;
        }

        // 2. Для нового документа: если подставилась запрещенная касса, сбрасываем или выбираем первую разрешенную
        if (string.IsNullOrEmpty(ItemId))
        {
            var allowedDepositories = Depositories?.List?.ToList();

            if (!IsDepositoryAllowed(Details.DepositoryId))
            {
                Details.DepositoryId = allowedDepositories?.FirstOrDefault()?.Id;
            }

            if (!IsDepositoryAllowed(Details.DestinationDepositoryId))
            {
                Details.DestinationDepositoryId = null;
            }
        }

        Depositories?.RaisePropertyChanged("List");
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

    public ICommand SelectDestinationDepositoryCommand
    {
        get
        {
            return new MvxAsyncCommand(OnSelectDestinationDepositoryCommandAsync, () => !this.IsBusy && this.HasSaveAccess);
        }
    }

    private async Task OnSelectDestinationDepositoryCommandAsync()
    {
        this.Details.DestinationDepositoryId = await this.NavigationService.Navigate<ListViewModel<Depository>, string, string>(this.Details.DestinationDepositoryId ?? Guid.Empty.ToString());
    }

    protected override async Task<bool> OnSaveAsync()
    {
        try
        {
            // 1. Обязательно должна быть указана касса-отправитель
            if (string.IsNullOrEmpty(Details.DepositoryId))
            {
                throw new Exception(this["Field '{0}' is required", this["Source Depository"]]);
            }

            // 2. Обязательно должна быть указана касса-получатель
            if (string.IsNullOrEmpty(Details.DestinationDepositoryId))
            {
                throw new Exception(this["Field '{0}' is required", this["Destination Depository"]]);
            }

            // 3. Защита: проверка прав на выбранные кассы
            if (!IsDepositoryAllowed(Details.DepositoryId))
            {
                throw new Exception("You do not have permission to use the selected source depository.");
            }

            if (!IsDepositoryAllowed(Details.DestinationDepositoryId))
            {
                throw new Exception("You do not have permission to use the selected destination depository.");
            }

            // 4. Касса-отправитель и касса-получатель не могут совпадать
            if (Details.DepositoryId == Details.DestinationDepositoryId)
            {
                throw new Exception(this["Source and destination depositories cannot be the same"]);
            }

            // 5. Документ не может быть пустым
            if (Details.Lines == null || !Details.Lines.Any())
            {
                throw new Exception(this["Document cannot be empty"]);
            }

            // 6. Проверка строк
            foreach (var line in Details.Lines)
            {
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

        return await base.OnSaveAsync();
    }

    protected override void Details_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "DisplayCurrencyId")
        {
            var newCurrencyId = this.Details.DisplayCurrencyId;

            if (this.Details.Lines != null && !string.IsNullOrEmpty(newCurrencyId))
            {
                var targetCurrency = this.Currencies?.List?.FirstOrDefault(c => c.Id == newCurrencyId);

                foreach (var line in this.Details.Lines)
                {
                    if (string.IsNullOrEmpty(line.CurrencyId) || line.CurrencyId == newCurrencyId)
                        continue;

                    var sourceCurrency = this.Currencies?.List?.FirstOrDefault(c => c.Id == line.CurrencyId);

                    if (sourceCurrency != null && targetCurrency != null)
                    {
                        var sourceRate = sourceCurrency.GetRate(this.Details.Date);
                        var targetRate = targetCurrency.GetRate(this.Details.Date);

                        if (sourceRate != null && targetRate != null && sourceRate.Divider != 0 && targetRate.Multiplier != 0)
                        {
                            decimal sMult = sourceRate.Multiplier;
                            decimal sDiv = sourceRate.Divider;
                            decimal tMult = targetRate.Multiplier;
                            decimal tDiv = targetRate.Divider;

                            decimal conversionRate = (sMult / sDiv) * (tDiv / tMult);

                            line.Amount = Math.Round(line.Amount * conversionRate, targetCurrency.Decimals);
                            line.ReceivedAmount = Math.Round(line.ReceivedAmount * conversionRate, targetCurrency.Decimals);
                            line.CurrencyId = newCurrencyId;
                        }
                    }
                }
            }

            System.Threading.Tasks.Task.Run(async () =>
            {
                await System.Threading.Tasks.Task.Delay(150);
                InvokeOnMainThread(() =>
                {
                    if (this.Details.Lines != null)
                    {
                        foreach (var line in this.Details.Lines)
                        {
                            line.RaisePropertyChanged("Amount");
                            line.RaisePropertyChanged("ReceivedAmount");
                            line.RaisePropertyChanged("ActionTotal");
                            line.RaisePropertyChanged("ActionReceivedTotal");
                            line.RaisePropertyChanged("DisplayTotal");
                            line.RaisePropertyChanged("DisplayReceivedTotal");
                        }
                    }

                    this.Details.RaisePropertyChanged("ActionTotal");
                    this.Details.RaisePropertyChanged("ActionReceivedTotal");
                    this.Details.RaisePropertyChanged("DisplayTotal");
                    this.Details.RaisePropertyChanged("IsConflicted");
                    this.Details.RaisePropertyChanged("Lines");
                });
            });

            return;
        }

        base.Details_PropertyChanged(sender, e);
    }
}
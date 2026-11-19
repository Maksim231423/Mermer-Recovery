using MvvmCross.Core.Navigation;
using MvvmCross.Core.ViewModels;
using Mermer.Authorization.Enums;
using Mermer.Authorization.Services;
using Mermer.Enterprise.Models;
using Mermer.FundsManagement.Models;
using Mermer.Transactions.Models;
using Mermer.Transactions.Models.Authorizers;
using Mermer.Transactions.Services;
using Mermer.Ui.Core.Helpers;
using Mermer.Data.Authorizers;
using Mermer.Data.Storage;
using Mermer.Mvvm.Services;
using Mermer.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

#nullable disable
namespace Mermer.Ui.Core.ViewModels.Transactions;

public class FundsTransactionDetailsViewModel<T, TLine, TType> : FundsTransactionDetailsViewModel<T, TLine>
  where T : FundsTransaction<TLine>
  where TLine : FundsTransactionLine
{
    private ListHelper<TType>[] _types;
    private bool _canChangeType;

    public FundsTransactionDetailsViewModel(
        IRepository<T> repository,
        IListAuthorizer<T> authorizer,
        IConfigurator configurator,
        ILoginService loginService,
        Reference<Currency> currencies,
        Reference<Depository> depositories,
        IMvxNavigationService navigationService,
        ITransactionCodeGenerationService codegentor,
        IUserInteractionService userInteractionService)
        : base(repository, authorizer, configurator, loginService, currencies, depositories, navigationService, codegentor, userInteractionService)
    {
    }

    protected override MvxInpcInterceptionResult InterceptRaisePropertyChanged(PropertyChangedEventArgs changedArgs)
    {
        if (changedArgs.PropertyName == "HasSaveAccess")
            this.RaisePropertyChanged<bool>(() => this.CanChangeType);
        return base.InterceptRaisePropertyChanged(changedArgs);
    }

    public virtual ListHelper<TType>[] Types
    {
        get => this._types;
        set => this.SetProperty<ListHelper<TType>[]>(ref this._types, value, nameof(Types));
    }

    public bool CanChangeType
    {
        get => this.HasSaveAccess && this._canChangeType;
        set => this.SetProperty<bool>(ref this._canChangeType, value, nameof(CanChangeType));
    }

    protected override Task PreLoad()
    {
        if (this.Authorizer is ITransactionAuthorizer<T> authorizer)
        {
            this.Types = Enum.GetValues(typeof(TType)).Cast<TType>().Select(x => new ListHelper<TType>()
            {
                Text = this[x.ToString(), Array.Empty<object>()],
                Value = x
            }).ToArray();

            if (this.LoginService.Session.IsAdmin)
                this.CanChangeType = true;
            else if (!string.IsNullOrEmpty(this.ItemId))
            {
                this.CanChangeType = false;
            }
            else
            {
                IEnumerable<string> createableActions = authorizer.GetAvailableActions(TransactionAccessLevel.Create);
                this.Types = this.Types.Where(x => createableActions.Contains(x.Value.ToString())).ToArray();
                this.CanChangeType = true;
            }
        }
        return base.PreLoad();
    }
}
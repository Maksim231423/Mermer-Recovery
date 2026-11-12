using Mermer.Authorization.Services;
using Mermer.Data.Authorizers;
using Mermer.Data.Storage;
using Mermer.Enterprise.Models;
using Mermer.Finance.Models;
using Mermer.FundsManagement.Models;
using Mermer.Mvvm.Services;
using Mermer.Services;
using Mermer.Transactions.Models;
using Mermer.Transactions.Services;
using Mermer.Ui.Core.Helpers;
using Mermer.Ui.Core.ViewModels.Transactions;
using MvvmCross.Core.Navigation;
using MvvmCross.Core.ViewModels;
using MvvmCross.Platform;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

#nullable disable
namespace Mermer.Ui.Core.ViewModels.Finance;

public class FundsSlipDetailsViewModel :
  FundsTransactionDetailsViewModel<FundsSlip, FundsSlipLine, FundsSlipType>,
  IMvxViewModel<FundsSlipType>,
  IMvxViewModel
{
    private FundsSlipType _newSlipType;
    private bool _isUpdatingCurrency;

    public FundsSlipDetailsViewModel(
        IConfigurator configurator,
        ILoginService loginService,
        Reference<Currency> currencies,
        IRepository<FundsSlip> repository,
        IListAuthorizer<FundsSlip> authorizer,
        Reference<Depository> depositories,
        IMvxNavigationService navigationService,
        ITransactionCodeGenerationService codegentor,
        IUserInteractionService userInteractionService)
        : base(repository, authorizer, configurator, loginService, currencies, depositories, navigationService, codegentor, userInteractionService)
    {
    }

    public void Prepare(FundsSlipType parameter) => this._newSlipType = parameter;

    protected override async Task PreLoad()
    {
        await base.PreLoad();

        this.Types = Enum.GetValues(typeof(FundsSlipType))
            .Cast<FundsSlipType>()
            .Select(x => new ListHelper<FundsSlipType>
            {
                Text = this[x.ToString()],
                Value = x
            }).ToArray();

        this.CanChangeType = true;
    }

    protected override async Task PostLoad()
    {
        await base.PostLoad();

        if (this.Depositories.List == null || !this.Depositories.List.Any())
        {
            try
            {
                if (Mvx.CanResolve<IRepository<Depository>>())
                {
                    var repo = Mvx.Resolve<IRepository<Depository>>();
                    var list = await repo.GetAsync();
                    if (list != null && list.Any())
                    {
                        this.Depositories.SuspendLoading = true;
                        this.Depositories.Filter = null;
                        this.Depositories.List = list.ToList();

                        if (Details != null && string.IsNullOrEmpty(Details.DepositoryId))
                        {
                            Details.DepositoryId = list.FirstOrDefault()?.Id;
                        }
                    }
                }
            }
            catch { }
        }

        if (Details != null)
        {
            if (string.IsNullOrEmpty(Details.Code))
                Details.Code = $"FS-{DateTime.Now:yyMMddHHmmss}";

            if (string.IsNullOrEmpty(Details.DepositoryId) && Depositories?.List != null)
            {
                Details.DepositoryId = Depositories.List.FirstOrDefault()?.Id;
            }

            if (Currencies?.List != null)
            {
                var activeCurrencies = Currencies.List.Where(c => !c.IsDisabled).ToList();

                if (string.IsNullOrEmpty(Details.DisplayCurrencyId))
                    Details.DisplayCurrencyId = activeCurrencies.FirstOrDefault(c => c.IsDefault)?.Id ?? activeCurrencies.FirstOrDefault()?.Id;
            }

            Details.RaiseChangeEvents = true;

            // Синхронизируем строки документа с выбранной валютой шапки
            SyncAllLinesDisplayCurrency();

            SubscribeLines();

            if (string.IsNullOrEmpty(ItemId))
            {
                Details.SlipType = _newSlipType;
            }

            RaisePropertyChanged(() => Details);
            RaisePropertyChanged(() => Types);
            Depositories?.RaisePropertyChanged("List");
        }
    }

    /// <summary>
    /// Передает в каждую строку объект конвертации валюты шапки и принудительно обновляет расчет
    /// </summary>
    private void SyncAllLinesDisplayCurrency()
    {
        if (Details?.Lines == null || string.IsNullOrEmpty(Details.DisplayCurrencyId))
            return;

        var displayConv = Details.CurrencyConverter(Details.DisplayCurrencyId);
        if (displayConv == null)
            return;

        foreach (var line in Details.Lines)
        {
            line.UpdateDisplayCurrencyConvertion(displayConv, true);
            line.UpdateCurrencyConvertion();
        }

        Details.RaisePropertyChanged("ActionTotal");
        Details.RaisePropertyChanged("DisplayTotal");
    }

    private void SubscribeLines()
    {
        if (Details?.Lines != null)
        {
            Details.Lines.CollectionChanged -= Lines_CollectionChanged;
            Details.Lines.CollectionChanged += Lines_CollectionChanged;

            var displayConv = !string.IsNullOrEmpty(Details.DisplayCurrencyId)
                ? Details.CurrencyConverter(Details.DisplayCurrencyId)
                : null;

            foreach (var line in Details.Lines)
            {
                line.PropertyChanged -= Line_PropertyChanged;
                line.PropertyChanged += Line_PropertyChanged;

                if (displayConv != null)
                {
                    line.UpdateDisplayCurrencyConvertion(displayConv, false);
                }

                if (string.IsNullOrEmpty(ItemId) && !string.IsNullOrEmpty(Details.DisplayCurrencyId) && string.IsNullOrEmpty(line.CurrencyId))
                {
                    _isUpdatingCurrency = true;
                    try
                    {
                        line.CurrencyId = Details.DisplayCurrencyId;
                        line.UpdateCurrencyConvertion();
                    }
                    finally
                    {
                        _isUpdatingCurrency = false;
                    }
                }
            }
        }
    }

    private void Lines_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is FundsSlipLine line)
                    line.PropertyChanged -= Line_PropertyChanged;
            }
        }

        if (e.NewItems != null)
        {
            var displayConv = !string.IsNullOrEmpty(Details?.DisplayCurrencyId)
                ? Details.CurrencyConverter(Details.DisplayCurrencyId)
                : null;

            _isUpdatingCurrency = true;
            try
            {
                foreach (var item in e.NewItems)
                {
                    if (item is FundsSlipLine line)
                    {
                        line.PropertyChanged -= Line_PropertyChanged;
                        line.PropertyChanged += Line_PropertyChanged;

                        // Новая строка по умолчанию наследует валюту шапки
                        if (!string.IsNullOrEmpty(Details?.DisplayCurrencyId))
                        {
                            line.CurrencyId = Details.DisplayCurrencyId;
                        }

                        if (displayConv != null)
                        {
                            line.UpdateDisplayCurrencyConvertion(displayConv, true);
                        }

                        line.UpdateCurrencyConvertion();
                    }
                }
            }
            finally
            {
                _isUpdatingCurrency = false;
            }
        }

        Details?.RaisePropertyChanged("ActionTotal");
        Details?.RaisePropertyChanged("DisplayTotal");
    }

    /// <summary>
    /// Если пользователь вручную изменил валюту в конкретной строке таблицы — пересчитываем её по курсу
    /// </summary>
    private void Line_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (_isUpdatingCurrency) return;

        if (e.PropertyName == "CurrencyId" && sender is FundsSlipLine line)
        {
            var displayConv = !string.IsNullOrEmpty(Details?.DisplayCurrencyId)
                ? Details.CurrencyConverter(Details.DisplayCurrencyId)
                : null;

            if (displayConv != null)
            {
                line.UpdateDisplayCurrencyConvertion(displayConv, true);
            }

            line.UpdateCurrencyConvertion();

            Details?.RaisePropertyChanged("ActionTotal");
            Details?.RaisePropertyChanged("DisplayTotal");
        }
    }

    protected override void Details_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        base.Details_PropertyChanged(sender, e);

        if (_isUpdatingCurrency) return;

        if (e.PropertyName == "Lines")
        {
            SubscribeLines();
        }

        if (e.PropertyName == "ActionTotal")
        {
            Details?.RaisePropertyChanged("DisplayTotal");
        }

        // Если пользователь изменил валюту в шапке документа
        if (e.PropertyName == "DisplayCurrencyId")
        {
            _isUpdatingCurrency = true;
            try
            {
                var displayConv = Details.CurrencyConverter(Details.DisplayCurrencyId);

                if (Details.Lines != null)
                {
                    foreach (var line in Details.Lines)
                    {
                        // В новом документе переключаем строки на валюту шапки -> расчет будет 1 к 1
                        if (string.IsNullOrEmpty(ItemId))
                        {
                            line.CurrencyId = Details.DisplayCurrencyId;
                        }

                        if (displayConv != null)
                        {
                            line.UpdateDisplayCurrencyConvertion(displayConv, true);
                        }

                        line.UpdateCurrencyConvertion();
                    }
                }
            }
            finally
            {
                _isUpdatingCurrency = false;
            }

            Details?.RaisePropertyChanged("ActionTotal");
            Details?.RaisePropertyChanged("DisplayTotal");
        }
    }

    protected override async Task<bool> OnSaveAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(Details.DepositoryId))
                throw new Exception(this["Field '{0}' is required", this["Depository"]]);

            if (Details.Lines == null || !Details.Lines.Any())
                throw new Exception(this["Document cannot be empty"]);

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
}
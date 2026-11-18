using Mermer.Authorization.Services;
using Mermer.Data;
using Mermer.Data.Authorizers;
using Mermer.Data.Storage;
using Mermer.Enterprise.Models;
using Mermer.Finance.Models;
using Mermer.FundsManagement.Models;
using Mermer.Mvvm.Services;
using Mermer.Mvvm.ViewModels;
using Mermer.Services;
using Mermer.Transactions.Models;
using Mermer.Transactions.Services;
using Mermer.Ui.Core.Helpers;
using Mermer.Ui.Core.ViewModels.Transactions;
using MvvmCross.Core.Navigation;
using MvvmCross.Core.ViewModels;
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

        await Task.WhenAll(
            Depositories.Initialize(),
            Currencies.Initialize()
        );
    }

    protected override async Task OnLoad()
    {
        await base.OnLoad();
        if (!string.IsNullOrEmpty(ItemId))
            return;

        await Currencies.Initialize();

        if (Details.CurrencyConvertions == null)
        {
            Details.CurrencyConvertions = new WatchedObservableCollection<CurrencyConvertion>();
        }

        var activeCurrencies = Currencies?.List?
            .Where(c => !c.IsDisabled)
            .ToList() ?? new List<Currency>();

        var activeIds = activeCurrencies.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var invalidItems = Details.CurrencyConvertions
            .Where(c => string.IsNullOrEmpty(c.CurrencyId) || !activeIds.Contains(c.CurrencyId))
            .ToList();

        foreach (var item in invalidItems)
        {
            Details.CurrencyConvertions.Remove(item);
        }

        foreach (var curr in activeCurrencies)
        {
            if (!Details.CurrencyConvertions.Any(c => string.Equals(c.CurrencyId, curr.Id, StringComparison.OrdinalIgnoreCase)))
            {
                Details.CurrencyConvertions.Add(new CurrencyConvertion
                {
                    CurrencyId = curr.Id,
                    Multiplier = 1m,
                    Divider = 1m
                });
            }
        }
    }

    protected override async Task PostLoad()
    {
        await base.PostLoad();

        if (string.IsNullOrEmpty(ItemId))
        {
            Details.SlipType = _newSlipType;

            if (Currencies?.List != null)
            {
                var activeCurrencies = Currencies.List.Where(c => !c.IsDisabled).ToList();
                if (string.IsNullOrEmpty(Details.DisplayCurrencyId))
                    Details.DisplayCurrencyId = activeCurrencies.FirstOrDefault(c => c.IsDefault)?.Id ?? activeCurrencies.FirstOrDefault()?.Id;
            }

            if (string.IsNullOrEmpty(Details.DepositoryId) && Depositories?.List != null)
            {
                Details.DepositoryId = Depositories.List.FirstOrDefault(d => !d.IsDisabled)?.Id ?? Depositories.List.FirstOrDefault()?.Id;
            }

            if (string.IsNullOrEmpty(Details.Code))
            {
                Details.Code = $"FS-{DateTime.Now:yyMMddHHmmss}";
            }
        }

        // Заполняем актуальные курсы валют
        if (Details != null)
        {
            if (Details.CurrencyConvertions == null)
                Details.CurrencyConvertions = new WatchedObservableCollection<CurrencyConvertion>();

            var activeCurrencies = Currencies?.List?.Where(c => !c.IsDisabled).ToList() ?? new List<Currency>();
            var activeIds = activeCurrencies.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var toRemove = Details.CurrencyConvertions
                .Where(c => string.IsNullOrEmpty(c.CurrencyId) || !activeIds.Contains(c.CurrencyId))
                .ToList();

            foreach (var item in toRemove)
                Details.CurrencyConvertions.Remove(item);

            var docDate = Details.Date != default ? Details.Date.Date : DateTime.Today;

            foreach (var curr in activeCurrencies)
            {
                CurrencyRate matchedRate = null;
                if (curr.Rates != null && curr.Rates.Any())
                {
                    matchedRate = curr.Rates
                        .Where(r => r.ValidFrom.Date <= docDate)
                        .OrderByDescending(r => r.ValidFrom)
                        .FirstOrDefault()
                        ?? curr.Rates.OrderByDescending(r => r.ValidFrom).FirstOrDefault();
                }

                decimal mult = (matchedRate != null && matchedRate.Multiplier != 0m) ? matchedRate.Multiplier : 1m;
                decimal div = (matchedRate != null && matchedRate.Divider != 0m) ? matchedRate.Divider : 1m;

                var existingConv = Details.CurrencyConvertions.FirstOrDefault(c =>
                    string.Equals(c.CurrencyId, curr.Id, StringComparison.OrdinalIgnoreCase));

                if (existingConv == null)
                {
                    Details.CurrencyConvertions.Add(new CurrencyConvertion
                    {
                        CurrencyId = curr.Id,
                        Multiplier = mult,
                        Divider = div
                    });
                }
                else if (existingConv.Multiplier == 1m && existingConv.Divider == 1m && (mult != 1m || div != 1m))
                {
                    existingConv.Multiplier = mult;
                    existingConv.Divider = div;
                }
            }

            Details.UpdateCurrencyConvertion();
            if (Details.Lines != null)
            {
                foreach (var line in Details.Lines)
                {
                    line.UpdateCurrencyConvertion();
                }
            }

            SubscribeLines();
        }

        Details.RaisePropertyChanged("ActionTotal");
        Details.RaisePropertyChanged("DisplayTotal");
        Depositories?.RaisePropertyChanged("List");
    }

    private void SubscribeLines()
    {
        if (Details?.Lines != null)
        {
            Details.Lines.CollectionChanged -= Lines_CollectionChanged;
            Details.Lines.CollectionChanged += Lines_CollectionChanged;

            // Если открыт новый документ, в пустых строках ставим валюту шапки
            if (string.IsNullOrEmpty(ItemId) && !string.IsNullOrEmpty(Details.DisplayCurrencyId))
            {
                foreach (var line in Details.Lines)
                {
                    if (line.Amount == 0m || string.IsNullOrEmpty(line.CurrencyId))
                    {
                        line.CurrencyId = Details.DisplayCurrencyId;
                        line.UpdateCurrencyConvertion();
                    }
                }
            }
        }
    }

    private void Lines_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is FundsSlipLine line && !string.IsNullOrEmpty(Details?.DisplayCurrencyId))
                {
                    // Новая строка всегда наследует валюту, выбранную в шапке документа
                    line.CurrencyId = Details.DisplayCurrencyId;
                    line.UpdateCurrencyConvertion();
                }
            }
        }
    }

    protected override void Details_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        base.Details_PropertyChanged(sender, e);

        if (e.PropertyName == "Lines")
        {
            SubscribeLines();
        }

        if (e.PropertyName == "DisplayCurrencyId")
        {
            if (Details.Lines != null)
            {
                foreach (var line in Details.Lines)
                {
                    // Меняем валюту строки, если это новый документ и строка пустая (Amount == 0)
                    if (string.IsNullOrEmpty(ItemId) && line.Amount == 0m)
                    {
                        line.CurrencyId = Details.DisplayCurrencyId;
                    }
                    line.UpdateCurrencyConvertion();
                }
            }

            Details.UpdateCurrencyConvertion();

            Details.RaisePropertyChanged("ActionTotal");
            Details.RaisePropertyChanged("DisplayTotal");
        }
    }

    protected override async Task<bool> OnSaveAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(Details.DepositoryId))
            {
                throw new Exception(this["Field '{0}' is required", this["Depository"]]);
            }

            if (Details.Lines == null || !Details.Lines.Any())
            {
                throw new Exception(this["Document cannot be empty"]);
            }

            foreach (var line in Details.Lines)
            {
                if (line.Amount <= 0m)
                {
                    throw new Exception(this["Field '{0}' is required and must be greater than zero", this["Amount"]]);
                }

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
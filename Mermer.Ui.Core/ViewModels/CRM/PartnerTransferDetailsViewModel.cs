using MvvmCross.Core.Navigation;
using MvvmCross.Core.ViewModels;
using Mermer.Authorization.Enums;
using Mermer.Authorization.Services;
using Mermer.CRM.Models;
using Mermer.Enterprise.Models;
using Mermer.FundsManagement.Models;
using Mermer.FundsManagement.Models.Extenders;
using Mermer.Transactions.Models;
using Mermer.Transactions.Models.Authorizers;
using Mermer.Transactions.Services;
using Mermer.Ui.Core.Helpers;
using Mermer.Ui.Core.ViewModels.Common;
using Mermer.Data.Authorizers;
using Mermer.Data.Models;
using Mermer.Data.Storage;
using Mermer.Mvvm.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using System.Windows.Input;

#nullable disable
namespace Mermer.Ui.Core.ViewModels.CRM;

public class PartnerTransferDetailsViewModel : TransactionDetailsViewModel<PartnerTransfer>
{
    private bool _canChangeDate;
    private string[] _groupNames;
    private string[] _tagNames;
    private PartnerTransferLine _selectedLine;

    public PartnerTransferDetailsViewModel(
      Reference<Office> offices,
      Reference<Partner> partners,
      Reference<Currency> currencies,
      ITransactionCodeGenerationService codeGenerationService,
      IRepository<PartnerTransfer> repository,
      IListAuthorizer<PartnerTransfer> authorizer,
      ILoginService loginService,
      IMvxNavigationService navigationService,
      IUserInteractionService userInteractionService)
      : base(codeGenerationService, repository, authorizer, loginService, navigationService, userInteractionService)
    {
        this.Offices = offices;
        this.Partners = partners;
        this.Currencies = currencies;
    }

    protected override MvxInpcInterceptionResult InterceptRaisePropertyChanged(
      PropertyChangedEventArgs changedArgs)
    {
        if (changedArgs.PropertyName == "HasSaveAccess")
            this.RaisePropertyChanged<bool>((Expression<Func<bool>>)(() => this.CanChangeDate));
        return base.InterceptRaisePropertyChanged(changedArgs);
    }

    public Reference<Office> Offices { get; }

    public Reference<Partner> Partners { get; }

    public Reference<Currency> Currencies { get; }

    public bool CanChangeDate
    {
        get => this.HasSaveAccess && this._canChangeDate;
        set => this.SetProperty<bool>(ref this._canChangeDate, value, nameof(CanChangeDate));
    }

    public virtual string[] GroupNames
    {
        get => this._groupNames;
        set => this.SetProperty<string[]>(ref this._groupNames, value, nameof(GroupNames));
    }

    public virtual string[] TagNames
    {
        get => this._tagNames;
        set => this.SetProperty<string[]>(ref this._tagNames, value, nameof(TagNames));
    }

    protected virtual async Task LoadFacetsAsync()
    {
        PartnerTransferDetailsViewModel detailsViewModel = this;
        Dictionary<string, Dictionary<string, int>> facets = await ((IRepositoryWithFacets<PartnerTransfer>)detailsViewModel.Repository).GetFacets("GroupNames", "TagNames");
        detailsViewModel.GroupNames = facets["GroupNames"].Select<KeyValuePair<string, int>, string>((Func<KeyValuePair<string, int>, string>)(x => x.Key)).ToArray<string>();
        detailsViewModel.TagNames = facets["TagNames"].Select<KeyValuePair<string, int>, string>((Func<KeyValuePair<string, int>, string>)(x => x.Key)).ToArray<string>();
    }

    protected override Task PreLoad()
    {
        this.CanChangeDate = !(this.Authorizer is ITransactionAuthorizer<PartnerTransfer> authorizer) || authorizer.CanChangeDate();
        return Task.WhenAll(base.PreLoad(), this.LoadFacetsAsync(), this.Offices.Initialize(), this.Partners.Initialize(), this.Currencies.Initialize());
    }

    protected override async Task PostLoad()
    {
        await base.PostLoad();

        if (Details.Lines == null)
            Details.Lines = new ObservableCollection<PartnerTransferLine>();

        Details.Lines.CollectionChanged += Lines_CollectionChanged;

        foreach (var line in Details.Lines)
            line.PropertyChanged += Line_PropertyChanged;

        if (Details.CurrencyConvertions == null)
            Details.CurrencyConvertions = new ObservableCollection<CurrencyConvertion>();

        // В строках таблицы выпадающий список Office показывает только офисы с правом Operate
        // Для существующих строк допускается отображение офиса с правом Read
        var usedOfficeIds = Details.Lines.Select(x => x.OfficeId).Distinct().ToHashSet();
        Offices.Filter = x => !x.IsDisabled && (AccountSecurityHelper.CanOperate(LoginService, x.Id) ||
                              (!string.IsNullOrEmpty(ItemId) && usedOfficeIds.Contains(x.Id) && AccountSecurityHelper.CanRead(LoginService, x.Id)));
        Offices?.RaisePropertyChanged("List");

        IEnumerable<string> usedPartnerIds = Details.Lines.Select(x => x.PartnerId).Distinct();
        Partners.Filter = x => !x.IsDisabled || usedPartnerIds.Contains(x.Id);

        IEnumerable<string> usedCurrencyIds = Details.Lines.Select(x => x.CreditCurrencyId)
            .Union(Details.Lines.Select(x => x.DebitCurrencyId)).Distinct();
        Currencies.Filter = x => !x.IsDisabled || usedCurrencyIds.Contains(x.Id);
    }

    private void Lines_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems == null)
            return;
        foreach (PartnerTransferLine partnerTransferLine in e.NewItems.Cast<PartnerTransferLine>())
        {
            this.UpdateCurrencyRateConvertion(partnerTransferLine.DebitCurrencyId);
            this.UpdateCurrencyRateConvertion(partnerTransferLine.CreditCurrencyId);
            partnerTransferLine.PropertyChanged += new PropertyChangedEventHandler(this.Line_PropertyChanged);
        }
    }

    private void Line_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case "DebitCurrencyId":
                this.UpdateCurrencyRateConvertion(sender is PartnerTransferLine partnerTransferLine1 ? partnerTransferLine1.DebitCurrencyId : (string)null);
                break;
            case "CreditCurrencyId":
                this.UpdateCurrencyRateConvertion(sender is PartnerTransferLine partnerTransferLine2 ? partnerTransferLine2.CreditCurrencyId : (string)null);
                break;
        }
    }

    private void UpdateCurrencyRateConvertion(string currencyId)
    {
        if (string.IsNullOrEmpty(currencyId))
            return;

        Currency currency = this.Currencies.List?.FirstOrDefault(x => x.Id == currencyId);
        if (currency == null)
            return;

        if (this.Details.CurrencyConvertions == null)
            this.Details.CurrencyConvertions = new ObservableCollection<CurrencyConvertion>();

        if (!this.Details.CurrencyConvertions.All(x => x.CurrencyId != currency.Id))
            return;

        CurrencyRate rate = currency.GetRate(new DateTime?(this.Details.Date));
        decimal multiplier = rate != null ? rate.Multiplier : 1m;
        decimal divider = rate != null && rate.Divider != 0 ? rate.Divider : 1m;

        this.Details.CurrencyConvertions.Add(new CurrencyConvertion()
        {
            CurrencyId = currency.Id,
            Multiplier = multiplier,
            Divider = divider
        });
    }

    public virtual PartnerTransferLine SelectedLine
    {
        get => this._selectedLine;
        set
        {
            this.SetProperty<PartnerTransferLine>(ref this._selectedLine, value, nameof(SelectedLine));
            this.RaisePropertyChanged<bool>((Expression<Func<bool>>)(() => this.CanEditSelectedLine));
        }
    }

    protected override async Task<bool> OnSaveAsync()
    {
        try
        {
            if (Details.Lines == null || !Details.Lines.Any())
            {
                throw new Exception(this["Document cannot be empty"]);
            }

            foreach (var line in Details.Lines)
            {
                if (string.IsNullOrEmpty(line.PartnerId))
                    throw new Exception(this["Field '{0}' is required", this["Partner"]]);

                if (string.IsNullOrEmpty(line.OfficeId))
                    throw new Exception(this["Field '{0}' is required", this["Office"]]);

                // Валидация прав на офис в строке
                if (!AccountSecurityHelper.CanOperate(LoginService, line.OfficeId))
                {
                    throw new Exception("Access Denied: You do not have permission to operate with the office selected in one of the lines.");
                }

                if (line.DebitAmount == 0 && line.CreditAmount == 0)
                    throw new Exception(this["Amount must be greater than zero"]);

                if (line.DebitAmount > 0 && string.IsNullOrEmpty(line.DebitCurrencyId))
                    throw new Exception(this["Field '{0}' is required", this["Currency"]]);

                if (line.CreditAmount > 0 && string.IsNullOrEmpty(line.CreditCurrencyId))
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

    public bool CanEditSelectedLine => this.HasSaveAccess && this.SelectedLine != null;

    public ICommand SelectedLineDeleteCommand
    {
        get
        {
            return (ICommand)new MvxCommand(new Action(this.OnSelectedLineDeleteCommand), (Func<bool>)(() => !this.IsBusy && this.CanEditSelectedLine));
        }
    }

    private void OnSelectedLineDeleteCommand() => this.Details.Lines.Remove(this.SelectedLine);
}
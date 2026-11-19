using MvvmCross.Core.Navigation;
using MvvmCross.Core.ViewModels;
using Mermer.Authorization.Enums;
using Mermer.Authorization.Services;
using Mermer.Common.Settings;
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
using Mermer.Mvvm.ViewModels;
using Mermer.Services;
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

public class PartnerSlipDetailsViewModel :
  TransactionDetailsViewModel<PartnerSlip>,
  IMvxViewModel<PartnerSlipType>,
  IMvxViewModel
{
    private readonly IConfigurator _configurator;
    private PartnerSlipType _newSlipType;
    private bool _canChangeDate;
    private string[] _groupNames;
    private string[] _tagNames;
    private PartnerSlipLine _selectedLine;

    public PartnerSlipDetailsViewModel(
      IConfigurator configurator,
      ITransactionCodeGenerationService codeGenerationService,
      Reference<Office> offices,
      Reference<Partner> partners,
      Reference<Currency> currencies,
      IRepository<PartnerSlip> repository,
      IListAuthorizer<PartnerSlip> authorizer,
      ILoginService loginService,
      IMvxNavigationService navigationService,
      IUserInteractionService userInteractionService)
      : base(codeGenerationService, repository, authorizer, loginService, navigationService, userInteractionService)
    {
        this._configurator = configurator;
        this.Offices = offices;
        this.Partners = partners;
        this.Currencies = currencies;
        this.SlipTypes = Enum.GetValues(typeof(PartnerSlipType)).Cast<PartnerSlipType>().Select<PartnerSlipType, ListHelper<PartnerSlipType>>((Func<PartnerSlipType, ListHelper<PartnerSlipType>>)(x => new ListHelper<PartnerSlipType>()
        {
            Text = this[x.ToString(), Array.Empty<object>()],
            Value = x
        })).ToArray<ListHelper<PartnerSlipType>>();
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

    public ListHelper<PartnerSlipType>[] SlipTypes { get; set; }

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
        PartnerSlipDetailsViewModel detailsViewModel = this;
        Dictionary<string, Dictionary<string, int>> facets = await ((IRepositoryWithFacets<PartnerSlip>)detailsViewModel.Repository).GetFacets("GroupNames", "TagNames");
        detailsViewModel.GroupNames = facets["GroupNames"].Select<KeyValuePair<string, int>, string>((Func<KeyValuePair<string, int>, string>)(x => x.Key)).ToArray<string>();
        detailsViewModel.TagNames = facets["TagNames"].Select<KeyValuePair<string, int>, string>((Func<KeyValuePair<string, int>, string>)(x => x.Key)).ToArray<string>();
    }

    public void Prepare(PartnerSlipType parameter) => this._newSlipType = parameter;

    protected override Task PreLoad()
    {
        this.CanChangeDate = !(this.Authorizer is ITransactionAuthorizer<PartnerSlip> authorizer) || authorizer.CanChangeDate();
        return Task.WhenAll(base.PreLoad(), this.LoadFacetsAsync(), this.Offices.Initialize(), this.Partners.Initialize(), this.Currencies.Initialize());
    }

    protected override async Task PostLoad()
    {
        await base.PostLoad();

        if (string.IsNullOrEmpty(ItemId))
        {
            Details.SlipType = _newSlipType;
            AppSettings configAsync = await _configurator.GetConfigAsync<AppSettings>();
            string defOffice = configAsync.DefaultOfficeId;

            // Подставляем только разрешенный для операций офис
            if (AccountSecurityHelper.CanOperate(LoginService, defOffice))
            {
                Details.OfficeId = defOffice;
            }
            else
            {
                var firstAllowed = Offices?.List?.FirstOrDefault(o => !o.IsDisabled && AccountSecurityHelper.CanOperate(LoginService, o.Id));
                Details.OfficeId = firstAllowed?.Id;
            }
        }

        if (Details.Lines == null)
            Details.Lines = new ObservableCollection<PartnerSlipLine>();

        Details.Lines.CollectionChanged += Lines_CollectionChanged;

        foreach (var line in Details.Lines)
            line.PropertyChanged += Line_PropertyChanged;

        if (Details.CurrencyConvertions == null)
            Details.CurrencyConvertions = new ObservableCollection<CurrencyConvertion>();

        // Фильтруем офисы: только те, где разрешены операции (или открытый документ с правом Read)
        Offices.Filter = x => !x.IsDisabled && (AccountSecurityHelper.CanOperate(LoginService, x.Id) ||
                              (!string.IsNullOrEmpty(ItemId) && x.Id == Details?.OfficeId && AccountSecurityHelper.CanRead(LoginService, x.Id)));
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
        foreach (PartnerSlipLine partnerSlipLine in e.NewItems.Cast<PartnerSlipLine>())
        {
            this.UpdateCurrencyRateConvertion(partnerSlipLine.DebitCurrencyId);
            this.UpdateCurrencyRateConvertion(partnerSlipLine.CreditCurrencyId);
            partnerSlipLine.PropertyChanged += new PropertyChangedEventHandler(this.Line_PropertyChanged);
        }
    }

    private void Line_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case "DebitCurrencyId":
                this.UpdateCurrencyRateConvertion(sender is PartnerSlipLine partnerSlipLine1 ? partnerSlipLine1.DebitCurrencyId : (string)null);
                break;
            case "CreditCurrencyId":
                this.UpdateCurrencyRateConvertion(sender is PartnerSlipLine partnerSlipLine2 ? partnerSlipLine2.CreditCurrencyId : (string)null);
                break;
        }
    }

    private void UpdateCurrencyRateConvertion(string currencyId)
    {
        if (string.IsNullOrEmpty(currencyId))
            return;

        Currency currency = this.Currencies.List.FirstOrDefault(x => x.Id == currencyId);
        if (currency == null)
            return;

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

    public virtual PartnerSlipLine SelectedLine
    {
        get => this._selectedLine;
        set
        {
            this.SetProperty<PartnerSlipLine>(ref this._selectedLine, value, nameof(SelectedLine));
            this.RaisePropertyChanged<bool>((Expression<Func<bool>>)(() => this.CanEditSelectedLine));
        }
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

    public ICommand SelectOfficeCommand
    {
        get
        {
            return (ICommand)new MvxAsyncCommand(new Func<Task>(this.OnSelectDepositoryCommandAsync), (Func<bool>)(() => !this.IsBusy && this.HasSaveAccess));
        }
    }

    private async Task OnSelectDepositoryCommandAsync()
    {
        PartnerSlipDetailsViewModel detailsViewModel = this;
        PartnerSlip partnerSlip = detailsViewModel.Details;
        partnerSlip.OfficeId = await detailsViewModel.NavigationService.Navigate<ListViewModel<Office>, string, string>(detailsViewModel.Details.OfficeId ?? Guid.Empty.ToString());
        partnerSlip = (PartnerSlip)null;
    }

    public ICommand ImportCommand
    {
        get
        {
            return (ICommand)new MvxAsyncCommand(new Func<Task>(this.OnImportCommandAsync), (Func<bool>)(() => !this.IsBusy && this.HasSaveAccess));
        }
    }

    protected virtual async Task OnImportCommandAsync()
    {
        PartnerSlipDetailsViewModel detailsViewModel = this;
        IEnumerable<object> source1 = await detailsViewModel.NavigationService.Navigate<DataImportViewModel, Type, IEnumerable<object>>(typeof(PartnerSlipDetailsViewModel.LineImport));
        int num1 = 0;
        detailsViewModel.IsBusy = true;
        detailsViewModel.SuspendLoading = true;
        try
        {
            IEnumerable<PartnerSlipDetailsViewModel.LineImport> source2 = source1 != null ? source1.Cast<PartnerSlipDetailsViewModel.LineImport>() : (IEnumerable<PartnerSlipDetailsViewModel.LineImport>)null;
            if (source2 != null)
            {
                int num2 = source2.Count<PartnerSlipDetailsViewModel.LineImport>();
                foreach (PartnerSlipDetailsViewModel.LineImport lineImport in source2)
                {
                    PartnerSlipDetailsViewModel.LineImport item = lineImport;
                    ++num1;
                    detailsViewModel.Status = detailsViewModel["Importing {0} of {1} lines", new object[2]
                    {
            (object) num1,
            (object) num2
                    }];
                    Partner partner = detailsViewModel.Partners.List.Single<Partner>((Func<Partner, bool>)(x => x.Code == item.PartnerCode));
                    PartnerSlipLine partnerSlipLine = new PartnerSlipLine()
                    {
                        PartnerId = partner.Id,
                        DebitAmount = item.DebitAmount,
                        DebitCurrencyId = detailsViewModel.Currencies.List.SingleOrDefault<Currency>((Func<Currency, bool>)(x => x.Name == item.DebitCurrency))?.Id,
                        CreditAmount = item.CreditAmount,
                        CreditCurrencyId = detailsViewModel.Currencies.List.SingleOrDefault<Currency>((Func<Currency, bool>)(x => x.Name == item.CreditCurrency))?.Id
                    };
                    detailsViewModel.Details.Lines.Add(partnerSlipLine);
                }
            }
        }
        catch (Exception ex)
        {
            detailsViewModel.UserInteractionService.ShowExceptionMessage(ex);
        }
        detailsViewModel.Status = (string)null;
        detailsViewModel.SuspendLoading = false;
        detailsViewModel.IsBusy = false;
    }

    public class LineImport
    {
        public string PartnerCode { get; internal set; }

        public Decimal DebitAmount { get; internal set; }

        public string DebitCurrency { get; internal set; }

        public Decimal CreditAmount { get; internal set; }

        public string CreditCurrency { get; internal set; }
    }

    protected override async Task<bool> OnSaveAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(Details.OfficeId))
            {
                throw new Exception(this["Field '{0}' is required", this["Office"]]);
            }

            if (!AccountSecurityHelper.CanOperate(LoginService, Details.OfficeId))
            {
                throw new Exception("Access Denied: You do not have permission to operate with the selected office.");
            }

            if (Details.Lines == null || !Details.Lines.Any())
            {
                throw new Exception(this["Document cannot be empty"]);
            }

            foreach (var line in Details.Lines)
            {
                if (string.IsNullOrEmpty(line.PartnerId))
                    throw new Exception(this["Field '{0}' is required", this["Partner"]]);

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
}
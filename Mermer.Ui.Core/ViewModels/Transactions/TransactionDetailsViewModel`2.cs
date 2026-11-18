using MvvmCross.Core.Navigation;
using MvvmCross.Core.ViewModels;
using Mermer.Authorization.Services;
using Mermer.Common.Settings;
using Mermer.FundsManagement.Models;
using Mermer.FundsManagement.Models.Extenders;
using Mermer.Transactions.Models;
using Mermer.Transactions.Models.Authorizers;
using Mermer.Transactions.Services;
using Mermer.Ui.Core.Helpers;
using Mermer.Ui.Core.ViewModels.Common;
using Mermer.Data;
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

public class TransactionDetailsViewModel<T, TLine> : TransactionDetailsViewModel<T>
  where T : Transaction<TLine>
  where TLine : TransactionLine
{
    protected AppSettings AppSettings;
    protected readonly IConfigurator Configurator;
    private bool _canChangeDate;
    private string[] _groupNames;
    private string[] _tagNames;
    private TLine _selectedLine;
    private string _initialDisplayCurrencyId;
    public TransactionDetailsViewModel(
      IConfigurator configurator,
      IRepository<T> repository,
      IListAuthorizer<T> authorizer,
      ILoginService loginService,
      Reference<Currency> currencies,
      IMvxNavigationService navigationService,
      ITransactionCodeGenerationService codegentor,
      IUserInteractionService userInteractionService)
      : base(codegentor, repository, authorizer, loginService, navigationService, userInteractionService)
    {
        this.Configurator = configurator;
        this.Currencies = currencies;
    }

    protected override MvxInpcInterceptionResult InterceptRaisePropertyChanged(
      PropertyChangedEventArgs changedArgs)
    {
        if (changedArgs.PropertyName == "HasSaveAccess")
            this.RaisePropertyChanged<bool>((Expression<Func<bool>>)(() => this.CanChangeDate));
        return base.InterceptRaisePropertyChanged(changedArgs);
    }

    public override string Caption
    {
        get
        {
            return this[this.Details?.Type ?? typeof(T).Name, Array.Empty<object>()] + (this.IsDirty ? " *" : "");
        }
        set => base.Caption = value;
    }

    public bool CanChangeDate
    {
        get => this.HasSaveAccess && this._canChangeDate;
        set => this.SetProperty<bool>(ref this._canChangeDate, value, nameof(CanChangeDate));
    }

    public Reference<Currency> Currencies { get; }

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

    public virtual TLine SelectedLine
    {
        get => this._selectedLine;
        set
        {
            this.SetProperty<TLine>(ref this._selectedLine, value, nameof(SelectedLine));
            this.RaisePropertyChanged<bool>((Expression<Func<bool>>)(() => this.IsLineSelected));
            this.RaisePropertyChanged<bool>((Expression<Func<bool>>)(() => this.CanEditSelectedLine));
        }
    }

    public bool IsLineSelected => (object)this.SelectedLine != null;

    public bool CanEditSelectedLine => this.HasSaveAccess && (object)this.SelectedLine != null;

    protected virtual async Task LoadFacetsAsync()
    {
        TransactionDetailsViewModel<T, TLine> detailsViewModel = this;
        if (!(detailsViewModel.Repository is IRepositoryWithFacets<T> repository))
            return;
        string[] strArray = new string[2]
        {
      "GroupNames",
      "TagNames"
        };
        Dictionary<string, Dictionary<string, int>> facets = await repository.GetFacets(strArray);
        detailsViewModel.GroupNames = facets["GroupNames"].Select<KeyValuePair<string, int>, string>((Func<KeyValuePair<string, int>, string>)(x => x.Key)).ToArray<string>();
        detailsViewModel.TagNames = facets["TagNames"].Select<KeyValuePair<string, int>, string>((Func<KeyValuePair<string, int>, string>)(x => x.Key)).ToArray<string>();
    }

    protected override Task PreLoad()
    {
        this.CanChangeDate = !(this.Authorizer is ITransactionAuthorizer<T> authorizer) || authorizer.CanChangeDate();
        return Task.WhenAll(base.PreLoad(), this.LoadFacetsAsync(), this.Currencies.Initialize(), Task.Run((Func<Task>)(async () => this.AppSettings = await this.Configurator.GetConfigAsync<AppSettings>())));
    }

    protected override async Task PostLoad()
    {
        await base.PostLoad();

        Details.CurrencyConverterRequested += CurrencyConverter;
        Details.AmountFormatterRequested += AmountFormatter;
        Details.DefaultCurrencyIdRequested += DefaultCurrencyIdProvider;
        Details.RaiseChangeEvents = false;

        // Назначаем дефолтную валюту только при создании нового документа 
        // либо если у существующего документа она пустая:
        if (string.IsNullOrEmpty(this.ItemId) || string.IsNullOrEmpty(Details.DisplayCurrencyId))
        {
            Details.DisplayCurrencyId = DefaultCurrencyIdProvider();
        }

        // Запоминаем оригинальную валюту создания документа из базы
        this._initialDisplayCurrencyId = Details.DisplayCurrencyId;

        Details.PropertyChanged += Details_PropertyChanged;

        if (Details.Lines == null)
            Details.Lines = new WatchedObservableCollection<TLine>();

        if (Details.CurrencyConvertions == null)
            Details.CurrencyConvertions = new WatchedObservableCollection<CurrencyConvertion>();

        // --- ПРИНУДИТЕЛЬНО ЗАПОЛНЯЕМ ВСЕ ВАЛЮТЫ И ИХ РЕАЛЬНЫЕ КУРСЫ ---
        if (this.Currencies?.List != null)
        {
            foreach (var currency in this.Currencies.List)
            {
                var existing = Details.CurrencyConvertions.FirstOrDefault(c => c.CurrencyId == currency.Id);
                var rate = currency.GetRate(new DateTime?(this.Details.Date));
                decimal mult = rate != null && rate.Multiplier != 0 ? rate.Multiplier : 1m;
                decimal div = rate != null && rate.Divider != 0 ? rate.Divider : 1m;

                if (existing != null)
                {
                    if (string.IsNullOrEmpty(this.ItemId))
                    {
                        existing.Multiplier = mult;
                        existing.Divider = div;
                    }
                }
                else
                {
                    Details.CurrencyConvertions.Add(new CurrencyConvertion
                    {
                        CurrencyId = currency.Id,
                        Multiplier = mult,
                        Divider = div
                    });
                }
            }
        }

        Details.UpdateCurrencyConvertion();
        Details.UpdateDisplayCurrencyId(false);
        Details.RaisePropertyChanged("LinesCount");
        Details.RaisePropertyChanged("DisplayTotal");
        Details.RaisePropertyChanged("Total");
        Details.RaisePropertyChanged("ActionTotal");

        WatchedObservableCollection<TLine> lines = Details.Lines;
        if (lines != null)
            lines.FirstOrDefault()?.UpdateDisplayCurrencyId(false);

        IEnumerable<string> usedCurrencyIds = Details.Lines.Select(x => x.CurrencyId).Distinct();
        Currencies.Filter = x => !x.IsDisabled || usedCurrencyIds.Contains(x.Id);

        // ВАЖНО: Включаем обратно генерацию событий после завершения загрузки!
        Details.RaiseChangeEvents = true;
    }

    protected override Task<bool> OnSaveAsync()
    {
        this.Details.PropertyChanged -= new PropertyChangedEventHandler(this.Details_PropertyChanged);

        // Если документ уже существовал, перед отправкой на сервер возвращаем его оригинальную валюту
        if (!string.IsNullOrEmpty(this.ItemId) && !string.IsNullOrEmpty(this._initialDisplayCurrencyId))
        {
            this.Details.DisplayCurrencyId = this._initialDisplayCurrencyId;
        }

        return base.OnSaveAsync();
    }

    protected virtual void Details_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "DisplayCurrencyId" && !this.IsBusy)
        {
            // Принудительно запускаем пересчет отображения строк и итогов
            this.Details.UpdateDisplayCurrencyId(true);

            if (this.Details.LinesCount > 0)
            {
                int num1 = 0;
                if ((object)this.SelectedLine != null)
                {
                    int num2 = this.Details.Lines.IndexOf(this.SelectedLine);
                    if (num2 > 10)
                        num1 = num2 - 10;
                }
                int num3 = num1 + 20;
                if (num3 > this.Details.LinesCount)
                    num3 = this.Details.LinesCount;
                for (int index = num1; index < num3; ++index)
                    this.Details.Lines.Move(index, index);
            }
        }

        this.RaisePropertyChanged<string>((Expression<Func<string>>)(() => this.SubCaption));
    }

    protected CurrencyConvertion CurrencyConverter(string currencyId)
    {
        Reference<Currency> currencies = this.Currencies;
        Currency currency1;
        if (currencies == null)
        {
            currency1 = (Currency)null;
        }
        else
        {
            IEnumerable<Currency> list = currencies.List;
            currency1 = list != null ? list.SingleOrDefault<Currency>((Func<Currency, bool>)(x => x.Id == currencyId)) : (Currency)null;
        }
        Currency currency2 = currency1;
        CurrencyRate rate = currency2 != null ? currency2.GetRate(new DateTime?(this.Details.Date)) : (CurrencyRate)null;
        if (rate == null)
            return (CurrencyConvertion)null;
        return new CurrencyConvertion()
        {
            CurrencyId = currency2.Id,
            Multiplier = rate.Multiplier != 0 ? rate.Multiplier : 1m,
            Divider = rate.Divider != 0 ? rate.Divider : 1m
        };
    }

    protected string AmountFormatter(Decimal amount, string currencyId)
    {
        Reference<Currency> currencies = this.Currencies;
        Currency currency1;
        if (currencies == null)
        {
            currency1 = (Currency)null;
        }
        else
        {
            IEnumerable<Currency> list = currencies.List;
            currency1 = list != null ? list.SingleOrDefault<Currency>((Func<Currency, bool>)(x => x.Id == currencyId)) : (Currency)null;
        }
        Currency currency2 = currency1;
        return currency2 == null ? (string)null : string.Format("{0:#,##0.00} {1}", (object)amount, (object)currency2.Name);
    }

    private string DefaultCurrencyIdProvider()
    {
        if (!string.IsNullOrEmpty(this.AppSettings?.DefaultCurrencyId))
            return this.AppSettings.DefaultCurrencyId;
        Reference<Currency> currencies = this.Currencies;
        if (currencies == null)
            return (string)null;
        IEnumerable<Currency> list = currencies.List;
        if (list == null)
            return (string)null;
        return list.SingleOrDefault<Currency>((Func<Currency, bool>)(x => x.IsDefault))?.Id;
    }
}
using MvvmCross.Core.Navigation;
using MvvmCross.Core.ViewModels;
using Mermer.Authorization.Enums;
using Mermer.Authorization.Models;
using Mermer.Authorization.Services;
using Mermer.Commerce.Models;
using Mermer.Common.Services;
using Mermer.Common.Settings;
using Mermer.CRM.Models;
using Mermer.Enterprise.Models;
using Mermer.Finance.DailyRegistery.Models;
using Mermer.Finance.Models;
using Mermer.Finance.Spending.Models;
using Mermer.FundsManagement.Models;
using Mermer.StockManagement.Models;
using Mermer.Ui.Core.ViewModels.Authorization;
using Mermer.Ui.Core.ViewModels.Commerce;
using Mermer.Ui.Core.ViewModels.CRM;
using Mermer.Ui.Core.ViewModels.Finance;
using Mermer.Ui.Core.ViewModels.Finance.Spending;
using Mermer.Ui.Core.ViewModels.FundsManagement;
using Mermer.Ui.Core.ViewModels.Reporting;
using Mermer.Ui.Core.ViewModels.Settings;
using Mermer.Ui.Core.ViewModels.StockManagement;
using Mermer.Ui.Core.ViewModels.Warehousing;
using Mermer.Ui.Core.ViewModels.Warehousing.Ordering;
using Mermer.Warehousing.Models;
using Mermer.Warehousing.Ordering.Models;
using Mermer.Warehousing.Revisioning.Models;
using Mermer.Mvvm.Messages;
using Mermer.Mvvm.Services;
using Mermer.Mvvm.Tools;
using Mermer.Mvvm.ViewModels;
using Mermer.Services;
using System;
using System.Threading.Tasks;
using System.Windows.Input;

#nullable disable
namespace Mermer.Ui.Core.ViewModels;

public class MainViewModel : BaseViewModel
{
  private readonly ILoginService _loginService;
  private readonly IConfigurator _configurator;
  private readonly IAuthorizationService _authService;
  private readonly IDocumentChangeListener _changeListener;
  
  private bool _isAdmin;
  private string _currentUser;
  private bool _openPostOnLoad;
  private bool _allowReporting;
  private bool _autoHideMenu = false;
    private static bool _isLanguageMetadataOverridden = false;
    private static bool _isFirstAppLoad = true;
    public MainViewModel(
    ILoginService loginService,
    IConfigurator configurator,
    IAuthorizationService authService,
    IDocumentChangeListener changeListener,
    IMvxNavigationService navigationService,
    IUserInteractionService userInteractionService)
    : base(navigationService, userInteractionService)
    {
        this._loginService = loginService;
        this._configurator = configurator;
        this._authService = authService;
        this._changeListener = changeListener;
        try
        {
            AppSettings config = configurator.GetConfig<AppSettings>();
            if (config != null && !string.IsNullOrEmpty(config.Culture))
            {
                var culture = new System.Globalization.CultureInfo(config.Culture);
                System.Threading.Thread.CurrentThread.CurrentCulture = culture;
                System.Threading.Thread.CurrentThread.CurrentUICulture = culture;
            }
        }
        catch { }
    }

    public bool IsInDebugMode => false;

  public bool IsAdmin
  {
    get => this._isAdmin;
    set => this.SetProperty<bool>(ref this._isAdmin, value, nameof (IsAdmin));
  }

  public virtual string CurrentUser
  {
    get => this._currentUser;
    set => this.SetProperty<string>(ref this._currentUser, value, nameof (CurrentUser));
  }

  public bool OpenPosOnLoad
  {
    get => this._openPostOnLoad;
    set => this.SetProperty<bool>(ref this._openPostOnLoad, value, nameof (OpenPosOnLoad));
  }

  public virtual bool AllowReporting
  {
    get => this._allowReporting;
    set => this.SetProperty<bool>(ref this._allowReporting, value, nameof (AllowReporting));
  }

  public virtual bool AutoHideMenu
  {
    get => this._autoHideMenu;
    set
    {
      if (!this.SetProperty<bool>(ref this._autoHideMenu, value, nameof (AutoHideMenu)) || this.IsBusy)
        return;
      AppSettings config = this._configurator.GetConfig<AppSettings>();
      config.AutoHideMenu = this._autoHideMenu;
      this._configurator.SetConfig<AppSettings>(config);
    }
  }

    public override async Task Initialize()
    {
        await base.Initialize();
        this.CurrentUser = this._loginService.Session.Username;
        this.IsAdmin = this._loginService.Session.IsAdmin;

        AppSettings config = this._configurator.GetConfig<AppSettings>();

        
        if (_isFirstAppLoad)
        {
            this.OpenPosOnLoad = config?.OpenPosOnLoad ?? false;
            _isFirstAppLoad = false; 
        }
        else
        {
            this.OpenPosOnLoad = false; 
        }

        this.AutoHideMenu = config?.AutoHideMenu ?? false;

        var connectionSettings = this._configurator.GetConfig<ConnectionSettings>();
        this.AllowReporting = connectionSettings?.AllowReporting ?? true;

        this._changeListener?.Start();
    }

    public ICommand LogoutCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.LogoutAsync), (Func<bool>) (() => !this.IsBusy));
    }
  }

    private async Task LogoutAsync()
    {
        MainViewModel mainViewModel = this;
        mainViewModel.IsBusy = true;
        try
        {
            // Обязательно глушим слушатель изменений базы, чтобы остановить фоновые потоки
            try
            {
                mainViewModel._changeListener?.Stop();
            }
            catch { }

            // Сбрасываем сессию пользователя
            await mainViewModel._loginService.LogoutAsync();

            // Безопасно пытаемся закрыть дочерние окна
            try
            {
                mainViewModel.ChangePresentation(new MvxCloseAllPresentationHint());
            }
            catch { }

            // Переходим на экран логина
            await mainViewModel.NavigationService.Navigate<LoginViewModel>();

            // Закрываем сам MainViewModel
            await mainViewModel.NavigationService.Close(mainViewModel);
        }
        catch (Exception ex)
        {
            mainViewModel.UserInteractionService.ShowExceptionMessage(ex);
        }
        finally
        {
            mainViewModel.IsBusy = false;
        }
    }

    public ICommand ShowAboutCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.OnShowAboutCommandAsync), (Func<bool>) (() => !this.IsBusy));
    }
  }

  private Task OnShowAboutCommandAsync() => this.NavigationService.Navigate<AboutViewModel>();

  public ICommand ShowOfficesCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.ShowOfficesAsync), (Func<bool>) (() => !this.IsBusy && this._authService.TryAuthorizeAction((Enum) ListActions.OfficesList, (Enum) ListAccessLevel.Read)));
    }
  }

  private async Task ShowOfficesAsync()
  {
    await this.NavigationService.Navigate<ListViewModel<Office>, string>(string.Empty);
  }

  public ICommand ShowWarehousesCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.ShowWarehousesAsync), (Func<bool>) (() => !this.IsBusy && this._authService.TryAuthorizeAction((Enum) ListActions.WarehousesList, (Enum) ListAccessLevel.Read)));
    }
  }

  private async Task ShowWarehousesAsync()
  {
    await this.NavigationService.Navigate<ListViewModel<Warehouse>, string>(string.Empty);
  }

  public ICommand ShowDepositoriesCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.ShowDepositoriesAsync), (Func<bool>) (() => !this.IsBusy && this._authService.TryAuthorizeAction((Enum) ListActions.DepositoriesList, (Enum) ListAccessLevel.Read)));
    }
  }

  private async Task ShowDepositoriesAsync()
  {
    await this.NavigationService.Navigate<ListViewModel<Depository>, string>(string.Empty);
  }

  public ICommand ShowRolesCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.ShowRolesAsync), (Func<bool>) (() => !this.IsBusy && this._authService.TryAuthorizeAction((Enum) Actions.UserManagement, (Enum) AccessLevel.Grant)));
    }
  }

  private async Task ShowRolesAsync()
  {
    await this.NavigationService.Navigate<ListViewModel<Role>, string>(string.Empty);
  }

  public ICommand ShowUsersCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.ShowUsersAsync), (Func<bool>) (() => !this.IsBusy && this._authService.TryAuthorizeAction((Enum) Actions.UserManagement, (Enum) AccessLevel.Grant)));
    }
  }

  private async Task ShowUsersAsync()
  {
    await this.NavigationService.Navigate<ListViewModel<User>, string>(string.Empty);
  }

  public ICommand ChangePasswordCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.OnChangePasswordCommandAsync), (Func<bool>) (() => !this.IsBusy));
    }
  }

  private Task OnChangePasswordCommandAsync()
  {
    return this.NavigationService.Navigate<ChangePasswordViewModel>();
  }

  public ICommand ShowAppConfigCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.ShowAppConfigAsync), (Func<bool>) (() => !this.IsBusy));
    }
  }

  private async Task ShowAppConfigAsync()
  {
    await this.NavigationService.Navigate<ApplicationSettingsViewModel>();
  }

  public ICommand ShowPrinterConfigCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.ShowPrinterConfigAsync), (Func<bool>) (() => !this.IsBusy));
    }
  }

  private async Task ShowPrinterConfigAsync()
  {
    await this.NavigationService.Navigate<PrinterSettingsViewModel>();
  }

  public ICommand ShowBarcodeConfigCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.ShowBarcodeConfigAsync), (Func<bool>) (() => !this.IsBusy));
    }
  }

  private async Task ShowBarcodeConfigAsync()
  {
    await this.NavigationService.Navigate<BarcodeSettingsViewModel>();
  }

  public ICommand ShowMockObjectsCreator
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.OnShowMockObjectsCreatorAsync), (Func<bool>) (() => !this.IsBusy));
    }
  }

  protected virtual async Task OnShowMockObjectsCreatorAsync()
  {
    MainViewModel mainViewModel = this;
    try
    {
      await mainViewModel.NavigationService.Navigate<MockObjectsViewModel>();
    }
    catch (Exception ex)
    {
      mainViewModel.UserInteractionService.ShowExceptionMessage(ex);
      throw;
    }
  }

  public ICommand ShowPosCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.ShowPosAsync), (Func<bool>) (() => !this.IsBusy && this._authService.TryAuthorizeAction((Enum) InvoiceType.Sales, (Enum) TransactionAccessLevel.Create)));
    }
  }

  private async Task ShowPosAsync()
  {
    await this.NavigationService.Navigate<InvoiceDetailsViewModel, InvoiceType>(InvoiceType.Sales);
  }

  public ICommand ShowStocksCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.ShowStocksAsync), (Func<bool>) (() => !this.IsBusy && this._authService.TryAuthorizeAction((Enum) ListActions.StocksList, (Enum) ListAccessLevel.Read)));
    }
  }

  private async Task ShowStocksAsync()
  {
    await this.NavigationService.Navigate<StocksListViewModel, string>(string.Empty);
  }

  public ICommand ShowStockNameComposersCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.OnShowStockNameComposersCommandAsync), (Func<bool>) (() => !this.IsBusy && this._authService.TryAuthorizeAction((Enum) ListActions.StockNameComposersList, (Enum) ListAccessLevel.Read)));
    }
  }

  private async Task OnShowStockNameComposersCommandAsync()
  {
    await this.NavigationService.Navigate<ListViewModel<StockNameComposer>>();
  }

  public ICommand ShowStockAlternativesCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.OnShowStockAlternativesCommandAsync), (Func<bool>) (() => !this.IsBusy && this._authService.TryAuthorizeAction((Enum) ListActions.StockAlternativesList, (Enum) ListAccessLevel.Read)));
    }
  }

  private async Task OnShowStockAlternativesCommandAsync()
  {
    await this.NavigationService.Navigate<ListViewModel<StockAlternative>>();
  }

  public ICommand ShowStockActionsCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.OnShowStockActionsCommandAsync), (Func<bool>) (() => !this.IsBusy && this._authService.TryAuthorizeAction((Enum) Actions.StockActionsList, (Enum) AccessLevel.Grant)));
    }
  }

  private async Task OnShowStockActionsCommandAsync()
  {
    await this.NavigationService.Navigate<StockActionsListViewModel>();
  }

  public ICommand ShowStockBalancesCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.OnShowStockBalancesCommandAsync), (Func<bool>) (() => !this.IsBusy && this._authService.TryAuthorizeAction((Enum) Actions.StockBalancesList, (Enum) AccessLevel.Grant)));
    }
  }

  private async Task OnShowStockBalancesCommandAsync()
  {
    await this.NavigationService.Navigate<StockBalancesListViewModel>();
  }

  public ICommand ShowStockBalancesByStatusesCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.OnShowStockBalancesByStatusesCommandAsync), (Func<bool>) (() => !this.IsBusy && this._authService.TryAuthorizeAction((Enum) Actions.StockBalancesList, (Enum) AccessLevel.Grant)));
    }
  }

  private async Task OnShowStockBalancesByStatusesCommandAsync()
  {
    await this.NavigationService.Navigate<StockBalancesByStatusesListViewModel>();
  }

  public ICommand ShowStockBalancesByWarehouseCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.OnShowStockBalancesByWarehousesAsync), (Func<bool>) (() => !this.IsBusy && this._authService.TryAuthorizeAction((Enum) Actions.StockBalancesList, (Enum) AccessLevel.Grant)));
    }
  }

  private async Task OnShowStockBalancesByWarehousesAsync()
  {
    await this.NavigationService.Navigate<StockBalancesByDateAndWarehousesListViewModel>();
  }

  public ICommand ShowStockTurnoverDataCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.OnShowStockTurnoverDataCommandAsync), (Func<bool>) (() => !this.IsBusy && this.AllowReporting && this._authService.TryAuthorizeAction((Enum) Actions.StockTurnoverDataList, (Enum) AccessLevel.Grant)));
    }
  }

  private async Task OnShowStockTurnoverDataCommandAsync()
  {
    await this.NavigationService.Navigate<StockTurnoverDataListViewModel>();
  }

  public ICommand ShowStockRepriceEffectsCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.OnShowStockRepriceEffectsCommandAsync), (Func<bool>) (() => !this.IsBusy && this.AllowReporting && this._authService.TryAuthorizeAction((Enum) Actions.StockRepriceEffectsList, (Enum) AccessLevel.Grant)));
    }
  }

  private async Task OnShowStockRepriceEffectsCommandAsync()
  {
    await this.NavigationService.Navigate<StockRepriceEffectsListViewModel>();
  }

  public ICommand ShowCurrenciesCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.ShowCurrenciesAsync), (Func<bool>) (() => !this.IsBusy && this._authService.TryAuthorizeAction((Enum) ListActions.CurrenciesList, (Enum) ListAccessLevel.Read)));
    }
  }

  private async Task ShowCurrenciesAsync()
  {
    await this.NavigationService.Navigate<ListViewModel<Currency>, string>(string.Empty);
  }

  public ICommand ShowFundsActionsCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.OnShowFundsActionsCommandAsync), (Func<bool>) (() => !this.IsBusy && this._authService.TryAuthorizeAction((Enum) Actions.FundsActionsList, (Enum) AccessLevel.Grant)));
    }
  }

  private async Task OnShowFundsActionsCommandAsync()
  {
    await this.NavigationService.Navigate<FundsActionsListViewModel>();
  }

  public ICommand ShowFundsBalancesCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.OnShowFundsBalancesCommandAsync), (Func<bool>) (() => !this.IsBusy && this._authService.TryAuthorizeAction((Enum) Actions.FundsBalancesList, (Enum) AccessLevel.Grant)));
    }
  }

  private async Task OnShowFundsBalancesCommandAsync()
  {
    await this.NavigationService.Navigate<FundsBalancesListViewModel>();
  }

  public ICommand ShowExpensesCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.ShowExpensesAsync), (Func<bool>) (() => !this.IsBusy && this._authService.TryAuthorizeAction((Enum) ListActions.ExpensesList, (Enum) ListAccessLevel.Read)));
    }
  }

  private async Task ShowExpensesAsync()
  {
    await this.NavigationService.Navigate<ListViewModel<Expense>, string>(string.Empty);
  }

  public ICommand ShowExpenseActionsCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.OnShowExpenseActionsCommandAsync), (Func<bool>) (() => !this.IsBusy && this._authService.TryAuthorizeAction((Enum) Actions.ExpenseActionsList, (Enum) AccessLevel.Grant)));
    }
  }

  private async Task OnShowExpenseActionsCommandAsync()
  {
    await this.NavigationService.Navigate<ExpenseActionsListViewModel>();
  }

  public ICommand ShowPartnersCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.ShowPartnersAsync), (Func<bool>) (() => !this.IsBusy && this._authService.TryAuthorizeAction((Enum) ListActions.PartnersList, (Enum) ListAccessLevel.Read)));
    }
  }

  private async Task ShowPartnersAsync()
  {
    await this.NavigationService.Navigate<ListViewModel<Partner>, string>(string.Empty);
  }

  public ICommand ShowPartnerActionsCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.OnShowPartnerActionsCommandAsync), (Func<bool>) (() => !this.IsBusy && this._authService.TryAuthorizeAction((Enum) Actions.PartnerActionsList, (Enum) AccessLevel.Grant)));
    }
  }

  private async Task OnShowPartnerActionsCommandAsync()
  {
    await this.NavigationService.Navigate<PartnerActionsListViewModel>();
  }

  public ICommand ShowPartnerBalancesCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.OnShowPartnerBalancesCommandAsync), (Func<bool>) (() => !this.IsBusy && this._authService.TryAuthorizeAction((Enum) Actions.PartnerBalancesList, (Enum) AccessLevel.Grant)));
    }
  }

  private async Task OnShowPartnerBalancesCommandAsync()
  {
    await this.NavigationService.Navigate<PartnerBalancesListViewModel>();
  }

  public ICommand ShowStockSlipsCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.ShowStockSlipsAsync), (Func<bool>) (() => !this.IsBusy && this._authService.TryAuthorizeAnyAction(typeof (StockSlipType), (Enum) TransactionAccessLevel.ReadOwn)));
    }
  }

  private async Task ShowStockSlipsAsync()
  {
    await this.NavigationService.Navigate<ListViewModel<StockSlip>, string>(string.Empty);
  }

  public ICommand ShowNewStockSlipCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand<StockSlipType>(new Func<StockSlipType, Task>(this.ShowNewStockSlipAsync), (Func<StockSlipType, bool>) (x => !this.IsBusy && this._authService.TryAuthorizeAction((Enum) x, (Enum) TransactionAccessLevel.Create)));
    }
  }

  private async Task ShowNewStockSlipAsync(StockSlipType type)
  {
    await this.NavigationService.Navigate<StockSlipDetailsViewModel, StockSlipType>(type);
  }

  public ICommand ShowStockTransfersCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.ShowStockTransfersAsync), (Func<bool>) (() => !this.IsBusy && this._authService.TryAuthorizeAction((Enum) TransactionActions.StockTransfers, (Enum) TransactionAccessLevel.ReadOwn)));
    }
  }

  private async Task ShowStockTransfersAsync()
  {
    await this.NavigationService.Navigate<ListViewModel<StockTransfer>, string>(string.Empty);
  }

  public ICommand ShowNewStockTransferCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.ShowNewStockTransferAsync), (Func<bool>) (() => !this.IsBusy && this._authService.TryAuthorizeAction((Enum) TransactionActions.StockTransfers, (Enum) TransactionAccessLevel.Create)));
    }
  }

  private async Task ShowNewStockTransferAsync()
  {
    await this.NavigationService.Navigate<DetailsViewModel<StockTransfer>>();
  }

  public ICommand ShowStockRevisionsCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.ShowStockRevisionsAsync), (Func<bool>) (() => !this.IsBusy && this._authService.TryAuthorizeAction((Enum) TransactionActions.StockRevisions, (Enum) TransactionAccessLevel.ReadOwn)));
    }
  }

  private async Task ShowStockRevisionsAsync()
  {
    await this.NavigationService.Navigate<ListViewModel<StockRevision>, string>(string.Empty);
  }

  public ICommand ShowStockOrdersCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.ShowStockOrdersAsync), (Func<bool>) (() => !this.IsBusy && this._authService.TryAuthorizeAction((Enum) TransactionActions.StockOrders, (Enum) TransactionAccessLevel.ReadOwn)));
    }
  }

  private async Task ShowStockOrdersAsync()
  {
    await this.NavigationService.Navigate<ListViewModel<StockOrder>, string>(string.Empty);
  }

  public ICommand ShowNewStockOrderCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.ShowNewStockOrderAsync), (Func<bool>) (() => !this.IsBusy && this._authService.TryAuthorizeAction((Enum) TransactionActions.StockOrders, (Enum) TransactionAccessLevel.Create)));
    }
  }

  private async Task ShowNewStockOrderAsync()
  {
    await this.NavigationService.Navigate<StockOrderDetailsViewModel>();
  }

  public ICommand ShowStockOrderTemplatesCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.ShowStockOrderTemplatesAsync), (Func<bool>) (() => !this.IsBusy && this._authService.TryAuthorizeAction((Enum) ListActions.StockOrderTemplatesList, (Enum) ListAccessLevel.Read)));
    }
  }

  private async Task ShowStockOrderTemplatesAsync()
  {
    await this.NavigationService.Navigate<ListViewModel<StockOrderTemplate>, string>(string.Empty);
  }

  public ICommand ShowAggregatedStockOrdersCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.ShowAggregatedStockOrdersAsync), (Func<bool>) (() => !this.IsBusy && this._authService.TryAuthorizeAction((Enum) TransactionActions.AggregatedStockOrders, (Enum) TransactionAccessLevel.ReadOwn)));
    }
  }

  private async Task ShowAggregatedStockOrdersAsync()
  {
    await this.NavigationService.Navigate<ListViewModel<AggregatedStockOrder>, string>(string.Empty);
  }

  public ICommand ShowFundsSlipsCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.ShowFundsSlipsAsync), (Func<bool>) (() => !this.IsBusy && this._authService.TryAuthorizeAnyAction(typeof (FundsSlipType), (Enum) TransactionAccessLevel.ReadOwn)));
    }
  }

  private async Task ShowFundsSlipsAsync()
  {
    await this.NavigationService.Navigate<ListViewModel<FundsSlip>, string>(string.Empty);
  }

  public ICommand ShowNewFundsSlipCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand<FundsSlipType>(new Func<FundsSlipType, Task>(this.ShowNewFundsSlipAsync), (Func<FundsSlipType, bool>) (x => !this.IsBusy && this._authService.TryAuthorizeAction((Enum) x, (Enum) TransactionAccessLevel.Create)));
    }
  }

  private async Task ShowNewFundsSlipAsync(FundsSlipType type)
  {
    await this.NavigationService.Navigate<FundsSlipDetailsViewModel, FundsSlipType>(type);
  }

  public ICommand ShowFundsTransfersCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.ShowFundsTransfersAsync), (Func<bool>) (() => !this.IsBusy && this._authService.TryAuthorizeAction((Enum) TransactionActions.FundsTransfers, (Enum) TransactionAccessLevel.ReadOwn)));
    }
  }

  private async Task ShowFundsTransfersAsync()
  {
    await this.NavigationService.Navigate<ListViewModel<FundsTransfer>, string>(string.Empty);
  }

  public ICommand ShowNewFundsTransferCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.ShowNewFundsTransferAsync), (Func<bool>) (() => !this.IsBusy && this._authService.TryAuthorizeAction((Enum) TransactionActions.FundsTransfers, (Enum) TransactionAccessLevel.Create)));
    }
  }

  private async Task ShowNewFundsTransferAsync()
  {
    await this.NavigationService.Navigate<DetailsViewModel<FundsTransfer>>();
  }

  public ICommand ShowExpenseSlipsCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.ShowExpenseSlipsAsync), (Func<bool>) (() => !this.IsBusy && this._authService.TryAuthorizeAction((Enum) TransactionActions.ExpenseSlips, (Enum) TransactionAccessLevel.ReadOwn)));
    }
  }

  private async Task ShowExpenseSlipsAsync()
  {
    await this.NavigationService.Navigate<ListViewModel<ExpenseSlip>, string>(string.Empty);
  }

  public ICommand ShowNewExpenseSlipCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.ShowNewExpenseSlipAsync), (Func<bool>) (() => !this.IsBusy && this._authService.TryAuthorizeAction((Enum) TransactionActions.StockTransfers, (Enum) TransactionAccessLevel.Create)));
    }
  }

  private async Task ShowNewExpenseSlipAsync()
  {
    await this.NavigationService.Navigate<DetailsViewModel<ExpenseSlip>>();
  }

  public ICommand ShowDailyFundsRegisteriesCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.ShowDailyFundsRegisteriesAsync), (Func<bool>) (() => !this.IsBusy && this._authService.TryAuthorizeAction((Enum) TransactionActions.DailyFundsRegisteries, (Enum) TransactionAccessLevel.ReadOwn)));
    }
  }

  private async Task ShowDailyFundsRegisteriesAsync()
  {
    await this.NavigationService.Navigate<ListViewModel<DailyFundsRegistery>, string>(string.Empty);
  }

  public ICommand ShowPartnerSlipsCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.ShowPartnerSlipsAsync), (Func<bool>) (() => !this.IsBusy && this._authService.TryAuthorizeAction((Enum) TransactionActions.PartnerSlips, (Enum) TransactionAccessLevel.ReadOwn)));
    }
  }

  private async Task ShowPartnerSlipsAsync()
  {
    await this.NavigationService.Navigate<ListViewModel<PartnerSlip>, string>(string.Empty);
  }

  public ICommand ShowNewPartnerSlipCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand<PartnerSlipType>(new Func<PartnerSlipType, Task>(this.ShowNewPartnerSlipAsync), (Func<PartnerSlipType, bool>) (x => !this.IsBusy && this._authService.TryAuthorizeAction((Enum) TransactionActions.PartnerSlips, (Enum) TransactionAccessLevel.Create)));
    }
  }

  private async Task ShowNewPartnerSlipAsync(PartnerSlipType type)
  {
    await this.NavigationService.Navigate<PartnerSlipDetailsViewModel, PartnerSlipType>(type);
  }

  public ICommand ShowPartnerBalanceTransfersCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.ShowPartnerBalanceTransfersAsync), (Func<bool>) (() => !this.IsBusy && this._authService.TryAuthorizeAction((Enum) TransactionActions.PartnerTransfers, (Enum) TransactionAccessLevel.ReadOwn)));
    }
  }

  private async Task ShowPartnerBalanceTransfersAsync()
  {
    await this.NavigationService.Navigate<ListViewModel<PartnerTransfer>, string>(string.Empty);
  }

  public ICommand ShowNewPartnerBalanceTransferCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.ShowNewPartnerBalanceTransferAsync), (Func<bool>) (() => !this.IsBusy && this._authService.TryAuthorizeAction((Enum) TransactionActions.PartnerTransfers, (Enum) TransactionAccessLevel.Create)));
    }
  }

  private async Task ShowNewPartnerBalanceTransferAsync()
  {
    await this.NavigationService.Navigate<PartnerTransferDetailsViewModel>();
  }

  public ICommand ShowInvoicesCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.ShowInvoicesAsync), (Func<bool>) (() => !this.IsBusy && this._authService.TryAuthorizeAnyAction(typeof (InvoiceType), (Enum) TransactionAccessLevel.ReadOwn)));
    }
  }

  private async Task ShowInvoicesAsync()
  {
    await this.NavigationService.Navigate<InvoicesListViewModel>();
  }

  public ICommand ShowNewInvoiceCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand<InvoiceType>(new Func<InvoiceType, Task>(this.ShowNewInvoiceAsync), (Func<InvoiceType, bool>) (x => !this.IsBusy && this._authService.TryAuthorizeAction((Enum) x, (Enum) TransactionAccessLevel.Create)));
    }
  }

  private async Task ShowNewInvoiceAsync(InvoiceType type)
  {
    await this.NavigationService.Navigate<InvoiceDetailsViewModel, InvoiceType>(type);
  }

  public ICommand ShowInvoicePaymentDataCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand<InvoiceType>(new Func<InvoiceType, Task>(this.ShowInvoicePaymentDataAsync), (Func<InvoiceType, bool>) (x => !this.IsBusy && this.AllowReporting && this._authService.TryAuthorizeAction((Enum) InvoiceType.Sales, (Enum) TransactionAccessLevel.ReadOwn)));
    }
  }

  private async Task ShowInvoicePaymentDataAsync(InvoiceType type)
  {
    await this.NavigationService.Navigate<InvoicesWithPaymentInfoListViewModel>();
  }

  public ICommand ShowBillsCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.ShowBillsAsync), (Func<bool>) (() => !this.IsBusy && this._authService.TryAuthorizeAnyAction(typeof (BillType), (Enum) TransactionAccessLevel.ReadOwn)));
    }
  }

  private async Task ShowBillsAsync()
  {
    await this.NavigationService.Navigate<ListViewModel<Bill>, string>(string.Empty);
  }

  public ICommand ShowNewBillCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand<BillType>(new Func<BillType, Task>(this.ShowNewBillAsync), (Func<BillType, bool>) (x => !this.IsBusy && this._authService.TryAuthorizeAction((Enum) x, (Enum) TransactionAccessLevel.Create)));
    }
  }

  private async Task ShowNewBillAsync(BillType type)
  {
    await this.NavigationService.Navigate<BillDetailsViewModel, BillType>(type);
  }

  public ICommand ShowAggregatedReportCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.OnShowAggregatedReportCommandAsync), (Func<bool>) (() => !this.IsBusy && this._authService.TryAuthorizeAction((Enum) Actions.AggregatedReport, (Enum) AccessLevel.Grant)));
    }
  }

  private async Task OnShowAggregatedReportCommandAsync()
  {
    await this.NavigationService.Navigate<AggregatedReportViewModel>();
  }

  public ICommand ShowRevenueReportCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.OnShowRevenueReportCommandAsync), (Func<bool>) (() => !this.IsBusy && this._authService.TryAuthorizeAction((Enum) Actions.RevenueReport, (Enum) AccessLevel.Grant)));
    }
  }

  private async Task OnShowRevenueReportCommandAsync()
  {
    await this.NavigationService.Navigate<RevenueReportViewModel>();
  }

  public ICommand ShowDataCopierCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.OnShowDataCopierCommandAsync), (Func<bool>) (() => !this.IsBusy));
    }
  }

  private async Task OnShowDataCopierCommandAsync()
  {
    await this.NavigationService.Navigate<CouchDataCopierViewModel>();
  }

  public ICommand ShowSyncDataFixerCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.OnSyncDataFixerCommandAsync), (Func<bool>) (() => !this.IsBusy));
    }
  }

  private async Task OnSyncDataFixerCommandAsync()
  {
    await this.NavigationService.Navigate<SyncDataFixerViewModel>();
  }
}

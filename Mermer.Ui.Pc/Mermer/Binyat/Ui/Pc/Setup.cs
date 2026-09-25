using Autofac;
using Autofac.Builder;
using Autofac.Core;
using Autofac.Core.Registration;
using Autofac.Extras.MvvmCross;
using Castle.DynamicProxy;
using Mermer.Authorization.Services;
using Mermer.Commerce.Services;
using Mermer.Common.Settings;
using Mermer.Licensing.Client;
using Mermer.Licensing.Client.Models;
using Mermer.Services;
using Mermer.Ui.Core;
using Mermer.Ui.Core.Pc.Tools;
using Mermer.Ui.Pc.Reports.Helpers;
using Mermer.Ui.Pc.Services;
using Microsoft.Extensions.Configuration;
using MvvmCross.Core.ViewModels;
using MvvmCross.Platform.IoC;
using MvvmCross.Platform.Platform;
using MvvmCross.Wpf.Platform;
using MvvmCross.Wpf.Views;
using MvvmCross.Wpf.Views.Presenters;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Windows.Threading;

namespace Mermer.Ui.Pc;

public class Setup : MvxWpfSetup
{
    private readonly IConfigurator _configurator;

    public Setup(Dispatcher dispatcher, IMvxWpfViewPresenter presenter)
        : base(dispatcher, presenter)
    {
        _configurator = new RegistryConfigurator("Software\\MermerCS\\Binyat");
        Configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", true, true)
            .Build();
    }

    public IConfigurationRoot Configuration { get; set; }

    protected override IMvxIoCProvider CreateIocProvider()
    {
        ContainerBuilder builder = new ContainerBuilder();
        Assembly assembly = typeof(Setup).Assembly;

        builder.Register<IConfigurator>(x => _configurator).As<IConfigurator>().SingleInstance();
        ConnectionSettings config = _configurator.GetConfig<ConnectionSettings>();

        builder.RegisterModule<CoreUiModule>();

        builder.RegisterModule(new MermerLicensingClientModule(new ActivationConfiguration
        {
            ActivationUrl = Configuration["ActivationUrl"] ?? Configuration["ApiUrl"] ?? "http://localhost:5050",
            PublicKey = Configuration.GetSection("PublicKey").AsString() ?? "dummy_key"
        }));

        builder.RegisterModule<Mermer.BinyatModule>();

        var mermerAssemblies = Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory, "Mermer*.dll")
            .Where(f => f.IndexOf("Synchronizer", StringComparison.OrdinalIgnoreCase) < 0)
            .Select(Assembly.LoadFrom)
            .ToArray();

        builder.RegisterAssemblyTypes(mermerAssemblies)
            .Where(t => t.IsClass && !t.IsAbstract
                        && t.Name != "PcJsonLocalizationResourceProvider"
                        && t.Name != "App"
                        && !typeof(ILoginService).IsAssignableFrom(t)
                        && !t.Name.EndsWith("ViewModel")
                        && !t.Name.EndsWith("View")
                        && !t.Name.EndsWith("Setup")
                        && !IsMvxSingletonDeep(t)
                        && !t.Name.Contains("Couch")
                        && !t.Name.Contains("Cluster")
                        && !t.Name.Contains("DocumentChangeListener"))
            .AsImplementedInterfaces()
            .InstancePerDependency();

        builder.Register(c =>
        {
            var configurator = c.Resolve<IConfigurator>();
            var connSettings = configurator.GetConfig<ConnectionSettings>();

            string serviceUrl = Configuration["ApiUrl"] ?? connSettings?.ServiceAddress ?? "http://127.0.0.1:5050";
            if (serviceUrl.Contains("localhost"))
            {
                serviceUrl = serviceUrl.Replace("localhost", "127.0.0.1");
            }
            serviceUrl = serviceUrl.TrimEnd('/') + "/";

            var handler = new HttpClientHandler { UseProxy = false, Proxy = null };
            return new HttpClient(handler)
            {
                BaseAddress = new Uri(serviceUrl),
                Timeout = TimeSpan.FromSeconds(15)
            };
        }).AsSelf().SingleInstance();

        builder.RegisterType<Mermer.Http.RestClient>().AsSelf().SingleInstance();

        LocalSqliteCache.InitializeDatabase();

        builder.RegisterType<DummyDocumentChangeListener>()
               .As<Mermer.Common.Services.IDocumentChangeListener>()
               .SingleInstance();

        builder.RegisterType<ApiUsersRepository>().As<Mermer.Data.Storage.IRepository<Mermer.Authorization.Models.User>>().SingleInstance();
        builder.RegisterType<ApiRolesRepository>().As<Mermer.Data.Storage.IRepository<Mermer.Authorization.Models.Role>>().SingleInstance();
        builder.RegisterType<ApiLoginService>().As<Mermer.Authorization.Services.ILoginService>().As<ILoginService>().AsSelf().SingleInstance();
        builder.RegisterType<ReportLayoutStorageService>().As<IReportLayoutStorageService>().SingleInstance();
        builder.RegisterType<LocalTransactionCodeGenerationService>().As<Mermer.Transactions.Services.ITransactionCodeGenerationService>().SingleInstance();
        builder.RegisterType<ApiPartnerCodeGenerator>().As<Mermer.CRM.Services.IPartnerCodeGenerationService>().SingleInstance();
        builder.RegisterType<ApiStockCodeGenerator>().As<Mermer.StockManagement.Services.IStockCodeGenerationService>().SingleInstance();

        builder.RegisterType<ApiWarehousesRepository>().As<Mermer.Data.Storage.IRepository<Mermer.Enterprise.Models.Warehouse>>().As<Mermer.Data.Storage.IReadOnlyRepository<Mermer.Enterprise.Models.Warehouse>>().SingleInstance();
        builder.RegisterType<ApiOfficesRepository>().As<Mermer.Data.Storage.IRepository<Mermer.Enterprise.Models.Office>>().As<Mermer.Data.Storage.IReadOnlyRepository<Mermer.Enterprise.Models.Office>>().SingleInstance();
        builder.RegisterType<ApiCurrenciesRepository>().As<Mermer.Data.Storage.IRepository<Mermer.FundsManagement.Models.Currency>>().As<Mermer.Data.Storage.IReadOnlyRepository<Mermer.FundsManagement.Models.Currency>>().SingleInstance();
        builder.RegisterType<ApiDepositoriesRepository>().As<Mermer.Data.Storage.IRepository<Mermer.Enterprise.Models.Depository>>().As<Mermer.Data.Storage.IReadOnlyRepository<Mermer.Enterprise.Models.Depository>>().As<Mermer.Data.Storage.IRepositoryWithFacets<Mermer.Enterprise.Models.Depository>>().SingleInstance();

        builder.RegisterType<ApiStocksRepository>().As<Mermer.Data.Storage.IRepository<Mermer.StockManagement.Models.Stock>>().As<Mermer.Data.Storage.IReadOnlyRepository<Mermer.StockManagement.Models.Stock>>().As<Mermer.Data.Storage.IRepositoryWithFacets<Mermer.StockManagement.Models.Stock>>().As<Mermer.StockManagement.Services.IStocksRepository>().SingleInstance();
        builder.RegisterType<ApiPartnersRepository>().As<Mermer.Data.Storage.IRepository<Mermer.CRM.Models.Partner>>().As<Mermer.Data.Storage.IReadOnlyRepository<Mermer.CRM.Models.Partner>>().As<Mermer.Data.Storage.IRepositoryWithFacets<Mermer.CRM.Models.Partner>>().As<Mermer.CRM.Services.IPartnersRepository>().SingleInstance();
        builder.RegisterType<ApiPartnerBalancesRepository>().AsImplementedInterfaces().SingleInstance();
        builder.RegisterType<ApiPartnerActionsRepository>().As<Mermer.CRM.Services.IPartnerActionsRepository>().SingleInstance();
        builder.RegisterType<ApiPartnerSlipsRepository>().As<Mermer.Data.Storage.IRepository<Mermer.CRM.Models.PartnerSlip>>().As<Mermer.Data.Storage.IReadOnlyRepository<Mermer.CRM.Models.PartnerSlip>>().As<Mermer.Data.Storage.IRepositoryWithFacets<Mermer.CRM.Models.PartnerSlip>>().SingleInstance();
        builder.RegisterType<ApiPartnerTransfersRepository>().As<Mermer.Data.Storage.IRepository<Mermer.CRM.Models.PartnerTransfer>>().As<Mermer.Data.Storage.IReadOnlyRepository<Mermer.CRM.Models.PartnerTransfer>>().As<Mermer.Data.Storage.IRepositoryWithFacets<Mermer.CRM.Models.PartnerTransfer>>().SingleInstance();

        builder.RegisterType<ApiInvoicesRepository>().As<Mermer.Data.Storage.IRepository<Mermer.Commerce.Models.Invoice>>().As<Mermer.Data.Storage.IReadOnlyRepository<Mermer.Commerce.Models.Invoice>>().As<Mermer.Commerce.Services.IInvoicesRepository>().SingleInstance();

        builder.RegisterType<ApiLastPurchasePricesRepository>()
       .As<ILastPurchasePricesRepository>()
       .SingleInstance();

        builder.RegisterType<ApiBillsRepository>().As<Mermer.Data.Storage.IRepository<Mermer.Commerce.Models.Bill>>().As<Mermer.Data.Storage.IReadOnlyRepository<Mermer.Commerce.Models.Bill>>().As<Mermer.Data.Storage.IRepositoryWithFacets<Mermer.Commerce.Models.Bill>>().SingleInstance();

        builder.RegisterType<ApiStockSlipsRepository>().As<Mermer.Data.Storage.IRepository<Mermer.Warehousing.Models.StockSlip>>().As<Mermer.Data.Storage.IReadOnlyRepository<Mermer.Warehousing.Models.StockSlip>>().As<Mermer.Data.Storage.IRepositoryWithFacets<Mermer.Warehousing.Models.StockSlip>>().SingleInstance();
        builder.RegisterType<ApiStockTransfersRepository>().As<Mermer.Data.Storage.IRepository<Mermer.Warehousing.Models.StockTransfer>>().As<Mermer.Data.Storage.IReadOnlyRepository<Mermer.Warehousing.Models.StockTransfer>>().SingleInstance();
        builder.RegisterType<ApiStockActionsRepository>().As<Mermer.StockManagement.Services.IStockActionsRepository>().SingleInstance();
        builder.RegisterType<ApiStockBalancesRepository>().As<Mermer.StockManagement.Services.IStockBalancesRepository>().SingleInstance();
        builder.RegisterType<ApiStockBalancesAggregatedRepository>().As<Mermer.StockManagement.Services.IStockBalancesAggregatedRepository>().SingleInstance();
        builder.RegisterType<ApiStockRevisionsRepository>().As<Mermer.Warehousing.Revisioning.Services.IStockRevisionsRepository>().As<Mermer.Data.Storage.IRepository<Mermer.Warehousing.Revisioning.Models.StockRevision>>().As<Mermer.Data.Storage.IReadOnlyRepository<Mermer.Warehousing.Revisioning.Models.StockRevision>>().As<Mermer.Data.Storage.IRepositoryWithFacets<Mermer.Warehousing.Revisioning.Models.StockRevision>>().SingleInstance();
        builder.RegisterType<ApiStockOrdersRepository>().As<Mermer.Data.Storage.IRepository<Mermer.Warehousing.Ordering.Models.StockOrder>>().As<Mermer.Data.Storage.IReadOnlyRepository<Mermer.Warehousing.Ordering.Models.StockOrder>>().As<Mermer.Data.Storage.IRepositoryWithFacets<Mermer.Warehousing.Ordering.Models.StockOrder>>().SingleInstance();
        builder.RegisterType<ApiAggregatedStockOrdersRepository>().As<Mermer.Data.Storage.IRepository<Mermer.Warehousing.Ordering.Models.AggregatedStockOrder>>().As<Mermer.Data.Storage.IReadOnlyRepository<Mermer.Warehousing.Ordering.Models.AggregatedStockOrder>>().As<Mermer.Data.Storage.IRepositoryWithFacets<Mermer.Warehousing.Ordering.Models.AggregatedStockOrder>>().SingleInstance();
        builder.RegisterType<ApiStockOrderTemplatesRepository>().As<Mermer.Data.Storage.IRepository<Mermer.Warehousing.Ordering.Models.StockOrderTemplate>>().As<Mermer.Data.Storage.IReadOnlyRepository<Mermer.Warehousing.Ordering.Models.StockOrderTemplate>>().As<Mermer.Data.Storage.IRepositoryWithFacets<Mermer.Warehousing.Ordering.Models.StockOrderTemplate>>().SingleInstance();
        builder.RegisterType<ApiStockOrderActionsRepository>().As<Mermer.Warehousing.Ordering.Services.IStockOrderActionsRepository>().SingleInstance();
        builder.RegisterType<ApiStockNameComposersRepository>().As<Mermer.Data.Storage.IRepository<Mermer.StockManagement.Models.StockNameComposer>>().As<Mermer.Data.Storage.IReadOnlyRepository<Mermer.StockManagement.Models.StockNameComposer>>().SingleInstance();
        builder.RegisterType<ApiStockAlternativesRepository>().As<Mermer.Data.Storage.IRepository<Mermer.StockManagement.Models.StockAlternative>>().As<Mermer.Data.Storage.IReadOnlyRepository<Mermer.StockManagement.Models.StockAlternative>>().As<Mermer.StockManagement.Services.IStockAlternativesRepository>().SingleInstance();
        builder.RegisterType<ApiStockTurnoverDataRepository>().As<Mermer.StockManagement.Services.IStockTurnoverDataRepository>().SingleInstance();
        builder.RegisterType<ApiStockRepriceEffectsRepository>().As<Mermer.StockManagement.Services.IStockRepriceEffectsRepository>().SingleInstance();

        builder.RegisterType<ApiFundsActionRepository>().As<Mermer.FundsManagement.Services.IFundsActionsRepository>().SingleInstance();
        builder.RegisterType<ApiFundsSlipsRepository>().As<Mermer.Data.Storage.IRepository<Mermer.Finance.Models.FundsSlip>>().As<Mermer.Data.Storage.IReadOnlyRepository<Mermer.Finance.Models.FundsSlip>>().As<Mermer.Data.Storage.IRepositoryWithFacets<Mermer.Finance.Models.FundsSlip>>().SingleInstance();
        builder.RegisterType<ApiFundsTransfersRepository>().As<Mermer.Data.Storage.IRepository<Mermer.Finance.Models.FundsTransfer>>().As<Mermer.Data.Storage.IReadOnlyRepository<Mermer.Finance.Models.FundsTransfer>>().As<Mermer.Data.Storage.IRepositoryWithFacets<Mermer.Finance.Models.FundsTransfer>>().SingleInstance();
        builder.RegisterType<ApiFundsBalancesRepository>().As<Mermer.FundsManagement.Services.IFundsBalancesRepository>().SingleInstance();

        builder.RegisterType<ApiExpensesRepository>().As<Mermer.Data.Storage.IRepository<Mermer.Finance.Spending.Models.Expense>>().As<Mermer.Data.Storage.IReadOnlyRepository<Mermer.Finance.Spending.Models.Expense>>().As<Mermer.Data.Storage.IRepositoryWithFacets<Mermer.Finance.Spending.Models.Expense>>().SingleInstance();
        builder.RegisterType<ApiExpenseSlipsRepository>().As<Mermer.Data.Storage.IRepository<Mermer.Finance.Spending.Models.ExpenseSlip>>().As<Mermer.Data.Storage.IReadOnlyRepository<Mermer.Finance.Spending.Models.ExpenseSlip>>().As<Mermer.Data.Storage.IRepositoryWithFacets<Mermer.Finance.Spending.Models.ExpenseSlip>>().SingleInstance();
        builder.RegisterType<ApiExpenseActionsRepository>().As<Mermer.Finance.Spending.Services.IExpenseActionsRepository>().SingleInstance();

        builder.RegisterType<ApiDailyFundsRegisteriesRepository>().As<Mermer.Data.Storage.IRepository<Mermer.Finance.DailyRegistery.Models.DailyFundsRegistery>>().As<Mermer.Data.Storage.IReadOnlyRepository<Mermer.Finance.DailyRegistery.Models.DailyFundsRegistery>>().As<Mermer.Finance.DailyRegistery.Services.IDailyFundsRegisteriesRepository>().As<Mermer.Data.Storage.IRepositoryWithFacets<Mermer.Finance.DailyRegistery.Models.DailyFundsRegistery>>().SingleInstance();
        builder.RegisterType<ApiAggregatedReportsRepository>().As<Mermer.Reporting.Services.IAggregatedReportsRepository>().SingleInstance();
        builder.RegisterType<ApiRevenueReportsRepository>().As<Mermer.Reporting.Services.IRevenueReportsRepository>().SingleInstance();

        builder.RegisterType<FluentValidation.Resources.LanguageManager>().As<FluentValidation.Resources.ILanguageManager>().SingleInstance();
        builder.RegisterAssemblyTypes(GetType().Assembly).Where(t => t.Name.EndsWith("Service")).AsImplementedInterfaces();
        builder.RegisterType<NameHelper>().AsSelf().InstancePerDependency();
        builder.RegisterAssemblyTypes(assembly).Where(x => x.Name.EndsWith("Mapper")).AsSelf().InstancePerDependency();
        builder.RegisterModule<AutoMapperModule>();

        // Оставляем только оригинальный источник для локализации
        builder.RegisterSource(new OldLocalizationSource());

        var container = builder.Build();

        var existingIoC = MvvmCross.Platform.Core.MvxSingleton<IMvxIoCProvider>.Instance;
        if (existingIoC != null)
        {
            if (existingIoC is IDisposable disposableIoc) disposableIoc.Dispose();
            var field = typeof(MvvmCross.Platform.Core.MvxSingleton<IMvxIoCProvider>).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
            if (field != null) field.SetValue(null, null);
        }

        return (IMvxIoCProvider)new AutofacMvxIocProvider(container);
    }

    private static bool IsMvxSingletonDeep(Type type)
    {
        Type current = type;
        while (current != null)
        {
            if (current.Name.Contains("MvxSingleton") || current.Name.Contains("MvxApplication"))
                return true;
            current = current.BaseType;
        }
        return false;
    }

    protected override void InitializeDebugServices()
    {
        if (MvvmCross.Platform.Platform.MvxTrace.Instance != null)
        {
            return;
        }
        base.InitializeDebugServices();
    }

    public override void Initialize()
    {
        base.Initialize();

        string cultureName = "ru-RU";
        string shortLocale = "ru";

        try
        {
            var configurator = MvvmCross.Platform.Mvx.Resolve<Mermer.Services.IConfigurator>();
            var config = configurator.GetConfig<Mermer.Common.Settings.AppSettings>();

            if (config != null && !string.IsNullOrEmpty(config.Culture))
            {
                cultureName = config.Culture;
                shortLocale = cultureName.Length >= 2 ? cultureName.Substring(0, 2).ToLowerInvariant() : "en";
            }
        }
        catch { }

        System.Globalization.CultureInfo culture;
        try { culture = new System.Globalization.CultureInfo(cultureName); }
        catch { culture = new System.Globalization.CultureInfo("ru-RU"); }

        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = culture;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = culture;
        System.Threading.Thread.CurrentThread.CurrentCulture = culture;
        System.Threading.Thread.CurrentThread.CurrentUICulture = culture;

        string locPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Localization");
        if (System.IO.Directory.Exists(locPath))
        {
            Mermer.Mvvm.Tools.LocalizationManager.Instance.Initialize(locPath, "en", "en");
            Mermer.Mvvm.Tools.LocalizationManager.Instance.CurrentLocale = shortLocale;
        }
    }

    protected override void InitializeSingletonCache()
    {
        try { base.InitializeSingletonCache(); } catch (MvvmCross.Platform.Exceptions.MvxException) { }
    }

    protected override IMvxWpfViewsContainer CreateWpfViewsContainer() => new ViewsContainer();
    protected override IMvxApplication CreateApp() => new Mermer.Ui.Core.App();
    protected override IMvxTrace CreateDebugTrace() => new DebugTrace();
}

public class DummyDocumentChangeListener : Mermer.Common.Services.IDocumentChangeListener
{
    public event EventHandler DocumentChanged { add { } remove { } }
    public bool Started => false;
    public void Start() { }
    public void Stop() { }
    public void Touch() { }
}

public class OldLocalizationSource : IRegistrationSource
{
    public bool IsAdapterForIndividualComponents => false;

    public IEnumerable<IComponentRegistration> RegistrationsFor(Service service, Func<Service, IEnumerable<IComponentRegistration>> registrationAccessor)
    {
        if (service is TypedService typedService && typedService.ServiceType.FullName == "Payhas.Binyat.Common.Services.ILocalizationService")
        {
            yield return RegistrationBuilder.ForDelegate((c, p) =>
            {
                var proxyGen = new ProxyGenerator();
                return proxyGen.CreateInterfaceProxyWithoutTarget(typedService.ServiceType, new DummyInterceptor());
            }).As(typedService.ServiceType).CreateRegistration();
        }
    }
}

public class DummyInterceptor : IInterceptor
{
    public void Intercept(IInvocation invocation)
    {
        if (invocation.Method.ReturnType == typeof(string))
            invocation.ReturnValue = "";
        else if (invocation.Method.ReturnType.IsValueType)
            invocation.ReturnValue = Activator.CreateInstance(invocation.Method.ReturnType);
        else
            invocation.ReturnValue = null;
    }
}
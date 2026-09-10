using Autofac;
using Autofac.Builder;
using Autofac.Core;
using Autofac.Core.Registration;
using Autofac.Extras.MvvmCross;
using Castle.DynamicProxy;
using Mermer.Authorization.Services;
using Mermer.Common.Settings;
using Mermer.Core.Couch;
using Mermer.Finance.Models;
using Mermer.Licensing.Client;
using Mermer.Licensing.Client.Models;
using Mermer.Mvvm.Tools;
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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
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

        // Добавляем Licensing
        builder.RegisterModule(new MermerLicensingClientModule(new ActivationConfiguration
        {
            ActivationUrl = Configuration["ActivationUrl"] ?? "http://localhost:5000",
            PublicKey = Configuration.GetSection("PublicKey").AsString() ?? "dummy_key"
        }));

        // =====================================================================
        // ШАГ 1: ЗАГРУЖАЕМ СТАРУЮ ЛОГИКУ ПЕРВОЙ
        // =====================================================================
        builder.RegisterModule<Mermer.BinyatModule>();

        var mermerAssemblies = Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory, "Mermer*.dll")
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
                        && !(t.Name.Contains("Couch") && t.Name.EndsWith("Repository")))
            .AsImplementedInterfaces()
            .InstancePerDependency();

        // =====================================================================
        // ШАГ 2: "КОВРОВАЯ БОМБАРДИРОВКА" COUCHBASE ЧЕРЕЗ РЕФЛЕКСИЮ
        // =====================================================================
        var proxy = new Castle.DynamicProxy.ProxyGenerator();
        var interceptor = new DummyInterceptor();

        var allModels = mermerAssemblies
            .SelectMany(a => {
                try { return a.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null); }
            })
            .Where(t => t.IsClass && !t.IsAbstract && !t.IsGenericTypeDefinition && typeof(Mermer.Data.Models.IModel).IsAssignableFrom(t));

        foreach (var modelType in allModels)
        {
            var dummyRepoType = typeof(Mermer.Ui.Pc.Services.DummyRepository<>).MakeGenericType(modelType);
            var iRepoType = typeof(Mermer.Data.Storage.IRepository<>).MakeGenericType(modelType);
            var iReadOnlyRepoType = typeof(Mermer.Data.Storage.IReadOnlyRepository<>).MakeGenericType(modelType);

            builder.RegisterType(dummyRepoType).As(iRepoType).As(iReadOnlyRepoType).SingleInstance();

            var listAuthType = typeof(Mermer.Data.Authorizers.IListAuthorizer<>).MakeGenericType(modelType);
            builder.Register(c => proxy.CreateInterfaceProxyWithoutTarget(listAuthType, interceptor))
                   .As(listAuthType).SingleInstance();

            var readOnlyListAuthType = typeof(Mermer.Data.Authorizers.IReadOnlyListAuthorizer<>).MakeGenericType(modelType);
            builder.Register(c => proxy.CreateInterfaceProxyWithoutTarget(readOnlyListAuthType, interceptor))
                   .As(readOnlyListAuthType).SingleInstance();
        }

        builder.Register(c => proxy.CreateInterfaceProxyWithoutTarget(typeof(Mermer.Data.Authorizers.IAuthorizer), interceptor))
               .As(typeof(Mermer.Data.Authorizers.IAuthorizer)).SingleInstance();


        // =====================================================================
        // ШАГ 3: ПОВЕРХ ЗАГЛУШЕК СТАВИМ НАШИ НАСТОЯЩИЕ REST API КЛАССЫ
        // =====================================================================

        builder.Register(c =>
        {
            var configurator = c.Resolve<IConfigurator>();
            var connSettings = configurator.GetConfig<ConnectionSettings>();

            // Сначала смотрим appsettings.json, если не задан — берем из реестра, иначе localhost:5000
            string serviceUrl = Configuration["ApiUrl"];

            if (string.IsNullOrWhiteSpace(serviceUrl))
            {
                serviceUrl = connSettings?.ServiceAddress;
            }

            if (string.IsNullOrWhiteSpace(serviceUrl))
            {
                serviceUrl = "http://localhost:5000";
            }

            // Убираем случайные слэши на конце для чистоты URI
            serviceUrl = serviceUrl.TrimEnd('/') + "/";

            Console.WriteLine($"[CLIENT HTTP SETUP] Connecting to: {serviceUrl}");
            System.Diagnostics.Debug.WriteLine($"[CLIENT HTTP SETUP] Connecting to: {serviceUrl}");

            var client = new System.Net.Http.HttpClient
            {
                BaseAddress = new Uri(serviceUrl),
                Timeout = TimeSpan.FromSeconds(15)
            };

            return client;
        }).AsSelf().SingleInstance();

        builder.RegisterType<Mermer.Http.RestClient>().AsSelf().SingleInstance();
        builder.RegisterType<Mermer.Ui.Pc.Services.ApiLoginService>().As<ILoginService>().SingleInstance();

        builder.RegisterType<Mermer.Ui.Pc.Services.ApiUsersRepository>()
               .As<Mermer.Data.Storage.IRepository<Mermer.Authorization.Models.User>>()
               .SingleInstance();

        builder.RegisterType<Mermer.Ui.Pc.Services.ApiRolesRepository>()
               .As<Mermer.Data.Storage.IRepository<Mermer.Authorization.Models.Role>>()
               .SingleInstance();

        builder.RegisterType<Mermer.Ui.Pc.Services.ApiLoginService>()
               .As<Mermer.Authorization.Services.ILoginService>()
               .SingleInstance();


        // --- ЛОКАЛЬНЫЙ ГЕНЕРАТОР КОДОВ (ОТКЛЮЧАЕТ COUCHBASE ДЛЯ ВСЕХ ФОРМ) ---
        builder.RegisterType<Mermer.Ui.Pc.Services.LocalTransactionCodeGenerationService>()
               .As<Mermer.Transactions.Services.ITransactionCodeGenerationService>()
               .SingleInstance();

        builder.RegisterType<Mermer.Ui.Pc.Services.ApiPartnerCodeGenerator>()
               .As<Mermer.CRM.Services.IPartnerCodeGenerationService>()
               .SingleInstance();

        builder.RegisterType<Mermer.Ui.Pc.Services.ApiStockCodeGenerator>()
               .As<Mermer.StockManagement.Services.IStockCodeGenerationService>()
               .SingleInstance();

        // --- СПРАВОЧНИКИ ---
        builder.RegisterType<Mermer.Ui.Pc.Services.ApiWarehousesRepository>()
               .As<Mermer.Data.Storage.IRepository<Mermer.Enterprise.Models.Warehouse>>()
               .As<Mermer.Data.Storage.IReadOnlyRepository<Mermer.Enterprise.Models.Warehouse>>().SingleInstance();

        builder.RegisterType<Mermer.Ui.Pc.Services.ApiOfficesRepository>()
               .As<Mermer.Data.Storage.IRepository<Mermer.Enterprise.Models.Office>>()
               .As<Mermer.Data.Storage.IReadOnlyRepository<Mermer.Enterprise.Models.Office>>().SingleInstance();

        builder.RegisterType<Mermer.Ui.Pc.Services.ApiCurrenciesRepository>()
               .As<Mermer.Data.Storage.IRepository<Mermer.FundsManagement.Models.Currency>>()
               .As<Mermer.Data.Storage.IReadOnlyRepository<Mermer.FundsManagement.Models.Currency>>().SingleInstance();

        builder.RegisterType<Mermer.Ui.Pc.Services.ApiStocksRepository>()
               .As<Mermer.Data.Storage.IRepository<Mermer.StockManagement.Models.Stock>>()
               .As<Mermer.Data.Storage.IReadOnlyRepository<Mermer.StockManagement.Models.Stock>>()
               .As<Mermer.Data.Storage.IRepositoryWithFacets<Mermer.StockManagement.Models.Stock>>()
               .As<Mermer.StockManagement.Services.IStocksRepository>()
               .SingleInstance();

        builder.RegisterType<Mermer.Ui.Pc.Services.ApiPartnersRepository>()
               .As<Mermer.Data.Storage.IRepository<Mermer.CRM.Models.Partner>>()
               .As<Mermer.Data.Storage.IReadOnlyRepository<Mermer.CRM.Models.Partner>>()
               .As<Mermer.Data.Storage.IRepositoryWithFacets<Mermer.CRM.Models.Partner>>()
               .SingleInstance();

        builder.RegisterType<Mermer.Ui.Pc.Services.ApiDepositoriesRepository>()
                .As<Mermer.Data.Storage.IRepository<Mermer.Enterprise.Models.Depository>>()
                .As<Mermer.Data.Storage.IReadOnlyRepository<Mermer.Enterprise.Models.Depository>>()
                .As<Mermer.Data.Storage.IRepositoryWithFacets<Mermer.Enterprise.Models.Depository>>() // <-- ДОБАВЛЕНО!
                .SingleInstance();

        builder.RegisterType<Mermer.Ui.Pc.Services.ApiStockSlipsRepository>()
                .As<Mermer.Data.Storage.IRepository<Mermer.Warehousing.Models.StockSlip>>()
                .As<Mermer.Data.Storage.IReadOnlyRepository<Mermer.Warehousing.Models.StockSlip>>()
                .As<Mermer.Data.Storage.IRepositoryWithFacets<Mermer.Warehousing.Models.StockSlip>>()
                .SingleInstance();

        // --- ФИНАНСЫ И ДОКУМЕНТЫ ---
        builder.RegisterType<Mermer.Ui.Pc.Services.ApiFundsActionRepository>()
               .As<Mermer.FundsManagement.Services.IFundsActionsRepository>()
               .SingleInstance();

        // Репозиторий кассовых ордеров (FundsSlip)
        builder.RegisterType<Mermer.Ui.Pc.Services.ApiFundsSlipsRepository>()
               .As<Mermer.Data.Storage.IRepository<Mermer.Finance.Models.FundsSlip>>()
               .As<Mermer.Data.Storage.IReadOnlyRepository<Mermer.Finance.Models.FundsSlip>>()
               .As<Mermer.Data.Storage.IRepositoryWithFacets<Mermer.Finance.Models.FundsSlip>>()
               .SingleInstance();

        builder.RegisterType<Mermer.Ui.Pc.Services.ApiInvoicesRepository>()
               .As<Mermer.Data.Storage.IRepository<Mermer.Commerce.Models.Invoice>>()
               .As<Mermer.Data.Storage.IReadOnlyRepository<Mermer.Commerce.Models.Invoice>>()
               .As<Mermer.Commerce.Services.IInvoicesRepository>()
               .SingleInstance();

        builder.RegisterType<Mermer.Ui.Pc.Services.ApiPartnerSlipsRepository>()
               .As<Mermer.Data.Storage.IRepository<Mermer.CRM.Models.PartnerSlip>>()
               .As<Mermer.Data.Storage.IReadOnlyRepository<Mermer.CRM.Models.PartnerSlip>>()
               .As<Mermer.Data.Storage.IRepositoryWithFacets<Mermer.CRM.Models.PartnerSlip>>()
               .SingleInstance();

        builder.RegisterType<Mermer.Ui.Pc.Services.ApiBillsRepository>()
               .As<Mermer.Data.Storage.IRepository<Mermer.Commerce.Models.Bill>>()
               .As<Mermer.Data.Storage.IReadOnlyRepository<Mermer.Commerce.Models.Bill>>()
               .As<Mermer.Data.Storage.IRepositoryWithFacets<Mermer.Commerce.Models.Bill>>()
               .SingleInstance();

        builder.RegisterType<Mermer.Ui.Pc.Services.ApiExpensesRepository>()
               .As<Mermer.Data.Storage.IRepository<Mermer.Finance.Spending.Models.Expense>>()
               .As<Mermer.Data.Storage.IReadOnlyRepository<Mermer.Finance.Spending.Models.Expense>>()
               .As<Mermer.Data.Storage.IRepositoryWithFacets<Mermer.Finance.Spending.Models.Expense>>()
               .SingleInstance();

        builder.RegisterType<Mermer.Ui.Pc.Services.ApiFundsTransfersRepository>()
               .As<Mermer.Data.Storage.IRepository<Mermer.Finance.Models.FundsTransfer>>()
               .As<Mermer.Data.Storage.IReadOnlyRepository<Mermer.Finance.Models.FundsTransfer>>()
               .As<Mermer.Data.Storage.IRepositoryWithFacets<Mermer.Finance.Models.FundsTransfer>>()
               .SingleInstance();

        builder.RegisterType<Mermer.Ui.Pc.Services.ApiExpenseSlipsRepository>()
               .As<Mermer.Data.Storage.IRepository<Mermer.Finance.Spending.Models.ExpenseSlip>>()
               .As<Mermer.Data.Storage.IReadOnlyRepository<Mermer.Finance.Spending.Models.ExpenseSlip>>()
               .As<Mermer.Data.Storage.IRepositoryWithFacets<Mermer.Finance.Spending.Models.ExpenseSlip>>()
               .SingleInstance();

        builder.RegisterType<Mermer.Ui.Pc.Services.ApiDailyFundsRegisteriesRepository>()
               .As<Mermer.Data.Storage.IRepository<Mermer.Finance.DailyRegistery.Models.DailyFundsRegistery>>()
               .As<Mermer.Data.Storage.IReadOnlyRepository<Mermer.Finance.DailyRegistery.Models.DailyFundsRegistery>>()
               .As<Mermer.Finance.DailyRegistery.Services.IDailyFundsRegisteriesRepository>()
               .As<Mermer.Data.Storage.IRepositoryWithFacets<Mermer.Finance.DailyRegistery.Models.DailyFundsRegistery>>()
               .SingleInstance();

        // Репозиторий Балансов касс (Сводка)
        builder.RegisterType<Mermer.Ui.Pc.Services.ApiFundsBalancesRepository>()
               .As<Mermer.FundsManagement.Services.IFundsBalancesRepository>()
               .SingleInstance();

        // Журнал детализации расходов (Expense Actions)
        builder.RegisterType<Mermer.Ui.Pc.Services.ApiExpenseActionsRepository>()
               .As<Mermer.Finance.Spending.Services.IExpenseActionsRepository>()
               .SingleInstance();

        // ДОБАВЛЯЕМ НОВЫЙ РЕПОЗИТОРИЙ ДЛЯ ПЕРЕВОДОВ ПАРТНЕРОВ!
        builder.RegisterType<Mermer.Ui.Pc.Services.ApiPartnerTransfersRepository>()
               .As<Mermer.Data.Storage.IRepository<Mermer.CRM.Models.PartnerTransfer>>()
               .As<Mermer.Data.Storage.IReadOnlyRepository<Mermer.CRM.Models.PartnerTransfer>>()
               .As<Mermer.Data.Storage.IRepositoryWithFacets<Mermer.CRM.Models.PartnerTransfer>>()
               .SingleInstance();

        builder.RegisterType<Mermer.Ui.Pc.Services.ApiPartnerActionsRepository>()
               .As<Mermer.CRM.Services.IPartnerActionsRepository>()
               .SingleInstance();

        builder.RegisterType<Mermer.Ui.Pc.Services.ApiFundsSlipsRepository>()
               .As<Mermer.Data.Storage.IRepository<Mermer.Finance.Models.FundsSlip>>()
               .As<Mermer.Data.Storage.IReadOnlyRepository<Mermer.Finance.Models.FundsSlip>>()
               .As<Mermer.Data.Storage.IRepositoryWithFacets<Mermer.Finance.Models.FundsSlip>>()
               .SingleInstance();

        // =====================================================================
        // ШАГ 4: ФИНАЛЬНЫЕ УТИЛИТЫ И ПЕРЕХВАТЧИК ИНТЕРФЕЙСОВ
        // =====================================================================

        builder.RegisterType<FluentValidation.Resources.LanguageManager>().As<FluentValidation.Resources.ILanguageManager>().SingleInstance();
        builder.RegisterAssemblyTypes(GetType().Assembly).Where(t => t.Name.EndsWith("Service")).AsImplementedInterfaces();
        builder.RegisterType<NameHelper>().AsSelf().InstancePerDependency();
        builder.RegisterAssemblyTypes(assembly).Where(x => x.Name.EndsWith("Mapper")).AsSelf().InstancePerDependency();
        builder.RegisterModule<AutoMapperModule>();
        builder.RegisterSource(new DummyInterfaceSource());
        Mermer.Ui.Pc.Services.LocalSqliteCache.InitializeDatabase();


        builder.RegisterType<Mermer.Ui.Pc.Services.ApiPartnerBalancesRepository>()
       .AsImplementedInterfaces()
       .SingleInstance();

        builder.RegisterType<Mermer.Ui.Pc.Services.ApiStockTransfersRepository>()
       .As<Mermer.Data.Storage.IRepository<Mermer.Warehousing.Models.StockTransfer>>()
       .As<Mermer.Data.Storage.IReadOnlyRepository<Mermer.Warehousing.Models.StockTransfer>>()
       .SingleInstance();

        // Журнал движения товаров (Stock Actions)
        builder.RegisterType<Mermer.Ui.Pc.Services.ApiStockActionsRepository>()
               .As<Mermer.StockManagement.Services.IStockActionsRepository>()
               .SingleInstance();

        builder.RegisterType<Mermer.Ui.Pc.Services.ApiStockBalancesRepository>()
               .As<Mermer.StockManagement.Services.IStockBalancesRepository>()
               .SingleInstance();

        builder.RegisterType<Mermer.Ui.Pc.Services.ApiStockBalancesAggregatedRepository>()
               .As<Mermer.StockManagement.Services.IStockBalancesAggregatedRepository>()
               .SingleInstance();

        // Инвентаризация (Stock Revisions)
        builder.RegisterType<Mermer.Ui.Pc.Services.ApiStockRevisionsRepository>()
               .As<Mermer.Warehousing.Revisioning.Services.IStockRevisionsRepository>()
               .As<Mermer.Data.Storage.IRepository<Mermer.Warehousing.Revisioning.Models.StockRevision>>()
               .As<Mermer.Data.Storage.IReadOnlyRepository<Mermer.Warehousing.Revisioning.Models.StockRevision>>()
               .As<Mermer.Data.Storage.IRepositoryWithFacets<Mermer.Warehousing.Revisioning.Models.StockRevision>>()
               .SingleInstance();

        // Заказы складов (Stock Orders)
        builder.RegisterType<Mermer.Ui.Pc.Services.ApiStockOrdersRepository>()
               .As<Mermer.Data.Storage.IRepository<Mermer.Warehousing.Ordering.Models.StockOrder>>()
               .As<Mermer.Data.Storage.IReadOnlyRepository<Mermer.Warehousing.Ordering.Models.StockOrder>>()
               .As<Mermer.Data.Storage.IRepositoryWithFacets<Mermer.Warehousing.Ordering.Models.StockOrder>>()
               .SingleInstance();

        builder.RegisterType<Mermer.Ui.Pc.Services.ApiAggregatedStockOrdersRepository>()
               .As<Mermer.Data.Storage.IRepository<Mermer.Warehousing.Ordering.Models.AggregatedStockOrder>>()
               .As<Mermer.Data.Storage.IReadOnlyRepository<Mermer.Warehousing.Ordering.Models.AggregatedStockOrder>>()
               .As<Mermer.Data.Storage.IRepositoryWithFacets<Mermer.Warehousing.Ordering.Models.AggregatedStockOrder>>()
               .SingleInstance();

        builder.RegisterType<Mermer.Ui.Pc.Services.ApiStockOrderTemplatesRepository>()
               .As<Mermer.Data.Storage.IRepository<Mermer.Warehousing.Ordering.Models.StockOrderTemplate>>()
               .As<Mermer.Data.Storage.IReadOnlyRepository<Mermer.Warehousing.Ordering.Models.StockOrderTemplate>>()
               .As<Mermer.Data.Storage.IRepositoryWithFacets<Mermer.Warehousing.Ordering.Models.StockOrderTemplate>>()
               .SingleInstance();

        builder.RegisterType<Mermer.Ui.Pc.Services.ApiStockOrderActionsRepository>()
               .As<Mermer.Warehousing.Ordering.Services.IStockOrderActionsRepository>()
               .SingleInstance();

        builder.RegisterType<Mermer.Ui.Pc.Services.ApiStockNameComposersRepository>()
               .As<Mermer.Data.Storage.IRepository<Mermer.StockManagement.Models.StockNameComposer>>()
               .As<Mermer.Data.Storage.IReadOnlyRepository<Mermer.StockManagement.Models.StockNameComposer>>()
               .SingleInstance();

        builder.RegisterType<Mermer.Ui.Pc.Services.ApiStockAlternativesRepository>()
               .As<Mermer.Data.Storage.IRepository<Mermer.StockManagement.Models.StockAlternative>>()
               .As<Mermer.Data.Storage.IReadOnlyRepository<Mermer.StockManagement.Models.StockAlternative>>()
               .As<Mermer.StockManagement.Services.IStockAlternativesRepository>()
               .SingleInstance();

        builder.RegisterType<Mermer.Ui.Pc.Services.ApiStockTurnoverDataRepository>()
               .As<Mermer.StockManagement.Services.IStockTurnoverDataRepository>()
               .SingleInstance();

        builder.RegisterType<Mermer.Ui.Pc.Services.ApiStockRepriceEffectsRepository>()
               .As<Mermer.StockManagement.Services.IStockRepriceEffectsRepository>()
               .SingleInstance();

        builder.RegisterType<Mermer.Ui.Pc.Services.ApiAggregatedReportsRepository>()
               .As<Mermer.Reporting.Services.IAggregatedReportsRepository>()
               .SingleInstance();

        builder.RegisterType<Mermer.Ui.Pc.Services.ApiRevenueReportsRepository>()
               .As<Mermer.Reporting.Services.IRevenueReportsRepository>()
               .SingleInstance();

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
        try
        {
            culture = new System.Globalization.CultureInfo(cultureName);
        }
        catch
        {
            culture = new System.Globalization.CultureInfo("ru-RU");
        }

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

// --- КЛАССЫ ДЛЯ ФЕЙКОВОГО СЕРВИСА (ДИНАМИЧЕСКИЕ ЗАГЛУШКИ) ---

public class DummyInterfaceSource : IRegistrationSource
{
    public bool IsAdapterForIndividualComponents => false;

    public IEnumerable<IComponentRegistration> RegistrationsFor(Service service, Func<Service, IEnumerable<IComponentRegistration>> registrationAccessor)
    {
        if (service is TypedService typedService && typedService.ServiceType.IsInterface)
        {
            var ns = typedService.ServiceType.Namespace ?? "";
            var name = typedService.ServiceType.Name;

            // Перехватываем все Couchbase-зависимости, из-за которых падали формы
            if (name == "ILocalizationService" ||
                name == "IInvoicesRepository" ||
                name == "ITransactionCodeGenerationService" ||
                name == "IStocksRepository" ||
                ns.Contains("Authorizers") ||
                ns.Contains("Couch"))
            {
                yield return RegistrationBuilder.ForDelegate((c, p) =>
                {
                    var proxyGen = new Castle.DynamicProxy.ProxyGenerator();
                    return proxyGen.CreateInterfaceProxyWithoutTarget(typedService.ServiceType, new DummyInterceptor());
                }).As(typedService.ServiceType).CreateRegistration();
            }
        }
    }
}

public class DummyInterceptor : Castle.DynamicProxy.IInterceptor
{
    public void Intercept(Castle.DynamicProxy.IInvocation invocation)
    {
        var methodName = invocation.Method.Name;
        var returnType = invocation.Method.ReturnType;

        // --- 1. ПЕРЕХВАТ МЕТОДОВ ПРОВЕРКИ ПРАВ И АВТОРИЗАЦИИ ---
        if (methodName.StartsWith("Can") || methodName.StartsWith("Check") || methodName.StartsWith("Has") || methodName.StartsWith("Is"))
        {
            if (returnType == typeof(bool))
            {
                invocation.ReturnValue = true;
                return;
            }
            if (returnType == typeof(Task<bool>))
            {
                invocation.ReturnValue = Task.FromResult(true);
                return;
            }
        }

        // --- 2. ГЕНЕРАЦИЯ КОДОВ ИСПРАВЛЕНА ---
        if (methodName == "GenerateCodeAsync" || methodName == "GetNextCode")
        {
            invocation.ReturnValue = Task.FromResult($"DOC-{DateTime.Now:yyMMddHHmmss}");
            return;
        }

        // --- 3. БАЛАНСЫ ПАРТНЕРОВ ---
        if (methodName == "GetBalanceToDateAsync")
        {
            invocation.ReturnValue = Task.FromResult(new Mermer.CRM.Models.PartnerBalanceResult { Balance = 0 });
            return;
        }

        // --- 4. ФАСЕТЫ ФИЛЬТРОВ ---
        if (methodName == "GetFacets" || methodName == "GetFacetsAsync")
        {
            var dict = new Dictionary<string, IEnumerable<KeyValuePair<string, int>>>();
            if (invocation.Arguments.Length > 0)
            {
                if (invocation.Arguments[0] is string singleKey)
                    dict[singleKey] = new List<KeyValuePair<string, int>>();
                else if (invocation.Arguments[0] is IEnumerable<string> keys)
                    foreach (var k in keys) dict[k] = new List<KeyValuePair<string, int>>();
            }
            invocation.ReturnValue = Task.FromResult(dict);
            return;
        }

        // --- 5. ОБРАБОТКА ВСЕХ ОСТАЛЬНЫХ ТИПОВ ВОЗВРАТА ---
        if (returnType == typeof(void))
        {
            return; // Просто выходим, если метод ничего не возвращает (void)
        }
        else if (returnType == typeof(Task))
        {
            invocation.ReturnValue = Task.CompletedTask;
        }
        if (returnType == typeof(Task))
        {
            invocation.ReturnValue = Task.CompletedTask;
        }
        else if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var taskResultType = returnType.GetGenericArguments()[0];
            object defaultResult = null;

            if (taskResultType == typeof(bool))
            {
                defaultResult = true;
            }
            else if (taskResultType == typeof(string))
            {
                defaultResult = "";
            }
            else if (taskResultType.IsGenericType && taskResultType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                var itemType = taskResultType.GetGenericArguments()[0];
                defaultResult = typeof(System.Linq.Enumerable).GetMethod("Empty").MakeGenericMethod(itemType).Invoke(null, null);
            }
            else if (taskResultType.IsValueType)
            {
                defaultResult = Activator.CreateInstance(taskResultType);
            }
            else if (taskResultType.IsClass)
            {
                try { defaultResult = Activator.CreateInstance(taskResultType); } catch { }
            }

            var fromResultMethod = typeof(Task).GetMethod("FromResult").MakeGenericMethod(taskResultType);
            invocation.ReturnValue = fromResultMethod.Invoke(null, new[] { defaultResult });
        }
        else if (returnType == typeof(bool))
        {
            invocation.ReturnValue = true;
        }
        else if (returnType == typeof(string))
        {
            invocation.ReturnValue = "";
        }
        else if (returnType.IsValueType)
        {
            invocation.ReturnValue = Activator.CreateInstance(returnType);
        }
        else
        {
            invocation.ReturnValue = null;
        }
    }
}
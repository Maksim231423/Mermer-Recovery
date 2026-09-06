using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Mermer.Data.Patcher.Services;
using Mermer.Data.Postgres;
using Mermer.Data.Postgres.Services;

namespace Mermer.Data.Patcher;

class Program
{
    static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("=== MERMER DATA PATCHER (MIGRATION TOOL) ===");

        // Загрузка конфигурации: CLI args -> Environment -> appsettings.json
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();

        // 1. Определение параметров подключения
        string? configuredConnStr = config.GetConnectionString("Postgres");
        var builder = new NpgsqlConnectionStringBuilder(!string.IsNullOrWhiteSpace(configuredConnStr)
            ? configuredConnStr
            : "Host=localhost;Port=5433;Database=mermer_creation;Username=mermer_creation;Password=mermer_strong_password_123");

        Console.WriteLine("\nНастройка подключения к PostgreSQL (Enter для подтверждения):");

        Console.Write($"Хост [{builder.Host}]: ");
        var inputHost = Console.ReadLine();
        if (!string.IsNullOrWhiteSpace(inputHost)) builder.Host = inputHost.Trim();

        Console.Write($"Порт [{builder.Port}]: ");
        var inputPort = Console.ReadLine();
        if (int.TryParse(inputPort, out var p)) builder.Port = p;

        Console.Write($"База данных [{builder.Database}]: ");
        var inputDb = Console.ReadLine();
        if (!string.IsNullOrWhiteSpace(inputDb)) builder.Database = inputDb.Trim();

        Console.Write($"Пользователь [{builder.Username}]: ");
        var inputUser = Console.ReadLine();
        if (!string.IsNullOrWhiteSpace(inputUser)) builder.Username = inputUser.Trim();

        Console.Write($"Пароль [{(string.IsNullOrEmpty(builder.Password) ? "не задан" : "******")}]: ");
        var inputPass = Console.ReadLine();
        if (!string.IsNullOrWhiteSpace(inputPass)) builder.Password = inputPass.Trim();

        // 2. Определение пути к файлу JSON экспорта
        string defaultPath = config["MigrationSettings:ExportJsonPath"] ?? @"D:\Программирование\binyat_export.json";
        string jsonFilePath = string.Empty;

        while (true)
        {
            Console.Write($"\nПуть к файлу экспорта [{defaultPath}]: ");
            var inputPath = Console.ReadLine();
            jsonFilePath = string.IsNullOrWhiteSpace(inputPath) ? defaultPath : inputPath.Trim();

            if (File.Exists(jsonFilePath)) break;

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[ОШИБКА] Файл не найден: {Path.GetFullPath(jsonFilePath)}");
            Console.ResetColor();
            Console.WriteLine("Пожалуйста, введите корректный путь к файлу.");
        }

        // 3. Выбор очистки таблиц
        bool defaultClean = bool.TryParse(config["MigrationSettings:CleanDatabaseBeforeImport"], out var cl) && cl;
        string defaultCleanPrompt = defaultClean ? "Y/n" : "y/N";

        Console.Write($"\nОчистить целевые таблицы перед импортом (TRUNCATE CASCADE)? [{defaultCleanPrompt}]: ");
        var cleanInput = Console.ReadLine()?.Trim().ToLowerInvariant();

        bool shouldClean;
        if (string.IsNullOrEmpty(cleanInput))
        {
            shouldClean = defaultClean;
        }
        else
        {
            shouldClean = cleanInput == "y" || cleanInput == "yes" || cleanInput == "да" || cleanInput == "д";
        }

        Console.WriteLine("\nИтоговые параметры:");
        Console.WriteLine($"Подключение: Host={builder.Host};Port={builder.Port};Database={builder.Database};Username={builder.Username}");
        Console.WriteLine($"Файл данных: {Path.GetFullPath(jsonFilePath)}");
        Console.WriteLine($"Режим очистки: {(shouldClean ? "ПОЛНАЯ ОЧИСТКА БАЗЫ ПЕРЕД ИМПОРТОМ" : "ДОПОЛНЕНИЕ СУЩЕСТВУЮЩИХ ДАННЫХ (БЕЗ ОЧИСТКИ)")}");

        var optionsBuilder = new DbContextOptionsBuilder<MermerDbContext>();
        optionsBuilder.UseNpgsql(builder.ConnectionString);

        using var dbContext = new MermerDbContext(optionsBuilder.Options);

        try
        {
            Console.WriteLine("\nПроверка соединения с базой данных...");
            await dbContext.Database.CanConnectAsync();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Связь с PostgreSQL успешно установлена!");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[ОШИБКА ПОДКЛЮЧЕНИЯ]: {ex.Message}");
            Console.ResetColor();
            Console.WriteLine("Нажмите любую клавишу для выхода...");
            Console.ReadKey();
            return;
        }

        if (shouldClean)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\nОчищаем целевые таблицы в PostgreSQL (TRUNCATE CASCADE)...");
            Console.ResetColor();

            await dbContext.Database.ExecuteSqlRawAsync(@"
                TRUNCATE TABLE 
                    offices, partners, warehouses, depositories, currencies, currency_rates, users, roles, user_roles,
                    stocks, stock_units, stock_prices, stock_additional_prices, stock_name_composers, stock_name_composer_values,
                    stock_alternatives, stock_alternative_lines,
                    invoices, invoice_lines, invoice_payments, invoice_currency_convertions, 
                    invoice_stock_unit_convertions, invoice_discounts, invoice_overheads,
                    stock_slips, stock_slip_lines,
                    stock_transfers, stock_transfer_lines,
                    stock_revisions, stock_revision_lines,
                    stock_orders, stock_order_lines, stock_order_unit_convertions,
                    stock_order_templates, stock_order_template_lines,
                    aggregated_stock_orders, aggregated_stock_order_lines,
                    funds_slips, funds_slip_lines,
                    funds_transfers, funds_transfer_lines,
                    expenses, expense_slips, expense_slip_lines,
                    daily_funds_registeries, daily_funds_registery_lines,
                    partner_slips, partner_slip_lines,
                    partner_transfers, partner_transfer_lines,
                    partner_actions,
                    stock_balances
                CASCADE;");
            Console.WriteLine("Таблицы успешно очищены.");
        }
        else
        {
            Console.WriteLine("\nОчистка таблиц пропущена. Импорт работает в режиме дополнения.");
        }

        // Инициализация сервисов импорта
        var partnerImporter = new PartnerImportService(dbContext);
        var enterpriseImporter = new EnterpriseImportService(dbContext);
        var nomenclatureImporter = new NomenclatureImportService(dbContext);
        var commerceImporter = new CommerceImportService(dbContext);
        var fundsImporter = new FundsImportService(dbContext);

        // Этап 1: Базовые справочники
        Console.WriteLine("\n--- Этап 1: Базовые справочники ---");
        await partnerImporter.MigratePartnersAsync(jsonFilePath);
        await enterpriseImporter.MigrateOfficesAsync(jsonFilePath);
        await enterpriseImporter.MigrateWarehousesAsync(jsonFilePath);
        await enterpriseImporter.MigrateDepositoriesAsync(jsonFilePath);
        await enterpriseImporter.MigrateCurrenciesAsync(jsonFilePath);
        await enterpriseImporter.MigrateUsersAsync(jsonFilePath);

        // Этап 2: Номенклатура и Статьи расходов
        Console.WriteLine("\n--- Этап 2: Номенклатура и Статьи расходов ---");
        await nomenclatureImporter.MigrateStocksAsync(jsonFilePath);
        await fundsImporter.MigrateExpensesAsync(jsonFilePath);

        // Этап 3: Документы
        Console.WriteLine("\n--- Этап 3: Документы ---");
        await commerceImporter.MigrateInvoicesAsync(jsonFilePath);
        await commerceImporter.MigrateStockSlipsAsync(jsonFilePath);
        await commerceImporter.MigrateStockTransfersAsync(jsonFilePath);
        await fundsImporter.MigrateFundsSlipsAsync(jsonFilePath);
        await fundsImporter.MigrateFundsTransfersAsync(jsonFilePath);
        await fundsImporter.MigrateExpenseSlipsAsync(jsonFilePath);

        // Расчет остатков
        Console.WriteLine("\n==========================================");
        Console.WriteLine("Расчет накопительного регистра остатков...");
        Console.WriteLine("==========================================");
        var calculator = new StockBalanceCalculator(dbContext);
        await calculator.RecalculateAllBalancesAsync();

        // Обновление View поиска товаров
        Console.WriteLine("\nОбновление материализованного представления mv_stock_search...");
        try
        {
            await dbContext.Database.ExecuteSqlRawAsync("REFRESH MATERIALIZED VIEW mv_stock_search;");
            Console.WriteLine("Материализованное представление успешно обновлено!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Предупреждение при обновлении mv_stock_search: {ex.Message}");
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\n[УСПЕХ] Миграция данных полностью завершена!");
        Console.ResetColor();
        Console.WriteLine("Нажмите любую клавишу для выхода...");
        Console.ReadKey();
    }
}
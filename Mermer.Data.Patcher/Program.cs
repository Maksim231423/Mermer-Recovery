using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Win32;
using Mermer.Data.Patcher.Services;
using Mermer.Data.Postgres;
using Mermer.Data.Postgres.Entities;
using Mermer.Data.Postgres.Services;

namespace Mermer.Data.Patcher;

class Program
{
    static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("=== MERMER DATA PATCHER (MIGRATION TOOL) ===");

        // 1. Дефолтные настройки для нашего Docker PostgreSQL
        string defaultHost = "localhost";
        string defaultPort = "5433";
        string defaultDb = "mermer_creation";
        string defaultUser = "mermer_creation";
        string defaultPass = "mermer_strong_password_123";

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\MermerCS\Binyat");
            if (key != null)
            {
                var addr = key.GetValue("DatabaseAddress")?.ToString()
                        ?? key.GetValue("ServiceAddress")?.ToString();

                if (!string.IsNullOrWhiteSpace(addr))
                {
                    addr = addr.Replace("http://", "").Replace("https://", "").TrimEnd('/');
                    var parts = addr.Split(':');
                    if (parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]))
                        defaultHost = parts[0];

                    // Игнорируем порты Couchbase (8091-8094, 11210) и Web API (5050, 8080)
                    if (parts.Length > 1 && int.TryParse(parts[1], out var parsedPort))
                    {
                        if (parsedPort == 5432 || parsedPort == 5433)
                        {
                            defaultPort = parts[1];
                        }
                    }
                }

                var db = key.GetValue("DatabaseName")?.ToString();
                if (!string.IsNullOrWhiteSpace(db) && !db.Contains("binyat"))
                    defaultDb = db;

                var u = key.GetValue("DatabaseUser")?.ToString();
                if (!string.IsNullOrWhiteSpace(u) && u != "admin")
                    defaultUser = u;

                var p = key.GetValue("DatabasePassword")?.ToString();
                if (!string.IsNullOrWhiteSpace(p) && p != "PwdAdm321")
                    defaultPass = p;
            }
        }
        catch { }

        // 2. Интерактивный ввод/подтверждение параметров
        Console.WriteLine("\nПараметры подключения к PostgreSQL (нажмите Enter для значения в скобках):");

        Console.Write($"Хост [{defaultHost}]: ");
        var inputHost = Console.ReadLine();
        string host = string.IsNullOrWhiteSpace(inputHost) ? defaultHost : inputHost.Trim();

        Console.Write($"Порт [{defaultPort}]: ");
        var inputPort = Console.ReadLine();
        string port = string.IsNullOrWhiteSpace(inputPort) ? defaultPort : inputPort.Trim();

        Console.Write($"База данных [{defaultDb}]: ");
        var inputDb = Console.ReadLine();
        string database = string.IsNullOrWhiteSpace(inputDb) ? defaultDb : inputDb.Trim();

        Console.Write($"Пользователь [{defaultUser}]: ");
        var inputUser = Console.ReadLine();
        string user = string.IsNullOrWhiteSpace(inputUser) ? defaultUser : inputUser.Trim();

        Console.Write($"Пароль [{defaultPass}]: ");
        var inputPass = Console.ReadLine();
        string password = string.IsNullOrWhiteSpace(inputPass) ? defaultPass : inputPass.Trim();

        string connectionString = $"Host={host};Port={port};Database={database};Username={user};Password={password}";

        Console.WriteLine($"\nПодключение к: Host={host};Port={port};Database={database};Username={user}");

        // 3. Путь к исходному JSON
        string defaultJsonPath = @"D:\Программирование\binyat_export.json";
        Console.Write($"Путь к файлу экспорта [{defaultJsonPath}]: ");
        var inputJson = Console.ReadLine();
        string jsonFilePath = string.IsNullOrWhiteSpace(inputJson) ? defaultJsonPath : inputJson.Trim();

        if (!File.Exists(jsonFilePath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[ОШИБКА] Файл не найден: {jsonFilePath}");
            Console.ResetColor();
            Console.WriteLine("Нажмите любую клавишу для выхода...");
            Console.ReadKey();
            return;
        }

        var optionsBuilder = new DbContextOptionsBuilder<MermerDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        using var dbContext = new MermerDbContext(optionsBuilder.Options);

        try
        {
            Console.WriteLine("\nПроверяем доступность базы данных...");
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

        Console.WriteLine("\nОчищаем целевые таблицы в PostgreSQL...");

        // Очистка таблиц
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

        // Инициализация сервисов
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
        await fundsImporter.MigrateExpenseSlipsAsync(jsonFilePath);

        // Расчет остатков
        Console.WriteLine("\n==========================================");
        Console.WriteLine("Расчет накопительного регистра остатков...");
        Console.WriteLine("==========================================");
        var calculator = new StockBalanceCalculator(dbContext);
        await calculator.RecalculateAllBalancesAsync();

        // Обновление View
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
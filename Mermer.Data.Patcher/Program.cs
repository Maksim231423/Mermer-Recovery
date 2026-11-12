using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
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
        Console.OutputEncoding = Encoding.UTF8;
        Console.Title = "Mermer Data Patcher";

        try
        {
            await RunPatcherAsync(args);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("\n=================================================");
            Console.WriteLine("[КРИТИЧЕСКАЯ НЕОБРАБОТАННАЯ ОШИБКА]:");
            Console.WriteLine(ex.ToString());
            Console.WriteLine("=================================================");
            Console.ResetColor();
        }
        finally
        {
            Console.WriteLine("\nНажмите любую клавишу для завершения работы...");
            Console.ReadKey();
        }
    }

    private static async Task RunPatcherAsync(string[] args)
    {
        Console.WriteLine("=================================================");
        Console.WriteLine("   MERMER DATA PATCHER (MIGRATION & SEED TOOL)   ");
        Console.WriteLine("=================================================");

        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();

        string? configuredConnStr = config.GetConnectionString("Postgres")
                                 ?? config.GetConnectionString("DefaultConnection");

        // 1. Попытка автоматического подключения и автопоиска пароля/порта
        Console.WriteLine("\nПоиск и проверка соединения с локальным PostgreSQL...");
        var (connectionString, workingPort, workingPassword) = await AutoDetectConnectionAsync(configuredConnStr);

        NpgsqlConnectionStringBuilder builder;

        if (!string.IsNullOrEmpty(connectionString))
        {
            builder = new NpgsqlConnectionStringBuilder(connectionString);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[АВТОПОДКЛЮЧЕНИЕ УСПЕШНО]: Порт {builder.Port}, Пользователь '{builder.Username}'");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[ВНИМАНИЕ] Не удалось подключиться автоматически по стандартным паролям.");
            Console.WriteLine("Пожалуйста, проверьте и подтвердите параметры вручную:");
            Console.ResetColor();

            builder = new NpgsqlConnectionStringBuilder(!string.IsNullOrWhiteSpace(configuredConnStr)
                ? configuredConnStr
                : "Host=127.0.0.1;Port=5432;Database=mermer_creation;Username=postgres;Password=leon#6315*.123");

            while (true)
            {
                Console.Write($"\nХост [{builder.Host}]: ");
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

                Console.WriteLine("\nПроверка связи...");
                if (await TryConnectDirectAsync(builder.ConnectionString))
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("Связь успешно установлена!");
                    Console.ResetColor();
                    break;
                }

                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Не удалось подключиться с этими данными. Попробуйте еще раз.");
                Console.ResetColor();
            }
        }

        // Проверяем существование базы данных mermer_creation и создаем ее при необходимости
        await EnsureDatabaseCreatedAsync(builder);

        var optionsBuilder = new DbContextOptionsBuilder<MermerDbContext>();
        optionsBuilder.UseNpgsql(builder.ConnectionString);

        using var dbContext = new MermerDbContext(optionsBuilder.Options);

        // =========================================================================
        // 2. ЗАПРОС ЛИЦЕНЗИИ ДЛЯ ПРИВЯЗКИ ТЕНАНТА (CLIENT_ID)
        // =========================================================================
        Console.WriteLine("\n-------------------------------------------------");
        Console.WriteLine("[ЛИЦЕНЗИЯ]: Привязка импортируемых данных к клиенту");
        Console.WriteLine("-------------------------------------------------");

        string? existingLicenseKey = null;
        try
        {
            await using var connCheck = new NpgsqlConnection(builder.ConnectionString);
            await connCheck.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT key FROM licenses WHERE is_active = true ORDER BY created_at DESC LIMIT 1;", connCheck);
            var res = await cmd.ExecuteScalarAsync();
            if (res != null && res != DBNull.Value)
            {
                existingLicenseKey = res.ToString();
            }
        }
        catch { }

        string targetClientId = "default";

        if (!string.IsNullOrWhiteSpace(existingLicenseKey))
        {
            var detectedClient = ExtractClientNameFromToken(existingLicenseKey);
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"Найдена лицензия: '{detectedClient}'");
            Console.ResetColor();
            Console.Write($"Использовать этот ключ? [Y/n] или вставьте новый: ");
            var keyDecision = Console.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(keyDecision) || keyDecision.Equals("y", StringComparison.OrdinalIgnoreCase) || keyDecision.Equals("да", StringComparison.OrdinalIgnoreCase))
            {
                targetClientId = detectedClient;
            }
            else if (keyDecision.Contains('.'))
            {
                targetClientId = ExtractClientNameFromToken(keyDecision);
            }
            else
            {
                Console.Write("Вставьте лицензионный ключ: ");
                var manualKey = Console.ReadLine()?.Trim();
                if (!string.IsNullOrEmpty(manualKey)) targetClientId = ExtractClientNameFromToken(manualKey);
            }
        }
        else
        {
            Console.Write("Вставьте лицензионный ключ (или Enter для 'default'): ");
            var inputKey = Console.ReadLine()?.Trim();
            if (!string.IsNullOrWhiteSpace(inputKey))
            {
                targetClientId = ExtractClientNameFromToken(inputKey);
            }
        }

        MermerDbContext.CurrentClientId = targetClientId;
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[OK] Все переносимые записи будут привязаны к client_id = '{targetClientId}'");
        Console.ResetColor();

        // 3. Определение пути к файлу JSON экспорта
        string defaultPath = config["MigrationSettings:ExportJsonPath"]
                          ?? Path.Combine(AppContext.BaseDirectory, "binyat_export.json");

        if (!File.Exists(defaultPath) && File.Exists(@"D:\Программирование\binyat_export.json"))
        {
            defaultPath = @"D:\Программирование\binyat_export.json";
        }

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
            Console.WriteLine("Пожалуйста, укажите верный путь к файлу выгрузки binyat_export.json.");
        }

        // 4. Очистка таблиц
        bool defaultClean = bool.TryParse(config["MigrationSettings:CleanDatabaseBeforeImport"], out var cl) && cl;
        string defaultCleanPrompt = defaultClean ? "Y/n" : "y/N";

        Console.Write($"\nОчистить целевые таблицы перед импортом (TRUNCATE CASCADE)? [{defaultCleanPrompt}]: ");
        var cleanInput = Console.ReadLine()?.Trim().ToLowerInvariant();

        bool shouldClean = string.IsNullOrEmpty(cleanInput)
            ? defaultClean
            : (cleanInput == "y" || cleanInput == "yes" || cleanInput == "да" || cleanInput == "д");

        if (shouldClean)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\nОчищаем целевые таблицы в PostgreSQL...");
            Console.ResetColor();

            await dbContext.Database.ExecuteSqlRawAsync(@"
                TRUNCATE TABLE 
                    offices, partners, warehouses, depositories, currencies, currency_rates, users, roles,
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
                    stock_balances
                CASCADE;");
            Console.WriteLine("Таблицы успешно очищены.");
        }
        else
        {
            Console.WriteLine("\nОчистка таблиц пропущена. Импорт работает в режиме дополнения.");
        }

        // Сервисы импорта
        var partnerImporter = new PartnerImportService(dbContext);
        var enterpriseImporter = new EnterpriseImportService(dbContext);
        var nomenclatureImporter = new NomenclatureImportService(dbContext);
        var commerceImporter = new CommerceImportService(dbContext);
        var fundsImporter = new FundsImportService(dbContext);

        Console.WriteLine("\n--- Этап 1: Базовые справочники ---");
        await partnerImporter.MigratePartnersAsync(jsonFilePath);
        await enterpriseImporter.MigrateOfficesAsync(jsonFilePath);
        await enterpriseImporter.MigrateWarehousesAsync(jsonFilePath);
        await enterpriseImporter.MigrateDepositoriesAsync(jsonFilePath);
        await enterpriseImporter.MigrateCurrenciesAsync(jsonFilePath);
        await enterpriseImporter.MigrateUsersAsync(jsonFilePath);

        Console.WriteLine("\n--- Этап 2: Номенклатура и Статьи расходов ---");
        await nomenclatureImporter.MigrateStocksAsync(jsonFilePath);
        await fundsImporter.MigrateExpensesAsync(jsonFilePath);

        Console.WriteLine("\n--- Этап 3: Документы ---");
        await commerceImporter.MigrateInvoicesAsync(jsonFilePath);
        await commerceImporter.MigrateStockSlipsAsync(jsonFilePath);
        await commerceImporter.MigrateStockTransfersAsync(jsonFilePath);
        await fundsImporter.MigrateFundsSlipsAsync(jsonFilePath);
        await fundsImporter.MigrateFundsTransfersAsync(jsonFilePath);
        await fundsImporter.MigrateExpenseSlipsAsync(jsonFilePath);

        Console.WriteLine("\n==========================================");
        Console.WriteLine("Расчет накопительного регистра остатков...");
        Console.WriteLine("==========================================");
        var calculator = new StockBalanceCalculator(dbContext);
        await calculator.RecalculateAllBalancesAsync();

        if (targetClientId != "default")
        {
            Console.WriteLine($"\nСинхронизация client_id = '{targetClientId}' для всех таблиц...");
            await FixClientIdsAsync(builder.ConnectionString, targetClientId);
        }

        Console.WriteLine("\nОбновление материализованного представления mv_stock_search...");
        try
        {
            await dbContext.Database.ExecuteSqlRawAsync("REFRESH MATERIALIZED VIEW mv_stock_search;");
            Console.WriteLine("Представление поиска успешно обновлено!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Предупреждение: {ex.Message}");
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\n[УСПЕХ] Миграция данных полностью завершена!");
        Console.ResetColor();
    }

    #region Connection Helpers
    private static async Task<(string? connectionString, int port, string password)> AutoDetectConnectionAsync(string? configuredConnStr)
    {
        var passwordsToTry = new List<string>();

        if (!string.IsNullOrWhiteSpace(configuredConnStr))
        {
            var cb = new NpgsqlConnectionStringBuilder(configuredConnStr);
            if (!string.IsNullOrEmpty(cb.Password)) passwordsToTry.Add(cb.Password);
        }

        passwordsToTry.AddRange(new[] { "leon#6315*.123", "1234", "123456", "postgres", "admin", "root", "" });
        var distinctPasswords = passwordsToTry.Distinct().ToList();

        var portsToTry = new[] { 5432, 5433, 5434 };
        var hostsToTry = new[] { "127.0.0.1", "localhost" };

        foreach (var host in hostsToTry)
        {
            foreach (var port in portsToTry)
            {
                foreach (var pass in distinctPasswords)
                {
                    // Пробуем системную базу postgres (она есть всегда)
                    var testBuilder = new NpgsqlConnectionStringBuilder
                    {
                        Host = host,
                        Port = port,
                        Database = "postgres",
                        Username = "postgres",
                        Password = pass,
                        Timeout = 2,
                        CommandTimeout = 3
                    };

                    if (await TryConnectDirectAsync(testBuilder.ConnectionString))
                    {
                        testBuilder.Database = "mermer_creation";
                        return (testBuilder.ConnectionString, port, pass);
                    }
                }
            }
        }

        return (null, 5432, "leon#6315*.123");
    }

    private static async Task<bool> TryConnectDirectAsync(string connStr)
    {
        try
        {
            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task EnsureDatabaseCreatedAsync(NpgsqlConnectionStringBuilder targetBuilder)
    {
        string targetDb = targetBuilder.Database;

        var sysBuilder = new NpgsqlConnectionStringBuilder(targetBuilder.ConnectionString)
        {
            Database = "postgres"
        };

        try
        {
            await using var conn = new NpgsqlConnection(sysBuilder.ConnectionString);
            await conn.OpenAsync();

            await using var checkCmd = new NpgsqlCommand($"SELECT 1 FROM pg_database WHERE datname = '{targetDb}';", conn);
            var exists = await checkCmd.ExecuteScalarAsync();

            if (exists == null)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"База данных '{targetDb}' не найдена. Создание базы данных...");
                Console.ResetColor();

                await using var createCmd = new NpgsqlCommand($"CREATE DATABASE \"{targetDb}\" WITH ENCODING 'UTF8';", conn);
                await createCmd.ExecuteNonQueryAsync();

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"База данных '{targetDb}' успешно создана!");
                Console.ResetColor();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Проверка БД]: {ex.Message}");
        }
    }
    #endregion

    private static string ExtractClientNameFromToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return "default";

        try
        {
            var parts = token.Trim().Split('.');
            if (parts.Length >= 2)
            {
                string payloadBase64 = parts[0].Replace('-', '+').Replace('_', '/');
                switch (payloadBase64.Length % 4)
                {
                    case 2: payloadBase64 += "=="; break;
                    case 3: payloadBase64 += "="; break;
                }

                var jsonBytes = Convert.FromBase64String(payloadBase64);
                using var doc = JsonDocument.Parse(jsonBytes);
                if (doc.RootElement.TryGetProperty("c", out var cProp))
                {
                    var val = cProp.GetString();
                    if (!string.IsNullOrWhiteSpace(val)) return val.Trim();
                }
            }
        }
        catch { }

        return "default";
    }

    private static async Task FixClientIdsAsync(string connectionString, string clientId)
    {
        var tables = new[]
        {
            "offices", "warehouses", "depositories", "currencies", "currency_rates",
            "partners", "partner_slips", "partner_slip_lines", "partner_transfers", "partner_transfer_lines",
            "funds_slips", "funds_slip_lines", "funds_transfers", "funds_transfer_lines",
            "daily_funds_registeries", "daily_funds_registery_lines", "expenses", "expense_slips", "expense_slip_lines",
            "stocks", "stock_units", "stock_prices", "stock_additional_prices", "stock_balances",
            "stock_alternatives", "stock_alternative_lines", "stock_name_composers", "stock_name_composer_values",
            "stock_slips", "stock_slip_lines", "stock_transfers", "stock_transfer_lines",
            "stock_revisions", "stock_revision_lines", "stock_orders", "stock_order_lines",
            "stock_order_unit_convertions", "stock_order_templates", "stock_order_template_lines",
            "aggregated_stock_orders", "aggregated_stock_order_lines", "invoices", "invoice_lines",
            "invoice_discounts", "invoice_payments", "invoice_overheads", "invoice_currency_convertions",
            "invoice_stock_unit_convertions", "users", "roles"
        };

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        foreach (var tbl in tables)
        {
            try
            {
                await using var cmd = new NpgsqlCommand(
                    $"UPDATE \"{tbl}\" SET client_id = @clientId WHERE client_id = 'default' OR client_id IS NULL;", conn);
                cmd.Parameters.AddWithValue("clientId", clientId);
                await cmd.ExecuteNonQueryAsync();
            }
            catch { }
        }
    }
}
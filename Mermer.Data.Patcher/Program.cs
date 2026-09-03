using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Mermer.Data.Patcher.Services;
using Mermer.Data.Postgres;
using Mermer.Data.Postgres.Entities;
using Mermer.Data.Postgres.Services;

namespace Mermer.Data.Patcher;

class Program
{
    static async Task Main(string[] args)
    {
        string jsonFilePath = @"D:\Программирование\binyat_export.json";

        var optionsBuilder = new DbContextOptionsBuilder<MermerDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Database=mermer_db;Username=postgres;Password=1234");

        using var dbContext = new MermerDbContext(optionsBuilder.Options);

        Console.WriteLine("Проверяем структуру БД и очищаем старые данные...");

        // 1. Полная каскадная очистка всех таблиц
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

        // 2. Инициализируем сервисы
        var partnerImporter = new PartnerImportService(dbContext);
        var enterpriseImporter = new EnterpriseImportService(dbContext);
        var nomenclatureImporter = new NomenclatureImportService(dbContext);
        var commerceImporter = new CommerceImportService(dbContext);
        var fundsImporter = new FundsImportService(dbContext);

        // 3. Этап 1: Базовые справочники
        Console.WriteLine("\n--- Этап 1: Базовые справочники ---");
        await partnerImporter.MigratePartnersAsync(jsonFilePath);
        await enterpriseImporter.MigrateOfficesAsync(jsonFilePath);
        await enterpriseImporter.MigrateWarehousesAsync(jsonFilePath);
        await enterpriseImporter.MigrateDepositoriesAsync(jsonFilePath);
        await enterpriseImporter.MigrateCurrenciesAsync(jsonFilePath);
        await enterpriseImporter.MigrateUsersAsync(jsonFilePath);

        // 4. Этап 2: Номенклатура и Статьи расходов
        Console.WriteLine("\n--- Этап 2: Номенклатура и Статьи расходов ---");
        await nomenclatureImporter.MigrateStocksAsync(jsonFilePath);
        await fundsImporter.MigrateExpensesAsync(jsonFilePath);

        // 5. Этап 3: Документы (Складские + Торговые + Кассовые)
        Console.WriteLine("\n--- Этап 3: Документы ---");
        await commerceImporter.MigrateInvoicesAsync(jsonFilePath);
        await commerceImporter.MigrateStockSlipsAsync(jsonFilePath);
        await commerceImporter.MigrateStockTransfersAsync(jsonFilePath);
        await fundsImporter.MigrateFundsSlipsAsync(jsonFilePath);
        await fundsImporter.MigrateExpenseSlipsAsync(jsonFilePath);

        // 6. Расчет регистра остатков товаров
        Console.WriteLine("\n==========================================");
        Console.WriteLine("Расчет накопительного регистра остатков...");
        Console.WriteLine("==========================================");
        var calculator = new StockBalanceCalculator(dbContext);
        await calculator.RecalculateAllBalancesAsync();

        // 7. Обновление материализованного представления поиска товаров
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

        Console.WriteLine("\n[УСПЕХ] Миграция данных по всем новым таблицам полностью завершена!");
        Console.WriteLine("Нажмите любую клавишу для выхода...");
        Console.ReadKey();
    }
}
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Mermer.Data.Postgres;
using Mermer.Data.Postgres.Entities;

namespace Mermer.Data.Patcher.Services;

public class CommerceImportService
{
    private readonly MermerDbContext _dbContext;

    public CommerceImportService(MermerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task MigrateInvoicesAsync(string jsonFilePath)
    {
        Console.WriteLine("Начинаем импорт документов Invoice (Накладные/Счета) и их зависимостей...");

        var validUsers = (await _dbContext.Users.Select(x => x.Id).ToListAsync()).ToHashSet();
        var validOffices = (await _dbContext.Offices.Select(x => x.Id).ToListAsync()).ToHashSet();
        var validWarehouses = (await _dbContext.Warehouses.Select(x => x.Id).ToListAsync()).ToHashSet();
        var validDepositories = (await _dbContext.Depositories.Select(x => x.Id).ToListAsync()).ToHashSet();
        var validPartners = (await _dbContext.Partners.Select(x => x.Id).ToListAsync()).ToHashSet();
        var validCurrencies = (await _dbContext.Currencies.Select(x => x.Id).ToListAsync()).ToHashSet();
        var validStocks = (await _dbContext.Stocks.Select(x => x.Id).ToListAsync()).ToHashSet();
        var validUnits = (await _dbContext.StockUnits.Select(x => x.Id).ToListAsync()).ToHashSet();

        using var stream = File.OpenRead(jsonFilePath);
        using var reader = new StreamReader(stream);

        // Батчи для всех связанных таблиц
        var invoicesBatch = new List<InvoiceEntity>();
        var linesBatch = new List<InvoiceLineEntity>();
        var paymentsBatch = new List<InvoicePaymentEntity>();
        var currencyConvBatch = new List<InvoiceCurrencyConvertionEntity>();
        var stockUnitConvBatch = new List<InvoiceStockUnitConvertionEntity>();
        var discountsBatch = new List<InvoiceDiscountEntity>();
        var overheadsBatch = new List<InvoiceOverheadEntity>();

        var processedInvoiceIds = new HashSet<Guid>();
        var processedLineIds = new HashSet<Guid>();
        var processedOtherIds = new HashSet<Guid>();

        string? line;
        int totalInvoices = 0;

        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (IsTargetDocType(root, "Invoice"))
            {
                var targetContainer = GetTargetContainer(root);
                if (!TryGetGuidProperty(root, targetContainer, "id", out var invoiceId)) continue;
                if (!processedInvoiceIds.Add(invoiceId)) continue;

                // 1. Формируем шапку документа
                var invoice = new InvoiceEntity
                {
                    Id = invoiceId,
                    Code = GetStringProperty(targetContainer, "code"),
                    InvoiceType = GetStringProperty(targetContainer, "type") ?? "Sales",
                    UserName = GetStringProperty(targetContainer, "userName"),
                    StockPriceGroup = GetStringProperty(targetContainer, "stockPriceGroup"),
                    DebitCreditLeftAmount = GetBoolProperty(targetContainer, "debitCreditLeftAmount"),
                    IsCompleted = GetBoolProperty(targetContainer, "isCompleted"),
                    IsDisabled = GetBoolProperty(targetContainer, "isDisabled"),
                    Group = GetStringProperty(targetContainer, "group"),
                    Description = GetStringProperty(targetContainer, "description"),
                    Tags = GetStringArrayProperty(targetContainer, "tags"),
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };

                if (targetContainer.TryGetProperty("date", out var dateProp) && DateTimeOffset.TryParse(dateProp.GetString(), out var dateVal))
                    invoice.Date = dateVal.ToUniversalTime();
                else
                    invoice.Date = DateTimeOffset.UtcNow;

                if (targetContainer.TryGetProperty("dueDate", out var dueDateProp) && DateTimeOffset.TryParse(dueDateProp.GetString(), out var dueDateVal))
                    invoice.DueDate = dueDateVal.ToUniversalTime();

                invoice.UserId = GetValidId(targetContainer, "userId", validUsers);
                invoice.OfficeId = GetValidId(targetContainer, "officeId", validOffices);
                invoice.WarehouseId = GetValidId(targetContainer, "warehouseId", validWarehouses);
                invoice.DepositoryId = GetValidId(targetContainer, "depositoryId", validDepositories);
                invoice.PartnerId = GetValidId(targetContainer, "partnerId", validPartners);
                invoice.DisplayCurrencyId = GetValidId(targetContainer, "displayCurrencyId", validCurrencies);

                invoicesBatch.Add(invoice);

                // 2. Строки документа (lines)
                if (targetContainer.TryGetProperty("lines", out var linesArray) && linesArray.ValueKind == JsonValueKind.Array)
                {
                    int sortOrder = 0;
                    foreach (var elem in linesArray.EnumerateArray())
                    {
                        if (!elem.TryGetProperty("id", out var idProp) || !Guid.TryParse(idProp.GetString(), out var lineId)) continue;
                        if (!processedLineIds.Add(lineId)) continue;

                        linesBatch.Add(new InvoiceLineEntity
                        {
                            Id = lineId,
                            InvoiceId = invoiceId,
                            SourceId = GetGuidOrNull(elem, "sourceId"),
                            StockId = GetValidId(elem, "stockId", validStocks),
                            UnitId = GetValidId(elem, "unitId", validUnits),
                            CurrencyId = GetValidId(elem, "currencyId", validCurrencies),
                            Quantity = GetDecimalProperty(elem, "quantity") ?? 0m,
                            Price = GetDecimalProperty(elem, "price") ?? 0m,
                            SortOrder = sortOrder++
                        });
                    }
                }

                // 3. Платежи (payments)
                if (targetContainer.TryGetProperty("payments", out var paymentsArray) && paymentsArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in paymentsArray.EnumerateArray())
                    {
                        var currencyId = GetValidId(elem, "currencyId", validCurrencies);
                        // Если валюта удалена из базы или невалидна - пропускаем платеж
                        if (currencyId == null) continue;

                        var id = GetGuidOrNull(elem, "id") ?? Guid.NewGuid();
                        if (!processedOtherIds.Add(id)) continue;

                        paymentsBatch.Add(new InvoicePaymentEntity
                        {
                            Id = id,
                            InvoiceId = invoiceId,
                            Amount = GetDecimalProperty(elem, "amount") ?? 0m,
                            CurrencyId = currencyId.Value // Теперь безопасно берем .Value
                        });
                    }
                }

                // 4. Конвертации валют (currencyConvertions)
                if (targetContainer.TryGetProperty("currencyConvertions", out var curConvArray) && curConvArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in curConvArray.EnumerateArray())
                    {
                        var currencyId = GetValidId(elem, "currencyId", validCurrencies);
                        // Если валюта удалена из базы или невалидна - пропускаем конвертацию
                        if (currencyId == null) continue;

                        var id = GetGuidOrNull(elem, "id") ?? Guid.NewGuid();
                        if (!processedOtherIds.Add(id)) continue;

                        currencyConvBatch.Add(new InvoiceCurrencyConvertionEntity
                        {
                            Id = id,
                            InvoiceId = invoiceId,
                            CurrencyId = currencyId.Value, // Безопасно берем .Value
                            Multiplier = GetDecimalProperty(elem, "multiplier") ?? 1m,
                            Divider = GetDecimalProperty(elem, "divider") ?? 1m
                        });
                    }
                }

                // 5. Конвертации единиц измерения (stockUnitConvertions)
                if (targetContainer.TryGetProperty("stockUnitConvertions", out var suConvArray) && suConvArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in suConvArray.EnumerateArray())
                    {
                        var stockId = GetValidId(elem, "stockId", validStocks);
                        var unitId = GetValidId(elem, "unitId", validUnits);

                        // Если товар ИЛИ единица измерения отсутствуют в базе - пропускаем запись
                        if (stockId == null || unitId == null) continue;

                        var id = GetGuidOrNull(elem, "id") ?? Guid.NewGuid();
                        if (!processedOtherIds.Add(id)) continue;

                        stockUnitConvBatch.Add(new InvoiceStockUnitConvertionEntity
                        {
                            Id = id,
                            InvoiceId = invoiceId,
                            StockId = stockId.Value, // Безопасно
                            UnitId = unitId.Value,   // Безопасно
                            Multiplier = GetDecimalProperty(elem, "multiplier") ?? 1m,
                            Divider = GetDecimalProperty(elem, "divider") ?? 1m
                        });
                    }
                }

                // 6. Скидки (discounts)
                if (targetContainer.TryGetProperty("discounts", out var discountsArray) && discountsArray.ValueKind == JsonValueKind.Array)
                {
                    int sortOrder = 0;
                    foreach (var elem in discountsArray.EnumerateArray())
                    {
                        var id = GetGuidOrNull(elem, "id") ?? Guid.NewGuid();
                        if (!processedOtherIds.Add(id)) continue;

                        discountsBatch.Add(new InvoiceDiscountEntity
                        {
                            Id = id,
                            InvoiceId = invoiceId,
                            DiscountType = GetStringProperty(elem, "type") ?? "Flat",
                            Amount = GetDecimalProperty(elem, "amount") ?? 0m,
                            Description = GetStringProperty(elem, "description"),
                            SortOrder = sortOrder++
                        });
                    }
                }

                // 7. Накладные расходы (overheads)
                if (targetContainer.TryGetProperty("overheads", out var overheadsArray) && overheadsArray.ValueKind == JsonValueKind.Array)
                {
                    int sortOrder = 0;
                    foreach (var elem in overheadsArray.EnumerateArray())
                    {
                        var id = GetGuidOrNull(elem, "id") ?? Guid.NewGuid();
                        if (!processedOtherIds.Add(id)) continue;

                        overheadsBatch.Add(new InvoiceOverheadEntity
                        {
                            Id = id,
                            InvoiceId = invoiceId,
                            Amount = GetDecimalProperty(elem, "amount") ?? 0m,
                            CurrencyId = GetValidId(elem, "currencyId", validCurrencies),
                            Description = GetStringProperty(elem, "description"),
                            SortOrder = sortOrder++
                        });
                    }
                }

                // Пакетное сохранение
                if (invoicesBatch.Count >= 500)
                {
                    await SaveCommerceBatchAsync(invoicesBatch, linesBatch, paymentsBatch, currencyConvBatch, stockUnitConvBatch, discountsBatch, overheadsBatch);
                    totalInvoices += invoicesBatch.Count;

                    invoicesBatch.Clear();
                    linesBatch.Clear();
                    paymentsBatch.Clear();
                    currencyConvBatch.Clear();
                    stockUnitConvBatch.Clear();
                    discountsBatch.Clear();
                    overheadsBatch.Clear();

                    Console.WriteLine($"Сохранено документов: {totalInvoices}...");
                }
            }
        }

        // Сохраняем остатки
        if (invoicesBatch.Any())
        {
            await SaveCommerceBatchAsync(invoicesBatch, linesBatch, paymentsBatch, currencyConvBatch, stockUnitConvBatch, discountsBatch, overheadsBatch);
            totalInvoices += invoicesBatch.Count;
        }

        Console.WriteLine($"Готово! Импортировано Invoice: {totalInvoices} и все связанные коллекции.");
    }

    public async Task MigrateStockSlipsAsync(string jsonFilePath)
    {
        Console.WriteLine("Начинаем импорт документов StockSlip (Складские ордера) и их строк...");

        // Подгружаем справочники для валидации внешних ключей
        var validUsers = (await _dbContext.Users.Select(x => x.Id).ToListAsync()).ToHashSet();
        var validWarehouses = (await _dbContext.Warehouses.Select(x => x.Id).ToListAsync()).ToHashSet();
        var validStocks = (await _dbContext.Stocks.Select(x => x.Id).ToListAsync()).ToHashSet();
        var validUnits = (await _dbContext.StockUnits.Select(x => x.Id).ToListAsync()).ToHashSet();

        using var stream = File.OpenRead(jsonFilePath);
        using var reader = new StreamReader(stream);

        // Батчи для складских документов
        var stockSlipsBatch = new List<StockSlipEntity>();
        var linesBatch = new List<StockSlipLineEntity>();
        // Если у тебя есть таблица для конвертаций в складских ордерах, раскомментируй:
        // var unitConvsBatch = new List<StockSlipUnitConvertionEntity>();

        var processedSlipIds = new HashSet<Guid>();
        var processedLineIds = new HashSet<Guid>();

        string? line;
        int totalSlips = 0;

        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (IsTargetDocType(root, "StockSlip"))
            {
                var targetContainer = GetTargetContainer(root);
                if (!TryGetGuidProperty(root, targetContainer, "id", out var slipId)) continue;
                if (!processedSlipIds.Add(slipId)) continue;

                // 1. Формируем шапку ордера
                var slip = new StockSlipEntity
                {
                    Id = slipId,
                    Code = GetStringProperty(targetContainer, "code"),
                    SlipType = GetStringProperty(targetContainer, "type") ?? "StockOpening",
                    IsCompleted = GetBoolProperty(targetContainer, "isCompleted"),
                    IsStockIncome = GetBoolProperty(targetContainer, "isStockIncome"),
                    DisplayTotal = GetDecimalProperty(targetContainer, "displayTotal") ?? 0m,
                    Description = GetStringProperty(targetContainer, "description"),
                    Tags = GetStringArrayProperty(targetContainer, "tags"),
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };

                if (targetContainer.TryGetProperty("date", out var dateProp) && DateTimeOffset.TryParse(dateProp.GetString(), out var dateVal))
                    slip.Date = dateVal.ToUniversalTime();
                else
                    slip.Date = DateTimeOffset.UtcNow;

                slip.UserId = GetValidId(targetContainer, "userId", validUsers);
                slip.WarehouseId = GetValidId(targetContainer, "warehouseId", validWarehouses);

                stockSlipsBatch.Add(slip);

                // 2. Строки ордера (lines)
                if (targetContainer.TryGetProperty("lines", out var linesArray) && linesArray.ValueKind == JsonValueKind.Array)
                {
                    int sortOrder = 0;
                    foreach (var elem in linesArray.EnumerateArray())
                    {
                        if (!elem.TryGetProperty("id", out var idProp) || !Guid.TryParse(idProp.GetString(), out var lineId)) continue;
                        if (!processedLineIds.Add(lineId)) continue;

                        linesBatch.Add(new StockSlipLineEntity
                        {
                            Id = lineId,
                            StockSlipId = slipId,
                            StockId = GetValidId(elem, "stockId", validStocks),
                            UnitId = GetValidId(elem, "unitId", validUnits),
                            Quantity = GetDecimalProperty(elem, "quantity") ?? 0m,
                            ActionQuantity = GetDecimalProperty(elem, "actionQuantity") ?? 0m,
                            Price = GetDecimalProperty(elem, "price") ?? 0m,
                            ActionTotal = GetDecimalProperty(elem, "actionTotal") ?? 0m,
                            SortOrder = sortOrder++
                        });
                    }
                }

                // Пакетное сохранение
                if (stockSlipsBatch.Count >= 500)
                {
                    await SaveStockSlipsBatchAsync(stockSlipsBatch, linesBatch);
                    totalSlips += stockSlipsBatch.Count;

                    stockSlipsBatch.Clear();
                    linesBatch.Clear();

                    Console.WriteLine($"Сохранено складских документов: {totalSlips}...");
                }
            }
        }

        // Сохраняем остатки батча
        if (stockSlipsBatch.Any())
        {
            await SaveStockSlipsBatchAsync(stockSlipsBatch, linesBatch);
            totalSlips += stockSlipsBatch.Count;
        }

        Console.WriteLine($"Готово! Импортировано StockSlip: {totalSlips} и их строк.");
    }

    private async Task SaveStockSlipsBatchAsync(
        List<StockSlipEntity> slips,
        List<StockSlipLineEntity> lines)
    {
        if (slips.Any()) await _dbContext.Set<StockSlipEntity>().AddRangeAsync(slips);
        if (lines.Any()) await _dbContext.Set<StockSlipLineEntity>().AddRangeAsync(lines);

        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();
    }

    private async Task SaveCommerceBatchAsync(
        List<InvoiceEntity> invoices,
        List<InvoiceLineEntity> lines,
        List<InvoicePaymentEntity> payments,
        List<InvoiceCurrencyConvertionEntity> currencyConvs,
        List<InvoiceStockUnitConvertionEntity> stockUnitConvs,
        List<InvoiceDiscountEntity> discounts,
        List<InvoiceOverheadEntity> overheads)
    {
        if (invoices.Any()) await _dbContext.Set<InvoiceEntity>().AddRangeAsync(invoices);
        if (lines.Any()) await _dbContext.Set<InvoiceLineEntity>().AddRangeAsync(lines);
        if (payments.Any()) await _dbContext.Set<InvoicePaymentEntity>().AddRangeAsync(payments);
        if (currencyConvs.Any()) await _dbContext.Set<InvoiceCurrencyConvertionEntity>().AddRangeAsync(currencyConvs);
        if (stockUnitConvs.Any()) await _dbContext.Set<InvoiceStockUnitConvertionEntity>().AddRangeAsync(stockUnitConvs);
        if (discounts.Any()) await _dbContext.Set<InvoiceDiscountEntity>().AddRangeAsync(discounts);
        if (overheads.Any()) await _dbContext.Set<InvoiceOverheadEntity>().AddRangeAsync(overheads);

        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();
    }

    public async Task MigrateStockTransfersAsync(string jsonFilePath)
    {
        Console.WriteLine("Начинаем импорт документов StockTransfer (Перемещения со склада на склад)...");

        var validWarehouses = (await _dbContext.Warehouses.Select(x => x.Id).ToListAsync()).ToHashSet();
        var validStocks = (await _dbContext.Stocks.Select(x => x.Id).ToListAsync()).ToHashSet();
        var validUnits = (await _dbContext.StockUnits.Select(x => x.Id).ToListAsync()).ToHashSet();
        var validCurrencies = (await _dbContext.Currencies.Select(x => x.Id).ToListAsync()).ToHashSet();

        using var stream = File.OpenRead(jsonFilePath);
        using var reader = new StreamReader(stream);

        var transfersBatch = new List<StockTransferEntity>();
        var linesBatch = new List<StockTransferLineEntity>();

        var processedTransferIds = new HashSet<Guid>();
        var processedLineIds = new HashSet<Guid>();
        string? line;

        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (IsTargetDocType(root, "StockTransfer"))
            {
                var c = GetTargetContainer(root);
                if (!TryGetGuidProperty(root, c, "id", out var transferId) || !processedTransferIds.Add(transferId)) continue;

                var transfer = new StockTransferEntity
                {
                    Id = transferId,
                    Code = GetStringProperty(c, "code"),
                    Date = c.TryGetProperty("date", out var dP) && DateTimeOffset.TryParse(dP.GetString(), out var dV) ? dV.ToUniversalTime() : DateTimeOffset.UtcNow,
                    WarehouseId = GetValidId(c, "warehouseId", validWarehouses),
                    DestinationWarehouseId = GetValidId(c, "destinationWarehouseId", validWarehouses),
                    DisplayCurrencyId = GetValidId(c, "displayCurrencyId", validCurrencies),
                    IsCompleted = GetBoolProperty(c, "isCompleted"),
                    IsDisabled = GetBoolProperty(c, "isDisabled"),
                    UserName = GetStringProperty(c, "userName"),
                    GroupName = GetStringProperty(c, "group") ?? GetStringProperty(c, "groupName"),
                    Description = GetStringProperty(c, "description"),
                    Tags = GetStringArrayProperty(c, "tags"),
                    ActionTotal = GetDecimalProperty(c, "actionTotal") ?? 0m,
                    ActionReceivedTotal = GetDecimalProperty(c, "actionReceivedTotal") ?? 0m,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };

                transfersBatch.Add(transfer);

                if (c.TryGetProperty("lines", out var linesArray) && linesArray.ValueKind == JsonValueKind.Array)
                {
                    int sortOrder = 0;
                    foreach (var elem in linesArray.EnumerateArray())
                    {
                        if (!elem.TryGetProperty("id", out var idProp) || !Guid.TryParse(idProp.GetString(), out var lineId)) continue;
                        if (!processedLineIds.Add(lineId)) continue;

                        linesBatch.Add(new StockTransferLineEntity
                        {
                            Id = lineId,
                            StockTransferId = transferId,
                            StockId = GetValidId(elem, "stockId", validStocks),
                            UnitId = GetValidId(elem, "unitId", validUnits),
                            ReceivedUnitId = GetValidId(elem, "receivedUnitId", validUnits) ?? GetValidId(elem, "unitId", validUnits),
                            Quantity = GetDecimalProperty(elem, "quantity") ?? 0m,
                            ReceivedQuantity = GetDecimalProperty(elem, "receivedQuantity") ?? GetDecimalProperty(elem, "quantity") ?? 0m,
                            Price = GetDecimalProperty(elem, "price") ?? 0m,
                            ActionTotal = GetDecimalProperty(elem, "actionTotal") ?? 0m,
                            ActionReceivedTotal = GetDecimalProperty(elem, "actionReceivedTotal") ?? 0m,
                            SortOrder = sortOrder++
                        });
                    }
                }

                if (transfersBatch.Count >= 500)
                {
                    await _dbContext.Set<StockTransferEntity>().AddRangeAsync(transfersBatch);
                    await _dbContext.Set<StockTransferLineEntity>().AddRangeAsync(linesBatch);
                    await _dbContext.SaveChangesAsync();
                    _dbContext.ChangeTracker.Clear();

                    transfersBatch.Clear();
                    linesBatch.Clear();
                }
            }
        }

        if (transfersBatch.Any())
        {
            await _dbContext.Set<StockTransferEntity>().AddRangeAsync(transfersBatch);
            await _dbContext.Set<StockTransferLineEntity>().AddRangeAsync(linesBatch);
            await _dbContext.SaveChangesAsync();
            _dbContext.ChangeTracker.Clear();
        }
    }

    #region Вспомогательные методы парсинга

    private Guid? GetValidId(JsonElement container, string propertyName, HashSet<Guid> validIds)
    {
        if (container.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            if (Guid.TryParse(prop.GetString(), out var id) && validIds.Contains(id))
            {
                return id;
            }
        }
        return null;
    }

    private Guid? GetGuidOrNull(JsonElement container, string propertyName)
    {
        if (container.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            if (Guid.TryParse(prop.GetString(), out var id))
                return id;
        }
        return null;
    }

    private static bool IsTargetDocType(JsonElement root, string targetDocType)
    {
        if (root.ValueKind != JsonValueKind.Object) return false;
        if (root.TryGetProperty("docType", out var dt) && dt.ValueKind == JsonValueKind.String)
            return dt.GetString() == targetDocType;
        if (root.TryGetProperty("patch", out var patch) && patch.ValueKind == JsonValueKind.Object &&
            patch.TryGetProperty("docType", out var pdt) && pdt.ValueKind == JsonValueKind.String)
            return pdt.GetString() == targetDocType;
        return false;
    }

    private static JsonElement GetTargetContainer(JsonElement root)
    {
        if (root.TryGetProperty("patch", out var patch) && patch.ValueKind == JsonValueKind.Object)
        {
            if (patch.TryGetProperty("propertyPatches", out var props) && props.ValueKind == JsonValueKind.Object)
                return props;
            return patch;
        }
        return root;
    }

    private static bool TryGetGuidProperty(JsonElement root, JsonElement container, string propertyName, out Guid result)
    {
        result = Guid.Empty;
        if (container.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
            return Guid.TryParse(prop.GetString(), out result);
        if (root.TryGetProperty("patch", out var patch) && patch.ValueKind == JsonValueKind.Object &&
            patch.TryGetProperty(propertyName, out var patchProp) && patchProp.ValueKind == JsonValueKind.String)
            return Guid.TryParse(patchProp.GetString(), out result);
        if (root.TryGetProperty(propertyName, out var rootProp) && rootProp.ValueKind == JsonValueKind.String)
            return Guid.TryParse(rootProp.GetString(), out result);
        return false;
    }

    private static string? GetStringProperty(JsonElement container, string propertyName)
    {
        if (container.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString();
        return null;
    }

    private static string[]? GetStringArrayProperty(JsonElement container, string propertyName)
    {
        if (container.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Array)
            return prop.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToArray();
        return null;
    }

    private static decimal? GetDecimalProperty(JsonElement container, string propertyName)
    {
        if (container.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number)
            return prop.GetDecimal();
        return null;
    }

    private static bool GetBoolProperty(JsonElement container, string propertyName)
    {
        if (container.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.True) return true;
            if (prop.ValueKind == JsonValueKind.False) return false;
        }
        return false;
    }

    #endregion
}
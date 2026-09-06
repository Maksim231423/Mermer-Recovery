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
            if (root.ValueKind != JsonValueKind.Object) continue;

            if (IsTargetDocType(root, "Invoice"))
            {
                var targetContainer = GetTargetContainer(root);
                if (targetContainer.ValueKind != JsonValueKind.Object) continue;

                if (!TryGetGuidProperty(root, targetContainer, "id", out var invoiceId)) continue;
                if (!processedInvoiceIds.Add(invoiceId)) continue;

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

                if (targetContainer.TryGetProperty("date", out var dateProp) &&
                    dateProp.ValueKind == JsonValueKind.String &&
                    DateTimeOffset.TryParse(dateProp.GetString(), out var dateVal))
                {
                    invoice.Date = dateVal.ToUniversalTime();
                }
                else
                {
                    invoice.Date = DateTimeOffset.UtcNow;
                }

                if (targetContainer.TryGetProperty("dueDate", out var dueDateProp) &&
                    dueDateProp.ValueKind == JsonValueKind.String &&
                    DateTimeOffset.TryParse(dueDateProp.GetString(), out var dueDateVal))
                {
                    invoice.DueDate = dueDateVal.ToUniversalTime();
                }

                invoice.UserId = GetValidId(targetContainer, "userId", validUsers);
                invoice.OfficeId = GetValidId(targetContainer, "officeId", validOffices);
                invoice.WarehouseId = GetValidId(targetContainer, "warehouseId", validWarehouses);
                invoice.DepositoryId = GetValidId(targetContainer, "depositoryId", validDepositories);
                invoice.PartnerId = GetValidId(targetContainer, "partnerId", validPartners);
                invoice.DisplayCurrencyId = GetValidId(targetContainer, "displayCurrencyId", validCurrencies);

                invoicesBatch.Add(invoice);

                // Поиск строк с жесткой защитой от Null
                JsonElement linesArray = default;
                if (targetContainer.TryGetProperty("lines", out var lArr) && lArr.ValueKind == JsonValueKind.Array)
                {
                    linesArray = lArr;
                }
                else if (root.TryGetProperty("patch", out var pObj) && pObj.ValueKind == JsonValueKind.Object &&
                         pObj.TryGetProperty("subListPatches", out var slObj) && slObj.ValueKind == JsonValueKind.Object &&
                         slObj.TryGetProperty("lines", out var slLines) && slLines.ValueKind == JsonValueKind.Array)
                {
                    linesArray = slLines;
                }

                if (linesArray.ValueKind == JsonValueKind.Array)
                {
                    int sortOrder = 0;
                    foreach (var elem in linesArray.EnumerateArray())
                    {
                        if (elem.ValueKind != JsonValueKind.Object) continue;

                        JsonElement lineObj = elem;
                        if (elem.TryGetProperty("propertyPatches", out var pProps) && pProps.ValueKind == JsonValueKind.Object)
                            lineObj = pProps;

                        if (!TryGetGuidProperty(elem, lineObj, "id", out var lineId)) lineId = Guid.NewGuid();
                        if (!processedLineIds.Add(lineId)) continue;

                        linesBatch.Add(new InvoiceLineEntity
                        {
                            Id = lineId,
                            InvoiceId = invoiceId,
                            SourceId = GetGuidOrNull(lineObj, "sourceId"),
                            StockId = GetValidId(lineObj, "stockId", validStocks),
                            UnitId = GetValidId(lineObj, "unitId", validUnits),
                            CurrencyId = GetValidId(lineObj, "currencyId", validCurrencies),
                            Quantity = GetDecimalProperty(lineObj, "quantity") ?? 0m,
                            Price = GetDecimalProperty(lineObj, "price") ?? 0m,
                            SortOrder = sortOrder++
                        });
                    }
                }

                // Платежи (payments)
                if (targetContainer.TryGetProperty("payments", out var paymentsArray) && paymentsArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in paymentsArray.EnumerateArray())
                    {
                        if (elem.ValueKind != JsonValueKind.Object) continue;
                        var currencyId = GetValidId(elem, "currencyId", validCurrencies);
                        if (currencyId == null) continue;

                        var id = GetGuidOrNull(elem, "id") ?? Guid.NewGuid();
                        if (!processedOtherIds.Add(id)) continue;

                        paymentsBatch.Add(new InvoicePaymentEntity
                        {
                            Id = id,
                            InvoiceId = invoiceId,
                            Amount = GetDecimalProperty(elem, "amount") ?? 0m,
                            CurrencyId = currencyId.Value
                        });
                    }
                }

                // Валютные конвертации (currencyConvertions)
                if (targetContainer.TryGetProperty("currencyConvertions", out var curConvArray) && curConvArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in curConvArray.EnumerateArray())
                    {
                        if (elem.ValueKind != JsonValueKind.Object) continue;
                        var currencyId = GetValidId(elem, "currencyId", validCurrencies);
                        if (currencyId == null) continue;

                        var id = GetGuidOrNull(elem, "id") ?? Guid.NewGuid();
                        if (!processedOtherIds.Add(id)) continue;

                        currencyConvBatch.Add(new InvoiceCurrencyConvertionEntity
                        {
                            Id = id,
                            InvoiceId = invoiceId,
                            CurrencyId = currencyId.Value,
                            Multiplier = GetDecimalProperty(elem, "multiplier") ?? 1m,
                            Divider = GetDecimalProperty(elem, "divider") ?? 1m
                        });
                    }
                }

                // Конвертации единиц измерения (stockUnitConvertions)
                if (targetContainer.TryGetProperty("stockUnitConvertions", out var suConvArray) && suConvArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in suConvArray.EnumerateArray())
                    {
                        if (elem.ValueKind != JsonValueKind.Object) continue;
                        var stockId = GetValidId(elem, "stockId", validStocks);
                        var unitId = GetValidId(elem, "unitId", validUnits);
                        if (stockId == null || unitId == null) continue;

                        var id = GetGuidOrNull(elem, "id") ?? Guid.NewGuid();
                        if (!processedOtherIds.Add(id)) continue;

                        stockUnitConvBatch.Add(new InvoiceStockUnitConvertionEntity
                        {
                            Id = id,
                            InvoiceId = invoiceId,
                            StockId = stockId.Value,
                            UnitId = unitId.Value,
                            Multiplier = GetDecimalProperty(elem, "multiplier") ?? 1m,
                            Divider = GetDecimalProperty(elem, "divider") ?? 1m
                        });
                    }
                }

                // Скидки (discounts)
                if (targetContainer.TryGetProperty("discounts", out var discountsArray) && discountsArray.ValueKind == JsonValueKind.Array)
                {
                    int sortOrder = 0;
                    foreach (var elem in discountsArray.EnumerateArray())
                    {
                        if (elem.ValueKind != JsonValueKind.Object) continue;
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

                // Накладные расходы (overheads)
                if (targetContainer.TryGetProperty("overheads", out var overheadsArray) && overheadsArray.ValueKind == JsonValueKind.Array)
                {
                    int sortOrder = 0;
                    foreach (var elem in overheadsArray.EnumerateArray())
                    {
                        if (elem.ValueKind != JsonValueKind.Object) continue;
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

        var validUsers = (await _dbContext.Users.Select(x => x.Id).ToListAsync()).ToHashSet();
        var validWarehouses = (await _dbContext.Warehouses.Select(x => x.Id).ToListAsync()).ToHashSet();
        var validStocks = (await _dbContext.Stocks.Select(x => x.Id).ToListAsync()).ToHashSet();
        var validUnits = (await _dbContext.StockUnits.Select(x => x.Id).ToListAsync()).ToHashSet();

        using var stream = File.OpenRead(jsonFilePath);
        using var reader = new StreamReader(stream);

        var stockSlipsBatch = new List<StockSlipEntity>();
        var linesBatch = new List<StockSlipLineEntity>();
        var processedSlipIds = new HashSet<Guid>();
        var processedLineIds = new HashSet<Guid>();

        string? line;
        int totalSlips = 0;

        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) continue;

            if (IsTargetDocType(root, "StockSlip"))
            {
                var targetContainer = GetTargetContainer(root);
                if (targetContainer.ValueKind != JsonValueKind.Object) continue;

                if (!TryGetGuidProperty(root, targetContainer, "id", out var slipId)) continue;
                if (!processedSlipIds.Add(slipId)) continue;

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

                if (targetContainer.TryGetProperty("date", out var dateProp) &&
                    dateProp.ValueKind == JsonValueKind.String &&
                    DateTimeOffset.TryParse(dateProp.GetString(), out var dateVal))
                {
                    slip.Date = dateVal.ToUniversalTime();
                }
                else
                {
                    slip.Date = DateTimeOffset.UtcNow;
                }

                slip.UserId = GetValidId(targetContainer, "userId", validUsers);
                slip.WarehouseId = GetValidId(targetContainer, "warehouseId", validWarehouses);

                stockSlipsBatch.Add(slip);

                JsonElement linesArray = default;
                if (targetContainer.TryGetProperty("lines", out var lArr) && lArr.ValueKind == JsonValueKind.Array)
                {
                    linesArray = lArr;
                }
                else if (root.TryGetProperty("patch", out var pObj) && pObj.ValueKind == JsonValueKind.Object &&
                         pObj.TryGetProperty("subListPatches", out var slObj) && slObj.ValueKind == JsonValueKind.Object &&
                         slObj.TryGetProperty("lines", out var slLines) && slLines.ValueKind == JsonValueKind.Array)
                {
                    linesArray = slLines;
                }

                if (linesArray.ValueKind == JsonValueKind.Array)
                {
                    int sortOrder = 0;
                    foreach (var elem in linesArray.EnumerateArray())
                    {
                        if (elem.ValueKind != JsonValueKind.Object) continue;

                        JsonElement lineObj = elem;
                        if (elem.TryGetProperty("propertyPatches", out var pProps) && pProps.ValueKind == JsonValueKind.Object)
                            lineObj = pProps;

                        if (!TryGetGuidProperty(elem, lineObj, "id", out var lineId)) lineId = Guid.NewGuid();
                        if (!processedLineIds.Add(lineId)) continue;

                        linesBatch.Add(new StockSlipLineEntity
                        {
                            Id = lineId,
                            StockSlipId = slipId,
                            StockId = GetValidId(lineObj, "stockId", validStocks),
                            UnitId = GetValidId(lineObj, "unitId", validUnits),
                            Quantity = GetDecimalProperty(lineObj, "quantity") ?? 0m,
                            ActionQuantity = GetDecimalProperty(lineObj, "actionQuantity") ?? 0m,
                            Price = GetDecimalProperty(lineObj, "price") ?? 0m,
                            ActionTotal = GetDecimalProperty(lineObj, "actionTotal") ?? 0m,
                            SortOrder = sortOrder++
                        });
                    }
                }

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

        if (stockSlipsBatch.Any())
        {
            await SaveStockSlipsBatchAsync(stockSlipsBatch, linesBatch);
            totalSlips += stockSlipsBatch.Count;
        }

        Console.WriteLine($"Готово! Импортировано StockSlip: {totalSlips} и их строк.");
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
        int totalTransfers = 0;

        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) continue;

            if (IsTargetDocType(root, "StockTransfer"))
            {
                var c = GetTargetContainer(root);
                if (c.ValueKind != JsonValueKind.Object) continue;

                if (!TryGetGuidProperty(root, c, "id", out var transferId) || !processedTransferIds.Add(transferId)) continue;

                var transfer = new StockTransferEntity
                {
                    Id = transferId,
                    Code = GetStringProperty(c, "code"),
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

                if (c.TryGetProperty("date", out var dP) &&
                    dP.ValueKind == JsonValueKind.String &&
                    DateTimeOffset.TryParse(dP.GetString(), out var dV))
                {
                    transfer.Date = dV.ToUniversalTime();
                }
                else
                {
                    transfer.Date = DateTimeOffset.UtcNow;
                }

                transfersBatch.Add(transfer);

                JsonElement linesArray = default;
                if (c.TryGetProperty("lines", out var lArr) && lArr.ValueKind == JsonValueKind.Array)
                {
                    linesArray = lArr;
                }
                else if (root.TryGetProperty("patch", out var pObj) && pObj.ValueKind == JsonValueKind.Object &&
                         pObj.TryGetProperty("subListPatches", out var slObj) && slObj.ValueKind == JsonValueKind.Object &&
                         slObj.TryGetProperty("lines", out var slLines) && slLines.ValueKind == JsonValueKind.Array)
                {
                    linesArray = slLines;
                }

                if (linesArray.ValueKind == JsonValueKind.Array)
                {
                    int sortOrder = 0;
                    foreach (var elem in linesArray.EnumerateArray())
                    {
                        if (elem.ValueKind != JsonValueKind.Object) continue;

                        JsonElement lineObj = elem;
                        if (elem.TryGetProperty("propertyPatches", out var pProps) && pProps.ValueKind == JsonValueKind.Object)
                            lineObj = pProps;

                        if (!TryGetGuidProperty(elem, lineObj, "id", out var lineId)) lineId = Guid.NewGuid();
                        if (!processedLineIds.Add(lineId)) continue;

                        linesBatch.Add(new StockTransferLineEntity
                        {
                            Id = lineId,
                            StockTransferId = transferId,
                            StockId = GetValidId(lineObj, "stockId", validStocks),
                            UnitId = GetValidId(lineObj, "unitId", validUnits),
                            ReceivedUnitId = GetValidId(lineObj, "receivedUnitId", validUnits) ?? GetValidId(lineObj, "unitId", validUnits),
                            Quantity = GetDecimalProperty(lineObj, "quantity") ?? 0m,
                            ReceivedQuantity = GetDecimalProperty(lineObj, "receivedQuantity") ?? GetDecimalProperty(lineObj, "quantity") ?? 0m,
                            Price = GetDecimalProperty(lineObj, "price") ?? 0m,
                            ActionTotal = GetDecimalProperty(lineObj, "actionTotal") ?? 0m,
                            ActionReceivedTotal = GetDecimalProperty(lineObj, "actionReceivedTotal") ?? 0m,
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

                    totalTransfers += transfersBatch.Count;
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
            totalTransfers += transfersBatch.Count;
        }

        Console.WriteLine($"Готово! Импортировано StockTransfer: {totalTransfers} и их строк.");
    }

    private async Task SaveStockSlipsBatchAsync(List<StockSlipEntity> slips, List<StockSlipLineEntity> lines)
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

    #region Вспомогательные методы
    private Guid? GetValidId(JsonElement container, string propertyName, HashSet<Guid> validIds)
    {
        if (container.ValueKind == JsonValueKind.Object &&
            container.TryGetProperty(propertyName, out var prop) &&
            prop.ValueKind == JsonValueKind.String)
        {
            if (Guid.TryParse(prop.GetString(), out var id) && validIds.Contains(id))
                return id;
        }
        return null;
    }

    private Guid? GetGuidOrNull(JsonElement container, string propertyName)
    {
        if (container.ValueKind == JsonValueKind.Object &&
            container.TryGetProperty(propertyName, out var prop) &&
            prop.ValueKind == JsonValueKind.String)
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
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("patch", out var patch) && patch.ValueKind == JsonValueKind.Object)
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
        if (container.ValueKind == JsonValueKind.Object && container.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
            return Guid.TryParse(prop.GetString(), out result);
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("patch", out var patch) && patch.ValueKind == JsonValueKind.Object &&
            patch.TryGetProperty(propertyName, out var patchProp) && patchProp.ValueKind == JsonValueKind.String)
            return Guid.TryParse(patchProp.GetString(), out result);
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(propertyName, out var rootProp) && rootProp.ValueKind == JsonValueKind.String)
            return Guid.TryParse(rootProp.GetString(), out result);
        return false;
    }

    private static string? GetStringProperty(JsonElement container, string propertyName)
    {
        if (container.ValueKind == JsonValueKind.Object && container.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString();
        return null;
    }

    private static string[]? GetStringArrayProperty(JsonElement container, string propertyName)
    {
        if (container.ValueKind == JsonValueKind.Object && container.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Array)
            return prop.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToArray();
        return null;
    }

    private static decimal? GetDecimalProperty(JsonElement container, string propertyName)
    {
        if (container.ValueKind == JsonValueKind.Object && container.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number) return prop.GetDecimal();
            if (prop.ValueKind == JsonValueKind.String && decimal.TryParse(prop.GetString(), out var val)) return val;
        }
        return null;
    }

    private static bool GetBoolProperty(JsonElement container, string propertyName)
    {
        if (container.ValueKind == JsonValueKind.Object && container.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.True) return true;
            if (prop.ValueKind == JsonValueKind.False) return false;
        }
        return false;
    }
    #endregion
}
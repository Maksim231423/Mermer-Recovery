using Mermer.Data.Postgres;
using Mermer.Data.Postgres.Entities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

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
        Console.WriteLine("Начинаем умную агрегацию накладных (Invoice)...");

        var validUsers = (await _dbContext.Users.Select(x => x.Id).ToListAsync()).ToHashSet();
        var validOffices = (await _dbContext.Offices.Select(x => x.Id).ToListAsync()).ToHashSet();
        var validWarehouses = (await _dbContext.Warehouses.Select(x => x.Id).ToListAsync()).ToHashSet();
        var validDepositories = (await _dbContext.Depositories.Select(x => x.Id).ToListAsync()).ToHashSet();
        var validPartners = (await _dbContext.Partners.Select(x => x.Id).ToListAsync()).ToHashSet();
        var validCurrencies = (await _dbContext.Currencies.Select(x => x.Id).ToListAsync()).ToHashSet();
        var validStocks = (await _dbContext.Stocks.Select(x => x.Id).ToListAsync()).ToHashSet();
        var validUnits = (await _dbContext.StockUnits.Select(x => x.Id).ToListAsync()).ToHashSet();

        var existingInvoiceIds = (await _dbContext.Invoices.Select(x => x.Id).ToListAsync()).ToHashSet();
        var existingLineIds = (await _dbContext.InvoiceLines.Select(x => x.Id).ToListAsync()).ToHashSet();

        var invoiceMap = new Dictionary<Guid, InvoiceEntity>();
        var linesMap = new Dictionary<Guid, InvoiceLineEntity>();
        var paymentsMap = new Dictionary<Guid, InvoicePaymentEntity>();
        var curConvMap = new Dictionary<Guid, InvoiceCurrencyConvertionEntity>();
        var stockUnitConvMap = new Dictionary<Guid, InvoiceStockUnitConvertionEntity>();
        var discountsMap = new Dictionary<Guid, InvoiceDiscountEntity>();
        var overheadsMap = new Dictionary<Guid, InvoiceOverheadEntity>();

        using var stream = File.OpenRead(jsonFilePath);
        using var reader = new StreamReader(stream);

        string? line;
        int linesRead = 0;

        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            linesRead++;

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) continue;

            if (!IsDocType(root, "Invoice")) continue;

            var docBody = GetDocBody(root);
            if (!TryGetGuidAnywhere(root, docBody, "id", out var invoiceId)) continue;
            if (existingInvoiceIds.Contains(invoiceId)) continue;

            if (!invoiceMap.TryGetValue(invoiceId, out var invoice))
            {
                invoice = new InvoiceEntity
                {
                    Id = invoiceId,
                    InvoiceType = "Sales",
                    Date = DateTimeOffset.UtcNow,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                invoiceMap[invoiceId] = invoice;
            }

            // Дополняем реквизиты шапки из каждого встреченного патча
            if (TryGetString(docBody, "code", out var code) && !string.IsNullOrWhiteSpace(code)) invoice.Code = code;
            if (TryGetString(docBody, "type", out var type) && !string.IsNullOrWhiteSpace(type)) invoice.InvoiceType = type;
            if (TryGetString(docBody, "userName", out var uName)) invoice.UserName = uName;
            if (TryGetString(docBody, "stockPriceGroup", out var spg)) invoice.StockPriceGroup = spg;
            if (TryGetString(docBody, "group", out var grp)) invoice.Group = grp;
            if (TryGetString(docBody, "description", out var desc)) invoice.Description = desc;
            if (TryGetBool(docBody, "debitCreditLeftAmount", out var dcl)) invoice.DebitCreditLeftAmount = dcl;
            if (TryGetBool(docBody, "isCompleted", out var ic)) invoice.IsCompleted = ic;
            if (TryGetBool(docBody, "isDisabled", out var idis)) invoice.IsDisabled = idis;
            if (TryGetArray(docBody, "tags", out var tags)) invoice.Tags = tags;

            if (TryGetDateTime(docBody, "date", out var dt)) invoice.Date = dt;
            if (TryGetDateTime(docBody, "dueDate", out var ddt)) invoice.DueDate = ddt;

            if (TryGetValidGuid(docBody, "userId", validUsers, out var uid)) invoice.UserId = uid;
            if (TryGetValidGuid(docBody, "officeId", validOffices, out var oid)) invoice.OfficeId = oid;
            if (TryGetValidGuid(docBody, "warehouseId", validWarehouses, out var wid)) invoice.WarehouseId = wid;
            if (TryGetValidGuid(docBody, "depositoryId", validDepositories, out var depid)) invoice.DepositoryId = depid;
            if (TryGetValidGuid(docBody, "partnerId", validPartners, out var pid)) invoice.PartnerId = pid;
            if (TryGetValidGuid(docBody, "displayCurrencyId", validCurrencies, out var dcid)) invoice.DisplayCurrencyId = dcid;

            // Извлечение строк накладной
            if (TryGetSubList(root, docBody, "lines", out var linesArray))
            {
                int sort = linesMap.Values.Count(x => x.InvoiceId == invoiceId);
                foreach (var el in linesArray.EnumerateArray())
                {
                    var lObj = GetElementBody(el);
                    if (!TryGetGuidAnywhere(el, lObj, "id", out var lineId)) lineId = Guid.NewGuid();
                    if (existingLineIds.Contains(lineId)) continue;

                    if (!linesMap.TryGetValue(lineId, out var lineEntity))
                    {
                        lineEntity = new InvoiceLineEntity
                        {
                            Id = lineId,
                            InvoiceId = invoiceId,
                            SortOrder = sort++
                        };
                        linesMap[lineId] = lineEntity;
                    }

                    if (TryGetGuid(lObj, "sourceId", out var srcId)) lineEntity.SourceId = srcId;
                    if (TryGetValidGuid(lObj, "stockId", validStocks, out var stId)) lineEntity.StockId = stId;
                    if (TryGetValidGuid(lObj, "unitId", validUnits, out var unId)) lineEntity.UnitId = unId;
                    if (TryGetValidGuid(lObj, "currencyId", validCurrencies, out var cId)) lineEntity.CurrencyId = cId;
                    if (TryGetDecimal(lObj, "quantity", out var qty)) lineEntity.Quantity = qty;
                    if (TryGetDecimal(lObj, "price", out var prc)) lineEntity.Price = prc;
                }
            }

            // Извлечение оплат
            if (TryGetSubList(root, docBody, "payments", out var payArray))
            {
                foreach (var el in payArray.EnumerateArray())
                {
                    var pObj = GetElementBody(el);
                    if (!TryGetValidGuid(pObj, "currencyId", validCurrencies, out var curId)) continue;
                    if (!TryGetGuidAnywhere(el, pObj, "id", out var payId)) payId = Guid.NewGuid();

                    paymentsMap[payId] = new InvoicePaymentEntity
                    {
                        Id = payId,
                        InvoiceId = invoiceId,
                        Amount = TryGetDecimal(pObj, "amount", out var a) ? a : 0m,
                        CurrencyId = curId.Value
                    };
                }
            }

            // Конвертации валют и единиц
            if (TryGetSubList(root, docBody, "currencyConvertions", out var ccArray))
            {
                foreach (var el in ccArray.EnumerateArray())
                {
                    var cObj = GetElementBody(el);
                    if (!TryGetValidGuid(cObj, "currencyId", validCurrencies, out var curId)) continue;
                    if (!TryGetGuidAnywhere(el, cObj, "id", out var ccId)) ccId = Guid.NewGuid();

                    curConvMap[ccId] = new InvoiceCurrencyConvertionEntity
                    {
                        Id = ccId,
                        InvoiceId = invoiceId,
                        CurrencyId = curId.Value,
                        Multiplier = TryGetDecimal(cObj, "multiplier", out var m) ? m : 1m,
                        Divider = TryGetDecimal(cObj, "divider", out var d) ? d : 1m
                    };
                }
            }

            if (TryGetSubList(root, docBody, "stockUnitConvertions", out var suArray))
            {
                foreach (var el in suArray.EnumerateArray())
                {
                    var uObj = GetElementBody(el);
                    if (!TryGetValidGuid(uObj, "stockId", validStocks, out var sId) ||
                        !TryGetValidGuid(uObj, "unitId", validUnits, out var uId)) continue;
                    if (!TryGetGuidAnywhere(el, uObj, "id", out var suId)) suId = Guid.NewGuid();

                    stockUnitConvMap[suId] = new InvoiceStockUnitConvertionEntity
                    {
                        Id = suId,
                        InvoiceId = invoiceId,
                        StockId = sId.Value,
                        UnitId = uId.Value,
                        Multiplier = TryGetDecimal(uObj, "multiplier", out var m) ? m : 1m,
                        Divider = TryGetDecimal(uObj, "divider", out var d) ? d : 1m
                    };
                }
            }

            // Скидки и накладные расходы
            if (TryGetSubList(root, docBody, "discounts", out var discArray))
            {
                int sort = 0;
                foreach (var el in discArray.EnumerateArray())
                {
                    var dObj = GetElementBody(el);
                    if (!TryGetGuidAnywhere(el, dObj, "id", out var discId)) discId = Guid.NewGuid();

                    discountsMap[discId] = new InvoiceDiscountEntity
                    {
                        Id = discId,
                        InvoiceId = invoiceId,
                        DiscountType = TryGetString(dObj, "type", out var t) ? t! : "Flat",
                        Amount = TryGetDecimal(dObj, "amount", out var a) ? a : 0m,
                        Description = TryGetString(dObj, "description", out var ds) ? ds : null,
                        SortOrder = sort++
                    };
                }
            }

            if (TryGetSubList(root, docBody, "overheads", out var ovhArray))
            {
                int sort = 0;
                foreach (var el in ovhArray.EnumerateArray())
                {
                    var oObj = GetElementBody(el);
                    if (!TryGetGuidAnywhere(el, oObj, "id", out var ovhId)) ovhId = Guid.NewGuid();

                    TryGetValidGuid(oObj, "currencyId", validCurrencies, out var cId);

                    overheadsMap[ovhId] = new InvoiceOverheadEntity
                    {
                        Id = ovhId,
                        InvoiceId = invoiceId,
                        Amount = TryGetDecimal(oObj, "amount", out var a) ? a : 0m,
                        CurrencyId = cId,
                        Description = TryGetString(oObj, "description", out var ds) ? ds : null,
                        SortOrder = sort++
                    };
                }
            }
        }

        // Фильтрация мусорных документов: оставляем только те, у которых есть строки либо оплаты
        var invoiceIdsWithLines = linesMap.Values.Select(x => x.InvoiceId).ToHashSet();
        var invoiceIdsWithPayments = paymentsMap.Values.Select(x => x.InvoiceId).ToHashSet();

        var validInvoices = invoiceMap.Values
            .Where(inv => invoiceIdsWithLines.Contains(inv.Id) || invoiceIdsWithPayments.Contains(inv.Id))
            .ToList();

        int skippedEmpty = invoiceMap.Count - validInvoices.Count;
        Console.WriteLine($"Собрано накладных: {validInvoices.Count} (отсеяно пустых черновиков без строк: {skippedEmpty}). Строк: {linesMap.Count}");

        await BatchInsertAsync(_dbContext.Invoices, validInvoices);
        await BatchInsertAsync(_dbContext.InvoiceLines, linesMap.Values.Where(l => invoiceIdsWithLines.Contains(l.InvoiceId)).ToList());
        await BatchInsertAsync(_dbContext.Set<InvoicePaymentEntity>(), paymentsMap.Values.ToList());
        await BatchInsertAsync(_dbContext.Set<InvoiceCurrencyConvertionEntity>(), curConvMap.Values.ToList());
        await BatchInsertAsync(_dbContext.Set<InvoiceStockUnitConvertionEntity>(), stockUnitConvMap.Values.ToList());
        await BatchInsertAsync(_dbContext.Set<InvoiceDiscountEntity>(), discountsMap.Values.ToList());
        await BatchInsertAsync(_dbContext.Set<InvoiceOverheadEntity>(), overheadsMap.Values.ToList());

        Console.WriteLine("Импорт Invoice успешно завершен!");
    }

    public async Task MigrateStockSlipsAsync(string jsonFilePath)
    {
        Console.WriteLine("Начинаем умную агрегацию складских ордеров (StockSlip)...");

        var validUsers = (await _dbContext.Users.Select(x => x.Id).ToListAsync()).ToHashSet();
        var validWarehouses = (await _dbContext.Warehouses.Select(x => x.Id).ToListAsync()).ToHashSet();
        var validStocks = (await _dbContext.Stocks.Select(x => x.Id).ToListAsync()).ToHashSet();
        var validUnits = (await _dbContext.StockUnits.Select(x => x.Id).ToListAsync()).ToHashSet();

        var existingSlipIds = (await _dbContext.StockSlips.Select(x => x.Id).ToListAsync()).ToHashSet();
        var existingLineIds = (await _dbContext.StockSlipLines.Select(x => x.Id).ToListAsync()).ToHashSet();

        var slipMap = new Dictionary<Guid, StockSlipEntity>();
        var linesMap = new Dictionary<Guid, StockSlipLineEntity>();

        using var stream = File.OpenRead(jsonFilePath);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) continue;

            if (!IsDocType(root, "StockSlip")) continue;

            var docBody = GetDocBody(root);
            if (!TryGetGuidAnywhere(root, docBody, "id", out var slipId)) continue;
            if (existingSlipIds.Contains(slipId)) continue;

            if (!slipMap.TryGetValue(slipId, out var slip))
            {
                slip = new StockSlipEntity
                {
                    Id = slipId,
                    SlipType = "StockOpening",
                    Date = DateTimeOffset.UtcNow,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                slipMap[slipId] = slip;
            }

            if (TryGetString(docBody, "code", out var code) && !string.IsNullOrWhiteSpace(code)) slip.Code = code;
            if (TryGetString(docBody, "type", out var type) && !string.IsNullOrWhiteSpace(type)) slip.SlipType = type;
            if (TryGetBool(docBody, "isCompleted", out var ic)) slip.IsCompleted = ic;
            if (TryGetBool(docBody, "isStockIncome", out var isi)) slip.IsStockIncome = isi;
            if (TryGetDecimal(docBody, "displayTotal", out var dt)) slip.DisplayTotal = dt;
            if (TryGetString(docBody, "description", out var desc)) slip.Description = desc;
            if (TryGetArray(docBody, "tags", out var tags)) slip.Tags = tags;
            if (TryGetDateTime(docBody, "date", out var dateVal)) slip.Date = dateVal;

            if (TryGetValidGuid(docBody, "userId", validUsers, out var uid)) slip.UserId = uid;
            if (TryGetValidGuid(docBody, "warehouseId", validWarehouses, out var wid)) slip.WarehouseId = wid;

            if (TryGetSubList(root, docBody, "lines", out var linesArray))
            {
                int sort = linesMap.Values.Count(x => x.StockSlipId == slipId);
                foreach (var el in linesArray.EnumerateArray())
                {
                    var lObj = GetElementBody(el);
                    if (!TryGetGuidAnywhere(el, lObj, "id", out var lineId)) lineId = Guid.NewGuid();
                    if (existingLineIds.Contains(lineId)) continue;

                    if (!linesMap.TryGetValue(lineId, out var lineEntity))
                    {
                        lineEntity = new StockSlipLineEntity
                        {
                            Id = lineId,
                            StockSlipId = slipId,
                            SortOrder = sort++
                        };
                        linesMap[lineId] = lineEntity;
                    }

                    if (TryGetValidGuid(lObj, "stockId", validStocks, out var sId)) lineEntity.StockId = sId;
                    if (TryGetValidGuid(lObj, "unitId", validUnits, out var uId)) lineEntity.UnitId = uId;
                    if (TryGetDecimal(lObj, "quantity", out var q)) lineEntity.Quantity = q;
                    if (TryGetDecimal(lObj, "actionQuantity", out var aq)) lineEntity.ActionQuantity = aq;
                    if (TryGetDecimal(lObj, "price", out var p)) lineEntity.Price = p;
                    if (TryGetDecimal(lObj, "actionTotal", out var at)) lineEntity.ActionTotal = at;
                }
            }
        }

        var validSlipIds = linesMap.Values.Select(x => x.StockSlipId).ToHashSet();
        var validSlips = slipMap.Values.Where(s => validSlipIds.Contains(s.Id) || s.DisplayTotal > 0).ToList();

        await BatchInsertAsync(_dbContext.StockSlips, validSlips);
        await BatchInsertAsync(_dbContext.StockSlipLines, linesMap.Values.Where(l => validSlipIds.Contains(l.StockSlipId)).ToList());

        Console.WriteLine($"Готово! Импортировано StockSlip: {validSlips.Count}, Строк: {linesMap.Count}");
    }

    public async Task MigrateStockTransfersAsync(string jsonFilePath)
    {
        Console.WriteLine("Начинаем умную агрегацию перемещений (StockTransfer)...");

        var validWarehouses = (await _dbContext.Warehouses.Select(x => x.Id).ToListAsync()).ToHashSet();
        var validStocks = (await _dbContext.Stocks.Select(x => x.Id).ToListAsync()).ToHashSet();
        var validUnits = (await _dbContext.StockUnits.Select(x => x.Id).ToListAsync()).ToHashSet();
        var validCurrencies = (await _dbContext.Currencies.Select(x => x.Id).ToListAsync()).ToHashSet();

        var existingTransferIds = (await _dbContext.StockTransfers.Select(x => x.Id).ToListAsync()).ToHashSet();
        var existingLineIds = (await _dbContext.StockTransferLines.Select(x => x.Id).ToListAsync()).ToHashSet();

        var transferMap = new Dictionary<Guid, StockTransferEntity>();
        var linesMap = new Dictionary<Guid, StockTransferLineEntity>();

        using var stream = File.OpenRead(jsonFilePath);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) continue;

            if (!IsDocType(root, "StockTransfer")) continue;

            var docBody = GetDocBody(root);
            if (!TryGetGuidAnywhere(root, docBody, "id", out var trId)) continue;
            if (existingTransferIds.Contains(trId)) continue;

            if (!transferMap.TryGetValue(trId, out var transfer))
            {
                transfer = new StockTransferEntity
                {
                    Id = trId,
                    Date = DateTimeOffset.UtcNow,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                transferMap[trId] = transfer;
            }

            if (TryGetString(docBody, "code", out var code) && !string.IsNullOrWhiteSpace(code)) transfer.Code = code;
            if (TryGetString(docBody, "userName", out var uName)) transfer.UserName = uName;
            if (TryGetString(docBody, "group", out var grp) || TryGetString(docBody, "groupName", out grp)) transfer.GroupName = grp;
            if (TryGetString(docBody, "description", out var desc)) transfer.Description = desc;
            if (TryGetBool(docBody, "isCompleted", out var ic)) transfer.IsCompleted = ic;
            if (TryGetBool(docBody, "isDisabled", out var idis)) transfer.IsDisabled = idis;
            if (TryGetArray(docBody, "tags", out var tags)) transfer.Tags = tags;
            if (TryGetDateTime(docBody, "date", out var dt)) transfer.Date = dt;
            if (TryGetDecimal(docBody, "actionTotal", out var at)) transfer.ActionTotal = at;
            if (TryGetDecimal(docBody, "actionReceivedTotal", out var art)) transfer.ActionReceivedTotal = art;

            if (TryGetValidGuid(docBody, "warehouseId", validWarehouses, out var wid)) transfer.WarehouseId = wid;
            if (TryGetValidGuid(docBody, "destinationWarehouseId", validWarehouses, out var dwid)) transfer.DestinationWarehouseId = dwid;
            if (TryGetValidGuid(docBody, "displayCurrencyId", validCurrencies, out var dcid)) transfer.DisplayCurrencyId = dcid;

            if (TryGetSubList(root, docBody, "lines", out var linesArray))
            {
                int sort = linesMap.Values.Count(x => x.StockTransferId == trId);
                foreach (var el in linesArray.EnumerateArray())
                {
                    var lObj = GetElementBody(el);
                    if (!TryGetGuidAnywhere(el, lObj, "id", out var lineId)) lineId = Guid.NewGuid();
                    if (existingLineIds.Contains(lineId)) continue;

                    if (!linesMap.TryGetValue(lineId, out var lineEntity))
                    {
                        lineEntity = new StockTransferLineEntity
                        {
                            Id = lineId,
                            StockTransferId = trId,
                            SortOrder = sort++
                        };
                        linesMap[lineId] = lineEntity;
                    }

                    if (TryGetValidGuid(lObj, "stockId", validStocks, out var sId)) lineEntity.StockId = sId;
                    if (TryGetValidGuid(lObj, "unitId", validUnits, out var uId)) lineEntity.UnitId = uId;
                    if (TryGetValidGuid(lObj, "receivedUnitId", validUnits, out var ruId)) lineEntity.ReceivedUnitId = ruId;
                    else if (lineEntity.UnitId.HasValue) lineEntity.ReceivedUnitId = lineEntity.UnitId;

                    if (TryGetDecimal(lObj, "quantity", out var q)) lineEntity.Quantity = q;
                    if (TryGetDecimal(lObj, "receivedQuantity", out var rq)) lineEntity.ReceivedQuantity = rq;
                    else if (lineEntity.Quantity > 0) lineEntity.ReceivedQuantity = lineEntity.Quantity;

                    if (TryGetDecimal(lObj, "price", out var p)) lineEntity.Price = p;
                    if (TryGetDecimal(lObj, "actionTotal", out var lat)) lineEntity.ActionTotal = lat;
                    if (TryGetDecimal(lObj, "actionReceivedTotal", out var lart)) lineEntity.ActionReceivedTotal = lart;
                }
            }
        }

        var validTrIds = linesMap.Values.Select(x => x.StockTransferId).ToHashSet();
        var validTransfers = transferMap.Values.Where(t => validTrIds.Contains(t.Id) || t.ActionTotal > 0).ToList();

        await BatchInsertAsync(_dbContext.StockTransfers, validTransfers);
        await BatchInsertAsync(_dbContext.StockTransferLines, linesMap.Values.Where(l => validTrIds.Contains(l.StockTransferId)).ToList());

        Console.WriteLine($"Готово! Импортировано StockTransfer: {validTransfers.Count}, Строк: {linesMap.Count}");
    }

    private async Task BatchInsertAsync<T>(DbSet<T> dbSet, List<T> items) where T : class
    {
        const int batchSize = 1000;
        for (int i = 0; i < items.Count; i += batchSize)
        {
            await dbSet.AddRangeAsync(items.Skip(i).Take(batchSize));
            await _dbContext.SaveChangesAsync();
            _dbContext.ChangeTracker.Clear();
        }
    }

    #region Хелперы регистронезависимого разбора
    private static bool IsDocType(JsonElement root, string targetDocType)
    {
        if (TryGetPropCaseInsensitive(root, "docType", out var dt) && dt.ValueKind == JsonValueKind.String)
            return string.Equals(dt.GetString(), targetDocType, StringComparison.OrdinalIgnoreCase);

        if (TryGetPropCaseInsensitive(root, "patch", out var patch) && patch.ValueKind == JsonValueKind.Object &&
            TryGetPropCaseInsensitive(patch, "docType", out var pdt) && pdt.ValueKind == JsonValueKind.String)
            return string.Equals(pdt.GetString(), targetDocType, StringComparison.OrdinalIgnoreCase);

        return false;
    }

    private static JsonElement GetDocBody(JsonElement root)
    {
        if (TryGetPropCaseInsensitive(root, "patch", out var patch) && patch.ValueKind == JsonValueKind.Object)
        {
            if (TryGetPropCaseInsensitive(patch, "propertyPatches", out var props) && props.ValueKind == JsonValueKind.Object)
                return props;
            return patch;
        }
        return root;
    }

    private static JsonElement GetElementBody(JsonElement elem)
    {
        if (elem.ValueKind == JsonValueKind.Object && TryGetPropCaseInsensitive(elem, "propertyPatches", out var pp) && pp.ValueKind == JsonValueKind.Object)
            return pp;
        return elem;
    }

    private static bool TryGetPropertyCaseInsensitive(JsonElement el, string name, out JsonElement val)
    {
        return TryGetPropCaseInsensitive(el, name, out val);
    }

    private static bool TryGetPropCaseInsensitive(JsonElement el, string name, out JsonElement val)
    {
        if (el.ValueKind != JsonValueKind.Object)
        {
            val = default;
            return false;
        }
        if (el.TryGetProperty(name, out val)) return true;
        foreach (var p in el.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                val = p.Value;
                return true;
            }
        }
        val = default;
        return false;
    }

    private static bool TryGetPropAnywhere(JsonElement body, string name, out JsonElement val)
    {
        if (TryGetPropCaseInsensitive(body, name, out val)) return true;
        if (TryGetPropCaseInsensitive(body, "propertyPatches", out var pp) && pp.ValueKind == JsonValueKind.Object)
        {
            if (TryGetPropCaseInsensitive(pp, name, out val)) return true;
        }
        return false;
    }

    private static bool TryGetGuidAnywhere(JsonElement root, JsonElement body, string name, out Guid res)
    {
        res = Guid.Empty;
        if (TryGetPropAnywhere(body, name, out var val) && val.ValueKind == JsonValueKind.String && Guid.TryParse(val.GetString(), out res))
            return true;
        if (TryGetPropAnywhere(root, name, out var rVal) && rVal.ValueKind == JsonValueKind.String && Guid.TryParse(rVal.GetString(), out res))
            return true;
        return false;
    }

    private static bool TryGetGuid(JsonElement body, string name, out Guid res)
    {
        res = Guid.Empty;
        return TryGetPropAnywhere(body, name, out var v) && v.ValueKind == JsonValueKind.String && Guid.TryParse(v.GetString(), out res);
    }

    private static bool TryGetValidGuid(JsonElement body, string name, HashSet<Guid> validIds, out Guid? res)
    {
        res = null;
        if (TryGetGuid(body, name, out var g) && validIds.Contains(g))
        {
            res = g;
            return true;
        }
        return false;
    }

    private static bool TryGetString(JsonElement body, string name, out string? res)
    {
        res = null;
        if (TryGetPropAnywhere(body, name, out var v) && v.ValueKind == JsonValueKind.String)
        {
            res = v.GetString();
            return true;
        }
        return false;
    }

    private static bool TryGetDecimal(JsonElement body, string name, out decimal res)
    {
        res = 0m;
        if (TryGetPropAnywhere(body, name, out var v))
        {
            if (v.ValueKind == JsonValueKind.Number) { res = v.GetDecimal(); return true; }
            if (v.ValueKind == JsonValueKind.String && decimal.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
            {
                res = d;
                return true;
            }
        }
        return false;
    }

    private static bool TryGetBool(JsonElement body, string name, out bool res)
    {
        res = false;
        if (TryGetPropAnywhere(body, name, out var v))
        {
            if (v.ValueKind == JsonValueKind.True) { res = true; return true; }
            if (v.ValueKind == JsonValueKind.False) { res = false; return true; }
        }
        return false;
    }

    private static bool TryGetDateTime(JsonElement body, string name, out DateTimeOffset res)
    {
        res = default;
        if (TryGetPropAnywhere(body, name, out var v) && v.ValueKind == JsonValueKind.String && DateTime.TryParse(v.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            res = new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc), TimeSpan.Zero);
            return true;
        }
        return false;
    }

    private static bool TryGetArray(JsonElement body, string name, out string[]? res)
    {
        res = null;
        if (TryGetPropAnywhere(body, name, out var v) && v.ValueKind == JsonValueKind.Array)
        {
            res = v.EnumerateArray()
                   .Where(x => x.ValueKind == JsonValueKind.String)
                   .Select(x => x.GetString()!)
                   .ToArray();
            return true;
        }
        return false;
    }

    private static bool TryGetSubList(JsonElement root, JsonElement body, string listName, out JsonElement array)
    {
        array = default;
        if (TryGetPropCaseInsensitive(body, listName, out array) && array.ValueKind == JsonValueKind.Array) return true;
        if (TryGetPropCaseInsensitive(body, "subListPatches", out var slp) && slp.ValueKind == JsonValueKind.Object &&
            TryGetPropCaseInsensitive(slp, listName, out array) && array.ValueKind == JsonValueKind.Array) return true;
        if (TryGetPropCaseInsensitive(root, "patch", out var pObj) && pObj.ValueKind == JsonValueKind.Object &&
            TryGetPropCaseInsensitive(pObj, "subListPatches", out var rSlp) && rSlp.ValueKind == JsonValueKind.Object &&
            TryGetPropCaseInsensitive(rSlp, listName, out array) && array.ValueKind == JsonValueKind.Array) return true;
        if (TryGetPropCaseInsensitive(root, listName, out array) && array.ValueKind == JsonValueKind.Array) return true;
        return false;
    }
    #endregion
}
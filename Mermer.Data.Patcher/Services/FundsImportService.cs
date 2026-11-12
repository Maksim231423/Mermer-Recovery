using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Mermer.Data.Postgres;
using Mermer.Data.Postgres.Entities;

namespace Mermer.Data.Patcher.Services;

public class FundsImportService
{
    private readonly MermerDbContext _db;

    public FundsImportService(MermerDbContext db)
    {
        _db = db;
    }

    public async Task MigrateExpensesAsync(string jsonPath)
    {
        Console.WriteLine("Импорт статей расходов (Expense)...");

        var existingExpenseIds = (await _db.Expenses.Select(e => e.Id).ToListAsync()).ToHashSet();

        using var stream = File.OpenRead(jsonPath);
        using var reader = new StreamReader(stream);

        var expenseMap = new Dictionary<Guid, ExpenseEntity>();
        string? line;

        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) continue;
            if (!IsDocType(root, "Expense")) continue;

            var c = GetDocBody(root);
            if (!TryGetGuidAnywhere(root, c, "id", out var id)) continue;
            if (existingExpenseIds.Contains(id)) continue;

            if (!expenseMap.TryGetValue(id, out var exp))
            {
                exp = new ExpenseEntity
                {
                    Id = id,
                    Name = "Расход",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                expenseMap[id] = exp;
            }

            if (TryGetString(c, "name", out var name) && !string.IsNullOrWhiteSpace(name)) exp.Name = name;
            if (TryGetString(c, "type", out var type)) exp.Type = type;
            if (TryGetString(c, "group", out var grp) || TryGetString(c, "groupName", out grp)) exp.Group = grp;
            if (TryGetString(c, "description", out var desc)) exp.Description = desc;
            if (TryGetBool(c, "isDisabled", out var dis)) exp.IsDisabled = dis;
            if (TryGetArray(c, "tags", out var tags)) exp.Tags = tags;
        }

        await BatchInsertAsync(_db.Expenses, expenseMap.Values.ToList());
        Console.WriteLine($"Готово! Всего импортировано Expense: {expenseMap.Count}");
    }

    public async Task MigrateFundsSlipsAsync(string jsonPath)
    {
        Console.WriteLine("Начинаем умную агрегацию кассовых ордеров (FundsSlip / Bill)...");

        var validUsers = (await _db.Users.Select(x => x.Id).ToListAsync()).ToHashSet();
        var validOffices = (await _db.Offices.Select(x => x.Id).ToListAsync()).ToHashSet();
        var validDepositories = (await _db.Depositories.Select(x => x.Id).ToListAsync()).ToHashSet();
        var validPartners = (await _db.Partners.Select(x => x.Id).ToListAsync()).ToHashSet();
        var validCurrencies = (await _db.Currencies.Select(x => x.Id).ToListAsync()).ToHashSet();

        var defaultCurrency = await _db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => c.IsDefault && !c.IsDisabled)
                              ?? await _db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => !c.IsDisabled);
        Guid? fallbackCurrencyId = defaultCurrency?.Id;

        var existingSlipIds = (await _db.FundsSlips.Select(s => s.Id).ToListAsync()).ToHashSet();
        var existingLineIds = (await _db.FundsSlipLines.Select(l => l.Id).ToListAsync()).ToHashSet();

        var slipMap = new Dictionary<Guid, FundsSlipEntity>();
        var linesMap = new Dictionary<Guid, FundsSlipLineEntity>();
        var rootTotals = new Dictionary<Guid, decimal>();

        using var stream = File.OpenRead(jsonPath);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) continue;

            if (!IsDocType(root, "FundsSlip") && !IsDocType(root, "Bill")) continue;

            var c = GetDocBody(root);
            if (!TryGetGuidAnywhere(root, c, "id", out var slipId)) continue;
            if (existingSlipIds.Contains(slipId)) continue;

            if (!slipMap.TryGetValue(slipId, out var slip))
            {
                slip = new FundsSlipEntity
                {
                    Id = slipId,
                    FundsSlipType = "Collection",
                    Date = DateTime.UtcNow,
                    DisplayCurrencyId = fallbackCurrencyId,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                slipMap[slipId] = slip;
            }

            if (TryGetString(c, "code", out var code) && !string.IsNullOrWhiteSpace(code)) slip.Code = code;

            // Проверяем все варианты имени типа операции
            if (TryGetString(c, "fundsSlipType", out var fst)) slip.FundsSlipType = fst;
            else if (TryGetString(c, "slipType", out var st)) slip.FundsSlipType = st;
            else if (TryGetString(c, "type", out var tp)) slip.FundsSlipType = tp;
            else if (TryGetInt(c, "billType", out var bt)) slip.FundsSlipType = bt == 1 ? "Payment" : "Collection";

            if (TryGetString(c, "userName", out var uName)) slip.UserName = uName;
            if (TryGetString(c, "group", out var grp) || TryGetString(c, "groupName", out grp)) slip.Group = grp;
            if (TryGetString(c, "description", out var desc)) slip.Description = desc;
            if (TryGetBool(c, "isCompleted", out var ic)) slip.IsCompleted = ic;
            if (TryGetBool(c, "isDisabled", out var idis)) slip.IsDisabled = idis;
            if (TryGetArray(c, "tags", out var tags)) slip.Tags = tags;
            if (TryGetDateTime(c, "date", out var dt)) slip.Date = dt;

            if (TryGetValidGuid(c, "userId", validUsers, out var uid)) slip.UserId = uid;
            if (TryGetValidGuid(c, "officeId", validOffices, out var oid)) slip.OfficeId = oid;
            if (TryGetValidGuid(c, "depositoryId", validDepositories, out var depid)) slip.DepositoryId = depid;
            if (TryGetValidGuid(c, "partnerId", validPartners, out var pid)) slip.PartnerId = pid;
            if (TryGetValidGuid(c, "displayCurrencyId", validCurrencies, out var dcid)) slip.DisplayCurrencyId = dcid;

            // Запоминаем сумму из шапки документа
            if (TryGetDecimal(c, "total", out var tot) || TryGetDecimal(c, "actionTotal", out tot) || TryGetDecimal(c, "amount", out tot))
            {
                if (tot > 0) rootTotals[slipId] = tot;
            }

            // Извлечение строк ордера
            if (TryGetSubList(root, c, "lines", out var linesArray))
            {
                int sort = linesMap.Values.Count(x => x.FundsSlipId == slipId);
                foreach (var el in linesArray.EnumerateArray())
                {
                    var lObj = GetElementBody(el);
                    if (!TryGetGuidAnywhere(el, lObj, "id", out var lineId)) lineId = Guid.NewGuid();
                    if (existingLineIds.Contains(lineId)) continue;

                    if (!linesMap.TryGetValue(lineId, out var lineEntity))
                    {
                        lineEntity = new FundsSlipLineEntity
                        {
                            Id = lineId,
                            FundsSlipId = slipId,
                            SortOrder = sort++
                        };
                        linesMap[lineId] = lineEntity;
                    }

                    if (TryGetDecimal(lObj, "amount", out var amt) || TryGetDecimal(lObj, "actionTotal", out amt) || TryGetDecimal(lObj, "total", out amt))
                        lineEntity.Amount = amt;

                    if (TryGetValidGuid(lObj, "currencyId", validCurrencies, out var curId))
                        lineEntity.CurrencyId = curId;
                    else
                        lineEntity.CurrencyId = slip.DisplayCurrencyId ?? fallbackCurrencyId;
                }
            }
        }

        // Страховка от "пустых документов": если строк нет, но сумма в шапке > 0, создаем строку
        foreach (var slip in slipMap.Values)
        {
            bool hasLines = linesMap.Values.Any(l => l.FundsSlipId == slip.Id);
            if (!hasLines && rootTotals.TryGetValue(slip.Id, out var rootTotal) && rootTotal > 0)
            {
                var fallbackLineId = Guid.NewGuid();
                linesMap[fallbackLineId] = new FundsSlipLineEntity
                {
                    Id = fallbackLineId,
                    FundsSlipId = slip.Id,
                    Amount = rootTotal,
                    CurrencyId = slip.DisplayCurrencyId ?? fallbackCurrencyId,
                    SortOrder = 0
                };
            }
        }

        var validSlipsWithLines = linesMap.Values.Select(x => x.FundsSlipId).ToHashSet();
        var validSlips = slipMap.Values.Where(s => validSlipsWithLines.Contains(s.Id)).ToList();

        await BatchInsertAsync(_db.FundsSlips, validSlips);
        await BatchInsertAsync(_db.FundsSlipLines, linesMap.Values.Where(l => validSlipsWithLines.Contains(l.FundsSlipId)).ToList());

        Console.WriteLine($"Готово! Всего импортировано FundsSlip/Bill: {validSlips.Count}, Строк: {linesMap.Count}");
    }

    public async Task MigrateExpenseSlipsAsync(string jsonPath)
    {
        Console.WriteLine("Начинаем умную агрегацию актов списания (ExpenseSlip)...");

        var validUsers = (await _db.Users.Select(x => x.Id).ToListAsync()).ToHashSet();
        var validOffices = (await _db.Offices.Select(x => x.Id).ToListAsync()).ToHashSet();
        var validDepositories = (await _db.Depositories.Select(x => x.Id).ToListAsync()).ToHashSet();
        var validCurrencies = (await _db.Currencies.Select(x => x.Id).ToListAsync()).ToHashSet();
        var validExpenses = (await _db.Expenses.Select(x => x.Id).ToListAsync()).ToHashSet();

        var existingSlipIds = (await _db.ExpenseSlips.Select(s => s.Id).ToListAsync()).ToHashSet();
        var existingLineIds = (await _db.ExpenseSlipLines.Select(l => l.Id).ToListAsync()).ToHashSet();

        var slipMap = new Dictionary<Guid, ExpenseSlipEntity>();
        var linesMap = new Dictionary<Guid, ExpenseSlipLineEntity>();
        var rootTotals = new Dictionary<Guid, decimal>();

        using var stream = File.OpenRead(jsonPath);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) continue;

            if (!IsDocType(root, "ExpenseSlip")) continue;

            var c = GetDocBody(root);
            if (!TryGetGuidAnywhere(root, c, "id", out var slipId)) continue;
            if (existingSlipIds.Contains(slipId)) continue;

            if (!slipMap.TryGetValue(slipId, out var slip))
            {
                slip = new ExpenseSlipEntity
                {
                    Id = slipId,
                    Date = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                slipMap[slipId] = slip;
            }

            if (TryGetString(c, "code", out var code) && !string.IsNullOrWhiteSpace(code)) slip.Code = code;
            if (TryGetString(c, "userName", out var uName)) slip.UserName = uName;
            if (TryGetString(c, "group", out var grp) || TryGetString(c, "groupName", out grp)) slip.GroupName = grp;
            if (TryGetString(c, "description", out var desc)) slip.Description = desc;
            if (TryGetBool(c, "isCompleted", out var ic)) slip.IsCompleted = ic;
            if (TryGetBool(c, "isDisabled", out var idis)) slip.IsDisabled = idis;
            if (TryGetArray(c, "tags", out var tags)) slip.Tags = tags;
            if (TryGetDateTime(c, "date", out var dt)) slip.Date = dt;

            if (TryGetValidGuid(c, "userId", validUsers, out var uid)) slip.UserId = uid;
            if (TryGetValidGuid(c, "officeId", validOffices, out var oid)) slip.OfficeId = oid;
            if (TryGetValidGuid(c, "depositoryId", validDepositories, out var depid)) slip.DepositoryId = depid;
            if (TryGetValidGuid(c, "displayCurrencyId", validCurrencies, out var dcid)) slip.DisplayCurrencyId = dcid;

            if (TryGetDecimal(c, "total", out var tot) || TryGetDecimal(c, "actionTotal", out tot) || TryGetDecimal(c, "amount", out tot))
            {
                if (tot > 0) rootTotals[slipId] = tot;
            }

            if (TryGetSubList(root, c, "lines", out var linesArray))
            {
                int sort = linesMap.Values.Count(x => x.ExpenseSlipId == slipId);
                foreach (var el in linesArray.EnumerateArray())
                {
                    var lObj = GetElementBody(el);
                    if (!TryGetGuidAnywhere(el, lObj, "id", out var lineId)) lineId = Guid.NewGuid();
                    if (existingLineIds.Contains(lineId)) continue;

                    if (!linesMap.TryGetValue(lineId, out var lineEntity))
                    {
                        lineEntity = new ExpenseSlipLineEntity
                        {
                            Id = lineId,
                            ExpenseSlipId = slipId,
                            SortOrder = sort++
                        };
                        linesMap[lineId] = lineEntity;
                    }

                    if (TryGetValidGuid(lObj, "expenseId", validExpenses, out var eId)) lineEntity.ExpenseId = eId;
                    if (TryGetValidGuid(lObj, "currencyId", validCurrencies, out var curId)) lineEntity.CurrencyId = curId;
                    if (TryGetDecimal(lObj, "amount", out var a) || TryGetDecimal(lObj, "total", out a)) lineEntity.Amount = a;
                }
            }
        }

        foreach (var slip in slipMap.Values)
        {
            bool hasLines = linesMap.Values.Any(l => l.ExpenseSlipId == slip.Id);
            if (!hasLines && rootTotals.TryGetValue(slip.Id, out var rootTotal) && rootTotal > 0)
            {
                var fallbackLineId = Guid.NewGuid();
                linesMap[fallbackLineId] = new ExpenseSlipLineEntity
                {
                    Id = fallbackLineId,
                    ExpenseSlipId = slip.Id,
                    Amount = rootTotal,
                    CurrencyId = slip.DisplayCurrencyId,
                    SortOrder = 0
                };
            }
        }

        var validSlipsWithLines = linesMap.Values.Select(x => x.ExpenseSlipId).ToHashSet();
        var validSlips = slipMap.Values.Where(s => validSlipsWithLines.Contains(s.Id)).ToList();

        await BatchInsertAsync(_db.ExpenseSlips, validSlips);
        await BatchInsertAsync(_db.ExpenseSlipLines, linesMap.Values.Where(l => validSlipsWithLines.Contains(l.ExpenseSlipId)).ToList());

        Console.WriteLine($"Готово! Всего импортировано ExpenseSlip: {validSlips.Count}, Строк: {linesMap.Count}");
    }

    public async Task MigrateFundsTransfersAsync(string jsonPath)
    {
        Console.WriteLine("Начинаем умную агрегацию перемещений денег (FundsTransfer)...");

        var validUsers = (await _db.Users.Select(x => x.Id).ToListAsync()).ToHashSet();
        var validDepositories = (await _db.Depositories.Select(x => x.Id).ToListAsync()).ToHashSet();
        var validCurrencies = (await _db.Currencies.Select(x => x.Id).ToListAsync()).ToHashSet();

        var existingTransferIds = (await _db.FundsTransfers.Select(t => t.Id).ToListAsync()).ToHashSet();
        var existingLineIds = (await _db.FundsTransferLines.Select(l => l.Id).ToListAsync()).ToHashSet();

        var transferMap = new Dictionary<Guid, FundsTransferEntity>();
        var linesMap = new Dictionary<Guid, FundsTransferLineEntity>();

        using var stream = File.OpenRead(jsonPath);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) continue;

            if (!IsDocType(root, "FundsTransfer")) continue;

            var c = GetDocBody(root);
            if (!TryGetGuidAnywhere(root, c, "id", out var transferId)) continue;
            if (existingTransferIds.Contains(transferId)) continue;

            if (!transferMap.TryGetValue(transferId, out var transfer))
            {
                transfer = new FundsTransferEntity
                {
                    Id = transferId,
                    Date = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                transferMap[transferId] = transfer;
            }

            if (TryGetString(c, "code", out var code) && !string.IsNullOrWhiteSpace(code)) transfer.Code = code;
            if (TryGetString(c, "userName", out var uName)) transfer.UserName = uName;
            if (TryGetString(c, "group", out var grp)) transfer.Group = grp;
            if (TryGetString(c, "description", out var desc)) transfer.Description = desc;
            if (TryGetBool(c, "isCompleted", out var ic)) transfer.IsCompleted = ic;
            if (TryGetBool(c, "isDisabled", out var idis)) transfer.IsDisabled = idis;
            if (TryGetArray(c, "tags", out var tags)) transfer.Tags = tags;
            if (TryGetDateTime(c, "date", out var dt)) transfer.Date = dt;

            if (TryGetValidGuid(c, "depositoryId", validDepositories, out var fromDep) || TryGetValidGuid(c, "fromDepositoryId", validDepositories, out fromDep))
                transfer.FromDepositoryId = fromDep;

            if (TryGetValidGuid(c, "destinationDepositoryId", validDepositories, out var toDep) || TryGetValidGuid(c, "toDepositoryId", validDepositories, out toDep))
                transfer.ToDepositoryId = toDep;

            if (TryGetValidGuid(c, "displayCurrencyId", validCurrencies, out var dcid)) transfer.DisplayCurrencyId = dcid;

            if (TryGetSubList(root, c, "lines", out var linesArray))
            {
                int sort = linesMap.Values.Count(x => x.FundsTransferId == transferId);
                foreach (var el in linesArray.EnumerateArray())
                {
                    var lObj = GetElementBody(el);
                    if (!TryGetGuidAnywhere(el, lObj, "id", out var lineId)) lineId = Guid.NewGuid();
                    if (existingLineIds.Contains(lineId)) continue;

                    if (!linesMap.TryGetValue(lineId, out var lineEntity))
                    {
                        lineEntity = new FundsTransferLineEntity
                        {
                            Id = lineId,
                            FundsTransferId = transferId,
                            SortOrder = sort++
                        };
                        linesMap[lineId] = lineEntity;
                    }

                    decimal amt = 0m;
                    if (TryGetDecimal(lObj, "amount", out var a) || TryGetDecimal(lObj, "total", out a)) amt = a;
                    decimal recAmt = TryGetDecimal(lObj, "receivedAmount", out var ra) ? ra : amt;

                    lineEntity.Amount = amt;
                    lineEntity.ReceivedAmount = recAmt;
                    if (TryGetValidGuid(lObj, "currencyId", validCurrencies, out var curId)) lineEntity.CurrencyId = curId;
                }
            }
        }

        var validTrWithLines = linesMap.Values.Select(x => x.FundsTransferId).ToHashSet();
        var validTransfers = transferMap.Values.Where(t => validTrWithLines.Contains(t.Id)).ToList();

        await BatchInsertAsync(_db.FundsTransfers, validTransfers);
        await BatchInsertAsync(_db.FundsTransferLines, linesMap.Values.Where(l => validTrWithLines.Contains(l.FundsTransferId)).ToList());

        Console.WriteLine($"Готово! Всего импортировано FundsTransfer: {validTransfers.Count}, Строк: {linesMap.Count}");
    }

    private async Task BatchInsertAsync<T>(DbSet<T> dbSet, List<T> items) where T : class
    {
        const int batchSize = 1000;
        for (int i = 0; i < items.Count; i += batchSize)
        {
            await dbSet.AddRangeAsync(items.Skip(i).Take(batchSize));
            await _db.SaveChangesAsync();
            _db.ChangeTracker.Clear();
        }
    }

    #region Хелперы
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

    private static bool TryGetPropertyCaseInsensitive(JsonElement el, string name, out JsonElement val) =>
        TryGetPropCaseInsensitive(el, name, out val);

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

    private static bool TryGetInt(JsonElement body, string name, out int res)
    {
        res = 0;
        if (TryGetPropAnywhere(body, name, out var v))
        {
            if (v.ValueKind == JsonValueKind.Number) { res = v.GetInt32(); return true; }
            if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var i)) { res = i; return true; }
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

    private static bool TryGetDateTime(JsonElement body, string name, out DateTime res)
    {
        res = DateTime.UtcNow;
        if (TryGetPropAnywhere(body, name, out var v) && v.ValueKind == JsonValueKind.String)
        {
            var str = v.GetString()?.Replace(" ", "+");
            if (DateTimeOffset.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
            {
                res = dto.UtcDateTime;
                return true;
            }
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
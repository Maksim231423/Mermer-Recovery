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
        using var stream = File.OpenRead(jsonPath);
        using var reader = new StreamReader(stream);

        var batch = new List<ExpenseEntity>();
        var seen = new HashSet<Guid>();
        string? line;

        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) continue;
            if (!IsDocType(root, "Expense")) continue;

            var c = GetTargetContainer(root);
            if (c.ValueKind != JsonValueKind.Object) continue;
            if (!TryGetGuid(root, c, "id", out var id) || !seen.Add(id)) continue;

            batch.Add(new ExpenseEntity
            {
                Id = id,
                Name = GetString(c, "name") ?? "Расход",
                Type = GetString(c, "type"),
                Group = GetString(c, "group") ?? GetString(c, "groupName"),
                Description = GetString(c, "description"),
                Tags = GetStringArray(c, "tags"),
                IsDisabled = GetBool(c, "isDisabled"),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            if (batch.Count >= 500)
            {
                await _db.Set<ExpenseEntity>().AddRangeAsync(batch);
                await _db.SaveChangesAsync();
                _db.ChangeTracker.Clear();
                batch.Clear();
            }
        }
        if (batch.Any())
        {
            await _db.Set<ExpenseEntity>().AddRangeAsync(batch);
            await _db.SaveChangesAsync();
            _db.ChangeTracker.Clear();
        }
    }

    public async Task MigrateFundsSlipsAsync(string jsonPath)
    {
        Console.WriteLine("Импорт кассовых ордеров (FundsSlip / Bill)...");
        var validUsers = (await _db.Users.Select(x => x.Id).ToListAsync()).ToHashSet();
        var validOffices = (await _db.Offices.Select(x => x.Id).ToListAsync()).ToHashSet();
        var validDepositories = (await _db.Depositories.Select(x => x.Id).ToListAsync()).ToHashSet();
        var validPartners = (await _db.Partners.Select(x => x.Id).ToListAsync()).ToHashSet();
        var validCurrencies = (await _db.Currencies.Select(x => x.Id).ToListAsync()).ToHashSet();

        using var stream = File.OpenRead(jsonPath);
        using var reader = new StreamReader(stream);

        var slips = new List<FundsSlipEntity>();
        var lines = new List<FundsSlipLineEntity>();
        var seenSlips = new HashSet<Guid>();
        var seenLines = new HashSet<Guid>();
        string? line;
        int totalImported = 0;

        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) continue;

            if (!IsDocType(root, "FundsSlip") && !IsDocType(root, "Bill")) continue;

            var c = GetTargetContainer(root);
            if (c.ValueKind != JsonValueKind.Object) continue;
            if (!TryGetGuid(root, c, "id", out var slipId) || !seenSlips.Add(slipId)) continue;

            string slipType = "Collection";
            string? rawType = GetString(c, "type") ?? GetString(c, "fundsSlipType");
            if (!string.IsNullOrEmpty(rawType))
            {
                slipType = rawType;
            }
            else if (c.TryGetProperty("billType", out var btProp) && btProp.ValueKind == JsonValueKind.Number)
            {
                slipType = btProp.GetInt32() == 1 ? "Payment" : "Collection";
            }

            var slip = new FundsSlipEntity
            {
                Id = slipId,
                Code = GetString(c, "code") ?? string.Empty,
                Date = GetDate(c, "date"),
                FundsSlipType = slipType,
                UserId = GetValidId(c, "userId", validUsers),
                UserName = GetString(c, "userName") ?? string.Empty,
                OfficeId = GetValidId(c, "officeId", validOffices),
                DepositoryId = GetValidId(c, "depositoryId", validDepositories),
                PartnerId = GetValidId(c, "partnerId", validPartners),
                DisplayCurrencyId = GetValidId(c, "displayCurrencyId", validCurrencies),
                IsCompleted = GetBool(c, "isCompleted"),
                IsDisabled = GetBool(c, "isDisabled"),
                Group = GetString(c, "group") ?? GetString(c, "groupName") ?? string.Empty,
                Tags = GetStringArray(c, "tags") ?? Array.Empty<string>(),
                Description = GetString(c, "description") ?? string.Empty,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            slips.Add(slip);

            // Безопасное извлечение коллекции строк
            JsonElement linesElement = default;
            if (c.TryGetProperty("lines", out var lProp) && lProp.ValueKind == JsonValueKind.Array)
            {
                linesElement = lProp;
            }
            else if (root.TryGetProperty("patch", out var pObj) && pObj.ValueKind == JsonValueKind.Object &&
                     pObj.TryGetProperty("subListPatches", out var slObj) && slObj.ValueKind == JsonValueKind.Object &&
                     slObj.TryGetProperty("lines", out var slLines) && slLines.ValueKind == JsonValueKind.Array)
            {
                linesElement = slLines;
            }

            if (linesElement.ValueKind == JsonValueKind.Array)
            {
                int sort = 0;
                foreach (var el in linesElement.EnumerateArray())
                {
                    if (el.ValueKind != JsonValueKind.Object) continue;

                    JsonElement lineObj = el;
                    if (el.TryGetProperty("propertyPatches", out var pPatches) && pPatches.ValueKind == JsonValueKind.Object)
                        lineObj = pPatches;

                    if (!TryGetGuid(el, lineObj, "id", out var lineId)) lineId = Guid.NewGuid();
                    if (!seenLines.Add(lineId)) continue;

                    lines.Add(new FundsSlipLineEntity
                    {
                        Id = lineId,
                        FundsSlipId = slipId,
                        Amount = GetDecimal(lineObj, "amount") ?? GetDecimal(lineObj, "actionTotal") ?? 0m,
                        CurrencyId = GetValidId(lineObj, "currencyId", validCurrencies) ?? slip.DisplayCurrencyId,
                        SortOrder = sort++
                    });
                }
            }
            else
            {
                decimal rootTotal = GetDecimal(c, "total") ?? GetDecimal(c, "actionTotal") ?? 0m;
                if (rootTotal > 0)
                {
                    lines.Add(new FundsSlipLineEntity
                    {
                        Id = Guid.NewGuid(),
                        FundsSlipId = slipId,
                        Amount = rootTotal,
                        CurrencyId = slip.DisplayCurrencyId,
                        SortOrder = 0
                    });
                }
            }

            if (slips.Count >= 500)
            {
                await _db.Set<FundsSlipEntity>().AddRangeAsync(slips);
                await _db.Set<FundsSlipLineEntity>().AddRangeAsync(lines);
                await _db.SaveChangesAsync();
                _db.ChangeTracker.Clear();
                totalImported += slips.Count;
                Console.WriteLine($"Сохранено кассовых ордеров: {totalImported}...");
                slips.Clear();
                lines.Clear();
            }
        }

        if (slips.Any())
        {
            await _db.Set<FundsSlipEntity>().AddRangeAsync(slips);
            await _db.Set<FundsSlipLineEntity>().AddRangeAsync(lines);
            await _db.SaveChangesAsync();
            _db.ChangeTracker.Clear();
            totalImported += slips.Count;
        }

        Console.WriteLine($"Готово! Всего импортировано FundsSlip: {totalImported}");
    }

    public async Task MigrateExpenseSlipsAsync(string jsonPath)
    {
        Console.WriteLine("Импорт актов списания расходов (ExpenseSlip)...");
        var validUsers = (await _db.Users.Select(x => x.Id).ToListAsync()).ToHashSet();
        var validOffices = (await _db.Offices.Select(x => x.Id).ToListAsync()).ToHashSet();
        var validDepositories = (await _db.Depositories.Select(x => x.Id).ToListAsync()).ToHashSet();
        var validCurrencies = (await _db.Currencies.Select(x => x.Id).ToListAsync()).ToHashSet();
        var validExpenses = (await _db.Set<ExpenseEntity>().Select(x => x.Id).ToListAsync()).ToHashSet();

        using var stream = File.OpenRead(jsonPath);
        using var reader = new StreamReader(stream);

        var slips = new List<ExpenseSlipEntity>();
        var lines = new List<ExpenseSlipLineEntity>();
        var seenSlips = new HashSet<Guid>();
        var seenLines = new HashSet<Guid>();
        string? line;
        int totalImported = 0;

        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) continue;
            if (!IsDocType(root, "ExpenseSlip")) continue;

            var c = GetTargetContainer(root);
            if (c.ValueKind != JsonValueKind.Object) continue;
            if (!TryGetGuid(root, c, "id", out var slipId) || !seenSlips.Add(slipId)) continue;

            slips.Add(new ExpenseSlipEntity
            {
                Id = slipId,
                Code = GetString(c, "code") ?? string.Empty,
                Date = GetDate(c, "date"),
                UserId = GetValidId(c, "userId", validUsers),
                UserName = GetString(c, "userName") ?? string.Empty,
                OfficeId = GetValidId(c, "officeId", validOffices),
                DepositoryId = GetValidId(c, "depositoryId", validDepositories),
                DisplayCurrencyId = GetValidId(c, "displayCurrencyId", validCurrencies),
                IsCompleted = GetBool(c, "isCompleted"),
                IsDisabled = GetBool(c, "isDisabled"),
                GroupName = GetString(c, "group") ?? GetString(c, "groupName") ?? string.Empty,
                Tags = GetStringArray(c, "tags") ?? Array.Empty<string>(),
                Description = GetString(c, "description") ?? string.Empty,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            JsonElement linesElement = default;
            if (c.TryGetProperty("lines", out var lArray) && lArray.ValueKind == JsonValueKind.Array)
            {
                linesElement = lArray;
            }
            else if (root.TryGetProperty("patch", out var pObj) && pObj.ValueKind == JsonValueKind.Object &&
                     pObj.TryGetProperty("subListPatches", out var slObj) && slObj.ValueKind == JsonValueKind.Object &&
                     slObj.TryGetProperty("lines", out var slLines) && slLines.ValueKind == JsonValueKind.Array)
            {
                linesElement = slLines;
            }

            if (linesElement.ValueKind == JsonValueKind.Array)
            {
                int sort = 0;
                foreach (var el in linesElement.EnumerateArray())
                {
                    if (el.ValueKind != JsonValueKind.Object) continue;

                    JsonElement lineObj = el;
                    if (el.TryGetProperty("propertyPatches", out var pPatches) && pPatches.ValueKind == JsonValueKind.Object)
                        lineObj = pPatches;

                    if (!TryGetGuid(el, lineObj, "id", out var lineId)) lineId = Guid.NewGuid();
                    if (!seenLines.Add(lineId)) continue;

                    lines.Add(new ExpenseSlipLineEntity
                    {
                        Id = lineId,
                        ExpenseSlipId = slipId,
                        ExpenseId = GetValidId(lineObj, "expenseId", validExpenses),
                        Amount = GetDecimal(lineObj, "amount") ?? 0m,
                        CurrencyId = GetValidId(lineObj, "currencyId", validCurrencies),
                        SortOrder = sort++
                    });
                }
            }

            if (slips.Count >= 500)
            {
                await _db.Set<ExpenseSlipEntity>().AddRangeAsync(slips);
                await _db.Set<ExpenseSlipLineEntity>().AddRangeAsync(lines);
                await _db.SaveChangesAsync();
                _db.ChangeTracker.Clear();
                totalImported += slips.Count;
                Console.WriteLine($"Сохранено актов списания: {totalImported}...");
                slips.Clear();
                lines.Clear();
            }
        }

        if (slips.Any())
        {
            await _db.Set<ExpenseSlipEntity>().AddRangeAsync(slips);
            await _db.Set<ExpenseSlipLineEntity>().AddRangeAsync(lines);
            await _db.SaveChangesAsync();
            _db.ChangeTracker.Clear();
            totalImported += slips.Count;
        }

        Console.WriteLine($"Готово! Всего импортировано ExpenseSlip: {totalImported}");
    }

    public async Task MigrateFundsTransfersAsync(string jsonPath)
    {
        Console.WriteLine("Импорт перемещений денежных средств (FundsTransfer)...");
        var validUsers = (await _db.Users.Select(x => x.Id).ToListAsync()).ToHashSet();
        var validDepositories = (await _db.Depositories.Select(x => x.Id).ToListAsync()).ToHashSet();
        var validCurrencies = (await _db.Currencies.Select(x => x.Id).ToListAsync()).ToHashSet();

        using var stream = File.OpenRead(jsonPath);
        using var reader = new StreamReader(stream);

        var transfers = new List<FundsTransferEntity>();
        var lines = new List<FundsTransferLineEntity>();
        var seenTransfers = new HashSet<Guid>();
        var seenLines = new HashSet<Guid>();
        string? line;
        int totalImported = 0;

        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) continue;
            if (!IsDocType(root, "FundsTransfer")) continue;

            var c = GetTargetContainer(root);
            if (c.ValueKind != JsonValueKind.Object) continue;
            if (!TryGetGuid(root, c, "id", out var transferId) || !seenTransfers.Add(transferId)) continue;

            transfers.Add(new FundsTransferEntity
            {
                Id = transferId,
                Code = GetString(c, "code") ?? string.Empty,
                Date = GetDate(c, "date"),
                FromDepositoryId = GetValidId(c, "depositoryId", validDepositories) ?? GetValidId(c, "fromDepositoryId", validDepositories),
                ToDepositoryId = GetValidId(c, "destinationDepositoryId", validDepositories) ?? GetValidId(c, "toDepositoryId", validDepositories),
                DisplayCurrencyId = GetValidId(c, "displayCurrencyId", validCurrencies),
                IsCompleted = GetBool(c, "isCompleted"),
                IsDisabled = GetBool(c, "isDisabled"),
                UserName = GetString(c, "userName") ?? "admin",
                Group = GetString(c, "group") ?? string.Empty,
                Tags = GetStringArray(c, "tags") ?? Array.Empty<string>(),
                Description = GetString(c, "description") ?? string.Empty,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            JsonElement linesElement = default;
            if (c.TryGetProperty("lines", out var lArray) && lArray.ValueKind == JsonValueKind.Array)
            {
                linesElement = lArray;
            }
            else if (root.TryGetProperty("patch", out var pObj) && pObj.ValueKind == JsonValueKind.Object &&
                     pObj.TryGetProperty("subListPatches", out var slObj) && slObj.ValueKind == JsonValueKind.Object &&
                     slObj.TryGetProperty("lines", out var slLines) && slLines.ValueKind == JsonValueKind.Array)
            {
                linesElement = slLines;
            }

            if (linesElement.ValueKind == JsonValueKind.Array)
            {
                int sort = 0;
                foreach (var el in linesElement.EnumerateArray())
                {
                    if (el.ValueKind != JsonValueKind.Object) continue;

                    JsonElement lineObj = el;
                    if (el.TryGetProperty("propertyPatches", out var pPatches) && pPatches.ValueKind == JsonValueKind.Object)
                        lineObj = pPatches;

                    if (!TryGetGuid(el, lineObj, "id", out var lineId)) lineId = Guid.NewGuid();
                    if (!seenLines.Add(lineId)) continue;

                    decimal amt = GetDecimal(lineObj, "amount") ?? 0m;
                    decimal recAmt = GetDecimal(lineObj, "receivedAmount") ?? amt;

                    lines.Add(new FundsTransferLineEntity
                    {
                        Id = lineId,
                        FundsTransferId = transferId,
                        Amount = amt,
                        ReceivedAmount = recAmt,
                        CurrencyId = GetValidId(lineObj, "currencyId", validCurrencies),
                        SortOrder = sort++
                    });
                }
            }

            if (transfers.Count >= 500)
            {
                await _db.Set<FundsTransferEntity>().AddRangeAsync(transfers);
                await _db.Set<FundsTransferLineEntity>().AddRangeAsync(lines);
                await _db.SaveChangesAsync();
                _db.ChangeTracker.Clear();
                totalImported += transfers.Count;
                Console.WriteLine($"Сохранено перемещений: {totalImported}...");
                transfers.Clear();
                lines.Clear();
            }
        }

        if (transfers.Any())
        {
            await _db.Set<FundsTransferEntity>().AddRangeAsync(transfers);
            await _db.Set<FundsTransferLineEntity>().AddRangeAsync(lines);
            await _db.SaveChangesAsync();
            _db.ChangeTracker.Clear();
            totalImported += transfers.Count;
        }

        Console.WriteLine($"Готово! Всего импортировано FundsTransfer: {totalImported}");
    }

    #region Хелперы
    private static bool IsDocType(JsonElement root, string t)
    {
        if (root.ValueKind != JsonValueKind.Object) return false;
        if (root.TryGetProperty("docType", out var d) && d.ValueKind == JsonValueKind.String && d.GetString() == t)
            return true;
        if (root.TryGetProperty("patch", out var p) && p.ValueKind == JsonValueKind.Object &&
            p.TryGetProperty("docType", out var pd) && pd.ValueKind == JsonValueKind.String && pd.GetString() == t)
            return true;
        return false;
    }

    private static JsonElement GetTargetContainer(JsonElement r)
    {
        if (r.ValueKind == JsonValueKind.Object && r.TryGetProperty("patch", out var p) && p.ValueKind == JsonValueKind.Object)
        {
            if (p.TryGetProperty("propertyPatches", out var pp) && pp.ValueKind == JsonValueKind.Object)
                return pp;
            return p;
        }
        return r;
    }

    private static bool TryGetGuid(JsonElement r, JsonElement c, string prop, out Guid res)
    {
        res = Guid.Empty;
        if (c.ValueKind == JsonValueKind.Object && c.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.String && Guid.TryParse(p.GetString(), out res))
            return true;
        if (r.ValueKind == JsonValueKind.Object && r.TryGetProperty("patch", out var patchObj) && patchObj.ValueKind == JsonValueKind.Object && patchObj.TryGetProperty(prop, out p) && p.ValueKind == JsonValueKind.String && Guid.TryParse(p.GetString(), out res))
            return true;
        if (r.ValueKind == JsonValueKind.Object && r.TryGetProperty(prop, out p) && p.ValueKind == JsonValueKind.String && Guid.TryParse(p.GetString(), out res))
            return true;
        return false;
    }

    private static Guid? GetValidId(JsonElement c, string p, HashSet<Guid> valids) =>
        c.ValueKind == JsonValueKind.Object && c.TryGetProperty(p, out var pr) && pr.ValueKind == JsonValueKind.String && Guid.TryParse(pr.GetString(), out var g) && valids.Contains(g) ? g : null;

    private static string? GetString(JsonElement c, string p) =>
        c.ValueKind == JsonValueKind.Object && c.TryGetProperty(p, out var pr) && pr.ValueKind == JsonValueKind.String ? pr.GetString() : null;

    private static decimal? GetDecimal(JsonElement c, string p)
    {
        if (c.ValueKind != JsonValueKind.Object || !c.TryGetProperty(p, out var pr)) return null;
        if (pr.ValueKind == JsonValueKind.Number) return pr.GetDecimal();
        if (pr.ValueKind == JsonValueKind.String && decimal.TryParse(pr.GetString(), out var d)) return d;
        return null;
    }

    private static bool GetBool(JsonElement c, string p) =>
        c.ValueKind == JsonValueKind.Object && c.TryGetProperty(p, out var pr) && pr.ValueKind == JsonValueKind.True;

    private static DateTime GetDate(JsonElement c, string p)
    {
        if (c.ValueKind == JsonValueKind.Object && c.TryGetProperty(p, out var pr) && pr.ValueKind == JsonValueKind.String && DateTime.TryParse(pr.GetString(), out var d))
            return DateTime.SpecifyKind(d, DateTimeKind.Utc);
        return DateTime.UtcNow;
    }

    private static string[]? GetStringArray(JsonElement c, string p) =>
        c.ValueKind == JsonValueKind.Object && c.TryGetProperty(p, out var pr) && pr.ValueKind == JsonValueKind.Array
            ? pr.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToArray()
            : null;
    #endregion
}
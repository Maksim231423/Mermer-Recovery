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
            if (!IsDocType(root, "Expense")) continue;

            var c = GetTargetContainer(root);
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
        Console.WriteLine("Импорт кассовых ордеров (FundsSlip)...");
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

        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!IsDocType(root, "FundsSlip")) continue;

            var c = GetTargetContainer(root);
            if (!TryGetGuid(root, c, "id", out var slipId) || !seenSlips.Add(slipId)) continue;

            var slip = new FundsSlipEntity
            {
                Id = slipId,
                Code = GetString(c, "code") ?? string.Empty,
                Date = GetDate(c, "date"),
                FundsSlipType = GetString(c, "type") ?? "Income",
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

            if (c.TryGetProperty("lines", out var lArray) && lArray.ValueKind == JsonValueKind.Array)
            {
                int sort = 0;
                foreach (var el in lArray.EnumerateArray())
                {
                    if (!el.TryGetProperty("id", out var lIdProp) || !Guid.TryParse(lIdProp.GetString(), out var lineId)) continue;
                    if (!seenLines.Add(lineId)) continue;

                    lines.Add(new FundsSlipLineEntity
                    {
                        Id = lineId,
                        FundsSlipId = slipId,
                        Amount = GetDecimal(el, "amount") ?? 0m,
                        CurrencyId = GetValidId(el, "currencyId", validCurrencies),
                        SortOrder = sort++
                    });
                }
            }

            if (slips.Count >= 500)
            {
                await _db.Set<FundsSlipEntity>().AddRangeAsync(slips);
                await _db.Set<FundsSlipLineEntity>().AddRangeAsync(lines);
                await _db.SaveChangesAsync();
                _db.ChangeTracker.Clear();
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
        }
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

        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!IsDocType(root, "ExpenseSlip")) continue;

            var c = GetTargetContainer(root);
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

            if (c.TryGetProperty("lines", out var lArray) && lArray.ValueKind == JsonValueKind.Array)
            {
                int sort = 0;
                foreach (var el in lArray.EnumerateArray())
                {
                    if (!el.TryGetProperty("id", out var lIdProp) || !Guid.TryParse(lIdProp.GetString(), out var lineId)) continue;
                    if (!seenLines.Add(lineId)) continue;

                    lines.Add(new ExpenseSlipLineEntity
                    {
                        Id = lineId,
                        ExpenseSlipId = slipId,
                        ExpenseId = GetValidId(el, "expenseId", validExpenses),
                        Amount = GetDecimal(el, "amount") ?? 0m,
                        CurrencyId = GetValidId(el, "currencyId", validCurrencies),
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
        }
    }

    #region Хелперы
    private static bool IsDocType(JsonElement root, string t)
    {
        if (root.ValueKind != JsonValueKind.Object) return false;

        if (root.TryGetProperty("docType", out var d) && d.ValueKind == JsonValueKind.String && d.GetString() == t)
            return true;

        if (root.TryGetProperty("patch", out var p) && p.ValueKind == JsonValueKind.Object)
        {
            if (p.TryGetProperty("docType", out var pd) && pd.ValueKind == JsonValueKind.String && pd.GetString() == t)
                return true;
        }

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
        if (r.ValueKind == JsonValueKind.Object && r.TryGetProperty(prop, out p) && p.ValueKind == JsonValueKind.String && Guid.TryParse(p.GetString(), out res))
            return true;
        return false;
    }

    private static Guid? GetValidId(JsonElement c, string p, HashSet<Guid> valids) =>
        c.ValueKind == JsonValueKind.Object && c.TryGetProperty(p, out var pr) && pr.ValueKind == JsonValueKind.String && Guid.TryParse(pr.GetString(), out var g) && valids.Contains(g) ? g : null;

    private static string? GetString(JsonElement c, string p) =>
        c.ValueKind == JsonValueKind.Object && c.TryGetProperty(p, out var pr) && pr.ValueKind == JsonValueKind.String ? pr.GetString() : null;

    private static decimal? GetDecimal(JsonElement c, string p) =>
        c.ValueKind == JsonValueKind.Object && c.TryGetProperty(p, out var pr) && pr.ValueKind == JsonValueKind.Number ? pr.GetDecimal() : null;

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
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

public class EnterpriseImportService
{
    private readonly MermerDbContext _dbContext;

    public EnterpriseImportService(MermerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task MigratePartnersAsync(string jsonFilePath)
    {
        Console.WriteLine("Начинаем импорт справочника Partner...");

        var existingIds = (await _dbContext.Partners.Select(x => x.Id).ToListAsync()).ToHashSet();

        using var stream = File.OpenRead(jsonFilePath);
        using var reader = new StreamReader(stream);

        var batch = new List<PartnerEntity>();
        var processedIds = new HashSet<Guid>();
        string? line;
        int totalImported = 0;
        int skipped = 0;

        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (IsTargetDocType(root, "Partner"))
            {
                var targetContainer = GetTargetContainer(root);
                if (!TryGetGuidProperty(root, targetContainer, "id", out var partnerId)) continue;
                if (existingIds.Contains(partnerId) || !processedIds.Add(partnerId))
                {
                    skipped++;
                    continue;
                }

                var pgPartner = new PartnerEntity
                {
                    Id = partnerId,
                    Code = GetStringProperty(targetContainer, "code") ?? string.Empty,
                    Name = GetStringProperty(targetContainer, "name") ?? "Без названия",
                    Phone = GetStringProperty(targetContainer, "phone"),
                    Address = GetStringProperty(targetContainer, "address"),
                    IsDisabled = GetBoolProperty(targetContainer, "isDisabled")
                };

                batch.Add(pgPartner);

                if (batch.Count >= 500)
                {
                    await _dbContext.Partners.AddRangeAsync(batch);
                    await _dbContext.SaveChangesAsync();
                    _dbContext.ChangeTracker.Clear();

                    totalImported += batch.Count;
                    Console.WriteLine($"Сохранено контрагентов: {totalImported}...");
                    batch.Clear();
                }
            }
        }

        if (batch.Any())
        {
            await _dbContext.Partners.AddRangeAsync(batch);
            await _dbContext.SaveChangesAsync();
            _dbContext.ChangeTracker.Clear();
            totalImported += batch.Count;
        }

        Console.WriteLine($"Готово! Всего импортировано Partner: {totalImported} (пропущено существующих: {skipped})");
    }

    public async Task MigrateOfficesAsync(string jsonFilePath)
    {
        Console.WriteLine("Начинаем импорт справочника Офисов (Office)...");

        var existingIds = (await _dbContext.Offices.Select(x => x.Id).ToListAsync()).ToHashSet();

        using var stream = File.OpenRead(jsonFilePath);
        using var reader = new StreamReader(stream);

        var batch = new List<OfficeEntity>();
        var processedIds = new HashSet<Guid>();
        string? line;
        int totalImported = 0;
        int skipped = 0;

        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (IsTargetDocType(root, "Office"))
            {
                var entity = ExtractOfficeEntity(root);
                if (entity != null)
                {
                    if (existingIds.Contains(entity.Id) || !processedIds.Add(entity.Id))
                    {
                        skipped++;
                        continue;
                    }

                    batch.Add(entity);

                    if (batch.Count >= 500)
                    {
                        await _dbContext.AddRangeAsync(batch);
                        await _dbContext.SaveChangesAsync();
                        _dbContext.ChangeTracker.Clear();

                        totalImported += batch.Count;
                        Console.WriteLine($"Сохранено офисов: {totalImported}...");
                        batch.Clear();
                    }
                }
            }
        }

        if (batch.Any())
        {
            await _dbContext.AddRangeAsync(batch);
            await _dbContext.SaveChangesAsync();
            _dbContext.ChangeTracker.Clear();
            totalImported += batch.Count;
        }

        Console.WriteLine($"Готово! Всего импортировано уникальных Офисов: {totalImported} (пропущено существующих: {skipped})");
    }

    public async Task MigrateWarehousesAsync(string jsonFilePath)
    {
        Console.WriteLine("Начинаем импорт справочника Складов (Warehouse)...");

        var existingOfficeIds = (await _dbContext.Offices.Select(o => o.Id).ToListAsync()).ToHashSet();
        var existingWarehouseIds = (await _dbContext.Warehouses.Select(w => w.Id).ToListAsync()).ToHashSet();

        using var stream = File.OpenRead(jsonFilePath);
        using var reader = new StreamReader(stream);

        var batch = new List<WarehouseEntity>();
        var processedIds = new HashSet<Guid>();
        string? line;
        int totalImported = 0;
        int skipped = 0;

        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (IsTargetDocType(root, "Warehouse"))
            {
                var entity = ExtractWarehouseEntity(root, existingOfficeIds);
                if (entity != null)
                {
                    if (existingWarehouseIds.Contains(entity.Id) || !processedIds.Add(entity.Id))
                    {
                        skipped++;
                        continue;
                    }

                    batch.Add(entity);

                    if (batch.Count >= 500)
                    {
                        await _dbContext.AddRangeAsync(batch);
                        await _dbContext.SaveChangesAsync();
                        _dbContext.ChangeTracker.Clear();

                        totalImported += batch.Count;
                        Console.WriteLine($"Сохранено складов: {totalImported}...");
                        batch.Clear();
                    }
                }
            }
        }

        if (batch.Any())
        {
            await _dbContext.AddRangeAsync(batch);
            await _dbContext.SaveChangesAsync();
            _dbContext.ChangeTracker.Clear();
            totalImported += batch.Count;
        }

        Console.WriteLine($"Готово! Всего импортировано уникальных Складов: {totalImported} (пропущено существующих: {skipped})");
    }

    public async Task MigrateDepositoriesAsync(string jsonFilePath)
    {
        Console.WriteLine("Начинаем импорт справочника Касс (Depository)...");

        var existingOfficeIds = (await _dbContext.Offices.Select(o => o.Id).ToListAsync()).ToHashSet();
        var existingDepositoryIds = (await _dbContext.Depositories.Select(d => d.Id).ToListAsync()).ToHashSet();

        using var stream = File.OpenRead(jsonFilePath);
        using var reader = new StreamReader(stream);

        var batch = new List<DepositoryEntity>();
        var processedIds = new HashSet<Guid>();
        string? line;
        int totalImported = 0;
        int skipped = 0;

        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (IsTargetDocType(root, "Depository"))
            {
                var entity = ExtractDepositoryEntity(root, existingOfficeIds);
                if (entity != null)
                {
                    if (existingDepositoryIds.Contains(entity.Id) || !processedIds.Add(entity.Id))
                    {
                        skipped++;
                        continue;
                    }

                    batch.Add(entity);

                    if (batch.Count >= 500)
                    {
                        await _dbContext.AddRangeAsync(batch);
                        await _dbContext.SaveChangesAsync();
                        _dbContext.ChangeTracker.Clear();

                        totalImported += batch.Count;
                        Console.WriteLine($"Сохранено касс: {totalImported}...");
                        batch.Clear();
                    }
                }
            }
        }

        if (batch.Any())
        {
            await _dbContext.AddRangeAsync(batch);
            await _dbContext.SaveChangesAsync();
            _dbContext.ChangeTracker.Clear();
            totalImported += batch.Count;
        }

        Console.WriteLine($"Готово! Всего импортировано уникальных Касс: {totalImported} (пропущено существующих: {skipped})");
    }

    public async Task MigrateCurrenciesAsync(string jsonFilePath)
    {
        Console.WriteLine("Начинаем импорт справочника Валют (Currency) и Курсов...");

        var existingCurrencyIds = (await _dbContext.Currencies.Select(c => c.Id).ToListAsync()).ToHashSet();
        var existingRateIds = (await _dbContext.CurrencyRates.Select(r => r.Id).ToListAsync()).ToHashSet();

        using var stream = File.OpenRead(jsonFilePath);
        using var reader = new StreamReader(stream);

        var currencyBatch = new List<CurrencyEntity>();
        var ratesBatch = new List<CurrencyRateEntity>();
        var processedCurrencyIds = new HashSet<Guid>();
        var processedRateIds = new HashSet<Guid>();

        string? line;
        int totalImportedCurrencies = 0;
        int totalImportedRates = 0;

        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (IsTargetDocType(root, "Currency"))
            {
                var targetContainer = GetTargetContainer(root);
                if (!TryGetGuidProperty(root, targetContainer, "id", out var currencyId)) continue;

                if (!existingCurrencyIds.Contains(currencyId) && processedCurrencyIds.Add(currencyId))
                {
                    var currency = new CurrencyEntity
                    {
                        Id = currencyId,
                        Name = GetStringProperty(targetContainer, "name") ?? "Unknown",
                        Decimals = targetContainer.TryGetProperty("decimals", out var dec) && dec.ValueKind == JsonValueKind.Number ? dec.GetInt32() : 2,
                        IsDefault = GetBoolProperty(targetContainer, "isDefault"),
                        Description = GetStringProperty(targetContainer, "description"),
                        IsDisabled = GetBoolProperty(targetContainer, "isDisabled"),
                        CreatedAt = DateTimeOffset.UtcNow,
                        UpdatedAt = DateTimeOffset.UtcNow
                    };

                    currencyBatch.Add(currency);
                }

                ExtractCurrencyRates(root, targetContainer, currencyId, ratesBatch, processedRateIds, existingRateIds);

                if (currencyBatch.Count >= 100 || ratesBatch.Count >= 500)
                {
                    if (currencyBatch.Any())
                    {
                        await _dbContext.Currencies.AddRangeAsync(currencyBatch);
                        totalImportedCurrencies += currencyBatch.Count;
                        currencyBatch.Clear();
                    }

                    if (ratesBatch.Any())
                    {
                        await _dbContext.Set<CurrencyRateEntity>().AddRangeAsync(ratesBatch);
                        totalImportedRates += ratesBatch.Count;
                        ratesBatch.Clear();
                    }

                    await _dbContext.SaveChangesAsync();
                    _dbContext.ChangeTracker.Clear();
                }
            }
        }

        if (currencyBatch.Any())
        {
            await _dbContext.Currencies.AddRangeAsync(currencyBatch);
            totalImportedCurrencies += currencyBatch.Count;
        }

        if (ratesBatch.Any())
        {
            await _dbContext.Set<CurrencyRateEntity>().AddRangeAsync(ratesBatch);
            totalImportedRates += ratesBatch.Count;
        }

        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        Console.WriteLine($"Готово! Новых валют: {totalImportedCurrencies}, Новых курсов: {totalImportedRates}");
    }

    public async Task MigrateUsersAsync(string jsonFilePath)
    {
        Console.WriteLine("Начинаем импорт справочника Пользователей (User)...");

        var existingUserIds = (await _dbContext.Users.Select(u => u.Id).ToListAsync()).ToHashSet();

        using var stream = File.OpenRead(jsonFilePath);
        using var reader = new StreamReader(stream);

        var batch = new List<UserEntity>();
        var processedIds = new HashSet<Guid>();
        string? line;
        int totalImported = 0;
        int skipped = 0;

        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (IsTargetDocType(root, "User"))
            {
                var targetContainer = GetTargetContainer(root);
                if (!TryGetGuidProperty(root, targetContainer, "id", out var id)) continue;

                if (existingUserIds.Contains(id) || !processedIds.Add(id))
                {
                    skipped++;
                    continue;
                }

                string username = GetStringProperty(targetContainer, "username") ?? string.Empty;
                if (string.IsNullOrEmpty(username)) continue;

                var user = new UserEntity
                {
                    Id = id,
                    Username = username,
                    Password = GetStringProperty(targetContainer, "password") ?? string.Empty,
                    IsAdmin = GetBoolProperty(targetContainer, "isAdmin"),
                    IsDisabled = GetBoolProperty(targetContainer, "isDisabled"),
                    Description = GetStringProperty(targetContainer, "description"),
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };

                batch.Add(user);

                if (batch.Count >= 500)
                {
                    await _dbContext.Set<UserEntity>().AddRangeAsync(batch);
                    await _dbContext.SaveChangesAsync();
                    _dbContext.ChangeTracker.Clear();

                    totalImported += batch.Count;
                    batch.Clear();
                }
            }
        }

        if (batch.Any())
        {
            await _dbContext.Set<UserEntity>().AddRangeAsync(batch);
            await _dbContext.SaveChangesAsync();
            _dbContext.ChangeTracker.Clear();
            totalImported += batch.Count;
        }

        Console.WriteLine($"Готово! Всего импортировано Пользователей: {totalImported} (пропущено: {skipped})");
    }

    #region Вспомогательные методы
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

    private static OfficeEntity? ExtractOfficeEntity(JsonElement root)
    {
        var targetContainer = GetTargetContainer(root);
        if (!TryGetGuidProperty(root, targetContainer, "id", out var id)) return null;

        return new OfficeEntity
        {
            Id = id,
            Name = GetStringProperty(targetContainer, "name") ?? "Без названия",
            Region = GetStringProperty(targetContainer, "region"),
            Description = GetStringProperty(targetContainer, "description"),
            IsDisabled = GetBoolProperty(targetContainer, "isDisabled"),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    private static WarehouseEntity? ExtractWarehouseEntity(JsonElement root, HashSet<Guid> existingOfficeIds)
    {
        var targetContainer = GetTargetContainer(root);
        if (!TryGetGuidProperty(root, targetContainer, "id", out var id)) return null;

        Guid? officeId = null;
        if (TryGetGuidProperty(root, targetContainer, "officeId", out var rawOfficeId) && existingOfficeIds.Contains(rawOfficeId))
            officeId = rawOfficeId;

        return new WarehouseEntity
        {
            Id = id,
            OfficeId = officeId,
            Name = GetStringProperty(targetContainer, "name") ?? string.Empty,
            Description = GetStringProperty(targetContainer, "description"),
            IsDisabled = GetBoolProperty(targetContainer, "isDisabled"),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    private static DepositoryEntity? ExtractDepositoryEntity(JsonElement root, HashSet<Guid> existingOfficeIds)
    {
        var targetContainer = GetTargetContainer(root);
        if (!TryGetGuidProperty(root, targetContainer, "id", out var id)) return null;

        Guid? officeId = null;
        if (TryGetGuidProperty(root, targetContainer, "officeId", out var rawOfficeId) && existingOfficeIds.Contains(rawOfficeId))
            officeId = rawOfficeId;

        return new DepositoryEntity
        {
            Id = id,
            OfficeId = officeId,
            Name = GetStringProperty(targetContainer, "name") ?? string.Empty,
            Description = GetStringProperty(targetContainer, "description"),
            IsDisabled = GetBoolProperty(targetContainer, "isDisabled"),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    private static void ExtractCurrencyRates(
        JsonElement root,
        JsonElement targetContainer,
        Guid currencyId,
        List<CurrencyRateEntity> ratesBatch,
        HashSet<Guid> processedRateIds,
        HashSet<Guid> existingRateIds)
    {
        JsonElement subList = default;
        if (targetContainer.TryGetProperty("subListPatches", out var slP)) subList = slP;
        else if (root.TryGetProperty("patch", out var p) && p.TryGetProperty("subListPatches", out var slP2)) subList = slP2;

        if (subList.ValueKind == JsonValueKind.Object && subList.TryGetProperty("rates", out var ratesArray) && ratesArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var rateItem in ratesArray.EnumerateArray())
            {
                if (!rateItem.TryGetProperty("id", out var idProp) || !Guid.TryParse(idProp.GetString(), out var rateId))
                    continue;

                if (existingRateIds.Contains(rateId) || !processedRateIds.Add(rateId)) continue;

                JsonElement props = rateItem;
                if (rateItem.TryGetProperty("propertyPatches", out var pProps) && pProps.ValueKind == JsonValueKind.Object)
                    props = pProps;

                DateTime validFrom = DateTime.UtcNow;
                if (props.TryGetProperty("validFrom", out var vfProp) && vfProp.ValueKind == JsonValueKind.String)
                {
                    DateTime.TryParse(vfProp.GetString(), out validFrom);
                }

                decimal multiplier = 1m;
                if (props.TryGetProperty("multiplier", out var mProp) && mProp.ValueKind == JsonValueKind.Number)
                    multiplier = mProp.GetDecimal();

                decimal divider = 1m;
                if (props.TryGetProperty("divider", out var dProp) && dProp.ValueKind == JsonValueKind.Number)
                    divider = dProp.GetDecimal();

                ratesBatch.Add(new CurrencyRateEntity
                {
                    Id = rateId,
                    CurrencyId = currencyId,
                    ValidFrom = DateTime.SpecifyKind(validFrom, DateTimeKind.Utc),
                    Multiplier = multiplier,
                    Divider = divider,
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }
        }
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
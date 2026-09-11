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

public class NomenclatureImportService
{
    private readonly MermerDbContext _dbContext;

    public NomenclatureImportService(MermerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task MigrateStocksAsync(string jsonFilePath)
    {
        Console.WriteLine("Начинаем умную агрегацию патчей Номенклатуры (Stock)...");

        var validCurrencyIds = (await _dbContext.Currencies.Select(c => c.Id).ToListAsync()).ToHashSet();

        // Загружаем существующие ключи для предотвращения конфликтов дубликатов
        var existingStockIds = (await _dbContext.Stocks.Select(s => s.Id).ToListAsync()).ToHashSet();
        var existingUnitIds = (await _dbContext.StockUnits.Select(u => u.Id).ToListAsync()).ToHashSet();
        var existingPriceIds = (await _dbContext.StockPrices.Select(p => p.Id).ToListAsync()).ToHashSet();

        using var stream = File.OpenRead(jsonFilePath);
        using var reader = new StreamReader(stream);

        var stockMap = new Dictionary<Guid, StockEntity>();
        var unitMap = new Dictionary<Guid, StockUnitEntity>();
        var priceMap = new Dictionary<Guid, StockPriceEntity>();

        string? line;
        int linesRead = 0;

        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            linesRead++;

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            JsonElement docBody = root;
            if (TryGetPropertyCaseInsensitive(root, "patch", out var patchObj) && patchObj.ValueKind == JsonValueKind.Object)
            {
                docBody = patchObj;
            }

            if (!TryGetPropertyCaseInsensitive(docBody, "docType", out var docTypeProp) ||
                !string.Equals(docTypeProp.GetString(), "Stock", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!TryGetGuidAnywhere(root, docBody, "id", out var stockId))
                continue;

            // Если товара ещё нет в PostgreSQL, формируем сущность для добавления
            if (!existingStockIds.Contains(stockId))
            {
                if (!stockMap.TryGetValue(stockId, out var stock))
                {
                    stock = new StockEntity
                    {
                        Id = stockId,
                        Name = "Без названия",
                        CreatedAt = DateTimeOffset.UtcNow,
                        UpdatedAt = DateTimeOffset.UtcNow
                    };
                    stockMap[stockId] = stock;
                }

                if (TryGetDynamicString(docBody, "code", out var code) && !string.IsNullOrWhiteSpace(code)) stock.Code = code;
                if (TryGetDynamicString(docBody, "name", out var name) && !string.IsNullOrWhiteSpace(name)) stock.Name = name;
                if (TryGetDynamicString(docBody, "shortName", out var shortName)) stock.ShortName = shortName;
                if (TryGetDynamicString(docBody, "type", out var type)) stock.Type = type;
                if (TryGetDynamicString(docBody, "group", out var group)) stock.Group = group;
                if (TryGetDynamicString(docBody, "description", out var desc)) stock.Description = desc;

                if (TryGetDynamicBool(docBody, "isDisabled", out var isDisabled)) stock.IsDisabled = isDisabled;
                if (TryGetDynamicDecimal(docBody, "limitMin", out var limitMin)) stock.LimitMin = limitMin;
                if (TryGetDynamicDecimal(docBody, "limitMax", out var limitMax)) stock.LimitMax = limitMax;

                if (TryGetDynamicArray(docBody, "tags", out var tags)) stock.Tags = tags;
                if (TryGetDynamicArray(docBody, "barcodes", out var barcodes)) stock.Barcodes = barcodes;
            }

            // Фильтруем дочерние единицы и цены, чтобы не пытаться вставить уже существующие ID
            ExtractUnits(root, docBody, stockId, unitMap, existingUnitIds);
            ExtractPrices(root, docBody, stockId, validCurrencyIds, priceMap, existingPriceIds);

            if (linesRead % 10000 == 0)
            {
                Console.WriteLine($"Прочитано {linesRead} строк... Новых товаров к добавлению: {stockMap.Count}");
            }
        }

        Console.WriteLine($"Агрегация завершена! Новых товаров: {stockMap.Count}, Единиц: {unitMap.Count}, Цен: {priceMap.Count}");
        await SaveAllInBatchesAsync(stockMap.Values.ToList(), unitMap.Values.ToList(), priceMap.Values.ToList());
        Console.WriteLine("Импорт номенклатуры успешно завершен!");
    }

    private async Task SaveAllInBatchesAsync(List<StockEntity> stocks, List<StockUnitEntity> units, List<StockPriceEntity> prices)
    {
        const int batchSize = 1000;

        for (int i = 0; i < stocks.Count; i += batchSize)
        {
            await _dbContext.Stocks.AddRangeAsync(stocks.Skip(i).Take(batchSize));
            await _dbContext.SaveChangesAsync();
            _dbContext.ChangeTracker.Clear();
        }

        for (int i = 0; i < units.Count; i += batchSize)
        {
            await _dbContext.StockUnits.AddRangeAsync(units.Skip(i).Take(batchSize));
            await _dbContext.SaveChangesAsync();
            _dbContext.ChangeTracker.Clear();
        }

        for (int i = 0; i < prices.Count; i += batchSize)
        {
            await _dbContext.StockPrices.AddRangeAsync(prices.Skip(i).Take(batchSize));
            await _dbContext.SaveChangesAsync();
            _dbContext.ChangeTracker.Clear();
        }
    }

    #region Умный парсинг

    private static bool TryGetPropertyCaseInsensitive(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        if (element.TryGetProperty(propertyName, out value)) return true;

        foreach (var prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool TryGetPropAnywhere(JsonElement docBody, string propName, out JsonElement value)
    {
        if (TryGetPropertyCaseInsensitive(docBody, propName, out value)) return true;

        if (TryGetPropertyCaseInsensitive(docBody, "propertyPatches", out var pp) && pp.ValueKind == JsonValueKind.Object)
        {
            if (TryGetPropertyCaseInsensitive(pp, propName, out value)) return true;
        }

        return false;
    }

    private static bool TryGetGuidAnywhere(JsonElement root, JsonElement docBody, string propName, out Guid result)
    {
        result = Guid.Empty;
        if (TryGetPropAnywhere(docBody, propName, out var val) && val.ValueKind == JsonValueKind.String && Guid.TryParse(val.GetString(), out result))
            return true;
        if (TryGetPropAnywhere(root, propName, out var rVal) && rVal.ValueKind == JsonValueKind.String && Guid.TryParse(rVal.GetString(), out result))
            return true;
        return false;
    }

    private static bool TryGetDynamicString(JsonElement docBody, string propName, out string? result)
    {
        result = null;
        if (TryGetPropAnywhere(docBody, propName, out var val) && val.ValueKind == JsonValueKind.String)
        {
            result = val.GetString();
            return true;
        }
        return false;
    }

    private static bool TryGetDynamicBool(JsonElement docBody, string propName, out bool result)
    {
        result = false;
        if (TryGetPropAnywhere(docBody, propName, out var val))
        {
            if (val.ValueKind == JsonValueKind.True) { result = true; return true; }
            if (val.ValueKind == JsonValueKind.False) { result = false; return true; }
        }
        return false;
    }

    private static bool TryGetDynamicDecimal(JsonElement docBody, string propName, out decimal result)
    {
        result = 0m;
        if (TryGetPropAnywhere(docBody, propName, out var val))
        {
            if (val.ValueKind == JsonValueKind.Number) { result = val.GetDecimal(); return true; }
            if (val.ValueKind == JsonValueKind.String && decimal.TryParse(val.GetString(), out var d)) { result = d; return true; }
        }
        return false;
    }

    private static bool TryGetDynamicArray(JsonElement docBody, string propName, out string[]? result)
    {
        result = null;
        if (TryGetPropAnywhere(docBody, propName, out var val) && val.ValueKind == JsonValueKind.Array)
        {
            result = val.EnumerateArray()
                      .Where(x => x.ValueKind == JsonValueKind.String)
                      .Select(x => x.GetString()!)
                      .ToArray();
            return true;
        }
        return false;
    }

    private static void ExtractUnits(JsonElement root, JsonElement docBody, Guid stockId, Dictionary<Guid, StockUnitEntity> unitMap, HashSet<Guid> existingUnitIds)
    {
        JsonElement unitsArray = default;
        if (!TryGetPropertyCaseInsensitive(docBody, "units", out unitsArray))
        {
            if (TryGetPropertyCaseInsensitive(docBody, "subListPatches", out var slp) && slp.ValueKind == JsonValueKind.Object)
                TryGetPropertyCaseInsensitive(slp, "units", out unitsArray);
            else if (root.TryGetProperty("patch", out var pObj) && pObj.TryGetProperty("subListPatches", out var rSlp))
                TryGetPropertyCaseInsensitive(rSlp, "units", out unitsArray);
        }

        if (unitsArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var elem in unitsArray.EnumerateArray())
            {
                if (!TryGetPropAnywhere(elem, "id", out var idProp) || !Guid.TryParse(idProp.GetString(), out var unitId))
                    continue;

                if (existingUnitIds.Contains(unitId)) continue;

                if (!unitMap.TryGetValue(unitId, out var unit))
                {
                    unit = new StockUnitEntity { Id = unitId, StockId = stockId, Name = "шт" };
                    unitMap[unitId] = unit;
                }

                if (TryGetDynamicString(elem, "name", out var name) && !string.IsNullOrWhiteSpace(name)) unit.Name = name;
                if (TryGetDynamicDecimal(elem, "multiplier", out var mult)) unit.Multiplier = mult > 0 ? mult : 1m;
                if (TryGetDynamicDecimal(elem, "divider", out var div)) unit.Divider = div > 0 ? div : 1m;
                if (TryGetDynamicBool(elem, "isDefault", out var isDef)) unit.IsDefault = isDef;
            }
        }
    }

    private static void ExtractPrices(JsonElement root, JsonElement docBody, Guid stockId, HashSet<Guid> validCurrencyIds, Dictionary<Guid, StockPriceEntity> priceMap, HashSet<Guid> existingPriceIds)
    {
        JsonElement pricesArray = default;
        if (!TryGetPropertyCaseInsensitive(docBody, "prices", out pricesArray))
        {
            if (TryGetPropertyCaseInsensitive(docBody, "subListPatches", out var slp) && slp.ValueKind == JsonValueKind.Object)
                TryGetPropertyCaseInsensitive(slp, "prices", out pricesArray);
            else if (root.TryGetProperty("patch", out var pObj) && pObj.TryGetProperty("subListPatches", out var rSlp))
                TryGetPropertyCaseInsensitive(rSlp, "prices", out pricesArray);
        }

        if (pricesArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var elem in pricesArray.EnumerateArray())
            {
                if (!TryGetPropAnywhere(elem, "id", out var idProp) || !Guid.TryParse(idProp.GetString(), out var priceId))
                    continue;

                if (existingPriceIds.Contains(priceId)) continue;

                if (!priceMap.TryGetValue(priceId, out var price))
                {
                    price = new StockPriceEntity { Id = priceId, StockId = stockId, CreatedAt = DateTimeOffset.UtcNow };
                    priceMap[priceId] = price;
                }

                if (TryGetPropAnywhere(elem, "currencyId", out var cIdProp) && Guid.TryParse(cIdProp.GetString(), out var cId))
                {
                    if (validCurrencyIds.Contains(cId)) price.CurrencyId = cId;
                }

                if (TryGetDynamicDecimal(elem, "price", out var pVal)) price.Price = pVal;
                if (TryGetDynamicString(elem, "priceGroup", out var pGroup)) price.PriceGroup = pGroup;

                if (TryGetPropAnywhere(elem, "validFrom", out var vfProp) && vfProp.ValueKind == JsonValueKind.String)
                {
                    if (DateTime.TryParse(vfProp.GetString(), out var vfDate))
                        price.ValidFrom = DateTime.SpecifyKind(vfDate, DateTimeKind.Utc);
                }
            }
        }
    }

    #endregion
}
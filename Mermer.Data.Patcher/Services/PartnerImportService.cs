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

public class PartnerImportService
{
    private readonly MermerDbContext _dbContext;

    public PartnerImportService(MermerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task MigratePartnersAsync(string jsonFilePath)
    {
        Console.WriteLine("Начинаем импорт справочника Partner...");

        using var stream = File.OpenRead(jsonFilePath);
        using var reader = new StreamReader(stream);

        var partnersBatch = new List<PartnerEntity>();
        var processedIds = new HashSet<Guid>();
        string? line;
        int totalImported = 0;

        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (IsTargetDocType(root, "Partner"))
            {
                var target = GetTargetContainer(root);
                if (!TryGetGuid(root, target, "id", out var partnerId)) continue;
                if (!processedIds.Add(partnerId)) continue;

                var pgPartner = new PartnerEntity
                {
                    Id = partnerId,
                    Code = GetString(target, "code") ?? string.Empty,
                    Name = GetString(target, "name") ?? "Без названия",
                    Phone = GetString(target, "phone"),
                    Address = GetString(target, "address"),
                    IsDisabled = GetBool(target, "isDisabled")
                };

                partnersBatch.Add(pgPartner);

                if (partnersBatch.Count >= 500)
                {
                    await _dbContext.Partners.AddRangeAsync(partnersBatch);
                    await _dbContext.SaveChangesAsync();
                    _dbContext.ChangeTracker.Clear();

                    totalImported += partnersBatch.Count;
                    Console.WriteLine($"Сохранено контрагентов: {totalImported}...");
                    partnersBatch.Clear();
                }
            }
        }

        if (partnersBatch.Any())
        {
            await _dbContext.Partners.AddRangeAsync(partnersBatch);
            await _dbContext.SaveChangesAsync();
            _dbContext.ChangeTracker.Clear();
            totalImported += partnersBatch.Count;
        }

        Console.WriteLine($"Готово! Всего импортировано Partner: {totalImported}");
    }

    #region Хелперы безопасного парсинга
    private static bool IsTargetDocType(JsonElement root, string docType)
    {
        if (root.ValueKind != JsonValueKind.Object) return false;
        if (root.TryGetProperty("docType", out var dt) && dt.ValueKind == JsonValueKind.String && dt.GetString() == docType)
            return true;
        if (root.TryGetProperty("patch", out var patch) && patch.ValueKind == JsonValueKind.Object &&
            patch.TryGetProperty("docType", out var pdt) && pdt.ValueKind == JsonValueKind.String && pdt.GetString() == docType)
            return true;
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

    private static bool TryGetGuid(JsonElement root, JsonElement target, string propName, out Guid result)
    {
        result = Guid.Empty;
        if (target.TryGetProperty(propName, out var prop) && prop.ValueKind == JsonValueKind.String && Guid.TryParse(prop.GetString(), out result))
            return true;
        if (root.TryGetProperty("patch", out var patch) && patch.TryGetProperty(propName, out var patchProp) && patchProp.ValueKind == JsonValueKind.String && Guid.TryParse(patchProp.GetString(), out result))
            return true;
        if (root.TryGetProperty(propName, out var rootProp) && rootProp.ValueKind == JsonValueKind.String && Guid.TryParse(rootProp.GetString(), out result))
            return true;
        return false;
    }

    private static string? GetString(JsonElement target, string propName)
    {
        if (target.TryGetProperty(propName, out var prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString();
        return null;
    }

    private static bool GetBool(JsonElement target, string propName)
    {
        if (target.TryGetProperty(propName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.True) return true;
            if (prop.ValueKind == JsonValueKind.False) return false;
        }
        return false;
    }
    #endregion
}
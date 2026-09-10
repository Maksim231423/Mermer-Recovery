using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Mermer.Data.Postgres;
using Mermer.Data.Postgres.Entities;

namespace Mermer.Api.Endpoints;

public static class EnterpriseEndpoints
{
    public static void MapEnterpriseEndpoints(this IEndpointRouteBuilder routes)
    {
        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = null };

        // Обработчик получения офисов
        async Task<IResult> GetOffices(MermerDbContext db)
        {
            var offices = await db.Offices.AsNoTracking().Where(o => !o.IsDisabled).ToListAsync();
            var result = offices.Select(o => new { Id = o.Id.ToString(), Name = o.Name, IsDisabled = o.IsDisabled });
            return Results.Json(result, jsonOptions);
        }

        routes.MapGet("/api/enterprise/offices", GetOffices).WithTags("Enterprise");
        routes.MapGet("/api/offices", GetOffices).WithTags("Enterprise");

        // Обработчик получения складов (поддерживаем оба маршрута)
        async Task<IResult> GetWarehouses(MermerDbContext db)
        {
            var warehouses = await db.Warehouses.AsNoTracking().Where(w => !w.IsDisabled).ToListAsync();
            var result = warehouses.Select(w => new
            {
                Id = w.Id.ToString(),
                Name = w.Name,
                OfficeId = w.OfficeId.HasValue ? w.OfficeId.Value.ToString() : null,
                IsDisabled = w.IsDisabled
            });
            return Results.Json(result, jsonOptions);
        }

        routes.MapGet("/api/enterprise/warehouses", GetWarehouses).WithTags("Enterprise");
        routes.MapGet("/api/warehouses", GetWarehouses).WithTags("Enterprise");

        // Валюты: список
        routes.MapGet("/api/currencies", async (MermerDbContext db, CancellationToken ct) =>
        {
            var currencies = await db.Currencies.Include(c => c.Rates).AsNoTracking().ToListAsync(ct);
            var result = currencies.Select(c => new
            {
                Id = c.Id.ToString(),
                Name = c.Name,
                Decimals = c.Decimals,
                IsDefault = c.IsDefault,
                Description = c.Description ?? string.Empty,
                IsDisabled = c.IsDisabled,
                Rates = c.Rates != null ? c.Rates.Select(r => (object)new
                {
                    Id = r.Id.ToString(),
                    CurrencyId = r.CurrencyId.ToString(),
                    ValidFrom = r.ValidFrom,
                    Multiplier = r.Multiplier,
                    Divider = r.Divider
                }).ToList() : new List<object>()
            });
            return Results.Json(result, jsonOptions);
        }).WithTags("Enterprise");

        // Валюты: по ID
        routes.MapGet("/api/currencies/{id}", async (string id, MermerDbContext db, CancellationToken ct) =>
        {
            if (!Guid.TryParse(id, out var guid)) return Results.NotFound();
            var c = await db.Currencies.Include(x => x.Rates).FirstOrDefaultAsync(x => x.Id == guid, ct);
            if (c == null) return Results.NotFound();

            var result = new
            {
                Id = c.Id.ToString(),
                Name = c.Name,
                Decimals = c.Decimals,
                IsDefault = c.IsDefault,
                Description = c.Description ?? string.Empty,
                IsDisabled = c.IsDisabled,
                Rates = c.Rates != null ? c.Rates.Select(r => (object)new
                {
                    Id = r.Id.ToString(),
                    CurrencyId = r.CurrencyId.ToString(),
                    ValidFrom = r.ValidFrom,
                    Multiplier = r.Multiplier,
                    Divider = r.Divider
                }).ToList() : new List<object>()
            };
            return Results.Json(result, jsonOptions);
        }).WithTags("Enterprise");

        // Сохранение валюты
        Func<HttpRequest, MermerDbContext, Task<IResult>> saveCurrencyHandler = async (request, db) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync();
            if (string.IsNullOrEmpty(body)) return Results.BadRequest("Empty body");

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            string? idStr = GetStringProperty(root, "id", "Id");
            Guid currencyId = Guid.TryParse(idStr, out var parsedGuid) && parsedGuid != Guid.Empty ? parsedGuid : Guid.NewGuid();

            var existing = await db.Currencies.Include(c => c.Rates).FirstOrDefaultAsync(c => c.Id == currencyId);

            string name = GetStringProperty(root, "name", "Name") ?? "Currency";
            string description = GetStringProperty(root, "description", "Description") ?? string.Empty;
            bool isDefault = GetBoolProperty(root, "isDefault", "IsDefault");
            bool isDisabled = GetBoolProperty(root, "isDisabled", "IsDisabled");
            int decimals = GetIntProperty(root, "decimals", "Decimals", 2);

            if (isDefault)
            {
                var currentDefaults = await db.Currencies.Where(c => c.IsDefault && c.Id != currencyId).ToListAsync();
                foreach (var cd in currentDefaults) cd.IsDefault = false;
            }

            var incomingRates = new List<CurrencyRateEntity>();
            if (TryGetPropertyCaseInsensitive(root, "rates", out var ratesProp) && ratesProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var rateJson in ratesProp.EnumerateArray())
                {
                    decimal multiplier = GetDecimalPropertyWithFallback(rateJson, 1m, "multiplier", "Multiplier", "rateMultiplier", "RateMultiplier");
                    decimal divider = GetDecimalPropertyWithFallback(rateJson, 1m, "divider", "Divider", "rateDivider", "RateDivider");
                    if (multiplier == 0m) multiplier = 1m;
                    if (divider == 0m) divider = 1m;

                    DateTime validFrom = DateTime.UtcNow.Date;
                    string? validFromStr = GetStringProperty(rateJson, "validFrom", "ValidFrom", "rateValidFrom", "RateValidFrom");
                    if (!string.IsNullOrEmpty(validFromStr) && DateTime.TryParse(validFromStr, out var parsedDate))
                    {
                        validFrom = parsedDate.Date;
                    }

                    incomingRates.Add(new CurrencyRateEntity
                    {
                        Id = Guid.NewGuid(),
                        CurrencyId = currencyId,
                        ValidFrom = validFrom,
                        Multiplier = multiplier,
                        Divider = divider,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            if (existing == null)
            {
                var entity = new CurrencyEntity
                {
                    Id = currencyId,
                    Name = name,
                    Description = description,
                    IsDefault = isDefault,
                    IsDisabled = isDisabled,
                    Decimals = decimals,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    Rates = incomingRates
                };
                await db.Currencies.AddAsync(entity);
            }
            else
            {
                existing.Name = name;
                existing.Description = description;
                existing.IsDefault = isDefault;
                existing.IsDisabled = isDisabled;
                existing.Decimals = decimals;
                existing.UpdatedAt = DateTime.UtcNow;

                if (existing.Rates != null && existing.Rates.Any())
                {
                    db.CurrencyRates.RemoveRange(existing.Rates);
                }

                foreach (var inc in incomingRates)
                {
                    inc.CurrencyId = existing.Id;
                    db.CurrencyRates.Add(inc);
                }
            }

            await db.SaveChangesAsync();
            return Results.Json(new { Id = currencyId.ToString(), Name = name }, jsonOptions);
        };

        routes.MapPost("/api/currencies", saveCurrencyHandler).WithTags("Enterprise");
        routes.MapPut("/api/currencies/{id}", saveCurrencyHandler).WithTags("Enterprise");
    }

    #region Helpers
    private static bool TryGetPropertyCaseInsensitive(JsonElement element, string propName, out JsonElement value)
    {
        foreach (var prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, propName, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static string? GetStringProperty(JsonElement element, params string[] propNames)
    {
        foreach (var name in propNames)
        {
            if (TryGetPropertyCaseInsensitive(element, name, out var prop) && prop.ValueKind == JsonValueKind.String)
                return prop.GetString();
        }
        return null;
    }

    private static bool GetBoolProperty(JsonElement element, params string[] propNames)
    {
        foreach (var name in propNames)
        {
            if (TryGetPropertyCaseInsensitive(element, name, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.True) return true;
                if (prop.ValueKind == JsonValueKind.False) return false;
            }
        }
        return false;
    }

    private static int GetIntProperty(JsonElement element, string name1, string name2, int fallback)
    {
        if (TryGetPropertyCaseInsensitive(element, name1, out var prop) || TryGetPropertyCaseInsensitive(element, name2, out prop))
        {
            if (prop.ValueKind == JsonValueKind.Number) return prop.GetInt32();
            if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out var val)) return val;
        }
        return fallback;
    }

    private static decimal GetDecimalPropertyWithFallback(JsonElement element, decimal fallback, params string[] propNames)
    {
        foreach (var name in propNames)
        {
            if (TryGetPropertyCaseInsensitive(element, name, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.Number) return prop.GetDecimal();
                if (prop.ValueKind == JsonValueKind.String && decimal.TryParse(prop.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var val)) return val;
            }
        }
        return fallback;
    }
    #endregion
}
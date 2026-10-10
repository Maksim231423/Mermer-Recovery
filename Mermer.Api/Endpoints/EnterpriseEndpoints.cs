using Dapper;
using Mermer.Data.Postgres;
using Mermer.Data.Postgres.Entities;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mermer.Api.Endpoints;

public static class EnterpriseEndpoints
{
    // ==========================================
    // СТАТИЧЕСКИЙ IN-MEMORY КЭШ СПРАВОЧНИКОВ
    // ==========================================
    private static object? _cachedOffices;
    private static object? _cachedWarehouses;
    private static object? _cachedCurrencies;

    public static void MapEnterpriseEndpoints(this IEndpointRouteBuilder routes)
    {
        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = null };

        // Обработчик получения офисов (с кэшированием)
        async Task<IResult> GetOffices(MermerDbContext db, CancellationToken ct)
        {
            if (_cachedOffices != null)
                return Results.Json(_cachedOffices, jsonOptions);

            var offices = await db.Offices.AsNoTracking().Where(o => !o.IsDisabled).ToListAsync(ct);
            _cachedOffices = offices.Select(o => new { Id = o.Id.ToString(), Name = o.Name, IsDisabled = o.IsDisabled }).ToList();

            return Results.Json(_cachedOffices, jsonOptions);
        }

        routes.MapGet("/api/enterprise/offices", GetOffices).WithTags("Enterprise");
        routes.MapGet("/api/offices", GetOffices).WithTags("Enterprise");

        // Обработчик получения складов (с кэшированием)
        async Task<IResult> GetWarehouses(MermerDbContext db, CancellationToken ct)
        {
            if (_cachedWarehouses != null)
                return Results.Json(_cachedWarehouses, jsonOptions);

            var warehouses = await db.Warehouses.AsNoTracking().Where(w => !w.IsDisabled).ToListAsync(ct);
            _cachedWarehouses = warehouses.Select(w => new
            {
                Id = w.Id.ToString(),
                Name = w.Name,
                OfficeId = w.OfficeId.HasValue ? w.OfficeId.Value.ToString() : null,
                IsDisabled = w.IsDisabled
            }).ToList();

            return Results.Json(_cachedWarehouses, jsonOptions);
        }

        routes.MapGet("/api/enterprise/warehouses", GetWarehouses).WithTags("Enterprise");
        routes.MapGet("/api/warehouses", GetWarehouses).WithTags("Enterprise");

        // Валюты: список (активные по умолчанию, все — только при includeDisabled=true)
        routes.MapGet("/api/currencies", async (bool? includeDisabled, MermerDbContext db, CancellationToken ct) =>
        {
            bool showDisabled = includeDisabled ?? false;

            var query = db.Currencies.Include(c => c.Rates).AsNoTracking();
            if (!showDisabled)
            {
                query = query.Where(c => !c.IsDisabled);
            }

            var currencies = await query.OrderBy(c => c.Name).ToListAsync(ct);
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
            }).ToList();

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

        // Сохранение валюты (Атомарный UPSERT без ошибок 23505)
        Func<HttpContext, MermerDbContext, CancellationToken, Task<IResult>> saveCurrencyHandler = async (context, db, ct) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var body = await reader.ReadToEndAsync(ct);
            if (string.IsNullOrEmpty(body)) return Results.BadRequest("Empty body");

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            string? routeId = context.GetRouteValue("id")?.ToString();
            string? jsonId = GetStringProperty(root, "id", "Id");
            string? effectiveIdStr = !string.IsNullOrWhiteSpace(routeId) ? routeId : jsonId;

            Guid currencyId = Guid.TryParse(effectiveIdStr, out var parsedGuid) && parsedGuid != Guid.Empty
                ? parsedGuid
                : Guid.NewGuid();

            string name = GetStringProperty(root, "name", "Name") ?? "Currency";
            string description = GetStringProperty(root, "description", "Description") ?? string.Empty;
            bool isDefault = GetBoolProperty(root, "isDefault", "IsDefault");
            bool isDisabled = GetBoolProperty(root, "isDisabled", "IsDisabled");
            int decimals = GetIntProperty(root, "decimals", "Decimals", 2);

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
                        var utcDate = parsedDate.ToUniversalTime();
                        validFrom = DateTime.SpecifyKind(utcDate.Date, DateTimeKind.Utc);
                    }
                    else
                    {
                        validFrom = DateTime.SpecifyKind(validFrom, DateTimeKind.Utc);
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

            var connStr = db.Database.GetConnectionString();
            await using var conn = new Npgsql.NpgsqlConnection(connStr);
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            try
            {
                // 1. Снимаем признак Default у остальных, если эта валюта выбрана по умолчанию
                if (isDefault)
                {
                    await conn.ExecuteAsync(
                        "UPDATE currencies SET is_default = false WHERE id != @id AND is_default = true;",
                        new { id = currencyId }, tx);
                }

                // 2. Атомарный UPSERT валюты (гарантирует отсутствие ошибки 23505 pkey)
                const string upsertCurrencySql = """
                    INSERT INTO currencies (id, name, description, is_default, is_disabled, decimals, created_at, updated_at)
                    VALUES (@id, @name, @description, @isDefault, @isDisabled, @decimals, @now, @now)
                    ON CONFLICT (id) DO UPDATE SET
                        name = EXCLUDED.name,
                        description = EXCLUDED.description,
                        is_default = EXCLUDED.is_default,
                        is_disabled = EXCLUDED.is_disabled,
                        decimals = EXCLUDED.decimals,
                        updated_at = EXCLUDED.updated_at;
                    """;

                await conn.ExecuteAsync(upsertCurrencySql, new
                {
                    id = currencyId,
                    name,
                    description,
                    isDefault,
                    isDisabled,
                    decimals,
                    now = DateTimeOffset.UtcNow
                }, tx);

                // 3. Обновляем курсы
                await conn.ExecuteAsync("DELETE FROM currency_rates WHERE currency_id = @id;", new { id = currencyId }, tx);

                if (incomingRates.Count > 0)
                {
                    const string insertRateSql = """
                        INSERT INTO currency_rates (id, currency_id, valid_from, multiplier, divider, created_at)
                        VALUES (@Id, @CurrencyId, @ValidFrom, @Multiplier, @Divider, @CreatedAt);
                        """;
                    await conn.ExecuteAsync(insertRateSql, incomingRates, tx);
                }

                await tx.CommitAsync(ct);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                Console.WriteLine($"[CURRENCY SAVE ERROR]: {ex}");
                return Results.Problem(ex.Message);
            }

            // Инвалидация кэша валют
            _cachedCurrencies = null;

            return Results.Json(new { Id = currencyId.ToString(), Name = name }, jsonOptions);
        };

        routes.MapPost("/api/currencies", async (HttpContext ctx, MermerDbContext db, CancellationToken ct) => await saveCurrencyHandler(ctx, db, ct)).WithTags("Enterprise");
        routes.MapPut("/api/currencies/{id}", async (HttpContext ctx, MermerDbContext db, CancellationToken ct) => await saveCurrencyHandler(ctx, db, ct)).WithTags("Enterprise");
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
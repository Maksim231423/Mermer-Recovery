using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Mermer.Api.Services;
using Mermer.Data.Postgres;

namespace Mermer.Api.Endpoints;

public static class LicensingEndpoints
{
    public static IEndpointRouteBuilder MapLicensingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/licensing").WithTags("Licensing");

        // ==========================================
        // 1. АКТИВАЦИЯ КЛЮЧА
        // ==========================================
        group.MapPost("/activate", async (
    HttpContext context,
    MermerDbContext db,
    IServerLicenseValidationService licenseValidator,
    ILicenseCryptoService cryptoService) =>
        {
            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            using var reader = new StreamReader(context.Request.Body);
            var rawBody = await reader.ReadToEndAsync();

            if (string.IsNullOrWhiteSpace(rawBody))
                return Results.BadRequest("Request body cannot be empty.");

            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;

            string? licenseId = GetPropString(root, "licenseId", "LicenseId");
            string? machineId = GetPropString(root, "machineId", "MachineId");
            string? note = GetPropString(root, "note", "Note");
            string requestedModule = GetRequestedModule(root);

            if (string.IsNullOrWhiteSpace(licenseId))
                return Results.BadRequest("License key identifier was not provided.");

            // --- КРИПТОГРАФИЧЕСКАЯ ВЕРИФИКАЦИЯ КЛЮЧА ---
            if (!cryptoService.TryValidateKey(licenseId, out var signedData, out string cryptoError))
            {
                await LogAction(db, licenseId, machineId, "ACTIVATE", "FAILED", $"Cryptographic failure: {cryptoError}", ip);
                return Results.BadRequest($"Invalid license signature: {cryptoError}");
            }

            // Проверка привязки к MachineId (если ключ сгенерирован под конкретное железо)
            if (!string.IsNullOrEmpty(signedData!.MachineId) &&
                !string.Equals(signedData.MachineId, "ANY", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(signedData.MachineId, machineId, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(machineId, "local-server-node", StringComparison.OrdinalIgnoreCase))
            {
                await LogAction(db, licenseId, machineId, "ACTIVATE", "FAILED", "Key is bound to different hardware ID", ip);
                return Results.BadRequest("This license key is cryptographically bound to a different machine ID.");
            }

            // Проверка разрешенного модуля
            var allowedModules = signedData.Modules.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            bool isAllowed = string.IsNullOrEmpty(requestedModule)
                             || allowedModules.Contains("all", StringComparer.OrdinalIgnoreCase)
                             || allowedModules.Contains(requestedModule, StringComparer.OrdinalIgnoreCase);

            if (!isAllowed)
            {
                await LogAction(db, licenseId, machineId, "ACTIVATE", "FAILED", $"Module '{requestedModule}' not permitted by signature", ip);
                return Results.BadRequest("This license key does not authorize the requested module.");
            }

            // Синхронизация с таблицей licenses в БД
            var cleanKey = licenseId.Trim();
            var license = await db.Licenses.FirstOrDefaultAsync(l => l.Key == cleanKey);
            if (license == null)
            {
                license = new Mermer.Data.Postgres.Entities.LicenseEntity
                {
                    Key = cleanKey,
                    ApplicationId = "55ddc105-8f48-4f78-b214-aea448d2a370",
                    ModuleId = signedData.Modules,
                    MachineId = machineId,
                    Note = note ?? signedData.ClientId,
                    ValidFrom = DateTimeOffset.UtcNow,
                    ValidTill = new DateTimeOffset(signedData.ValidTill, TimeSpan.Zero),
                    IsActive = true
                };
                db.Licenses.Add(license);
            }
            else
            {
                license.MachineId = machineId;
                license.ValidTill = new DateTimeOffset(signedData.ValidTill, TimeSpan.Zero);
                license.IsActive = true;
            }

            await db.SaveChangesAsync();
            licenseValidator.InvalidateCache();

            await LogAction(db, licenseId, machineId, "ACTIVATE", "SUCCESS", $"Activated signed module '{requestedModule}'", ip);

            string[] activeModules = allowedModules.Contains("all", StringComparer.OrdinalIgnoreCase)
                ? new[] { "dc60017b-9b20-46ca-8b2e-646de9965a9e", "9a953aa5-2fd9-418d-bcf7-fb5bd7d09553", "6b1495a1-60aa-4420-9c30-94718c121c26" }
                : new[] { requestedModule };

            var result = new
            {
                MachineId = machineId ?? string.Empty,
                ApplicationId = "55ddc105-8f48-4f78-b214-aea448d2a370",
                ApplicationModuleIds = activeModules,
                DateValidFrom = DateTime.UtcNow.AddDays(-1),
                DateValidTill = (DateTime?)signedData.ValidTill,
                Signature = "verified"
            };

            return Results.Ok(result);
        });

        // ==========================================
        // 2. РЕАКТИВАЦИЯ / ПРОДЛЕНИЕ
        // ==========================================
        group.MapPost("/reactivate", async (
            HttpContext context,
            MermerDbContext db,
            IServerLicenseValidationService licenseValidator) =>
        {
            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            using var reader = new StreamReader(context.Request.Body);
            var rawBody = await reader.ReadToEndAsync();

            if (string.IsNullOrWhiteSpace(rawBody))
                return Results.BadRequest("Reactivation request body is empty.");

            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;

            string? machineId = GetPropString(root, "machineId", "MachineId");
            string requestedModule = GetRequestedModule(root);

            if (string.IsNullOrWhiteSpace(machineId))
                return Results.BadRequest("Machine identifier parameter is required.");

            // Поиск активной лицензии с учетом комбинированных модулей
            var licenses = await db.Licenses
                .Where(l => (l.MachineId == machineId || l.MachineId == "local-server-node") && l.IsActive)
                .ToListAsync();

            var license = licenses.FirstOrDefault(l =>
            {
                var mods = (l.ModuleId ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                return mods.Contains("all", StringComparer.OrdinalIgnoreCase) || mods.Contains(requestedModule, StringComparer.OrdinalIgnoreCase);
            });

            if (license == null)
            {
                await LogAction(db, null, machineId, "REACTIVATE", "FAILED", "Active license not found for device.", ip);
                return Results.BadRequest("Active license for this device was not found. Please perform initial activation.");
            }

            if (license.ValidTill.HasValue && license.ValidTill.Value < DateTimeOffset.UtcNow)
            {
                await LogAction(db, license.Key, machineId, "REACTIVATE", "FAILED", "License validity period has expired.", ip);
                return Results.BadRequest("The license validity period has expired.");
            }

            await LogAction(db, license.Key, machineId, "REACTIVATE", "SUCCESS", "License successfully refreshed.", ip);

            // Мгновенный сброс кэша проверки
            licenseValidator.InvalidateCache();

            var result = new
            {
                MachineId = machineId,
                ApplicationId = license.ApplicationId,
                ApplicationModuleIds = new[] { requestedModule },
                DateValidFrom = license.ValidFrom.UtcDateTime.AddDays(-1),
                DateValidTill = license.ValidTill?.UtcDateTime,
                Signature = "verified"
            };

            return Results.Ok(result);
        });

        // ==========================================
        // 3. ДЕАКТИВАЦИЯ (ОТВЯЗКА ОТ ПК)
        // ==========================================
        group.MapPost("/deactivate", async (
            HttpContext context,
            MermerDbContext db,
            IServerLicenseValidationService licenseValidator) =>
        {
            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            using var reader = new StreamReader(context.Request.Body);
            var rawBody = await reader.ReadToEndAsync();

            if (!string.IsNullOrWhiteSpace(rawBody))
            {
                using var doc = JsonDocument.Parse(rawBody);
                string? machineId = GetPropString(doc.RootElement, "machineId", "MachineId");

                if (!string.IsNullOrEmpty(machineId))
                {
                    var licenses = await db.Licenses
                        .Where(l => l.MachineId == machineId)
                        .ToListAsync();

                    foreach (var lic in licenses)
                    {
                        lic.MachineId = null;
                        await LogAction(db, lic.Key, machineId, "DEACTIVATE", "SUCCESS", "License key unlinked from device.", ip);
                    }
                    await db.SaveChangesAsync();

                    // Мгновенный сброс кэша проверки
                    licenseValidator.InvalidateCache();
                }
            }

            return Results.Ok();
        });

        // ==========================================
        // 4. СТАТУС СЕРВЕРНОЙ ЛИЦЕНЗИИ (ДЛЯ UI И VDS)
        // ==========================================
        group.MapGet("/status", async (
            MermerDbContext db,
            IServerLicenseValidationService licenseValidator) =>
        {
            bool isServerActive = await licenseValidator.IsServerLicenseValidAsync();

            var activeLicense = await db.Licenses
                .Where(l => l.IsActive)
                .OrderByDescending(l => l.ValidTill)
                .FirstOrDefaultAsync();

            return Results.Ok(new
            {
                isServerActive = isServerActive,
                isActive = isServerActive,
                reason = isServerActive ? "Server license is active." : "Active server license not found or expired.",
                validTill = activeLicense?.ValidTill?.UtcDateTime,
                machineId = activeLicense?.MachineId
            });
        });

        return app;
    }

    private static async Task LogAction(MermerDbContext db, string? key, string? machineId, string action, string status, string details, string ip)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO license_logs (license_key, machine_id, action, status, details, ip_address) VALUES ({0}, {1}, {2}, {3}, {4}, {5})",
                key ?? (object)DBNull.Value,
                machineId ?? (object)DBNull.Value,
                action,
                status,
                details,
                ip
            );
        }
        catch { }
    }

    private static string GetRequestedModule(JsonElement root)
    {
        if (TryGetProp(root, "applicationModuleIds", out var modProp) || TryGetProp(root, "ApplicationModuleIds", out modProp))
        {
            if (modProp.ValueKind == JsonValueKind.Array && modProp.GetArrayLength() > 0)
            {
                return modProp.EnumerateArray().First().GetString() ?? string.Empty;
            }
        }
        return string.Empty;
    }

    private static bool TryGetProp(JsonElement el, string name, out JsonElement val)
    {
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

    private static string? GetPropString(JsonElement el, params string[] names)
    {
        foreach (var n in names)
        {
            if (TryGetProp(el, n, out var p) && p.ValueKind == JsonValueKind.String)
                return p.GetString();
        }
        return null;
    }
}
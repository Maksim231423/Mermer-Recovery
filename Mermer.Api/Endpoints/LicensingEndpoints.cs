using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Mermer.Api.DTOs;
using Mermer.Api.Services;
using Mermer.Data.Postgres;

namespace Mermer.Api.Endpoints;

public static class LicensingEndpoints
{
    public const string ServerModuleId = "9a953aa5-2fd9-418d-bcf7-fb5bd7d09553";
    public const string ClientModuleId = "dc60017b-9b20-46ca-8b2e-646de9965a9e";
    public const string SyncModuleId = "6b1495a1-60aa-4420-9c30-94718c121c26";

    public static IEndpointRouteBuilder MapLicensingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/licensing").WithTags("Licensing");

        // 1. АКТИВАЦИЯ
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

            if (!cryptoService.TryValidateKey(licenseId, out var signedData, out string cryptoError))
            {
                await LogAction(db, licenseId, machineId, "ACTIVATE", "FAILED", $"Cryptographic failure: {cryptoError}", ip);
                return Results.BadRequest($"Invalid license signature: {cryptoError}");
            }

            if (!string.IsNullOrEmpty(signedData!.MachineId) &&
                !string.Equals(signedData.MachineId, "ANY", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(signedData.MachineId, machineId, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(machineId, "local-server-node", StringComparison.OrdinalIgnoreCase))
            {
                await LogAction(db, licenseId, machineId, "ACTIVATE", "FAILED", "Key is bound to different hardware ID", ip);
                return Results.BadRequest("This license key is cryptographically bound to a different machine ID.");
            }

            var allowedModules = signedData.Modules.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            bool isAllowed = string.IsNullOrEmpty(requestedModule)
                             || allowedModules.Contains("all", StringComparer.OrdinalIgnoreCase)
                             || allowedModules.Contains(requestedModule, StringComparer.OrdinalIgnoreCase);

            if (!isAllowed)
            {
                await LogAction(db, licenseId, machineId, "ACTIVATE", "FAILED", $"Module '{requestedModule}' not permitted by signature", ip);
                return Results.BadRequest("This license key does not authorize the requested module.");
            }

            var cleanKey = licenseId.Trim();
            var now = DateTimeOffset.UtcNow;
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
                    ValidFrom = now,
                    ValidTill = new DateTimeOffset(signedData.ValidTill, TimeSpan.Zero),
                    IsActive = true,
                    CreatedAt = now
                };
                db.Licenses.Add(license);
            }
            else
            {
                license.MachineId = machineId;
                license.ValidFrom = now;
                license.ValidTill = new DateTimeOffset(signedData.ValidTill, TimeSpan.Zero);
                license.IsActive = true;
                if (!string.IsNullOrWhiteSpace(note))
                {
                    license.Note = note;
                }
            }

            await db.SaveChangesAsync();
            licenseValidator.InvalidateCache();

            await LogAction(db, licenseId, machineId, "ACTIVATE", "SUCCESS", $"Activated signed module '{requestedModule}'", ip);

            string[] activeModules = allowedModules.Contains("all", StringComparer.OrdinalIgnoreCase)
                ? new[] { ClientModuleId, ServerModuleId, SyncModuleId }
                : new[] { requestedModule };

            var result = new
            {
                MachineId = machineId ?? string.Empty,
                ApplicationId = "55ddc105-8f48-4f78-b214-aea448d2a370",
                ApplicationModuleIds = activeModules,
                DateValidFrom = license.ValidFrom.UtcDateTime,
                DateValidTill = (DateTime?)signedData.ValidTill,
                Signature = "verified"
            };

            return Results.Ok(result);
        });

        // 2. РЕАКТИВАЦИЯ
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
            licenseValidator.InvalidateCache();

            var result = new
            {
                MachineId = machineId,
                ApplicationId = license.ApplicationId,
                ApplicationModuleIds = new[] { requestedModule },
                DateValidFrom = license.ValidFrom.UtcDateTime,
                DateValidTill = license.ValidTill?.UtcDateTime,
                Signature = "verified"
            };

            return Results.Ok(result);
        });

        // 3. ТОЧЕЧНАЯ ДЕАКТИВАЦИЯ
        group.MapPost("/deactivate", async (
            HttpContext context,
            MermerDbContext db,
            IServerLicenseValidationService licenseValidator) =>
        {
            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            using var reader = new StreamReader(context.Request.Body);
            var rawBody = await reader.ReadToEndAsync();

            if (string.IsNullOrWhiteSpace(rawBody))
                return Results.Ok();

            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;
            string? machineId = GetPropString(root, "machineId", "MachineId");
            string requestedModule = GetRequestedModule(root);

            // Нормализация GUID модулей
            if (string.Equals(requestedModule, "Sync", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(requestedModule, "VdsSync", StringComparison.OrdinalIgnoreCase))
                requestedModule = SyncModuleId;
            else if (string.Equals(requestedModule, "Server", StringComparison.OrdinalIgnoreCase))
                requestedModule = ServerModuleId;
            else if (string.Equals(requestedModule, "Client", StringComparison.OrdinalIgnoreCase))
                requestedModule = ClientModuleId;

            if (string.IsNullOrEmpty(machineId))
                return Results.Ok();

            var licenses = await db.Licenses
                .Where(l => l.MachineId == machineId || l.MachineId == "local-server-node")
                .ToListAsync();

            foreach (var lic in licenses)
            {
                if (string.IsNullOrWhiteSpace(lic.ModuleId))
                    continue;

                // Точечная деактивация Sync: Server не затрагивается
                if (string.Equals(requestedModule, SyncModuleId, StringComparison.OrdinalIgnoreCase))
                {
                    if (lic.ModuleId.Equals("all", StringComparison.OrdinalIgnoreCase))
                    {
                        lic.ModuleId = $"{ClientModuleId},{ServerModuleId}";
                    }
                    else
                    {
                        var modules = lic.ModuleId
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .ToList();

                        modules.RemoveAll(m => string.Equals(m, SyncModuleId, StringComparison.OrdinalIgnoreCase));

                        if (modules.Count > 0)
                        {
                            lic.ModuleId = string.Join(",", modules);
                        }
                        else
                        {
                            lic.MachineId = null;
                            lic.IsActive = false;
                        }
                    }
                    await LogAction(db, lic.Key, machineId, "DEACTIVATE", "SUCCESS", $"Sync module detached. Remaining: {lic.ModuleId}", ip);
                }
                // Точечная деактивация Server
                else if (string.Equals(requestedModule, ServerModuleId, StringComparison.OrdinalIgnoreCase))
                {
                    if (lic.ModuleId.Equals("all", StringComparison.OrdinalIgnoreCase))
                    {
                        lic.ModuleId = $"{ClientModuleId},{SyncModuleId}";
                    }
                    else
                    {
                        var modules = lic.ModuleId
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .ToList();

                        modules.RemoveAll(m => string.Equals(m, ServerModuleId, StringComparison.OrdinalIgnoreCase));

                        if (modules.Count > 0)
                        {
                            lic.ModuleId = string.Join(",", modules);
                        }
                        else
                        {
                            lic.MachineId = null;
                            lic.IsActive = false;
                        }
                    }
                    await LogAction(db, lic.Key, machineId, "DEACTIVATE", "SUCCESS", $"Server module detached. Remaining: {lic.ModuleId}", ip);
                }
            }

            await db.SaveChangesAsync();
            licenseValidator.InvalidateCache();

            return Results.Ok();
        });

        // 4. СТАТУС ЛИЦЕНЗИЙ
        group.MapGet("/status", async (
            MermerDbContext db,
            IServerLicenseValidationService licenseValidator,
            IOptionsMonitor<SyncOptions> syncOptionsMonitor) =>
        {
            var now = DateTimeOffset.UtcNow;
            bool isCloudSyncEnabled = syncOptionsMonitor.CurrentValue.Enabled;

            var activeLicenses = await db.Licenses
                .AsNoTracking()
                .Where(l => l.IsActive && (!l.ValidTill.HasValue || l.ValidTill.Value > now))
                .ToListAsync();

            bool HasModule(string targetModuleId) => activeLicenses.Any(l =>
            {
                if (string.IsNullOrWhiteSpace(l.ModuleId)) return false;
                var parts = l.ModuleId.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                return parts.Contains("all", StringComparer.OrdinalIgnoreCase) ||
                       parts.Contains(targetModuleId, StringComparer.OrdinalIgnoreCase);
            });

            bool isServerLicensed = HasModule(ServerModuleId);
            bool isSyncLicensed = HasModule(SyncModuleId);
            bool isClientLicensed = HasModule(ClientModuleId);

            bool isServerActive = isServerLicensed && await licenseValidator.IsServerLicenseValidAsync();
            bool isSyncActive = isSyncLicensed;

            bool overallActive = isCloudSyncEnabled
                ? (isServerActive && isSyncActive)
                : isServerActive;

            string message = overallActive
                ? (isCloudSyncEnabled
                    ? "All required licenses (Server & Cloud Sync) are active."
                    : "Server License is active. Cloud synchronization is disabled.")
                : (!isServerActive
                    ? "Server License is missing or expired."
                    : "Cloud Synchronization License is missing or expired. Please activate it or disable Cloud Sync in settings.");

            var latestLicense = activeLicenses
                .OrderByDescending(l => l.ValidTill ?? DateTimeOffset.MaxValue)
                .FirstOrDefault();

            return Results.Ok(new
            {
                isServerLicensed = isServerLicensed,
                isServerActive = isServerActive,
                isSyncLicensed = isSyncLicensed,
                isSyncActive = isSyncActive,
                isClientLicensed = isClientLicensed,
                isCloudSyncEnabled = isCloudSyncEnabled,
                hasActiveLicense = overallActive,
                isActive = overallActive,
                modules = activeLicenses.Select(l => l.ModuleId).Distinct().ToList(),
                validTill = latestLicense?.ValidTill?.UtcDateTime,
                machineId = latestLicense?.MachineId,
                message = message
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
        string[] candidateProps = {
            "applicationModuleIds", "ApplicationModuleIds",
            "applicationModuleId", "ApplicationModuleId",
            "moduleId", "ModuleId",
            "module", "Module"
        };

        foreach (var prop in candidateProps)
        {
            if (TryGetProp(root, prop, out var modProp))
            {
                if (modProp.ValueKind == JsonValueKind.Array && modProp.GetArrayLength() > 0)
                {
                    return modProp.EnumerateArray().First().GetString() ?? string.Empty;
                }
                if (modProp.ValueKind == JsonValueKind.String)
                {
                    return modProp.GetString() ?? string.Empty;
                }
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
        foreach (var name in names)
        {
            if (TryGetProp(el, name, out var p) && p.ValueKind == JsonValueKind.String)
                return p.GetString();
        }
        return null;
    }
}
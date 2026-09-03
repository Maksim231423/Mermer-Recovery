using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
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
        group.MapPost("/activate", async (HttpContext context, MermerDbContext db) =>
        {
            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            using var reader = new StreamReader(context.Request.Body);
            var rawBody = await reader.ReadToEndAsync();

            if (string.IsNullOrWhiteSpace(rawBody))
            {
                await LogAction(db, null, null, "ACTIVATE", "FAILED", "Пустое тело запроса", ip);
                return Results.BadRequest("Пустое тело запроса.");
            }

            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;

            string? licenseId = GetPropString(root, "licenseId", "LicenseId");
            string? machineId = GetPropString(root, "machineId", "MachineId");
            string? note = GetPropString(root, "note", "Note");
            string requestedModule = GetRequestedModule(root);

            if (string.IsNullOrWhiteSpace(licenseId))
            {
                await LogAction(db, null, machineId, "ACTIVATE", "FAILED", "Не передан LicenseId", ip);
                return Results.BadRequest("Не передан лицензионный ключ.");
            }

            var cleanKey = licenseId.Trim().ToLower();
            var license = await db.Licenses.FirstOrDefaultAsync(l => l.Key.ToLower() == cleanKey);

            if (license == null)
            {
                await LogAction(db, licenseId, machineId, "ACTIVATE", "FAILED", "Ключ не найден", ip);
                return Results.BadRequest($"Лицензионный ключ '{licenseId}' не найден в системе.");
            }

            if (!license.IsActive)
            {
                await LogAction(db, licenseId, machineId, "ACTIVATE", "FAILED", "Ключ заблокирован", ip);
                return Results.BadRequest("Лицензия заблокирована или аннулирована.");
            }

            // Проверка модуля: клиент, сервер, синхронизатор
            if (!string.IsNullOrEmpty(requestedModule) &&
                !string.Equals(license.ModuleId, requestedModule, StringComparison.OrdinalIgnoreCase))
            {
                string expected = license.ModuleId switch
                {
                    "dc60017b-9b20-46ca-8b2e-646de9965a9e" => "клиента (рабочего места)",
                    "9a953aa5-2fd9-418d-bcf7-fb5bd7d09553" => "сервера",
                    "6b1495a1-60aa-4420-9c30-94718c121c26" => "синхронизации",
                    _ => "другого модуля"
                };
                await LogAction(db, licenseId, machineId, "ACTIVATE", "FAILED", $"Неверный модуль. Требуется: {expected}", ip);
                return Results.BadRequest($"Этот ключ предназначен для {expected}.");
            }

            // Привязка к оборудованию
            if (string.IsNullOrEmpty(license.MachineId))
            {
                license.MachineId = machineId;
                license.Note = note;
                await db.SaveChangesAsync();
            }
            else if (!string.Equals(license.MachineId, machineId, StringComparison.OrdinalIgnoreCase))
            {
                await LogAction(db, licenseId, machineId, "ACTIVATE", "FAILED", "Ключ уже привязан к другому ПК", ip);
                return Results.BadRequest("Данный ключ уже активирован на другом устройстве.");
            }

            if (license.ValidTill.HasValue && license.ValidTill.Value < DateTimeOffset.UtcNow)
            {
                await LogAction(db, licenseId, machineId, "ACTIVATE", "FAILED", "Срок действия истёк", ip);
                return Results.BadRequest("Срок действия лицензии истёк.");
            }

            await LogAction(db, licenseId, machineId, "ACTIVATE", "SUCCESS", $"Успешная активация модуля {license.ModuleId}", ip);

            var result = new
            {
                MachineId = machineId ?? string.Empty,
                ApplicationId = license.ApplicationId,
                ApplicationModuleIds = new[] { license.ModuleId },
                DateValidFrom = license.ValidFrom.UtcDateTime.AddDays(-1),
                DateValidTill = license.ValidTill?.UtcDateTime,
                Signature = "verified"
            };

            return Results.Ok(result);
        });

        // ==========================================
        // 2. РЕАКТИВАЦИЯ / ПРОДЛЕНИЕ
        // ==========================================
        group.MapPost("/reactivate", async (HttpContext context, MermerDbContext db) =>
        {
            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            using var reader = new StreamReader(context.Request.Body);
            var rawBody = await reader.ReadToEndAsync();

            if (string.IsNullOrWhiteSpace(rawBody))
                return Results.BadRequest("Пустой запрос реактивации.");

            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;

            string? machineId = GetPropString(root, "machineId", "MachineId");
            string requestedModule = GetRequestedModule(root);

            if (string.IsNullOrWhiteSpace(machineId))
                return Results.BadRequest("Не передан MachineId.");

            var license = await db.Licenses.FirstOrDefaultAsync(l =>
                l.MachineId == machineId &&
                l.ModuleId == requestedModule &&
                l.IsActive);

            if (license == null)
            {
                await LogAction(db, null, machineId, "REACTIVATE", "FAILED", "Активная лицензия для ПК не найдена", ip);
                return Results.BadRequest("Активная лицензия для данного устройства не найдена. Выполните первичную активацию.");
            }

            if (license.ValidTill.HasValue && license.ValidTill.Value < DateTimeOffset.UtcNow)
            {
                await LogAction(db, license.Key, machineId, "REACTIVATE", "FAILED", "Срок действия истёк", ip);
                return Results.BadRequest("Срок действия лицензии истёк.");
            }

            await LogAction(db, license.Key, machineId, "REACTIVATE", "SUCCESS", "Лицензия успешно обновлена", ip);

            var result = new
            {
                MachineId = machineId,
                ApplicationId = license.ApplicationId,
                ApplicationModuleIds = new[] { license.ModuleId },
                DateValidFrom = license.ValidFrom.UtcDateTime.AddDays(-1),
                DateValidTill = license.ValidTill?.UtcDateTime,
                Signature = "verified"
            };

            return Results.Ok(result);
        });

        // ==========================================
        // 3. ДЕАКТИВАЦИЯ (ОТВЯЗКА ОТ ПК)
        // ==========================================
        group.MapPost("/deactivate", async (HttpContext context, MermerDbContext db) =>
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
                    // Находим лицензию, привязанную к этому ПК, и отвязываем её
                    var licenses = await db.Licenses.Where(l => l.MachineId == machineId).ToListAsync();
                    foreach (var lic in licenses)
                    {
                        lic.MachineId = null; // Освобождаем ключ для активации на другом ПК
                        await LogAction(db, lic.Key, machineId, "DEACTIVATE", "SUCCESS", "Ключ отвязан от устройства", ip);
                    }
                    await db.SaveChangesAsync();
                }
            }

            return Results.Ok();
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
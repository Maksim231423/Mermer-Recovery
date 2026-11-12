using Mermer.Api.DTOs;
using Mermer.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Mermer.Api.Endpoints;

public static class SyncEndpoints
{
    public static void MapSyncEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/sync")
                       .WithTags("Synchronization");

        // 1. PUSH: Прием данных от кассы
        group.MapPost("/push", async (
            [FromBody] SyncPushRequestDto? request,
            [FromServices] ISyncService syncService,
            [FromServices] IServerLicenseValidationService licenseService,
            [FromServices] IOptionsMonitor<SyncOptions> syncOptionsMonitor,
            CancellationToken cancellationToken) =>
        {
            bool isCloudSyncEnabled = syncOptionsMonitor.CurrentValue.Enabled;
            var licenseCheck = await ValidateLicensesAsync(licenseService, isCloudSyncEnabled);
            if (!licenseCheck.IsValid)
            {
                return Results.Json(new SyncPushResponseDto
                {
                    Success = false,
                    Errors = new List<string> { licenseCheck.ErrorMessage }
                }, statusCode: StatusCodes.Status403Forbidden);
            }

            if (request == null)
            {
                return Results.BadRequest(new SyncPushResponseDto
                {
                    Success = false,
                    Errors = new List<string> { "Request body cannot be null or empty." }
                });
            }

            var response = await syncService.ProcessPushAsync(request, cancellationToken);

            if (!response.Success)
            {
                return Results.BadRequest(response);
            }

            return Results.Ok(response);
        })
        .WithName("PushSyncData")
        .WithSummary("Receives transactions from client and saves to DB")
        .WithOpenApi();

        // 2. PULL: Выгрузка справочников и остатков на кассу
        group.MapGet("/pull", async (
            [FromQuery] string? clientId,
            [FromServices] ISyncService syncService,
            [FromServices] IServerLicenseValidationService licenseService,
            [FromServices] IOptionsMonitor<SyncOptions> syncOptionsMonitor,
            CancellationToken cancellationToken) =>
        {
            bool isCloudSyncEnabled = syncOptionsMonitor.CurrentValue.Enabled;
            var licenseCheck = await ValidateLicensesAsync(licenseService, isCloudSyncEnabled);
            if (!licenseCheck.IsValid)
            {
                return Results.Json(new
                {
                    Error = licenseCheck.ErrorMessage
                }, statusCode: StatusCodes.Status403Forbidden);
            }

            var response = await syncService.ProcessPullAsync(clientId, cancellationToken);
            return Results.Ok(response);
        })
        .WithName("PullSyncData")
        .WithSummary("Provides catalog and balances to client")
        .WithOpenApi();

        // 3. CONFIG: Динамическое сохранение параметров синхронизации в appsettings.json API
        group.MapPost("/config", async (
            [FromBody] SyncConfigDto config,
            [FromServices] IWebHostEnvironment env,
            [FromServices] IServerLicenseValidationService licenseService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var appSettingsPath = Path.Combine(env.ContentRootPath, "appsettings.json");
                if (!File.Exists(appSettingsPath))
                {
                    return Results.NotFound(new { Message = "appsettings.json was not found on server." });
                }

                var json = await File.ReadAllTextAsync(appSettingsPath, cancellationToken);
                var root = JsonNode.Parse(json);
                if (root == null)
                {
                    return Results.Problem("Failed to parse appsettings.json.");
                }

                var syncNode = root["Sync"] ?? (root["Sync"] = new JsonObject());
                syncNode["Enabled"] = config.Enabled;
                syncNode["ServerUrl"] = config.ServerUrl;
                syncNode["SyncIntervalSeconds"] = config.SyncIntervalSeconds;

                var options = new JsonSerializerOptions { WriteIndented = true };
                await File.WriteAllTextAsync(appSettingsPath, root.ToJsonString(options), cancellationToken);

                licenseService.InvalidateCache();

                return Results.Ok(new { Success = true, Message = "Configuration applied successfully." });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to update configuration: {ex.Message}");
            }
        })
        .WithName("UpdateSyncConfig")
        .WithSummary("Updates Sync section in server appsettings.json")
        .WithOpenApi();
    }

    private static async Task<(bool IsValid, string ErrorMessage)> ValidateLicensesAsync(
        IServerLicenseValidationService licenseService,
        bool isCloudSyncEnabled)
    {
        // 1. Серверная лицензия обязательна всегда
        bool isServerValid = await licenseService.IsServerLicenseValidAsync();
        if (!isServerValid)
        {
            return (false, "Synchronization rejected: Active Server License is required. Please activate the Server License.");
        }

        // 2. Если галочка Enable Cloud Sync (VDS) отключена — работаем в локальном режиме,
        // лицензия Cloud Sync не требуется
        if (!isCloudSyncEnabled)
        {
            return (true, string.Empty);
        }

        // 3. Если облачная синхронизация включена — проверяем наличие лицензии на модуль Sync
        bool isSyncValid = await licenseService.IsModuleActiveAsync("Sync");
        if (!isSyncValid)
        {
            return (false, "Synchronization rejected: Active Cloud Synchronization License is required. Please activate the Sync module.");
        }

        return (true, string.Empty);
    }
}

public class SyncConfigDto
{
    public bool Enabled { get; set; }
    public string ServerUrl { get; set; } = string.Empty;
    public int SyncIntervalSeconds { get; set; } = 15;
}
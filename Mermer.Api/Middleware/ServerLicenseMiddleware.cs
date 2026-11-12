using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Mermer.Api.DTOs;
using Mermer.Api.Services;
using Mermer.Data.Postgres;
using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace Mermer.Api.Middleware;

public class ServerLicenseMiddleware
{
    private readonly RequestDelegate _next;

    public ServerLicenseMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IServerLicenseValidationService licenseService,
        IOptionsMonitor<SyncOptions> syncOptionsMonitor,
        MermerDbContext dbContext) // <-- ВНЕДРЯЕМ DbContext
    {
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? string.Empty;

        // Привязываем тенант из активной лицензии к DbContext
        if (string.IsNullOrWhiteSpace(MermerDbContext.CurrentClientId) || MermerDbContext.CurrentClientId == "default")
        {
            var activeKey = await dbContext.Licenses
                .AsNoTracking()
                .Where(l => l.IsActive)
                .OrderByDescending(l => l.CreatedAt)
                .Select(l => l.Key)
                .FirstOrDefaultAsync();

            if (!string.IsNullOrWhiteSpace(activeKey))
            {
                var client = MermerDbContext.ExtractClientNameFromToken(activeKey);
                if (!string.IsNullOrWhiteSpace(client) && client != "default")
                {
                    MermerDbContext.CurrentClientId = client;
                }
            }
        }

        // Allow licensing endpoints, Swagger, health checks, and static files
        if (path.StartsWith("/api/licensing") ||
            path.StartsWith("/swagger") ||
            path.StartsWith("/health") ||
            path.StartsWith("/favicon.ico"))
        {
            await _next(context);
            return;
        }

        // 1. Verify Server License
        bool isServerValid = await licenseService.IsServerLicenseValidAsync();
        if (!isServerValid)
        {
            await ReturnForbiddenAsync(context,
                "SERVER_LICENSE_REQUIRED",
                "The server database module is not activated or the license has expired.");
            return;
        }

        // 2. If Cloud Sync (VDS) is enabled, verify the Sync module license
        bool isCloudSyncEnabled = syncOptionsMonitor.CurrentValue.Enabled;
        if (isCloudSyncEnabled)
        {
            bool isSyncValid = await licenseService.IsModuleActiveAsync("Sync");
            if (!isSyncValid)
            {
                await ReturnForbiddenAsync(context,
                    "CLOUD_SYNC_LICENSE_REQUIRED",
                    "Cloud Synchronization (VDS) is enabled, but the required Sync module license is not activated.");
                return;
            }
        }

        await _next(context);
    }

    private static async Task ReturnForbiddenAsync(HttpContext context, string errorCode, string message)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/json";

        var errorResponse = new
        {
            statusCode = StatusCodes.Status403Forbidden,
            errorCode,
            message,
            details = "Please check and activate the required licenses in application settings or disable Cloud Sync."
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(errorResponse));
    }
}
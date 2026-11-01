using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Mermer.Api.Services;

namespace Mermer.Api.Middleware;

public class ServerLicenseMiddleware
{
    private readonly RequestDelegate _next;

    public ServerLicenseMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IServerLicenseValidationService licenseService)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? string.Empty;

        // Пропускаем Swagger, OpenAPI, запросы активации и статику
        if (path.StartsWith("/api/licensing") ||
            path.StartsWith("/swagger") ||
            path.StartsWith("/health") ||
            path.StartsWith("/favicon.ico"))
        {
            await _next(context);
            return;
        }

        // Проверяем наличие валидной серверной лицензии
        bool isValid = await licenseService.IsServerLicenseValidAsync();

        if (!isValid)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";

            var errorResponse = new
            {
                statusCode = StatusCodes.Status403Forbidden,
                errorCode = "SERVER_LICENSE_REQUIRED",
                message = "The server database module is not activated or the license validity period has expired.",
                details = "Please activate the server license via settings before continuing."
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(errorResponse));
            return;
        }

        await _next(context);
    }
}
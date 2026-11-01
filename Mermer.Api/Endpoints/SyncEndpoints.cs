using Mermer.Api.DTOs;
using Mermer.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using System.Threading;

namespace Mermer.Api.Endpoints;

public static class SyncEndpoints
{
    public static void MapSyncEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/sync")
                       .WithTags("Synchronization");

        // Эндпоинт PUSH
        group.MapPost("/push", async (
            [FromBody] SyncPushRequestDto? request,
            [FromServices] ISyncService syncService,
            CancellationToken cancellationToken) =>
        {
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
        .WithSummary("Принимает пакет данных от клиента и сохраняет в БД")
        .WithOpenApi();

        // Эндпоинт PULL
        group.MapGet("/pull", async (
            [FromQuery] string? clientId,
            [FromServices] ISyncService syncService,
            CancellationToken cancellationToken) =>
        {
            var response = await syncService.ProcessPullAsync(clientId, cancellationToken);
            return Results.Ok(response);
        })
        .WithName("PullSyncData")
        .WithSummary("Отдает актуальные справочники клиенту")
        .WithOpenApi();
    }
}
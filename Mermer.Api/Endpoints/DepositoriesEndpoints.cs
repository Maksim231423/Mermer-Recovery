using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Mermer.Data.Postgres;

namespace Mermer.Api.Endpoints;

public static class DepositoriesEndpoints
{
    public static void MapDepositoriesEndpoints(this IEndpointRouteBuilder routes)
    {
        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = null };

        async Task<IResult> GetAll(MermerDbContext db)
        {
            var depositories = await db.Depositories.AsNoTracking().Where(d => !d.IsDisabled).ToListAsync();
            var result = depositories.Select(d => new
            {
                Id = d.Id.ToString(),
                Name = d.Name,
                OfficeId = d.OfficeId.HasValue ? d.OfficeId.Value.ToString() : null,
                IsDisabled = d.IsDisabled
            });
            return Results.Json(result, jsonOptions);
        }

        routes.MapGet("/api/depositories", GetAll).WithTags("Depositories");
        routes.MapGet("/api/enterprise/depositories", GetAll).WithTags("Depositories");

        routes.MapGet("/api/depositories/next-code", async (MermerDbContext db) =>
        {
            var count = await db.Depositories.CountAsync();
            var nextCode = $"DEP-{(count + 1):D5}";
            return Results.Ok(new { code = nextCode });
        }).WithTags("Depositories");
    }
}
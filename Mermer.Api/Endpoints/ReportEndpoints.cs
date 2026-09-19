using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Mermer.Data.Postgres;

namespace Mermer.Api.Endpoints;

public static class ReportEndpoints
{
    public static void MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/reports/layout/{name}", async (string name, MermerDbContext db) =>
        {
            var layout = await db.Database
                .SqlQueryRaw<string>("SELECT layout FROM report_layouts WHERE id = {0}", "Report-" + name)
                .FirstOrDefaultAsync();

            return Results.Ok(new { Layout = layout });
        });

        app.MapPost("/api/reports/layout", async (ReportLayoutDto dto, MermerDbContext db) =>
        {
            string id = "Report-" + dto.Name;
            string sql = @"
                INSERT INTO report_layouts (id, name, layout, updated_at)
                VALUES ({0}, {1}, {2}, NOW())
                ON CONFLICT (id) DO UPDATE 
                SET layout = EXCLUDED.layout, updated_at = NOW();";

            await db.Database.ExecuteSqlRawAsync(sql, id, dto.Name, dto.Layout);
            return Results.Ok();
        });
    }
}

public record ReportLayoutDto(string Name, string Layout);
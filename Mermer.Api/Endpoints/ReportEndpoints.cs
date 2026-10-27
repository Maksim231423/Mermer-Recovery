using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Mermer.Data.Postgres;

namespace Mermer.Api.Endpoints;

public static class ReportEndpoints
{
    public static void MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        // 1. Получение макета отчета (через Dapper без ошибки t.Value)
        app.MapGet("/api/reports/layout/{name}", async (string name, MermerDbContext db) =>
        {
            var connStr = db.Database.GetConnectionString();
            await using var conn = new NpgsqlConnection(connStr);

            // Ищем макет как по "Report-Name", так и напрямую по "Name"
            string? layout = await conn.ExecuteScalarAsync<string>(
                "SELECT layout FROM report_layouts WHERE id = @id OR id = @rawId OR name = @rawId LIMIT 1;",
                new { id = "Report-" + name, rawId = name });

            return Results.Ok(new { Layout = layout });
        });

        // 2. Сохранение макета отчета
        app.MapPost("/api/reports/layout", async (ReportLayoutDto dto, MermerDbContext db) =>
        {
            var connStr = db.Database.GetConnectionString();
            await using var conn = new NpgsqlConnection(connStr);

            const string sql = """
                INSERT INTO report_layouts (id, name, layout, updated_at)
                VALUES (@id, @name, @layout, NOW())
                ON CONFLICT (id) DO UPDATE 
                SET layout = EXCLUDED.layout, updated_at = NOW();
                """;

            await conn.ExecuteAsync(sql, new
            {
                id = "Report-" + dto.Name,
                name = dto.Name,
                layout = dto.Layout
            });

            return Results.Ok();
        });
    }
}

public record ReportLayoutDto(string Name, string Layout);
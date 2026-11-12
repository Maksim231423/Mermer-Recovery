using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Mermer.Data.Postgres.Abstractions;

namespace Mermer.Api.Endpoints;

public static class BalancesEndpoints
{
    public static IEndpointRouteBuilder MapBalancesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/balances").WithTags("Balances");

        group.MapGet("/by-date", async (
            DateTime date,
            string? warehouseIds,
            string? displayCurrencyId,
            string? stockIds,
            string? priceGroup,
            IStockBalancesRepository repo,
            CancellationToken ct) =>
        {
            var warehouses = SplitCsv(warehouseIds);
            var stocks = SplitCsv(stockIds);

            var report = await repo.GetByDateAndWarehousesAsync(
                date,
                warehouses,
                displayCurrencyId,
                stocks,
                priceGroup,
                ct);

            return Results.Ok(report);
        })
        .WithName("BalancesByDate")
        .WithSummary(
            "Aggregated balance report at a point in time. " +
            "Supports price group (retail / wholesale / custom) and display currency.");

        group.MapGet("/stock/{stockId}", async (
            string stockId,
            DateTime? date,
            string? warehouseIds,
            IStockBalancesRepository repo,
            CancellationToken ct) =>
        {
            var warehouses = SplitCsv(warehouseIds)?.ToArray();
            var balances = await repo.GetAsync(
                stockId,
                date ?? DateTime.UtcNow,
                warehouses,
                ct);
            return Results.Ok(balances);
        })
        .WithName("BalancesByStock")
        .WithSummary("Per-warehouse balance for one stock at a date.");

        group.MapGet("/warehouse/{warehouseId}", async (
            string warehouseId,
            string stockIds,
            IStockBalancesRepository repo,
            CancellationToken ct) =>
        {
            var stocks = SplitCsv(stockIds)?.ToArray() ?? Array.Empty<string>();
            if (stocks.Length == 0)
                return Results.BadRequest(new { error = "Provide at least one stock id in 'stockIds' (csv)." });

            var balances = await repo.GetAsync(warehouseId, stocks, ct);
            return Results.Ok(balances);
        })
        .WithName("BalancesByWarehouse");

        group.MapGet("/by-type", async (
            string stockId,
            string warehouseIds,
            DateTime dateFrom,
            DateTime dateTill,
            bool? aggregate,
            IStockBalancesRepository repo,
            CancellationToken ct) =>
        {
            var warehouses = SplitCsv(warehouseIds)?.ToArray() ?? Array.Empty<string>();
            if (warehouses.Length == 0)
                return Results.BadRequest(new { error = "Provide at least one warehouse id in 'warehouseIds' (csv)." });

            var rows = await repo.GetByTypeAsync(warehouses, stockId, dateFrom, dateTill, aggregate ?? false, ct);
            return Results.Ok(rows);
        })
        .WithName("BalancesByType")
        .WithSummary("Per-day running balance split by invoice type.");

        return app;
    }

    private static List<string>? SplitCsv(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return null;
        return csv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }
}
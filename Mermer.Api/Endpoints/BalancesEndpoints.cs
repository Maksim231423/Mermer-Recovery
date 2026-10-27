using Mermer.Data.Postgres.Abstractions;

namespace Mermer.Api.Endpoints;

/// <summary>
/// Stock balance endpoints. Point-in-time balances are recomputed from
/// invoice_lines (not stock_balances) for historical accuracy, and the
/// "by date and warehouses" report supports price-group selection
/// (retail / wholesale / custom) and display currency.
/// </summary>
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

            // Если время не указано, берем срез на конец дня
            DateTime safeDate = date.TimeOfDay == TimeSpan.Zero && date.Kind != DateTimeKind.Utc
                ? date.Date.AddDays(1).AddTicks(-1).ToUniversalTime()
                : (date.Kind == DateTimeKind.Utc ? date : date.ToUniversalTime());

            var report = await repo.GetByDateAndWarehousesAsync(
                safeDate,
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

            var fromUtc = dateFrom.Kind == DateTimeKind.Utc ? dateFrom : dateFrom.ToUniversalTime();
            var tillUtc = dateTill.TimeOfDay == TimeSpan.Zero && dateTill.Kind != DateTimeKind.Utc
                ? dateTill.Date.AddDays(1).AddTicks(-1).ToUniversalTime()
                : (dateTill.Kind == DateTimeKind.Utc ? dateTill : dateTill.ToUniversalTime());

            var rows = await repo.GetByTypeAsync(warehouses, stockId, fromUtc, tillUtc, aggregate ?? false, ct);
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

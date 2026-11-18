using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Mermer.Data.Postgres;
using Mermer.Data.Postgres.Abstractions;
using PgInvoice = Mermer.Data.Postgres.Models.Invoice;

namespace Mermer.Api.Endpoints;

public static class InvoicesEndpoints
{
    public static IEndpointRouteBuilder MapInvoicesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/invoices").WithTags("Invoices");
        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = null };

        // 1. СПИСОК НАКЛАДНЫХ
        group.MapGet("/", async (
             DateTime? from,
             DateTime? till,
             string? displayCurrencyId,
             IInvoicesRepository repo,
             CancellationToken ct) =>
        {
            var startDate = EnsureUtc(from ?? DateTime.Now.AddMonths(-1), isTill: false);
            var endDate = EnsureUtc(till ?? DateTime.Now.AddDays(1), isTill: true, fromDt: from);

            var info = await repo.GetInfoAsync(startDate, endDate, displayCurrencyId, ct);

            var uiResponse = info.Select(i => new
            {
                Id = i.Id,
                Code = i.Code,
                Type = i.InvoiceType.ToString(),
                InvoiceType = i.InvoiceType.ToString(),
                Date = i.Date.ToString("yyyy-MM-ddTHH:mm:ss"),
                UserId = i.UserId,
                UserName = i.UserName,
                IsCash = true,
                IsCompleted = i.IsCompleted,
                IsDisabled = i.IsDisabled,
                Group = i.Group,
                Tags = i.Tags ?? new List<string>(),
                OfficeId = i.OfficeId,
                WarehouseId = i.WarehouseId,
                DepositoryId = i.DepositoryId,
                PartnerId = i.PartnerId,
                ActionTotal = i.Subtotal,
                ActionDiscountsTotal = i.DiscountsTotal,
                ActionGrandTotal = i.GrandTotal
            });

            return Results.Json(uiResponse, jsonOptions);
        })
        .WithName("InvoicesGetInfo");

        // 2. КОЛИЧЕСТВО НАКЛАДНЫХ
        group.MapGet("/count", async (DateTime? from, DateTime? till, IInvoicesRepository repo, CancellationToken ct) =>
        {
            var startDate = EnsureUtc(from ?? DateTime.Now.AddYears(-10), isTill: false);
            var endDate = EnsureUtc(till ?? DateTime.Now.AddYears(10), isTill: true, fromDt: from);
            var count = await repo.CountInfoAsync(startDate, endDate, ct);
            return Results.Json(new { count }, jsonOptions);
        })
        .WithName("InvoicesCountInfo");

        // 3. ФАСЕТЫ
        group.MapGet("/facets", async (string? fields, MermerDbContext db, CancellationToken ct) =>
        {
            var fieldList = fields?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            ?? new[] { "Date", "Group", "Tags" };

            var result = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);

            foreach (var field in fieldList)
            {
                if (field.Equals("Group", StringComparison.OrdinalIgnoreCase) || field.Equals("GroupNames", StringComparison.OrdinalIgnoreCase))
                {
                    var groups = await db.Invoices
                        .AsNoTracking()
                        .Where(x => !string.IsNullOrEmpty(x.Group))
                        .GroupBy(x => x.Group!)
                        .Select(g => new { Key = g.Key, Count = g.Count() })
                        .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

                    result[field] = groups;
                }
                else if (field.Equals("Tags", StringComparison.OrdinalIgnoreCase) || field.Equals("TagNames", StringComparison.OrdinalIgnoreCase))
                {
                    var allTags = await db.Invoices
                        .AsNoTracking()
                        .Where(x => x.Tags != null && x.Tags.Length > 0)
                        .Select(x => x.Tags)
                        .ToListAsync(ct);

                    var tagCounts = allTags
                        .SelectMany(t => t!)
                        .GroupBy(t => t)
                        .ToDictionary(g => g.Key, g => g.Count());

                    result[field] = tagCounts;
                }
                else if (field.Equals("Date", StringComparison.OrdinalIgnoreCase))
                {
                    var today = DateTime.Today;
                    var todayStart = DateTime.SpecifyKind(today, DateTimeKind.Utc);
                    var tomorrowStart = todayStart.AddDays(1);
                    var yesterdayStart = todayStart.AddDays(-1);

                    int diff = (7 + (today.DayOfWeek - DayOfWeek.Monday)) % 7;
                    var weekStart = DateTime.SpecifyKind(today.AddDays(-1 * diff), DateTimeKind.Utc);
                    var pastWeekStart = weekStart.AddDays(-7);

                    var monthStart = DateTime.SpecifyKind(new DateTime(today.Year, today.Month, 1), DateTimeKind.Utc);
                    var pastMonthStart = monthStart.AddMonths(-1);

                    var baseQuery = db.Invoices.AsNoTracking().Where(i => !i.IsDisabled);

                    var countToday = await baseQuery.CountAsync(i => i.Date >= todayStart && i.Date < tomorrowStart, ct);
                    var countYesterday = await baseQuery.CountAsync(i => i.Date >= yesterdayStart && i.Date < todayStart, ct);
                    var countWeek = await baseQuery.CountAsync(i => i.Date >= weekStart && i.Date < tomorrowStart, ct);
                    var countPastWeek = await baseQuery.CountAsync(i => i.Date >= pastWeekStart && i.Date < weekStart, ct);
                    var countMonth = await baseQuery.CountAsync(i => i.Date >= monthStart && i.Date < tomorrowStart, ct);
                    var countPastMonth = await baseQuery.CountAsync(i => i.Date >= pastMonthStart && i.Date < monthStart, ct);
                    var countAll = await baseQuery.CountAsync(ct);

                    result[field] = new Dictionary<string, int>
                    {
                        { "#Today", countToday },
                        { "#Yesturday", countYesterday },
                        { "#Yesterday", countYesterday },
                        { "#This Week", countWeek },
                        { "#Past Week", countPastWeek },
                        { "#This Month", countMonth },
                        { "#Past Month", countPastMonth },
                        { "#All Records", countAll }
                    };
                }
                else
                {
                    result[field] = new Dictionary<string, int>();
                }
            }

            return Results.Json(result, jsonOptions);
        })
        .WithName("InvoicesGetFacets");

        // 4. НАКЛАДНАЯ ПО ID
        group.MapGet("/{id}", async (string id, IInvoicesRepository repo, CancellationToken ct) =>
        {
            var inv = await repo.GetAsync(id, ct);
            if (inv is null) return Results.NotFound();

            return Results.Json(inv, jsonOptions);
        })
        .WithName("InvoicesGetById");

        // 5. АВТОНУМЕРАТОР
        group.MapGet("/next-code", async (MermerDbContext db) =>
        {
            var count = await db.Invoices.CountAsync();
            var nextCode = $"INV-{(count + 1):D6}";
            return Results.Json(new { code = nextCode }, jsonOptions);
        })
        .WithName("InvoicesGetNextCode");

        // 6. СОЗДАНИЕ И ОБНОВЛЕНИЕ
        Func<HttpRequest, IInvoicesRepository, MermerDbContext, CancellationToken, Task<IResult>> saveInvoiceHandler = async (request, repo, db, ct) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync(ct);
            if (string.IsNullOrEmpty(body)) return Results.BadRequest("Empty body");

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var pgInvoice = JsonSerializer.Deserialize<PgInvoice>(body, options);
            if (pgInvoice == null) return Results.BadRequest("Invalid JSON");

            using var doc = JsonDocument.Parse(body);
            var extractedTags = ExtractTagsFromRawJson(doc.RootElement);
            if (extractedTags.Count > 0)
            {
                pgInvoice.Tags = extractedTags;
            }

            pgInvoice.Date = DateTime.SpecifyKind(pgInvoice.Date, DateTimeKind.Utc);

            var defOffice = await db.Offices.AsNoTracking().FirstOrDefaultAsync(o => !o.IsDisabled, ct);
            Guid? defaultOfficeId = defOffice?.Id;

            if (string.IsNullOrWhiteSpace(pgInvoice.OfficeId) || pgInvoice.OfficeId == Guid.Empty.ToString())
            {
                pgInvoice.OfficeId = defaultOfficeId?.ToString();
            }

            Guid.TryParse(pgInvoice.OfficeId, out var currentOfficeGuid);

            if (string.IsNullOrWhiteSpace(pgInvoice.WarehouseId) || pgInvoice.WarehouseId == Guid.Empty.ToString())
            {
                var defWh = (currentOfficeGuid != Guid.Empty
                                ? await db.Warehouses.AsNoTracking().FirstOrDefaultAsync(w => w.OfficeId == currentOfficeGuid && !w.IsDisabled, ct)
                                : null)
                            ?? await db.Warehouses.AsNoTracking().FirstOrDefaultAsync(w => !w.IsDisabled, ct);

                pgInvoice.WarehouseId = defWh?.Id.ToString();
            }

            if (string.IsNullOrWhiteSpace(pgInvoice.DepositoryId) || pgInvoice.DepositoryId == Guid.Empty.ToString())
            {
                var defDep = (currentOfficeGuid != Guid.Empty
                                 ? await db.Depositories.AsNoTracking().FirstOrDefaultAsync(d => d.OfficeId == currentOfficeGuid && !d.IsDisabled, ct)
                                 : null)
                             ?? await db.Depositories.AsNoTracking().FirstOrDefaultAsync(d => !d.IsDisabled, ct);

                pgInvoice.DepositoryId = defDep?.Id.ToString();
            }

            if (string.IsNullOrWhiteSpace(pgInvoice.DisplayCurrencyId) || pgInvoice.DisplayCurrencyId == Guid.Empty.ToString())
            {
                var defCur = await db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => c.IsDefault && !c.IsDisabled, ct)
                             ?? await db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => !c.IsDisabled, ct);

                pgInvoice.DisplayCurrencyId = defCur?.Id.ToString();
            }

            if (pgInvoice.Lines != null && pgInvoice.Lines.Any())
            {
                foreach (var line in pgInvoice.Lines)
                {
                    if (string.IsNullOrWhiteSpace(line.UnitId) ||
                        line.UnitId == Guid.Empty.ToString() ||
                        !Guid.TryParse(line.UnitId, out var parsedUnitGuid))
                    {
                        line.UnitId = null;
                        continue;
                    }

                    bool unitExists = await db.StockUnits.AsNoTracking().AnyAsync(u => u.Id == parsedUnitGuid, ct);
                    if (!unitExists)
                    {
                        Guid? fallbackUnitId = null;
                        if (Guid.TryParse(line.StockId, out var parsedStockGuid))
                        {
                            var defaultUnit = await db.StockUnits.AsNoTracking()
                                .Where(u => u.StockId == parsedStockGuid)
                                .OrderByDescending(u => u.IsDefault)
                                .FirstOrDefaultAsync(ct);

                            fallbackUnitId = defaultUnit?.Id;
                        }

                        line.UnitId = fallbackUnitId?.ToString();
                    }
                }
            }

            var existing = await repo.GetAsync(pgInvoice.Id, ct);

            try
            {
                if (existing == null)
                {
                    await repo.CreateAsync(pgInvoice, ct);
                }
                else
                {
                    // Защита от перезаписи даты и валюты создания при редактировании:
                    pgInvoice.Date = existing.Date;
                    pgInvoice.DisplayCurrencyId = existing.DisplayCurrencyId;
                    await repo.UpdateAsync(pgInvoice, ct);
                }

                return Results.Json(pgInvoice, jsonOptions);
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[Invoice Save Error]: {ex}");
                return Results.Problem(ex.Message);
            }
        };

        group.MapPost("/", saveInvoiceHandler)
             .WithName("InvoicesCreate");

        group.MapPut("/{id}", async (string id, HttpRequest request, IInvoicesRepository repo, MermerDbContext db, CancellationToken ct) =>
            await saveInvoiceHandler(request, repo, db, ct))
            .WithName("InvoicesUpdate");

        // 7. УДАЛЕНИЕ
        group.MapDelete("/{id}", async (string id, IInvoicesRepository repo, CancellationToken ct) =>
        {
            await repo.DeleteAsync(id, ct);
            return Results.NoContent();
        })
        .WithName("InvoicesDelete");

        // 8. СПИСОК С ДЕТАЛИЗАЦИЕЙ ОПЛАТ
        group.MapGet("/payment-info", async (
            DateTime? from,
            DateTime? till,
            string? officeId,
            string? partnerId,
            string? displayCurrencyId,
            IInvoicesRepository repo,
            CancellationToken ct) =>
        {
            var startDate = EnsureUtc(from ?? DateTime.Now.AddYears(-10), isTill: false);
            var endDate = EnsureUtc(till ?? DateTime.Now.AddYears(10), isTill: true, fromDt: from);

            var result = await repo.GetPaymentInfoAsync(startDate, endDate, officeId, partnerId, displayCurrencyId, ct);
            return Results.Json(result, jsonOptions);
        })
        .WithName("InvoicesGetPaymentInfo");

        group.MapGet("/payment-info/count", async (
            DateTime? from,
            DateTime? till,
            string? officeId,
            string? partnerId,
            IInvoicesRepository repo,
            CancellationToken ct) =>
        {
            var startDate = EnsureUtc(from ?? DateTime.Now.AddYears(-10), isTill: false);
            var endDate = EnsureUtc(till ?? DateTime.Now.AddYears(10), isTill: true, fromDt: from);

            var count = await repo.CountPaymentInfoAsync(startDate, endDate, officeId, partnerId, ct);
            return Results.Json(new { count }, jsonOptions);
        })
        .WithName("InvoicesCountPaymentInfo");

        return app;
    }

    private static DateTime EnsureUtc(DateTime dt, bool isTill = false, DateTime? fromDt = null)
    {
        if (dt == DateTime.MinValue || dt.Year <= 2000) return DateTime.SpecifyKind(new DateTime(2000, 1, 1), DateTimeKind.Utc);
        if (dt == DateTime.MaxValue || dt.Year >= 2099) return DateTime.SpecifyKind(new DateTime(2099, 12, 31, 23, 59, 59), DateTimeKind.Utc);

        if (isTill && dt.TimeOfDay == TimeSpan.Zero)
        {
            if (fromDt.HasValue && dt.Date > fromDt.Value.Date)
            {
                dt = dt.AddTicks(-1);
            }
            else
            {
                dt = dt.Date.AddDays(1).AddTicks(-1);
            }
        }
        return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
    }

    private static List<string> ExtractTagsFromRawJson(JsonElement root)
    {
        var list = new List<string>();

        if (!root.TryGetProperty("tags", out var tagsProp) &&
            !root.TryGetProperty("Tags", out tagsProp))
        {
            return list;
        }

        if (tagsProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in tagsProp.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var s = item.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) list.Add(s.Trim());
                }
                else if (item.ValueKind == JsonValueKind.Object)
                {
                    if (item.TryGetProperty("Text", out var t) || item.TryGetProperty("Value", out t) || item.TryGetProperty("Name", out t))
                    {
                        var s = t.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) list.Add(s.Trim());
                    }
                }
            }
        }
        else if (tagsProp.ValueKind == JsonValueKind.String)
        {
            var raw = tagsProp.GetString();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                list.AddRange(raw.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }
        }

        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
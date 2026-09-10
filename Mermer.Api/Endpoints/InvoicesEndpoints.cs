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
            var startDate = EnsureUtc(from ?? DateTime.UtcNow.AddYears(-10));
            var endDate = EnsureUtc(till ?? DateTime.UtcNow.AddYears(10));

            var info = await repo.GetInfoAsync(startDate, endDate, displayCurrencyId, ct);

            var uiResponse = info.Select(i => new
            {
                Id = i.Id,
                Code = i.Code,
                Type = (int)i.InvoiceType, // Числовой enum предотвращает сбой десериализации
                InvoiceType = (int)i.InvoiceType,
                Date = i.Date,
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
            var startDate = EnsureUtc(from ?? DateTime.UtcNow.AddYears(-10));
            var endDate = EnsureUtc(till ?? DateTime.UtcNow.AddYears(10));
            var count = await repo.CountInfoAsync(startDate, endDate, ct);
            return Results.Json(new { count }, jsonOptions);
        })
        .WithName("InvoicesCountInfo");

        // 3. ФАСЕТЫ
        group.MapGet("/facets", async (string? fields, MermerDbContext db, CancellationToken ct) =>
        {
            var fieldList = fields?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            ?? Array.Empty<string>();

            var result = new Dictionary<string, Dictionary<string, int>>();

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
            return inv is null ? Results.NotFound() : Results.Json(inv, jsonOptions);
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
        Func<HttpRequest, IInvoicesRepository, CancellationToken, Task<IResult>> saveInvoiceHandler = async (request, repo, ct) =>
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

            var existing = await repo.GetAsync(pgInvoice.Id, ct);

            try
            {
                if (existing == null)
                {
                    await repo.CreateAsync(pgInvoice, ct);
                    return Results.Created($"/api/invoices/{pgInvoice.Id}", pgInvoice);
                }
                else
                {
                    await repo.UpdateAsync(pgInvoice, ct);
                    return Results.Json(pgInvoice, jsonOptions);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Invoice Save Error]: {ex}");
                return Results.Problem(ex.Message);
            }
        };

        group.MapPost("/", saveInvoiceHandler).WithName("InvoicesCreate");
        group.MapPut("/{id}", async (string id, HttpRequest request, IInvoicesRepository repo, CancellationToken ct) => await saveInvoiceHandler(request, repo, ct)).WithName("InvoicesUpdate");

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
            var startDate = EnsureUtc(from ?? DateTime.UtcNow.AddYears(-10));
            var endDate = EnsureUtc(till ?? DateTime.UtcNow.AddYears(10));

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
            var startDate = EnsureUtc(from ?? DateTime.UtcNow.AddYears(-10));
            var endDate = EnsureUtc(till ?? DateTime.UtcNow.AddYears(10));

            var count = await repo.CountPaymentInfoAsync(startDate, endDate, officeId, partnerId, ct);
            return Results.Json(new { count }, jsonOptions);
        })
        .WithName("InvoicesCountPaymentInfo");

        return app;
    }

    private static DateTime EnsureUtc(DateTime dt)
    {
        if (dt == DateTime.MinValue) return DateTime.SpecifyKind(new DateTime(2000, 1, 1), DateTimeKind.Utc);
        if (dt == DateTime.MaxValue) return DateTime.SpecifyKind(new DateTime(2099, 12, 31), DateTimeKind.Utc);
        return dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();
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
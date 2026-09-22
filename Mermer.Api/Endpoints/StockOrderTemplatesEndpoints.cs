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
using Mermer.Data.Postgres.Entities;

namespace Mermer.Api.Endpoints;

public static class StockOrderTemplatesEndpoints
{
    public static IEndpointRouteBuilder MapStockOrderTemplatesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/warehousing/order-templates").WithTags("StockOrderTemplates");

        // 1. СПИСОК ШАБЛОНОВ (С ЛИМИТОМ)
        group.MapGet("/", async (int? limit, int? offset, MermerDbContext db, CancellationToken ct) =>
        {
            int take = limit.HasValue ? Math.Clamp(limit.Value, 1, 500) : 100;
            int skip = offset.GetValueOrDefault(0);

            var list = await db.StockOrderTemplates
                .AsNoTracking()
                .Where(t => !t.IsDisabled)
                .OrderBy(t => t.Name)
                .Skip(skip)
                .Take(take)
                .Include(t => t.Lines)
                .AsSplitQuery()
                .ToListAsync(ct);

            return Results.Ok(list.Select(t => new
            {
                Id = t.Id.ToString(),
                Name = t.Name,
                IsDisabled = t.IsDisabled,
                Group = t.GroupName ?? "",
                Tags = t.Tags != null ? t.Tags.ToList() : new List<string>(),
                Description = t.Description ?? "",
                Lines = t.Lines.Select(l => new
                {
                    Id = l.Id.ToString(),
                    StockId = l.StockId?.ToString()
                })
            }));
        });

        // 2. ПОЛУЧЕНИЕ ПО ID
        group.MapGet("/{id}", async (string id, MermerDbContext db, CancellationToken ct) =>
        {
            if (!Guid.TryParse(id, out var guid)) return Results.NotFound();

            var t = await db.StockOrderTemplates
                .Include(x => x.Lines)
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == guid, ct);

            if (t == null) return Results.NotFound();

            return Results.Ok(new
            {
                Id = t.Id.ToString(),
                Name = t.Name,
                IsDisabled = t.IsDisabled,
                Group = t.GroupName ?? "",
                Tags = t.Tags != null ? t.Tags.ToList() : new List<string>(),
                Description = t.Description ?? "",
                Lines = t.Lines.Select(l => new
                {
                    Id = l.Id.ToString(),
                    StockId = l.StockId?.ToString()
                })
            });
        });

        // 3. ФАСЕТЫ
        group.MapGet("/facets", async (HttpContext ctx, MermerDbContext db, CancellationToken ct) =>
        {
            var allGroups = await db.StockOrderTemplates
                .AsNoTracking()
                .Where(x => !string.IsNullOrEmpty(x.GroupName) && !x.IsDisabled)
                .GroupBy(x => x.GroupName!)
                .Select(g => new { Key = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

            var tagsList = await db.StockOrderTemplates
                .AsNoTracking()
                .Where(x => x.Tags != null && x.Tags.Length > 0 && !x.IsDisabled)
                .Select(x => x.Tags)
                .ToListAsync(ct);

            var tagCounts = tagsList.SelectMany(t => t!)
                .GroupBy(t => t)
                .ToDictionary(g => g.Key, g => g.Count());

            var dict = new Dictionary<string, Dictionary<string, int>>
            {
                ["GroupNames"] = allGroups,
                ["TagNames"] = tagCounts
            };

            return Results.Ok(dict);
        });

        // 4. СОХРАНЕНИЕ ШАБЛОНА (POST / PUT)
        Func<HttpRequest, MermerDbContext, Task<IResult>> saveTemplateHandler = async (req, db) =>
        {
            using var reader = new StreamReader(req.Body);
            var body = await reader.ReadToEndAsync();
            if (string.IsNullOrEmpty(body)) return Results.BadRequest("Empty body");

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            string? idStr = GetStringProp(root, "id", "Id");
            Guid templateId = Guid.TryParse(idStr, out var parsedGuid) && parsedGuid != Guid.Empty ? parsedGuid : Guid.NewGuid();

            var existing = await db.StockOrderTemplates.FirstOrDefaultAsync(r => r.Id == templateId);

            string name = GetStringProp(root, "name", "Name") ?? "";
            string groupName = GetStringProp(root, "group", "Group", "groupName", "GroupName") ?? "";
            string description = GetStringProp(root, "description", "Description") ?? "";
            bool isDisabled = GetBoolProp(root, "isDisabled", "IsDisabled");
            var tagsList = ExtractTagsFromRawJson(root);

            var linesList = new List<StockOrderTemplateLineEntity>();
            if (TryGetPropCaseInsensitive(root, "lines", out var linesElem) && linesElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var le in linesElem.EnumerateArray())
                {
                    string? lidStr = GetStringProp(le, "id", "Id");
                    string? lStockStr = GetStringProp(le, "stockId", "StockId");

                    linesList.Add(new StockOrderTemplateLineEntity
                    {
                        Id = Guid.TryParse(lidStr, out var lG) && lG != Guid.Empty ? lG : Guid.NewGuid(),
                        StockOrderTemplateId = templateId,
                        StockId = Guid.TryParse(lStockStr, out var stG) ? stG : null
                    });
                }
            }

            if (existing == null)
            {
                await db.StockOrderTemplates.AddAsync(new StockOrderTemplateEntity
                {
                    Id = templateId,
                    Name = name,
                    GroupName = groupName,
                    Description = description,
                    IsDisabled = isDisabled,
                    Tags = tagsList.ToArray(),
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                    Lines = linesList
                });
            }
            else
            {
                existing.Name = name;
                existing.GroupName = groupName;
                existing.Description = description;
                existing.IsDisabled = isDisabled;
                existing.Tags = tagsList.ToArray();
                existing.UpdatedAt = DateTimeOffset.UtcNow;

                var oldLines = await db.StockOrderTemplateLines.Where(l => l.StockOrderTemplateId == templateId).ToListAsync();
                if (oldLines.Any()) db.StockOrderTemplateLines.RemoveRange(oldLines);
                if (linesList.Any()) await db.StockOrderTemplateLines.AddRangeAsync(linesList);

                db.StockOrderTemplates.Update(existing);
            }

            await db.SaveChangesAsync();
            return Results.Content($"{{\"id\":\"{templateId}\"}}", "application/json");
        };

        group.MapPost("/", saveTemplateHandler);
        group.MapPut("/{id}", saveTemplateHandler);

        // 5. УДАЛЕНИЕ
        group.MapDelete("/{id}", async (string id, MermerDbContext db) =>
        {
            if (Guid.TryParse(id, out var guid))
            {
                var o = await db.StockOrderTemplates.FirstOrDefaultAsync(x => x.Id == guid);
                if (o != null)
                {
                    o.IsDisabled = true;
                    o.UpdatedAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync();
                }
            }
            return Results.Ok();
        });

        return app;
    }

    #region Helpers
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
                list.AddRange(raw.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                                 .Select(x => x.Trim())
                                 .Where(x => !string.IsNullOrWhiteSpace(x)));
            }
        }

        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool TryGetPropCaseInsensitive(JsonElement el, string name, out JsonElement val)
    {
        foreach (var p in el.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                val = p.Value;
                return true;
            }
        }
        val = default;
        return false;
    }

    private static string? GetStringProp(JsonElement el, params string[] names)
    {
        foreach (var n in names)
        {
            if (TryGetPropCaseInsensitive(el, n, out var p) && p.ValueKind == JsonValueKind.String)
                return p.GetString();
        }
        return null;
    }

    private static decimal GetDecimalProp(JsonElement el, params string[] names)
    {
        foreach (var n in names)
        {
            if (TryGetPropCaseInsensitive(el, n, out var p))
            {
                if (p.ValueKind == JsonValueKind.Number) return p.GetDecimal();
                if (p.ValueKind == JsonValueKind.String && decimal.TryParse(p.GetString(), out var v)) return v;
            }
        }
        return 0m;
    }

    private static bool GetBoolProp(JsonElement el, params string[] names)
    {
        foreach (var n in names)
        {
            if (TryGetPropCaseInsensitive(el, n, out var p))
            {
                if (p.ValueKind == JsonValueKind.True) return true;
                if (p.ValueKind == JsonValueKind.False) return false;
            }
        }
        return false;
    }
    #endregion
}
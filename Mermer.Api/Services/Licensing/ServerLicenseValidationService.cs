using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Mermer.Data.Postgres;

namespace Mermer.Api.Services;

public interface ILicenseModuleValidator
{
    Task<bool> IsModuleActiveAsync(string moduleName);
}

public interface IServerLicenseValidationService : ILicenseModuleValidator
{
    Task<bool> IsServerLicenseValidAsync();
    void InvalidateCache();
}

public class ServerLicenseValidationService : IServerLicenseValidationService
{
    private readonly MermerDbContext _db;
    private readonly IMemoryCache _cache;
    private const string ServerCacheKey = "Mermer_Server_License_Status";
    private const string ServerModuleId = "9a953aa5-2fd9-418d-bcf7-fb5bd7d09553";
    private const string SyncModuleId = "6b1495a1-60aa-4420-9c30-94718c121c26";

    public ServerLicenseValidationService(MermerDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<bool> IsServerLicenseValidAsync()
    {
        if (_cache.TryGetValue(ServerCacheKey, out bool isValid))
        {
            return isValid;
        }

        var now = DateTimeOffset.UtcNow;

        var licenses = await _db.Licenses
            .AsNoTracking()
            .Where(l => l.IsActive && (!l.ValidTill.HasValue || l.ValidTill.Value >= now))
            .ToListAsync();

        isValid = licenses.Any(l =>
        {
            if (string.IsNullOrWhiteSpace(l.ModuleId)) return false;
            var modules = l.ModuleId
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            return modules.Contains("all", StringComparer.OrdinalIgnoreCase)
                   || modules.Contains("server", StringComparer.OrdinalIgnoreCase)
                   || modules.Contains(ServerModuleId, StringComparer.OrdinalIgnoreCase);
        });

        _cache.Set(ServerCacheKey, isValid, TimeSpan.FromSeconds(60));
        return isValid;
    }

    public async Task<bool> IsModuleActiveAsync(string moduleName)
    {
        string cacheKey = $"Mermer_Module_{moduleName}_Status";
        if (_cache.TryGetValue(cacheKey, out bool isActive))
        {
            return isActive;
        }

        var now = DateTimeOffset.UtcNow;
        var licenses = await _db.Licenses
            .AsNoTracking()
            .Where(l => l.IsActive && (!l.ValidTill.HasValue || l.ValidTill.Value >= now))
            .ToListAsync();

        string targetId = moduleName.Equals("Sync", StringComparison.OrdinalIgnoreCase)
            ? SyncModuleId
            : moduleName;

        isActive = licenses.Any(l =>
        {
            if (string.IsNullOrWhiteSpace(l.ModuleId)) return false;
            var modules = l.ModuleId
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            return modules.Contains("all", StringComparer.OrdinalIgnoreCase)
                   || modules.Contains(moduleName, StringComparer.OrdinalIgnoreCase)
                   || modules.Contains(targetId, StringComparer.OrdinalIgnoreCase);
        });

        _cache.Set(cacheKey, isActive, TimeSpan.FromSeconds(60));
        return isActive;
    }

    public void InvalidateCache()
    {
        _cache.Remove(ServerCacheKey);
        _cache.Remove("Mermer_Module_Sync_Status");
    }
}
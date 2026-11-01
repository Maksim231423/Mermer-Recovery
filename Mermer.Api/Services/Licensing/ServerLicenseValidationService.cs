using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Mermer.Data.Postgres;

namespace Mermer.Api.Services;

public interface IServerLicenseValidationService
{
    Task<bool> IsServerLicenseValidAsync();
    void InvalidateCache();
}

public class ServerLicenseValidationService : IServerLicenseValidationService
{
    private readonly MermerDbContext _db;
    private readonly IMemoryCache _cache;
    private const string CacheKey = "Mermer_Server_License_Status";
    private const string ServerModuleId = "9a953aa5-2fd9-418d-bcf7-fb5bd7d09553";

    public ServerLicenseValidationService(MermerDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<bool> IsServerLicenseValidAsync()
    {
        if (_cache.TryGetValue(CacheKey, out bool isValid))
        {
            return isValid;
        }

        var now = DateTimeOffset.UtcNow;

        // Ищем активную лицензию с модулем сервера или флагом 'all'
        var licenses = await _db.Licenses
            .AsNoTracking()
            .Where(l => l.IsActive && (!l.ValidTill.HasValue || l.ValidTill.Value >= now))
            .ToListAsync();

        isValid = licenses.Any(l =>
        {
            var modules = (l.ModuleId ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            return modules.Contains("all", StringComparer.OrdinalIgnoreCase)
                   || modules.Contains(ServerModuleId, StringComparer.OrdinalIgnoreCase);
        });

        // Кэшируем результат на 60 секунд
        _cache.Set(CacheKey, isValid, TimeSpan.FromSeconds(60));

        return isValid;
    }

    public void InvalidateCache()
    {
        _cache.Remove(CacheKey);
    }
}
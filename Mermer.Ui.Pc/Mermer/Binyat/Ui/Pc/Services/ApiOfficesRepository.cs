using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mermer.Data.Storage;
using Mermer.Enterprise.Models;
using Mermer.Http;

namespace Mermer.Ui.Pc.Services;

public class ApiOfficesRepository : BaseApiCacheRepository<Office>, IRepositoryWithFacets<Office>
{
    private readonly RestClient _restClient;

    public ApiOfficesRepository(RestClient restClient)
        : base(restClient, "Office", "offices")
    {
        _restClient = restClient;
    }

    // Сокрываем базовую реализацию с помощью new и реализуем IRepositoryWithFacets<Office>
    public new async Task<Dictionary<string, Dictionary<string, int>>> GetFacets(params string[] facetNames)
    {
        try
        {
            var serverFacets = await _restClient.GetAsync<Dictionary<string, Dictionary<string, int>>>("/api/offices/facets");
            if (serverFacets != null)
            {
                if (!serverFacets.ContainsKey("RegionNames"))
                    serverFacets["RegionNames"] = new Dictionary<string, int>();

                if (!serverFacets.ContainsKey("TagNames"))
                    serverFacets["TagNames"] = new Dictionary<string, int>();

                return serverFacets;
            }
        }
        catch
        {
            // Fallback на локальный кэш при ошибке сети
        }

        var localList = (await base.GetAsync()) ?? Enumerable.Empty<Office>();

        var regionDict = localList
            .Where(o => !string.IsNullOrWhiteSpace(o.Region))
            .GroupBy(o => o.Region.Trim())
            .ToDictionary(g => g.Key, g => g.Count());

        var tagDict = localList
            .Where(o => o.Tags != null)
            .SelectMany(o => o.Tags)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .GroupBy(t => t.Trim())
            .ToDictionary(g => g.Key, g => g.Count());

        return new Dictionary<string, Dictionary<string, int>>
        {
            ["RegionNames"] = regionDict,
            ["TagNames"] = tagDict
        };
    }

    // Явная реализация интерфейса IRepositoryWithFacets<Office> для MvvmCross-вызова
    Task<Dictionary<string, Dictionary<string, int>>> IRepositoryWithFacets<Office>.GetFacets(params string[] facetNames)
    {
        return this.GetFacets(facetNames);
    }
}
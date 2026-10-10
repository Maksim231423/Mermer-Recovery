using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mermer.Data.Storage;
using Mermer.Enterprise.Models;
using Mermer.Http;

namespace Mermer.Ui.Pc.Services;

public class ApiWarehousesRepository : BaseApiCacheRepository<Warehouse>, IRepositoryWithFacets<Warehouse>
{
    private readonly RestClient _restClient;

    public ApiWarehousesRepository(RestClient restClient)
        : base(restClient, "Warehouse", "warehouses")
    {
        _restClient = restClient;
    }

    public new async Task<Dictionary<string, Dictionary<string, int>>> GetFacets(params string[] facetNames)
    {
        try
        {
            var serverFacets = await _restClient.GetAsync<Dictionary<string, Dictionary<string, int>>>("/api/warehouses/facets");
            if (serverFacets != null)
            {
                if (!serverFacets.ContainsKey("TagNames"))
                    serverFacets["TagNames"] = new Dictionary<string, int>();

                return serverFacets;
            }
        }
        catch
        {
            // Fallback на локальные данные при отсутствии сети
        }

        var localList = (await base.GetAsync()) ?? Enumerable.Empty<Warehouse>();

        var tagDict = localList
            .Where(w => w.Tags != null)
            .SelectMany(w => w.Tags)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .GroupBy(t => t.Trim())
            .ToDictionary(g => g.Key, g => g.Count());

        return new Dictionary<string, Dictionary<string, int>>
        {
            ["TagNames"] = tagDict
        };
    }

    Task<Dictionary<string, Dictionary<string, int>>> IRepositoryWithFacets<Warehouse>.GetFacets(params string[] facetNames)
    {
        return this.GetFacets(facetNames);
    }
}
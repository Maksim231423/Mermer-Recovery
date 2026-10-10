using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mermer.Data.Storage;
using Mermer.Enterprise.Models;
using Mermer.Http;

namespace Mermer.Ui.Pc.Services;

public class ApiDepositoriesRepository : BaseApiCacheRepository<Depository>, IRepositoryWithFacets<Depository>
{
    private readonly RestClient _restClient;

    public ApiDepositoriesRepository(RestClient restClient)
        : base(restClient, "Depository", "depositories")
    {
        _restClient = restClient;
    }

    public new async Task<Dictionary<string, Dictionary<string, int>>> GetFacets(params string[] facetNames)
    {
        try
        {
            var serverFacets = await _restClient.GetAsync<Dictionary<string, Dictionary<string, int>>>("/api/depositories/facets");
            if (serverFacets != null)
            {
                if (!serverFacets.ContainsKey("TagNames"))
                    serverFacets["TagNames"] = new Dictionary<string, int>();

                return serverFacets;
            }
        }
        catch
        {
            // Fallback на локальный кэш
        }

        var localList = (await base.GetAsync()) ?? Enumerable.Empty<Depository>();

        var tagDict = localList
            .Where(d => d.Tags != null)
            .SelectMany(d => d.Tags)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .GroupBy(t => t.Trim())
            .ToDictionary(g => g.Key, g => g.Count());

        return new Dictionary<string, Dictionary<string, int>>
        {
            ["TagNames"] = tagDict
        };
    }

    Task<Dictionary<string, Dictionary<string, int>>> IRepositoryWithFacets<Depository>.GetFacets(params string[] facetNames)
    {
        return this.GetFacets(facetNames);
    }
}
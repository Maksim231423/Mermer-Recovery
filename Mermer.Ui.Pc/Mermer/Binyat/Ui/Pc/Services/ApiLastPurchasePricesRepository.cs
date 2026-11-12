using Mermer.Commerce.Models;
using Mermer.Commerce.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Mermer.Ui.Pc.Services;

public class ApiLastPurchasePricesRepository : ILastPurchasePricesRepository
{
    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ApiLastPurchasePricesRepository(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IEnumerable<LastPurchasePrice>> GetAsync(string warehouseId, string[] stockIds)
    {
        if (stockIds == null || stockIds.Length == 0)
            return Array.Empty<LastPurchasePrice>();

        try
        {
            var joinedStockIds = string.Join(",", stockIds);
            var url = $"/api/stocks/last-purchase-prices?warehouseId={warehouseId}&stockIds={Uri.EscapeDataString(joinedStockIds)}";

            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return Array.Empty<LastPurchasePrice>();

            using var stream = await response.Content.ReadAsStreamAsync();
            var dtos = await JsonSerializer.DeserializeAsync<List<LastPurchasePriceDto>>(stream, JsonOptions);

            if (dtos == null)
                return Array.Empty<LastPurchasePrice>();

            return dtos.Select(d => new LastPurchasePrice
            {
                StockId = d.StockId,
                Price = d.Price,
                CurrencyId = d.CurrencyId
            }).ToList();
        }
        catch
        {
            return Array.Empty<LastPurchasePrice>();
        }
    }

    private class LastPurchasePriceDto
    {
        public string StockId { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public string? CurrencyId { get; set; }
    }
}
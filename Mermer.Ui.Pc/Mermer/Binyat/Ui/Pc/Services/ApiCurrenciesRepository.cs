using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Mermer.FundsManagement.Models;
using Mermer.Http;

namespace Mermer.Ui.Pc.Services;

public class ApiCurrenciesRepository : BaseApiCacheRepository<Currency>
{
    public ApiCurrenciesRepository(RestClient restClient) : base(restClient, "Currency", "currencies") { }

    /// <summary>
    /// Вызывается комбобоксами и селекторами. Отдает ТОЛЬКО активные валюты.
    /// </summary>
    public override async Task<IEnumerable<Currency>> GetAllAsync()
    {
        var all = await base.GetAllAsync();
        if (all == null) return Enumerable.Empty<Currency>();

        return all.Where(c => !c.IsDisabled).ToList();
    }

    /// <summary>
    /// Обрабатывает вызовы Reference<Currency> и реестров.
    /// </summary>
    public override async Task<IEnumerable<Currency>> GetAsync(params Expression<Func<Currency, bool>>[] predicates)
    {
        // Проверяем, запрашивает ли вызывающий код удаленные валюты явно
        bool needsDisabled = false;
        if (predicates != null && predicates.Length > 0)
        {
            var strPredicates = string.Join(" ", predicates.Select(p => p?.ToString() ?? ""));
            // Совместимо с .NET Framework: используем IndexOf вместо Contains со StringComparison
            if (strPredicates.IndexOf("IsDisabled", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                needsDisabled = true;
            }
        }

        if (needsDisabled)
        {
            try
            {
                var remote = await _restClient.GetAsync<List<Currency>>("/api/currencies?includeDisabled=true");
                if (remote != null && remote.Any())
                {
                    var q = remote.AsQueryable();
                    foreach (var p in predicates.Where(x => x != null)) q = q.Where(p);
                    return q.ToList();
                }
            }
            catch { }
        }

        // Для всех обычных окон (включая Sales, Bills, Collection) отдаем только активные валюты
        var activeList = (await GetAllAsync()) ?? Enumerable.Empty<Currency>();
        var query = activeList.AsQueryable();

        if (predicates != null && predicates.Length > 0)
        {
            foreach (var p in predicates.Where(x => x != null)) query = query.Where(p);
        }

        return query.ToList();
    }
}
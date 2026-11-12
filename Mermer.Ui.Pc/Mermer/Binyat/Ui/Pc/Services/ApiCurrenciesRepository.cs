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
    /// Вызывается реестром валют (CurrenciesListViewModel) и комбобоксами.
    /// Загружает все валюты (включая удаленные/отключенные) с сервера.
    /// </summary>
    public override async Task<IEnumerable<Currency>> GetAllAsync()
    {
        try
        {
            // Запрашиваем полный список вместе с удаленными
            var remote = await _restClient.GetAsync<List<Currency>>("/api/currencies?includeDisabled=true");
            if (remote != null && remote.Any())
            {
                return remote;
            }
        }
        catch
        {
            // fallback к кэшу/базовой реализации, если эндпоинт недоступен
        }

        var all = await base.GetAllAsync();
        return all ?? Enumerable.Empty<Currency>();
    }

    /// <summary>
    /// Обрабатывает вызовы реестров и справочников с предикатами.
    /// </summary>
    public override async Task<IEnumerable<Currency>> GetAsync(params Expression<Func<Currency, bool>>[] predicates)
    {
        // 1. Получаем все валюты (включая удаленные)
        var allList = await GetAllAsync();
        var query = (allList ?? Enumerable.Empty<Currency>()).AsQueryable();

        // 2. Проверяем, запрашивает ли вызывающий код удаленные валюты явно
        bool mentionsDisabled = false;
        if (predicates != null && predicates.Length > 0)
        {
            var strPredicates = string.Join(" ", predicates.Select(p => p?.ToString() ?? ""));
            if (strPredicates.IndexOf("IsDisabled", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                mentionsDisabled = true;
            }
        }

        // Если вызывающий код НЕ проверяет статус активности сам и это фильтрованный запрос,
        // отфильтровываем удаленные (чтобы POS и заказы не получали скрытые валюты)
        if (!mentionsDisabled && predicates != null && predicates.Length > 0)
        {
            query = query.Where(c => !c.IsDisabled);
        }

        // Применяем переданные фильтры
        if (predicates != null && predicates.Length > 0)
        {
            foreach (var p in predicates.Where(x => x != null))
                query = query.Where(p);
        }

        return query.ToList();
    }
}
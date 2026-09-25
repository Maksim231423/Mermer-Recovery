using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mermer.Commerce.Models;
using Mermer.Commerce.Services;

namespace Mermer.Ui.Pc.Services
{
    public class ApiLastPurchasePricesRepository : ILastPurchasePricesRepository
    {
        public Task<IEnumerable<LastPurchasePrice>> GetAsync(string warehouseId, string[] stockIds)
        {
            if (stockIds == null || stockIds.Length == 0)
            {
                return Task.FromResult<IEnumerable<LastPurchasePrice>>(Array.Empty<LastPurchasePrice>());
            }

            // Возвращаем пустой набор (или дефолтные цены), не ломая построение формы
            // Поисковик товаров спокойно подставит базовые прайсовые цены
            return Task.FromResult<IEnumerable<LastPurchasePrice>>(Array.Empty<LastPurchasePrice>());
        }
    }
}
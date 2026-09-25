using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Mermer.Data.Storage;
using Mermer.Http;
using Mermer.Warehousing.Ordering.Models;

namespace Mermer.Ui.Pc.Services
{
    public class ApiStockOrdersRepository :
        IRepository<StockOrder>,
        IReadOnlyRepository<StockOrder>,
        IRepositoryWithFacets<StockOrder>
    {
        private readonly RestClient _restClient;
        private const string DocType = "StockOrder";

        private static readonly ConcurrentDictionary<string, StockOrder> _cardCache = new(StringComparer.OrdinalIgnoreCase);

        public ApiStockOrdersRepository(RestClient restClient)
        {
            _restClient = restClient ?? throw new ArgumentNullException(nameof(restClient));
        }

        public async Task<StockOrder> GetAsync(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            if (_cardCache.TryGetValue(id, out var cached))
                return cached;

            await GetAllAsync();
            if (_cardCache.TryGetValue(id, out cached))
                return cached;

            try
            {
                var remote = await _restClient.GetAsync<StockOrder>($"/api/warehousing/orders/{id}");
                if (remote != null)
                {
                    _cardCache[remote.Id] = remote;
                    LocalSqliteCache.SaveDocument(DocType, remote.Id, remote, isSynced: true);
                    return remote;
                }
            }
            catch { }

            return null;
        }

        public async Task<IEnumerable<StockOrder>> GetAsync(string[] ids)
        {
            if (ids == null || !ids.Any()) return Enumerable.Empty<StockOrder>();
            var result = new List<StockOrder>();
            foreach (var id in ids)
            {
                var item = await GetAsync(id);
                if (item != null) result.Add(item);
            }
            return result;
        }

        // --- БЫСТРАЯ ВЫБОРКА ПО ДАТАМ (ПРЯМОЙ СРЕЗ С СЕРВЕРА) ---
        public async Task<IEnumerable<StockOrder>> GetAsync(params Expression<Func<StockOrder, bool>>[] predicates)
        {
            var (hasDates, from, till) = TryExtractDateRange(predicates);

            if (hasDates)
            {
                try
                {
                    var fromStr = from.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
                    var tillStr = till.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");

                    var remoteSlice = await _restClient.GetAsync<List<StockOrder>>($"/api/warehousing/orders?from={fromStr}&till={tillStr}");
                    if (remoteSlice != null)
                    {
                        var query = remoteSlice.AsQueryable();
                        if (predicates != null)
                        {
                            foreach (var p in predicates.Where(x => x != null)) query = query.Where(p);
                        }
                        return query.ToList();
                    }
                }
                catch { }
            }

            var all = await GetAllAsync();
            var fallbackQuery = all.AsQueryable();
            if (predicates != null)
            {
                foreach (var p in predicates.Where(x => x != null)) fallbackQuery = fallbackQuery.Where(p);
            }
            return fallbackQuery.ToList();
        }

        public async Task<int> CountAsync(params Expression<Func<StockOrder, bool>>[] predicates)
        {
            var (hasDates, from, till) = TryExtractDateRange(predicates);

            if (hasDates)
            {
                try
                {
                    var fromStr = from.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
                    var tillStr = till.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");

                    var res = await _restClient.GetAsync<CountResponse>($"/api/warehousing/orders/count?from={fromStr}&till={tillStr}");
                    if (res != null) return res.Count;
                }
                catch { }
            }

            var items = await GetAsync(predicates);
            return items.Count();
        }

        public async Task<IEnumerable<StockOrder>> GetAllAsync()
        {
            if (_cardCache.Count > 0)
                return _cardCache.Values.ToList();

            var local = LocalSqliteCache.GetAllDocuments<StockOrder>(DocType);
            if (local != null)
            {
                foreach (var item in local)
                {
                    if (!string.IsNullOrEmpty(item?.Id)) _cardCache[item.Id] = item;
                }
            }

            return _cardCache.Values.ToList();
        }

        public Task CreateAsync(StockOrder entity) => SaveAsync(entity);
        public Task UpdateAsync(StockOrder entity) => SaveAsync(entity);

        public async Task SaveAsync(StockOrder entity)
        {
            if (entity == null) return;
            if (string.IsNullOrEmpty(entity.Id)) entity.Id = Guid.NewGuid().ToString();

            _cardCache[entity.Id] = entity;
            LocalSqliteCache.SaveDocument(DocType, entity.Id, entity, isSynced: false);

            _ = Task.Run(async () =>
            {
                try
                {
                    await _restClient.PostAsync("/api/warehousing/orders", entity);
                    LocalSqliteCache.SaveDocument(DocType, entity.Id, entity, isSynced: true);
                }
                catch { }
            });
        }

        public async Task DeleteAsync(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            _cardCache.TryRemove(id, out _);

            _ = Task.Run(async () =>
            {
                try { await _restClient.DeleteAsync($"/api/warehousing/orders/{id}"); } catch { }
            });
        }

        public async Task<Dictionary<string, Dictionary<string, int>>> GetFacets(params string[] fields)
        {
            var dict = new Dictionary<string, Dictionary<string, int>>();
            if (fields != null)
            {
                foreach (var f in fields) dict[f] = new Dictionary<string, int>();
            }

            try
            {
                var query = fields != null && fields.Any() ? "?fields=" + string.Join(",", fields) : "";
                var result = await _restClient.GetAsync<Dictionary<string, Dictionary<string, int>>>($"/api/warehousing/orders/facets{query}");
                if (result != null)
                {
                    foreach (var kvp in result) dict[kvp.Key] = kvp.Value;
                }
            }
            catch { }

            return dict;
        }

        private static (bool HasDates, DateTime From, DateTime Till) TryExtractDateRange(Expression<Func<StockOrder, bool>>[] predicates)
        {
            if (predicates == null || predicates.Length == 0)
                return (false, default, default);

            DateTime from = DateTime.MinValue;
            DateTime till = DateTime.MaxValue;
            bool found = false;

            foreach (var pred in predicates.Where(p => p != null))
            {
                try
                {
                    ExtractDatesFromExpression(pred.Body, ref from, ref till, ref found);
                }
                catch { }
            }

            return (found, from, till);
        }

        private static void ExtractDatesFromExpression(Expression expr, ref DateTime from, ref DateTime till, ref bool found)
        {
            if (expr is BinaryExpression bin)
            {
                if (bin.NodeType == ExpressionType.AndAlso || bin.NodeType == ExpressionType.And)
                {
                    ExtractDatesFromExpression(bin.Left, ref from, ref till, ref found);
                    ExtractDatesFromExpression(bin.Right, ref from, ref till, ref found);
                    return;
                }

                if (bin.NodeType == ExpressionType.GreaterThanOrEqual || bin.NodeType == ExpressionType.GreaterThan)
                {
                    var val = EvaluateExpression(bin.Right);
                    if (val is DateTime dt) { from = dt; found = true; }
                }
                else if (bin.NodeType == ExpressionType.LessThanOrEqual || bin.NodeType == ExpressionType.LessThan)
                {
                    var val = EvaluateExpression(bin.Right);
                    if (val is DateTime dt) { till = dt; found = true; }
                }
            }
        }

        private static object EvaluateExpression(Expression expr)
        {
            if (expr is ConstantExpression ce) return ce.Value;
            if (expr is MemberExpression me)
            {
                var target = EvaluateExpression(me.Expression);
                if (me.Member is System.Reflection.FieldInfo fi) return fi.GetValue(target);
                if (me.Member is System.Reflection.PropertyInfo pi) return pi.GetValue(target);
            }
            var lambda = Expression.Lambda(expr);
            return lambda.Compile().DynamicInvoke();
        }

        private class CountResponse
        {
            public int Count { get; set; }
        }
    }

    public class ApiAggregatedStockOrdersRepository :
        IRepository<AggregatedStockOrder>,
        IReadOnlyRepository<AggregatedStockOrder>,
        IRepositoryWithFacets<AggregatedStockOrder>
    {
        private readonly RestClient _restClient;
        private const string DocType = "AggregatedStockOrder";

        private static readonly ConcurrentDictionary<string, AggregatedStockOrder> _cardCache = new(StringComparer.OrdinalIgnoreCase);

        public ApiAggregatedStockOrdersRepository(RestClient restClient)
        {
            _restClient = restClient;
        }

        public async Task<IEnumerable<AggregatedStockOrder>> GetAllAsync()
        {
            if (_cardCache.Count > 0)
                return _cardCache.Values.ToList();

            var local = LocalSqliteCache.GetAllDocuments<AggregatedStockOrder>(DocType);
            if (local != null && local.Any())
            {
                foreach (var item in local)
                {
                    if (!string.IsNullOrEmpty(item?.Id)) _cardCache[item.Id] = item;
                }
                return _cardCache.Values.ToList();
            }

            try
            {
                var remote = await _restClient.GetAsync<List<AggregatedStockOrder>>("/api/warehousing/aggregated-orders");
                if (remote != null)
                {
                    foreach (var item in remote)
                    {
                        if (!string.IsNullOrEmpty(item?.Id))
                        {
                            _cardCache[item.Id] = item;
                            LocalSqliteCache.SaveDocument(DocType, item.Id, item, isSynced: true);
                        }
                    }
                    return remote;
                }
            }
            catch { }

            return _cardCache.Values.ToList();
        }

        public async Task<AggregatedStockOrder> GetAsync(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            if (_cardCache.TryGetValue(id, out var cached))
                return cached;

            await GetAllAsync();
            if (_cardCache.TryGetValue(id, out cached))
                return cached;

            return null;
        }

        public async Task<IEnumerable<AggregatedStockOrder>> GetAsync(string[] ids)
        {
            if (ids == null || !ids.Any()) return Enumerable.Empty<AggregatedStockOrder>();
            var all = await GetAllAsync();
            var idSet = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
            return all.Where(x => idSet.Contains(x.Id)).ToList();
        }

        public async Task<IEnumerable<AggregatedStockOrder>> GetAsync(params Expression<Func<AggregatedStockOrder, bool>>[] predicates)
        {
            var all = await GetAllAsync();
            var query = all.AsQueryable();
            if (predicates != null)
            {
                foreach (var p in predicates.Where(x => x != null)) query = query.Where(p);
            }
            return query.ToList();
        }

        public async Task<int> CountAsync(params Expression<Func<AggregatedStockOrder, bool>>[] predicates)
        {
            return (await GetAsync(predicates)).Count();
        }

        public Task CreateAsync(AggregatedStockOrder entity) => SaveAsync(entity);
        public Task UpdateAsync(AggregatedStockOrder entity) => SaveAsync(entity);

        public async Task SaveAsync(AggregatedStockOrder entity)
        {
            if (entity == null) return;
            if (string.IsNullOrEmpty(entity.Id)) entity.Id = Guid.NewGuid().ToString();

            _cardCache[entity.Id] = entity;
            LocalSqliteCache.SaveDocument(DocType, entity.Id, entity, isSynced: false);

            _ = Task.Run(async () =>
            {
                try
                {
                    await _restClient.PostAsync("/api/warehousing/aggregated-orders", entity);
                    LocalSqliteCache.SaveDocument(DocType, entity.Id, entity, isSynced: true);
                }
                catch { }
            });
        }

        public async Task DeleteAsync(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            _cardCache.TryRemove(id, out _);

            _ = Task.Run(async () =>
            {
                try { await _restClient.DeleteAsync($"/api/warehousing/aggregated-orders/{id}"); } catch { }
            });
        }

        public async Task<Dictionary<string, Dictionary<string, int>>> GetFacets(params string[] fields)
        {
            var dict = new Dictionary<string, Dictionary<string, int>>();
            if (fields != null)
            {
                foreach (var f in fields) dict[f] = new Dictionary<string, int>();
            }

            try
            {
                var query = fields != null && fields.Any() ? "?fields=" + string.Join(",", fields) : "";
                var result = await _restClient.GetAsync<Dictionary<string, Dictionary<string, int>>>($"/api/warehousing/aggregated-orders/facets{query}");
                if (result != null)
                {
                    foreach (var kvp in result) dict[kvp.Key] = kvp.Value;
                }
            }
            catch { }

            return dict;
        }
    }
}
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Mermer.Data.Storage;
using Mermer.Http;
using Mermer.StockManagement.Models;
using Mermer.Warehousing.Revisioning.Models;
using Mermer.Warehousing.Revisioning.Services;

namespace Mermer.Ui.Pc.Services
{
    public class ApiStockRevisionsRepository :
        IStockRevisionsRepository,
        IRepository<StockRevision>,
        IReadOnlyRepository<StockRevision>,
        IRepositoryWithFacets<StockRevision>
    {
        private readonly RestClient _restClient;
        private const string DocType = "StockRevision";

        private static readonly ConcurrentDictionary<string, StockRevision> _cardCache = new(StringComparer.OrdinalIgnoreCase);

        public ApiStockRevisionsRepository(RestClient restClient)
        {
            _restClient = restClient ?? throw new ArgumentNullException(nameof(restClient));
        }

        public async Task<StockRevision> GetAsync(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            if (_cardCache.TryGetValue(id, out var cached))
                return cached;

            await GetAllAsync();
            if (_cardCache.TryGetValue(id, out cached))
                return cached;

            try
            {
                var remote = await _restClient.GetAsync<StockRevision>($"/api/warehousing/revisions/{id}");
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

        public async Task<IEnumerable<StockRevision>> GetAsync(string[] ids)
        {
            if (ids == null || !ids.Any()) return Enumerable.Empty<StockRevision>();
            var result = new List<StockRevision>();
            foreach (var id in ids)
            {
                var item = await GetAsync(id);
                if (item != null) result.Add(item);
            }
            return result;
        }

        // --- БЫСТРАЯ ВЫБОРКА ПО ДАТАМ (ПРЯМОЙ ЗАПРОС СРЕЗА) ---
        public async Task<IEnumerable<StockRevision>> GetAsync(params Expression<Func<StockRevision, bool>>[] predicates)
        {
            var (hasDates, from, till) = TryExtractDateRange(predicates);

            if (hasDates)
            {
                try
                {
                    var fromStr = from.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
                    var tillStr = till.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");

                    var remoteSlice = await _restClient.GetAsync<List<StockRevision>>($"/api/warehousing/revisions?from={fromStr}&till={tillStr}");
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

        public async Task<int> CountAsync(params Expression<Func<StockRevision, bool>>[] predicates)
        {
            var (hasDates, from, till) = TryExtractDateRange(predicates);

            if (hasDates)
            {
                try
                {
                    var fromStr = from.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
                    var tillStr = till.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");

                    var res = await _restClient.GetAsync<CountResponse>($"/api/warehousing/revisions/count?from={fromStr}&till={tillStr}");
                    if (res != null) return res.Count;
                }
                catch { }
            }

            var items = await GetAsync(predicates);
            return items.Count();
        }

        public async Task<IEnumerable<StockRevision>> GetAllAsync()
        {
            if (_cardCache.Count > 0)
                return _cardCache.Values.ToList();

            var local = LocalSqliteCache.GetAllDocuments<StockRevision>(DocType);
            if (local != null)
            {
                foreach (var item in local)
                {
                    if (!string.IsNullOrEmpty(item?.Id)) _cardCache[item.Id] = item;
                }
            }

            return _cardCache.Values.ToList();
        }

        public Task CreateAsync(StockRevision entity) => SaveAsync(entity);
        public Task UpdateAsync(StockRevision entity) => SaveAsync(entity);

        public async Task SaveAsync(StockRevision entity)
        {
            if (entity == null) return;
            if (string.IsNullOrEmpty(entity.Id)) entity.Id = Guid.NewGuid().ToString();

            _cardCache[entity.Id] = entity;
            LocalSqliteCache.SaveDocument(DocType, entity.Id, entity, isSynced: false);

            _ = Task.Run(async () =>
            {
                try
                {
                    await _restClient.PostAsync("/api/warehousing/revisions", entity);
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
                try { await _restClient.DeleteAsync($"/api/warehousing/revisions/{id}"); } catch { }
            });
        }

        public async Task<Dictionary<string, Dictionary<string, int>>> GetFacets(params string[] fields)
        {
            var dict = new Dictionary<string, Dictionary<string, int>>();
            if (fields != null) foreach (var f in fields) dict[f] = new Dictionary<string, int>();

            try
            {
                var fieldsParam = fields != null && fields.Length > 0 ? string.Join(",", fields) : "Date";
                var apiResult = await _restClient.GetAsync<Dictionary<string, Dictionary<string, int>>>($"/api/warehousing/revisions/facets?fields={fieldsParam}");
                if (apiResult != null)
                {
                    foreach (var kvp in apiResult) dict[kvp.Key] = kvp.Value;
                }
            }
            catch { }

            return dict;
        }

        public async Task<IEnumerable<StockRevisionLine>> GetLinesAsync(string revisionId, params string[] lineIds)
        {
            if (string.IsNullOrEmpty(revisionId)) return Enumerable.Empty<StockRevisionLine>();
            try
            {
                var remote = await _restClient.GetAsync<List<StockRevisionLine>>($"/api/warehousing/revisions/{revisionId}/lines");
                if (remote != null && lineIds != null && lineIds.Any())
                    return remote.Where(l => lineIds.Contains(l.Id)).ToList();
                return remote ?? Enumerable.Empty<StockRevisionLine>();
            }
            catch { return Enumerable.Empty<StockRevisionLine>(); }
        }

        public async Task<StockRevisionLine> GetLineAsync(string stockRevisionLineId)
        {
            if (string.IsNullOrEmpty(stockRevisionLineId)) return null;
            return (await GetLinesAsync(null, stockRevisionLineId)).FirstOrDefault();
        }

        public async Task StoreLineAsync(StockRevisionLine line)
        {
            if (line == null || string.IsNullOrEmpty(line.StockRevisionId)) return;
            try
            {
                await _restClient.PostAsync($"/api/warehousing/revisions/{line.StockRevisionId}/lines", line);
            }
            catch { }
        }

        public async Task StoreLinesAsync(string revisionId, IEnumerable<StockRevisionLine> list)
        {
            if (string.IsNullOrEmpty(revisionId) || list == null) return;
            foreach (var line in list)
            {
                line.StockRevisionId = revisionId;
                await StoreLineAsync(line);
            }
        }

        public async Task DeleteLineAsync(string stockRevisionLineId)
        {
            if (string.IsNullOrEmpty(stockRevisionLineId)) return;
            try
            {
                await _restClient.DeleteAsync($"/api/warehousing/revisions/lines/{stockRevisionLineId}");
            }
            catch { }
        }

        public async Task<IEnumerable<StockRevisionLineInfo>> CalcLineInfosAsync(
            StockRevision revision,
            IEnumerable<StockRevisionLine> lines,
            Func<string[], Task<IEnumerable<Stock>>> stocksGetter,
            Func<string[], Task<IEnumerable<StockBalance>>> stockBalancesGetter,
            Func<(string stockId, DateTime? balanceDate)[], Task<IEnumerable<StockBalance>>> stockBalancesGetterAlt,
            string priceDisplayCurrencyId = null)
        {
            var revLines = lines?.ToArray() ?? Array.Empty<StockRevisionLine>();
            if (!revLines.Any()) return Enumerable.Empty<StockRevisionLineInfo>();

            var stockIds = revLines.Select(x => x.StockId).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToArray();
            var stocks = (await stocksGetter(stockIds)).ToDictionary(x => x.Id, x => x);
            var balances = (await stockBalancesGetter(stockIds)).GroupBy(b => b.StockId).ToDictionary(g => g.Key, g => g.Sum(x => x.Balance));

            return revLines.Select(l =>
            {
                stocks.TryGetValue(l.StockId ?? "", out var stock);
                balances.TryGetValue(l.StockId ?? "", out var computed);

                decimal price = l.Price ?? stock?.Price ?? 0m;
                string unitName = stock?.Units?.FirstOrDefault(u => u.Id == l.UnitId)?.Name ?? stock?.Unit ?? "";

                return new StockRevisionLineInfo
                {
                    StockRevisionId = l.StockRevisionId,
                    StockRevisionLineId = l.Id,
                    StockId = l.StockId,
                    StockCode = stock?.Code ?? "",
                    StockName = stock?.Name ?? "",
                    StockPrice = price,
                    StockPriceCurrencyId = l.CurrencyId ?? stock?.CurrencyId ?? "",
                    Date = l.Date,
                    Quantity = l.Quantity,
                    UnitId = l.UnitId,
                    Unit = unitName,
                    TotalCounted = l.Quantity,
                    TotalComputed = computed,
                    UserId = l.UserId,
                    UserName = l.UserName
                };
            }).ToList();
        }

        public Task<IEnumerable<StockRevisionLineInfo>> GetLineInfosAsync(string revisionId, params string[] lineIds) => Task.FromResult(Enumerable.Empty<StockRevisionLineInfo>());
        public Task<StockRevisionCountInfo> GetCountInfoAsync(string revisionId, string stockId, Func<string, DateTime?> countDateGetter = null) => Task.FromResult(new StockRevisionCountInfo { StockId = stockId });
        public Task<IEnumerable<StockRevisionCountInfoWithData>> GetCountInfosAsync(string revisionId, string priceDisplayCurrencyId = null) => Task.FromResult(Enumerable.Empty<StockRevisionCountInfoWithData>());
        public Task<IEnumerable<StockRevisionUncountedInfo>> GetUncountedAsync(string revisionId) => Task.FromResult(Enumerable.Empty<StockRevisionUncountedInfo>());

        private static (bool HasDates, DateTime From, DateTime Till) TryExtractDateRange(Expression<Func<StockRevision, bool>>[] predicates)
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
}
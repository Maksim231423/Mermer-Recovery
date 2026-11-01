using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Mermer.CRM.Models;
using Mermer.Data.Storage;
using Mermer.Http;

namespace Mermer.Ui.Pc.Services;

public class ApiPartnerSlipsRepository : IRepositoryWithFacets<PartnerSlip>, IRepository<PartnerSlip>, IReadOnlyRepository<PartnerSlip>
{
    private readonly RestClient _restClient;
    private static readonly ConcurrentDictionary<string, PartnerSlip> _cardCache = new(StringComparer.OrdinalIgnoreCase);

    public ApiPartnerSlipsRepository(RestClient restClient)
    {
        _restClient = restClient ?? throw new Exception("Http RestClient cannot be null.");
    }

    public async Task<PartnerSlip> GetAsync(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        if (_cardCache.TryGetValue(id, out var cached))
            return cached;

        var all = await GetAllAsync();
        return all.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IEnumerable<PartnerSlip>> GetAsync(string[] ids)
    {
        if (ids == null || !ids.Any()) return Enumerable.Empty<PartnerSlip>();
        var all = await GetAllAsync();
        var idSet = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        return all.Where(x => idSet.Contains(x.Id)).ToList();
    }

    public async Task<IEnumerable<PartnerSlip>> GetAsync(params Expression<Func<PartnerSlip, bool>>[] predicates)
    {
        var (hasDates, from, till) = TryExtractDateRange(predicates);

        if (hasDates)
        {
            try
            {
                var fromStr = from.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
                var tillStr = till.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");

                var remoteSlice = await _restClient.GetAsync<List<PartnerSlip>>($"/api/partners/slips?from={fromStr}&till={tillStr}");
                if (remoteSlice != null)
                {
                    foreach (var item in remoteSlice)
                    {
                        if (!string.IsNullOrEmpty(item?.Id)) _cardCache[item.Id] = item;
                    }

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

    public async Task<int> CountAsync(params Expression<Func<PartnerSlip, bool>>[] predicates)
    {
        var (hasDates, from, till) = TryExtractDateRange(predicates);

        try
        {
            if (hasDates)
            {
                var fromStr = from.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
                var tillStr = till.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");

                var res = await _restClient.GetAsync<CountResponse>($"/api/partners/slips/count?from={fromStr}&till={tillStr}");
                if (res != null) return res.Count;
            }
            else
            {
                var res = await _restClient.GetAsync<CountResponse>("/api/partners/slips/count");
                if (res != null) return res.Count;
            }
        }
        catch { }

        var result = await GetAsync(predicates);
        return result?.Count() ?? 0;
    }

    public async Task<IEnumerable<PartnerSlip>> GetAllAsync()
    {
        if (_cardCache.Count > 0)
            return _cardCache.Values.ToList();

        try
        {
            var remote = await _restClient.GetAsync<List<PartnerSlip>>("/api/partners/slips");
            if (remote != null && remote.Any())
            {
                foreach (var item in remote)
                {
                    if (!string.IsNullOrEmpty(item?.Id))
                        _cardCache[item.Id] = item;
                }
            }
        }
        catch { }

        return _cardCache.Values.ToList();
    }

    public Task CreateAsync(PartnerSlip model) => SaveAsync(model);
    public Task UpdateAsync(PartnerSlip model) => SaveAsync(model);

    public async Task SaveAsync(PartnerSlip model)
    {
        if (model == null)
        {
            throw new Exception("Cannot save an empty PartnerSlip entity.");
        }

        bool isNew = string.IsNullOrEmpty(model.Id) || model.Id == Guid.Empty.ToString();
        if (isNew) model.Id = Guid.NewGuid().ToString();

        _cardCache[model.Id] = model;

        try
        {
            if (isNew) await _restClient.PostAsync("/api/partners/slips", model);
            else await _restClient.PutAsync($"/api/partners/slips/{model.Id}", model);
        }
        catch (Exception ex)
        {
            var detail = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            throw new Exception(string.Format("Failed to save PartnerSlip '{0}'. Details: {1}", model.Id, detail), ex);
        }

        UiEventBus.NotifyDocumentModified<PartnerSlip>(model.Id);
    }

    public async Task DeleteAsync(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            throw new Exception("PartnerSlip identifier cannot be empty for delete operation.");
        }

        _cardCache.TryRemove(id, out _);

        try
        {
            await _restClient.DeleteAsync($"/api/partners/slips/{id}");
        }
        catch (Exception ex)
        {
            var detail = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            throw new Exception(string.Format("Failed to delete PartnerSlip '{0}'. Details: {1}", id, detail), ex);
        }

        UiEventBus.NotifyDocumentModified<PartnerSlip>(id);
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
            var fieldsParam = fields != null && fields.Length > 0 ? string.Join(",", fields) : "Date";
            var apiResult = await _restClient.GetAsync<Dictionary<string, Dictionary<string, int>>>($"/api/partners/slips/facets?fields={fieldsParam}");
            if (apiResult != null)
            {
                foreach (var kvp in apiResult) dict[kvp.Key] = kvp.Value;
            }
        }
        catch { }

        return dict;
    }

    private static (bool HasDates, DateTime From, DateTime Till) TryExtractDateRange(Expression<Func<PartnerSlip, bool>>[] predicates)
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
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Mermer.Commerce.Models;
using Mermer.Commerce.Services;
using Mermer.Data.Storage;
using Mermer.Http;

namespace Mermer.Ui.Pc.Services
{
    public class ApiInvoicesRepository : IRepository<Invoice>, IReadOnlyRepository<Invoice>, IRepositoryWithFacets<Invoice>, IInvoicesRepository
    {
        private readonly RestClient _restClient;
        private const string DocType = "Invoice";

        // In-memory кэш единичных карточек
        private static readonly ConcurrentDictionary<string, Invoice> _cardCache = new(StringComparer.OrdinalIgnoreCase);

        public ApiInvoicesRepository(RestClient restClient)
        {
            _restClient = restClient ?? throw new ArgumentNullException(nameof(restClient));
        }

        // --- IRepositoryWithFacets (Выпадающие списки Group и Tags) ---

        public async Task<Dictionary<string, Dictionary<string, int>>> GetFacets(params string[] fields)
        {
            try
            {
                var fieldsParam = fields != null && fields.Length > 0 ? string.Join(",", fields) : "";
                var res = await _restClient.GetAsync<Dictionary<string, Dictionary<string, int>>>($"/api/invoices/facets?fields={fieldsParam}");
                if (res != null) return res;
            }
            catch { }

            var fallback = new Dictionary<string, Dictionary<string, int>>();
            if (fields != null)
            {
                foreach (var field in fields) fallback[field] = new Dictionary<string, int>();
            }
            return fallback;
        }

        // --- IInvoicesRepository СРЕЗЫ ДАННЫХ (ПРЯМЫЕ И БЫСТРЫЕ ВЫЗОВЫ) ---

        public Task<IEnumerable<InvoiceInfo>> GetInfoAsync(DateTime from, DateTime till)
        {
            return GetInfoAsync(from, till, null);
        }

        public async Task<IEnumerable<InvoiceInfo>> GetInfoAsync(DateTime from, DateTime till, string displayCurrencyId)
        {
            try
            {
                var fromUtc = from.Kind == DateTimeKind.Utc ? from : from.ToUniversalTime();
                var tillUtc = till.Kind == DateTimeKind.Utc ? till : till.ToUniversalTime();

                var fromStr = fromUtc.ToString("yyyy-MM-ddTHH:mm:ssZ");
                var tillStr = tillUtc.ToString("yyyy-MM-ddTHH:mm:ssZ");
                var url = $"/api/invoices?from={fromStr}&till={tillStr}";
                if (!string.IsNullOrEmpty(displayCurrencyId)) url += $"&displayCurrencyId={displayCurrencyId}";

                var result = await _restClient.GetAsync<List<InvoiceInfo>>(url);
                return result ?? Enumerable.Empty<InvoiceInfo>();
            }
            catch
            {
                return Enumerable.Empty<InvoiceInfo>();
            }
        }

        public async Task<int> CountInfoAsync(DateTime from, DateTime till)
        {
            try
            {
                var fromUtc = from.Kind == DateTimeKind.Utc ? from : from.ToUniversalTime();
                var tillUtc = till.Kind == DateTimeKind.Utc ? till : till.ToUniversalTime();

                var fromStr = fromUtc.ToString("yyyy-MM-ddTHH:mm:ssZ");
                var tillStr = tillUtc.ToString("yyyy-MM-ddTHH:mm:ssZ");

                var res = await _restClient.GetAsync<CountResponse>($"/api/invoices/count?from={fromStr}&till={tillStr}");
                return res?.Count ?? 0;
            }
            catch
            {
                return 0;
            }
        }

        public Task<IEnumerable<InvoicePaymentInfo>> GetPaymentInfoAsync(DateTime from, DateTime till, string officeId, string partnerId)
        {
            return GetPaymentInfoAsync(from, till, officeId, partnerId, null);
        }

        public async Task<IEnumerable<InvoicePaymentInfo>> GetPaymentInfoAsync(DateTime from, DateTime till, string officeId, string partnerId, string displayCurrencyId)
        {
            try
            {
                var fromStr = from.ToString("yyyy-MM-ddTHH:mm:ssZ");
                var tillStr = till.ToString("yyyy-MM-ddTHH:mm:ssZ");
                var url = $"/api/invoices/payment-info?from={fromStr}&till={tillStr}";
                if (!string.IsNullOrEmpty(officeId)) url += $"&officeId={officeId}";
                if (!string.IsNullOrEmpty(partnerId)) url += $"&partnerId={partnerId}";
                if (!string.IsNullOrEmpty(displayCurrencyId)) url += $"&displayCurrencyId={displayCurrencyId}";

                var dtoResult = await _restClient.GetAsync<List<InvoicePaymentInfoDto>>(url);
                if (dtoResult != null && dtoResult.Any())
                {
                    return dtoResult.Select(d =>
                    {
                        var info = new InvoicePaymentInfo
                        {
                            Id = d.Id,
                            Code = d.Code,
                            Date = d.Date,
                            DueDate = d.DueDate ?? default,
                            InvoiceType = d.InvoiceType,
                            IsCompleted = d.IsCompleted,
                            PartnerId = d.PartnerId,
                            OfficeId = d.OfficeId,
                            UserName = d.UserName
                        };

                        info.UpdatePaymentInfo(new[]
                        {
                            new CRM.Models.PartnerActionInfo
                            {
                                TransactionId = d.Id,
                                TransactionDate = d.Date,
                                ActionCredit = d.Total > 0 ? d.Total : d.GrandTotal,
                                ActionDebit = d.PaymentsTotal
                            }
                        });

                        return info;
                    }).ToList();
                }
            }
            catch { }

            return Enumerable.Empty<InvoicePaymentInfo>();
        }

        private class InvoicePaymentInfoDto
        {
            public string Id { get; set; }
            public string Code { get; set; }
            public DateTime Date { get; set; }
            public DateTime? DueDate { get; set; }
            public InvoiceType InvoiceType { get; set; }
            public bool IsCompleted { get; set; }
            public string PartnerId { get; set; }
            public string OfficeId { get; set; }
            public string UserName { get; set; }
            public decimal Total { get; set; }
            public decimal GrandTotal { get; set; }
            public decimal PaymentsTotal { get; set; }
            public DateTime? LastPaymentDate { get; set; }
        }

        public async Task<int> CountPaymentInfoAsync(DateTime from, DateTime till, string officeId = null, string partnerId = null)
        {
            try
            {
                var fromStr = from.ToString("yyyy-MM-ddTHH:mm:ssZ");
                var tillStr = till.ToString("yyyy-MM-ddTHH:mm:ssZ");
                var url = $"/api/invoices/payment-info/count?from={fromStr}&till={tillStr}";
                if (!string.IsNullOrEmpty(officeId)) url += $"&officeId={officeId}";
                if (!string.IsNullOrEmpty(partnerId)) url += $"&partnerId={partnerId}";

                var res = await _restClient.GetAsync<CountResponse>(url);
                return res?.Count ?? 0;
            }
            catch
            {
                return 0;
            }
        }

        // --- БАЗОВЫЕ МЕТОДЫ КАРТОЧЕК ---

        public async Task<IEnumerable<Invoice>> GetAllAsync()
        {
            if (_cardCache.Count > 0)
                return _cardCache.Values.ToList();

            var local = LocalSqliteCache.GetAllDocuments<Invoice>(DocType);
            if (local != null)
            {
                foreach (var inv in local)
                {
                    if (!string.IsNullOrEmpty(inv?.Id)) _cardCache[inv.Id] = inv;
                }
            }

            return _cardCache.Values.ToList();
        }

        public async Task<Invoice> GetAsync(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            if (_cardCache.TryGetValue(id, out var cached))
                return cached;

            try
            {
                var inv = await _restClient.GetAsync<Invoice>($"/api/invoices/{id}");
                if (inv != null)
                {
                    _cardCache[inv.Id] = inv;
                    LocalSqliteCache.SaveDocument(DocType, inv.Id, inv, isSynced: true);
                }
                return inv;
            }
            catch
            {
                return null;
            }
        }

        public async Task<IEnumerable<Invoice>> GetAsync(string[] ids)
        {
            if (ids == null || !ids.Any()) return Enumerable.Empty<Invoice>();
            var result = new List<Invoice>();
            foreach (var id in ids)
            {
                var item = await GetAsync(id);
                if (item != null) result.Add(item);
            }
            return result;
        }

        public async Task<IEnumerable<Invoice>> GetAsync(params Expression<Func<Invoice, bool>>[] predicates)
        {
            var all = await GetAllAsync();
            var query = all.AsQueryable();
            if (predicates != null)
            {
                foreach (var p in predicates) if (p != null) query = query.Where(p);
            }
            return query.ToList();
        }

        public async Task<int> CountAsync(params Expression<Func<Invoice, bool>>[] predicates)
        {
            var result = await GetAsync(predicates);
            return result.Count();
        }

        // --- CUD ОПЕРАЦИИ ---

        public async Task SaveAsync(Invoice entity)
        {
            if (entity == null) return;

            bool isNew = string.IsNullOrEmpty(entity.Id) || entity.Id == Guid.Empty.ToString();
            if (isNew) entity.Id = Guid.NewGuid().ToString();

            _cardCache[entity.Id] = entity;
            LocalSqliteCache.SaveDocument(DocType, entity.Id, entity, isSynced: false);

            _ = Task.Run(async () =>
            {
                try
                {
                    if (isNew) await _restClient.PostAsync("/api/invoices", entity);
                    else await _restClient.PutAsync($"/api/invoices/{entity.Id}", entity);
                    LocalSqliteCache.SaveDocument(DocType, entity.Id, entity, isSynced: true);
                }
                catch { }
            });
        }

        public Task CreateAsync(Invoice entity) => SaveAsync(entity);
        public Task UpdateAsync(Invoice entity) => SaveAsync(entity);

        public async Task DeleteAsync(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            _cardCache.TryRemove(id, out _);
            _ = Task.Run(async () =>
            {
                try { await _restClient.DeleteAsync($"/api/invoices/{id}"); } catch { }
            });
        }

        private class CountResponse
        {
            public int Count { get; set; }
        }
    }
}
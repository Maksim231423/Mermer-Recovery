using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Mermer.Data.Storage;
using Mermer.Finance.Spending.Models;
using Mermer.Http;

namespace Mermer.Ui.Pc.Services;

public class ApiExpenseSlipsRepository : IRepositoryWithFacets<ExpenseSlip>, IRepository<ExpenseSlip>, IReadOnlyRepository<ExpenseSlip>
{
    private readonly RestClient _restClient;
    private const string DocType = "ExpenseSlip";

    public ApiExpenseSlipsRepository(RestClient restClient)
    {
        _restClient = restClient ?? throw new ArgumentNullException(nameof(restClient));
    }

    public async Task<ExpenseSlip> GetAsync(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        var allLocal = LocalSqliteCache.GetAllDocuments<ExpenseSlip>(DocType);
        var local = allLocal?.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        if (local != null) return local;

        try
        {
            var remote = await _restClient.GetAsync<ExpenseSlip>($"/api/spending/slips/{id}");
            if (remote != null)
            {
                LocalSqliteCache.SaveDocument(DocType, remote.Id, remote, isSynced: true);
                return remote;
            }
        }
        catch { }

        return null;
    }

    public async Task<IEnumerable<ExpenseSlip>> GetAsync(string[] ids)
    {
        if (ids == null || !ids.Any()) return Enumerable.Empty<ExpenseSlip>();
        var all = await GetAllAsync();
        var idSet = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        return all.Where(x => idSet.Contains(x.Id)).ToList();
    }

    public async Task<IEnumerable<ExpenseSlip>> GetAsync(params Expression<Func<ExpenseSlip, bool>>[] predicates)
    {
        var all = await GetAllAsync();
        var query = all.AsQueryable();

        if (predicates != null && predicates.Any())
        {
            foreach (var p in predicates.Where(x => x != null)) query = query.Where(p);
        }

        return query.ToList();
    }

    private async Task<IEnumerable<ExpenseSlip>> GetAllAsync()
    {
        // 1. Асинхронная синхронизация несинхронизированных документов в фоне
        _ = Task.Run(async () =>
        {
            try
            {
                var unsynced = LocalSqliteCache.GetUnsyncedDocuments<ExpenseSlip>(DocType);
                if (unsynced != null && unsynced.Any())
                {
                    foreach (var item in unsynced)
                    {
                        await _restClient.PostAsync("/api/spending/slips", item.entity);
                        LocalSqliteCache.SaveDocument(DocType, item.id, item.entity, isSynced: true);
                    }
                }
            }
            catch { }
        });

        // 2. Получаем данные с сервера
        try
        {
            var remote = await _restClient.GetAsync<List<ExpenseSlip>>("/api/spending/slips");
            if (remote != null && remote.Any())
            {
                // ОПТИМИЗАЦИЯ: Сохранение в локальный SQLite-кэш убираем из блокирующего пути UI в фоновый Task
                _ = Task.Run(() =>
                {
                    try
                    {
                        foreach (var item in remote)
                        {
                            LocalSqliteCache.SaveDocument(DocType, item.Id, item, isSynced: true);
                        }
                    }
                    catch { }
                });

                return remote;
            }
        }
        catch { }

        // Если сеть недоступна — отдаем локальный кэш
        return LocalSqliteCache.GetAllDocuments<ExpenseSlip>(DocType)?.ToList() ?? new List<ExpenseSlip>();
    }

    public async Task<int> CountAsync(params Expression<Func<ExpenseSlip, bool>>[] predicates)
    {
        return (await GetAsync(predicates)).Count();
    }

    public async Task CreateAsync(ExpenseSlip model) => await SaveAsync(model);

    public async Task UpdateAsync(ExpenseSlip model) => await SaveAsync(model);

    public async Task SaveAsync(ExpenseSlip model)
    {
        if (model == null) return;

        bool isNew = string.IsNullOrEmpty(model.Id) || model.Id == Guid.Empty.ToString();
        if (isNew) model.Id = Guid.NewGuid().ToString();

        // 1. Сохраняем локально со статусом "не синхронизировано"
        LocalSqliteCache.SaveDocument(DocType, model.Id, model, isSynced: false);

        try
        {
            // 2. В зависимости от того, новый это документ или нет, вызываем POST или PUT
            if (isNew)
                await _restClient.PostAsync("/api/spending/slips", model);
            else
                await _restClient.PutAsync($"/api/spending/slips/{model.Id}", model);

            // 3. Отмечаем как "синхронизировано"
            LocalSqliteCache.SaveDocument(DocType, model.Id, model, isSynced: true);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Бэкенд отклонил синхронизацию.\nОшибка: {ex.Message}",
                "Ошибка синхронизации",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);

            System.Diagnostics.Debug.WriteLine($"[EXPENSE SLIP SYNC ERROR]: {ex.Message}");
        }
    }

    public async Task DeleteAsync(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        try { await _restClient.DeleteAsync($"/api/spending/slips/{id}"); } catch { }
    }

    public async Task<Dictionary<string, Dictionary<string, int>>> GetFacets(params string[] fields)
    {
        var dict = new Dictionary<string, Dictionary<string, int>>();
        if (fields != null) foreach (var f in fields) dict[f] = new Dictionary<string, int>();

        try
        {
            var fieldsParam = fields != null && fields.Length > 0 ? string.Join(",", fields) : "Date";
            var apiResult = await _restClient.GetAsync<Dictionary<string, Dictionary<string, int>>>($"/api/spending/slips/facets?fields={fieldsParam}");
            if (apiResult != null)
            {
                foreach (var kvp in apiResult) dict[kvp.Key] = kvp.Value;
            }
        }
        catch { }

        return dict;
    }
}
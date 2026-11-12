using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Mermer.Api.DTOs;
using Mermer.Data.Postgres;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mermer.Api.Services;

public class SyncBackgroundWorker : BackgroundService
{
    private const string ServerModuleId = "9a953aa5-2fd9-418d-bcf7-fb5bd7d09553";
    private const string SyncModuleId = "6b1495a1-60aa-4420-9c30-94718c121c26";

    private readonly IServiceProvider _serviceProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<SyncOptions> _syncOptionsMonitor;

    public SyncBackgroundWorker(
        IServiceProvider serviceProvider,
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<SyncOptions> syncOptionsMonitor)
    {
        _serviceProvider = serviceProvider ?? throw new Exception("ServiceProvider cannot be null.");
        _httpClientFactory = httpClientFactory ?? throw new Exception("HttpClientFactory cannot be null.");
        _syncOptionsMonitor = syncOptionsMonitor ?? throw new Exception("SyncOptionsMonitor cannot be null.");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _syncOptionsMonitor.CurrentValue;

            if (!options.Enabled)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("[SYNC WORKER] Offline mode active (Sync:Enabled = false). Background sync is disabled.");
                Console.ResetColor();
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                continue;
            }

            if (string.IsNullOrWhiteSpace(options.ServerUrl) || options.ServerUrl.Contains("vds.mermer-erp.com"))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[SYNC WORKER] Target VDS URL is invalid or unconfigured. Please configure a valid VDS server address in settings.");
                Console.ResetColor();
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                continue;
            }

            using (var scope = _serviceProvider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MermerDbContext>();
                var (serverOk, syncOk) = await CheckLicensesDirectlyAsync(db, stoppingToken);

                if (!serverOk || !syncOk)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine(string.Format(
                        "[SYNC WORKER] Synchronization halted: Server License [{0}] and Cloud Sync License [{1}] are required. Please activate licenses.",
                        serverOk ? "ACTIVE" : "MISSING",
                        syncOk ? "ACTIVE" : "MISSING"));
                    Console.ResetColor();

                    await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
                    continue;
                }
            }

            var interval = TimeSpan.FromSeconds(Math.Max(options.SyncIntervalSeconds, 5));

            try
            {
                await DoSyncCycleAsync(options, stoppingToken);
            }
            catch (Exception ex)
            {
                var detail = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine(string.Format("[SYNC WORKER] Background cycle skipped: {0}", detail));
                Console.ResetColor();
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static async Task<(bool serverOk, bool syncOk)> CheckLicensesDirectlyAsync(MermerDbContext db, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        var activeLicenses = await db.Licenses
            .AsNoTracking()
            .Where(l => l.IsActive && (!l.ValidTill.HasValue || l.ValidTill.Value > now))
            .ToListAsync(cancellationToken);

        bool HasModule(string targetId) => activeLicenses.Any(l =>
        {
            if (string.IsNullOrWhiteSpace(l.ModuleId)) return false;
            var parts = l.ModuleId.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return parts.Contains("all", StringComparer.OrdinalIgnoreCase) ||
                   parts.Contains(targetId, StringComparer.OrdinalIgnoreCase);
        });

        return (HasModule(ServerModuleId), HasModule(SyncModuleId));
    }

    public static string? ExtractClientIdFromLicenseKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;

        try
        {
            var parts = key.Trim().Split('.');
            if (parts.Length >= 2)
            {
                string payloadBase64 = parts[0].Replace('-', '+').Replace('_', '/');
                switch (payloadBase64.Length % 4)
                {
                    case 2: payloadBase64 += "=="; break;
                    case 3: payloadBase64 += "="; break;
                }

                var jsonBytes = Convert.FromBase64String(payloadBase64);
                using var doc = JsonDocument.Parse(jsonBytes);
                if (doc.RootElement.TryGetProperty("c", out var cProp))
                {
                    var val = cProp.GetString();
                    if (!string.IsNullOrWhiteSpace(val)) return val.Trim();
                }
            }
        }
        catch { }

        return null;
    }

    private static async Task<string> ResolveRealClientIdAsync(MermerDbContext db, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        var activeLicenses = await db.Licenses
            .AsNoTracking()
            .Where(l => l.IsActive && (!l.ValidTill.HasValue || l.ValidTill.Value > now))
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync(cancellationToken);

        foreach (var lic in activeLicenses)
        {
            var extractedClient = ExtractClientIdFromLicenseKey(lic.Key);
            if (!string.IsNullOrWhiteSpace(extractedClient)) return extractedClient;
        }

        if (!string.IsNullOrWhiteSpace(LicenseManager.CurrentClientId) &&
            !string.Equals(LicenseManager.CurrentClientId, "default", StringComparison.OrdinalIgnoreCase))
        {
            return LicenseManager.CurrentClientId;
        }

        return "default";
    }

    private async Task DoSyncCycleAsync(SyncOptions options, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MermerDbContext>();

        var currentClientId = await ResolveRealClientIdAsync(db, cancellationToken);
        MermerDbContext.CurrentClientId = currentClientId;

        var client = _httpClientFactory.CreateClient("VdsSyncClient");
        var baseUrl = options.ServerUrl.TrimEnd('/');

        // 1. ВЫБОРКА ВСЕХ НЕОТПРАВЛЕННЫХ ТАБЛИЦ
        var unsyncedInvoices = await db.Invoices.AsSplitQuery().Include(i => i.Lines).Include(i => i.Payments).Include(i => i.Discounts).Where(i => !i.IsSynced).OrderBy(i => i.CreatedAt).Take(100).ToListAsync(cancellationToken);
        var unsyncedStockSlips = await db.StockSlips.AsSplitQuery().Include(s => s.Lines).Where(s => !EF.Property<bool>(s, "IsSynced")).OrderBy(s => s.CreatedAt).Take(100).ToListAsync(cancellationToken);
        var unsyncedStockTransfers = await db.StockTransfers.AsSplitQuery().Include(s => s.Lines).Where(s => !EF.Property<bool>(s, "IsSynced")).OrderBy(s => s.CreatedAt).Take(100).ToListAsync(cancellationToken);
        var unsyncedStockRevisions = await db.StockRevisions.AsSplitQuery().Include(s => s.Lines).Where(s => !EF.Property<bool>(s, "IsSynced")).OrderBy(s => s.CreatedAt).Take(100).ToListAsync(cancellationToken);
        var unsyncedStockOrders = await db.StockOrders.AsSplitQuery().Include(s => s.Lines).Where(s => !EF.Property<bool>(s, "IsSynced")).OrderBy(s => s.CreatedAt).Take(100).ToListAsync(cancellationToken);
        var unsyncedAggregatedOrders = await db.AggregatedStockOrders.AsSplitQuery().Include(s => s.Lines).Where(s => !EF.Property<bool>(s, "IsSynced")).OrderBy(s => s.CreatedAt).Take(100).ToListAsync(cancellationToken);

        var unsyncedFundsSlips = await db.FundsSlips.AsSplitQuery().Include(f => f.Lines).Where(f => !EF.Property<bool>(f, "IsSynced")).OrderBy(f => f.CreatedAt).Take(100).ToListAsync(cancellationToken);
        var unsyncedFundsTransfers = await db.FundsTransfers.AsSplitQuery().Include(f => f.Lines).Where(f => !EF.Property<bool>(f, "IsSynced")).OrderBy(f => f.CreatedAt).Take(100).ToListAsync(cancellationToken);
        var unsyncedDailyRegisters = await db.DailyFundsRegisteries.AsSplitQuery().Include(d => d.Lines).Where(d => !EF.Property<bool>(d, "IsSynced")).OrderBy(d => d.CreatedAt).Take(100).ToListAsync(cancellationToken);
        var unsyncedExpenseSlips = await db.ExpenseSlips.AsSplitQuery().Include(e => e.Lines).Where(e => !EF.Property<bool>(e, "IsSynced")).OrderBy(e => e.CreatedAt).Take(100).ToListAsync(cancellationToken);

        var unsyncedPartnerSlips = await db.PartnerSlips.AsSplitQuery().Include(p => p.Lines).Where(p => !EF.Property<bool>(p, "IsSynced")).OrderBy(p => p.CreatedAt).Take(100).ToListAsync(cancellationToken);
        var unsyncedPartnerTransfers = await db.PartnerTransfers.AsSplitQuery().Include(p => p.Lines).Where(p => !EF.Property<bool>(p, "IsSynced")).OrderBy(p => p.CreatedAt).Take(100).ToListAsync(cancellationToken);
        var unsyncedPartners = await db.Partners.Where(p => !EF.Property<bool>(p, "IsSynced")).OrderBy(p => p.CreatedAt).Take(100).ToListAsync(cancellationToken);

        bool hasDataToPush = unsyncedInvoices.Any() || unsyncedStockSlips.Any() || unsyncedStockTransfers.Any() ||
                             unsyncedStockRevisions.Any() || unsyncedStockOrders.Any() || unsyncedAggregatedOrders.Any() ||
                             unsyncedFundsSlips.Any() || unsyncedFundsTransfers.Any() || unsyncedDailyRegisters.Any() ||
                             unsyncedExpenseSlips.Any() || unsyncedPartnerSlips.Any() || unsyncedPartnerTransfers.Any() ||
                             unsyncedPartners.Any();

        if (hasDataToPush)
        {
            var pushDto = new SyncPushRequestDto
            {
                ClientId = currentClientId,

                Invoices = unsyncedInvoices.Select(i => new InvoiceSyncDto
                {
                    Id = i.Id,
                    InvoiceNumber = i.Code ?? string.Empty,
                    InvoiceType = i.InvoiceType ?? "Sale",
                    Date = i.Date.UtcDateTime,
                    ClientId = i.UserId,
                    WarehouseId = i.WarehouseId,
                    Description = i.Description,
                    Lines = i.Lines.Where(l => l.StockId.HasValue && l.StockId.Value != Guid.Empty).Select(l => new InvoiceLineSyncDto { Id = l.Id, ProductId = l.StockId!.Value, Quantity = l.Quantity, UnitPrice = l.Price, TotalPrice = l.Quantity * l.Price }).ToList(),
                    Payments = i.Payments.Select(p => new InvoicePaymentSyncDto { Id = p.Id, PaymentType = p.PaymentType ?? "Payment", Amount = p.Amount, CurrencyId = p.CurrencyId, SortOrder = p.SortOrder }).ToList(),
                    Discounts = i.Discounts.Select(d => new InvoiceDiscountSyncDto { Id = d.Id, DiscountType = d.DiscountType ?? "Flat", Amount = d.Amount, Description = d.Description, SortOrder = d.SortOrder }).ToList()
                }).ToList(),

                StockSlips = unsyncedStockSlips.Select(s => new StockSlipSyncDto
                {
                    Id = s.Id,
                    SlipNumber = s.Code ?? string.Empty,
                    SlipType = s.SlipType ?? "Incoming",
                    Date = s.Date.UtcDateTime,
                    StockId = s.WarehouseId ?? Guid.Empty,
                    Description = s.Description,
                    Lines = s.Lines.Where(l => l.StockId.HasValue && l.StockId.Value != Guid.Empty).Select(l => new StockSlipLineSyncDto { Id = l.Id, ProductId = l.StockId!.Value, Quantity = l.Quantity }).ToList()
                }).ToList(),

                StockTransfers = unsyncedStockTransfers.Select(st => new StockTransferSyncDto
                {
                    Id = st.Id,
                    SlipNumber = st.Code ?? string.Empty,
                    Date = st.Date.UtcDateTime,
                    WarehouseId = st.WarehouseId,
                    DestinationWarehouseId = st.DestinationWarehouseId,
                    DisplayCurrencyId = st.DisplayCurrencyId,
                    UserName = st.UserName,
                    Description = st.Description,
                    ActionTotal = st.ActionTotal,
                    ActionReceivedTotal = st.ActionReceivedTotal,
                    Lines = st.Lines.Where(l => l.StockId.HasValue).Select(l => new StockTransferLineSyncDto { Id = l.Id, ProductId = l.StockId!.Value, UnitId = l.UnitId, ReceivedUnitId = l.ReceivedUnitId, Quantity = l.Quantity, ReceivedQuantity = l.ReceivedQuantity, UnitPrice = l.Price, ActionTotal = l.ActionTotal, ActionReceivedTotal = l.ActionReceivedTotal, SortOrder = l.SortOrder }).ToList()
                }).ToList(),

                StockRevisions = unsyncedStockRevisions.Select(sr => new StockRevisionSyncDto
                {
                    Id = sr.Id,
                    SlipNumber = sr.Code ?? string.Empty,
                    Date = sr.Date.UtcDateTime,
                    FinishDate = sr.FinishDate?.UtcDateTime,
                    WarehouseId = sr.WarehouseId,
                    ExceedSlipId = sr.ExceedSlipId,
                    DeficitSlipId = sr.DeficitSlipId,
                    UserId = sr.UserId,
                    UserName = sr.UserName,
                    Description = sr.Description,
                    Lines = sr.Lines.Where(l => l.StockId.HasValue).Select(l => new StockRevisionLineSyncDto { Id = l.Id, ProductId = l.StockId!.Value, Date = l.Date.UtcDateTime, Quantity = l.Quantity, UnitId = l.UnitId, Price = l.Price, CurrencyId = l.CurrencyId }).ToList()
                }).ToList(),

                StockOrders = unsyncedStockOrders.Select(so => new StockOrderSyncDto
                {
                    Id = so.Id,
                    SlipNumber = so.Code ?? string.Empty,
                    Date = so.Date.UtcDateTime,
                    WarehouseId = so.WarehouseId,
                    PartnerId = so.PartnerId,
                    UserId = so.UserId,
                    UserName = so.UserName,
                    Description = so.Description,
                    Lines = so.Lines.Where(l => l.StockId.HasValue).Select(l => new StockOrderLineSyncDto { Id = l.Id, ProductId = l.StockId!.Value, Quantity = l.Quantity, UnitId = l.UnitId }).ToList()
                }).ToList(),

                AggregatedStockOrders = unsyncedAggregatedOrders.Select(ao => new AggregatedStockOrderSyncDto
                {
                    Id = ao.Id,
                    SlipNumber = ao.Code ?? string.Empty,
                    Date = ao.Date.UtcDateTime,
                    WarehouseId = ao.WarehouseId,
                    PartnerId = ao.PartnerId,
                    UserId = ao.UserId,
                    UserName = ao.UserName,
                    Description = ao.Description,
                    Lines = ao.Lines.Select(l => new AggregatedStockOrderLineSyncDto { Id = l.Id, ProductId = l.StockId, UnitId = l.UnitId, OrdersJson = l.OrdersJson }).ToList()
                }).ToList(),

                FundsSlips = unsyncedFundsSlips.Select(f => new FundsSlipSyncDto
                {
                    Id = f.Id,
                    SlipNumber = f.Code ?? string.Empty,
                    SlipType = f.FundsSlipType ?? "FundsOpening",
                    Date = f.Date,
                    UserId = f.UserId,
                    UserName = f.UserName,
                    OfficeId = f.OfficeId,
                    DepositoryId = f.DepositoryId,
                    PartnerId = f.PartnerId,
                    DisplayCurrencyId = f.DisplayCurrencyId,
                    Description = f.Description,
                    Lines = f.Lines.Select(l => new FundsSlipLineSyncDto { Id = l.Id, Amount = l.Amount, CurrencyId = l.CurrencyId, SortOrder = l.SortOrder }).ToList()
                }).ToList(),

                FundsTransfers = unsyncedFundsTransfers.Select(ft => new FundsTransferSyncDto
                {
                    Id = ft.Id,
                    SlipNumber = ft.Code ?? string.Empty,
                    Date = ft.Date,
                    UserId = ft.UserId,
                    UserName = ft.UserName,
                    FromDepositoryId = ft.FromDepositoryId,
                    ToDepositoryId = ft.ToDepositoryId,
                    DisplayCurrencyId = ft.DisplayCurrencyId,
                    Description = ft.Description,
                    Lines = ft.Lines.Select(l => new FundsTransferLineSyncDto { Id = l.Id, Amount = l.Amount, ReceivedAmount = l.ReceivedAmount, CurrencyId = l.CurrencyId, SortOrder = l.SortOrder }).ToList()
                }).ToList(),

                DailyFundsRegisteries = unsyncedDailyRegisters.Select(dr => new DailyFundsRegisterySyncDto
                {
                    Id = dr.Id,
                    SlipNumber = dr.Code ?? string.Empty,
                    Date = dr.Date,
                    UserId = dr.UserId,
                    UserName = dr.UserName,
                    DepositoryId = dr.DepositoryId,
                    DisplayCurrencyId = dr.DisplayCurrencyId,
                    Description = dr.Description,
                    Lines = dr.Lines.Select(l => new DailyFundsRegisteryLineSyncDto { Id = l.Id, Amount = l.Amount, CurrencyId = l.CurrencyId, SortOrder = l.SortOrder }).ToList()
                }).ToList(),

                ExpenseSlips = unsyncedExpenseSlips.Select(es => new ExpenseSlipSyncDto
                {
                    Id = es.Id,
                    SlipNumber = es.Code ?? string.Empty,
                    Date = es.Date,
                    UserId = es.UserId,
                    UserName = es.UserName,
                    OfficeId = es.OfficeId,
                    DepositoryId = es.DepositoryId,
                    DisplayCurrencyId = es.DisplayCurrencyId,
                    Description = es.Description,
                    Lines = es.Lines.Select(l => new ExpenseSlipLineSyncDto { Id = l.Id, ExpenseId = l.ExpenseId, Amount = l.Amount, CurrencyId = l.CurrencyId, SortOrder = l.SortOrder }).ToList()
                }).ToList(),

                PartnerSlips = unsyncedPartnerSlips.Select(ps => new PartnerSlipSyncDto
                {
                    Id = ps.Id,
                    SlipNumber = ps.Code ?? string.Empty,
                    SlipType = ps.SlipType ?? string.Empty,
                    Date = ps.Date,
                    OfficeId = ps.OfficeId,
                    UserId = ps.UserId,
                    UserName = ps.UserName,
                    Description = ps.Description,
                    Lines = ps.Lines.Select(l => new PartnerSlipLineSyncDto { Id = l.Id, PartnerId = l.PartnerId, DebitAmount = l.DebitAmount, DebitCurrencyId = l.DebitCurrencyId, CreditAmount = l.CreditAmount, CreditCurrencyId = l.CreditCurrencyId }).ToList()
                }).ToList(),

                PartnerTransfers = unsyncedPartnerTransfers.Select(pt => new PartnerTransferSyncDto
                {
                    Id = pt.Id,
                    SlipNumber = pt.Code ?? string.Empty,
                    Date = pt.Date,
                    UserId = pt.UserId,
                    UserName = pt.UserName,
                    Description = pt.Description,
                    Lines = pt.Lines.Select(l => new PartnerTransferLineSyncDto { Id = l.Id, OfficeId = l.OfficeId, PartnerId = l.PartnerId, DebitAmount = l.DebitAmount, DebitCurrencyId = l.DebitCurrencyId, CreditAmount = l.CreditAmount, CreditCurrencyId = l.CreditCurrencyId }).ToList()
                }).ToList(),

                Partners = unsyncedPartners.Select(p => new PartnerSyncDto
                {
                    Id = p.Id,
                    Code = p.Code,
                    Name = p.Name,
                    Phone = p.Phone,
                    Address = p.Address,
                    Group = p.Group,
                    CreditLimit = p.CreditLimit,
                    Tags = p.Tags,
                    Description = p.Description,
                    Rating = p.Rating,
                    CurrencyId = p.CurrencyId,
                    IsDisabled = p.IsDisabled
                }).ToList()
            };

            var pushResponse = await client.PostAsJsonAsync($"{baseUrl}/api/sync/push", pushDto, cancellationToken);

            if (pushResponse.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[SYNC WORKER] Push rejected: Active licenses required on server.");
                Console.ResetColor();
                return;
            }

            if (pushResponse.IsSuccessStatusCode)
            {
                var result = await pushResponse.Content.ReadFromJsonAsync<SyncPushResponseDto>(cancellationToken: cancellationToken);
                if (result != null && result.Success)
                {
                    // Атомарно помечаем отправленные пачки как синхронизированные прямо в БД
                    if (unsyncedInvoices.Any())
                    {
                        var ids = unsyncedInvoices.Select(x => x.Id).ToList();
                        await db.Invoices.Where(x => ids.Contains(x.Id)).ExecuteUpdateAsync(s => s.SetProperty(b => b.IsSynced, true), cancellationToken);
                    }

                    if (unsyncedStockSlips.Any())
                    {
                        var ids = unsyncedStockSlips.Select(x => x.Id).ToList();
                        await db.StockSlips.Where(x => ids.Contains(x.Id)).ExecuteUpdateAsync(s => s.SetProperty(b => EF.Property<bool>(b, "IsSynced"), true), cancellationToken);
                    }

                    if (unsyncedStockTransfers.Any())
                    {
                        var ids = unsyncedStockTransfers.Select(x => x.Id).ToList();
                        await db.StockTransfers.Where(x => ids.Contains(x.Id)).ExecuteUpdateAsync(s => s.SetProperty(b => EF.Property<bool>(b, "IsSynced"), true), cancellationToken);
                    }

                    if (unsyncedStockRevisions.Any())
                    {
                        var ids = unsyncedStockRevisions.Select(x => x.Id).ToList();
                        await db.StockRevisions.Where(x => ids.Contains(x.Id)).ExecuteUpdateAsync(s => s.SetProperty(b => EF.Property<bool>(b, "IsSynced"), true), cancellationToken);
                    }

                    if (unsyncedStockOrders.Any())
                    {
                        var ids = unsyncedStockOrders.Select(x => x.Id).ToList();
                        await db.StockOrders.Where(x => ids.Contains(x.Id)).ExecuteUpdateAsync(s => s.SetProperty(b => EF.Property<bool>(b, "IsSynced"), true), cancellationToken);
                    }

                    if (unsyncedAggregatedOrders.Any())
                    {
                        var ids = unsyncedAggregatedOrders.Select(x => x.Id).ToList();
                        await db.AggregatedStockOrders.Where(x => ids.Contains(x.Id)).ExecuteUpdateAsync(s => s.SetProperty(b => EF.Property<bool>(b, "IsSynced"), true), cancellationToken);
                    }

                    if (unsyncedFundsSlips.Any())
                    {
                        var ids = unsyncedFundsSlips.Select(x => x.Id).ToList();
                        await db.FundsSlips.Where(x => ids.Contains(x.Id)).ExecuteUpdateAsync(s => s.SetProperty(b => EF.Property<bool>(b, "IsSynced"), true), cancellationToken);
                    }

                    if (unsyncedFundsTransfers.Any())
                    {
                        var ids = unsyncedFundsTransfers.Select(x => x.Id).ToList();
                        await db.FundsTransfers.Where(x => ids.Contains(x.Id)).ExecuteUpdateAsync(s => s.SetProperty(b => EF.Property<bool>(b, "IsSynced"), true), cancellationToken);
                    }

                    if (unsyncedDailyRegisters.Any())
                    {
                        var ids = unsyncedDailyRegisters.Select(x => x.Id).ToList();
                        await db.DailyFundsRegisteries.Where(x => ids.Contains(x.Id)).ExecuteUpdateAsync(s => s.SetProperty(b => EF.Property<bool>(b, "IsSynced"), true), cancellationToken);
                    }

                    if (unsyncedExpenseSlips.Any())
                    {
                        var ids = unsyncedExpenseSlips.Select(x => x.Id).ToList();
                        await db.ExpenseSlips.Where(x => ids.Contains(x.Id)).ExecuteUpdateAsync(s => s.SetProperty(b => EF.Property<bool>(b, "IsSynced"), true), cancellationToken);
                    }

                    if (unsyncedPartnerSlips.Any())
                    {
                        var ids = unsyncedPartnerSlips.Select(x => x.Id).ToList();
                        await db.PartnerSlips.Where(x => ids.Contains(x.Id)).ExecuteUpdateAsync(s => s.SetProperty(b => EF.Property<bool>(b, "IsSynced"), true), cancellationToken);
                    }

                    if (unsyncedPartnerTransfers.Any())
                    {
                        var ids = unsyncedPartnerTransfers.Select(x => x.Id).ToList();
                        await db.PartnerTransfers.Where(x => ids.Contains(x.Id)).ExecuteUpdateAsync(s => s.SetProperty(b => EF.Property<bool>(b, "IsSynced"), true), cancellationToken);
                    }

                    if (unsyncedPartners.Any())
                    {
                        var ids = unsyncedPartners.Select(x => x.Id).ToList();
                        await db.Partners.Where(x => ids.Contains(x.Id)).ExecuteUpdateAsync(s => s.SetProperty(b => EF.Property<bool>(b, "IsSynced"), true), cancellationToken);
                    }

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"[SYNC WORKER] Pushed to VDS for client '{currentClientId}': " +
                                      $"{unsyncedInvoices.Count} Invoices, {unsyncedStockSlips.Count} StockSlips, " +
                                      $"{unsyncedFundsSlips.Count} FundsSlips, {unsyncedFundsTransfers.Count} FundsTransfers, " +
                                      $"{unsyncedStockTransfers.Count} StockTransfers, {unsyncedStockRevisions.Count} StockRevisions, " +
                                      $"{unsyncedStockOrders.Count} StockOrders, {unsyncedExpenseSlips.Count} ExpenseSlips, " +
                                      $"{unsyncedPartnerSlips.Count} PartnerSlips, {unsyncedDailyRegisters.Count} DailyRegisters.");
                    Console.ResetColor();
                }
            }
        }

        // 2. PULL: Запрос справочников с сервера
        var pullUrl = $"{baseUrl}/api/sync/pull?clientId={Uri.EscapeDataString(currentClientId)}";
        var pullResponse = await client.GetAsync(pullUrl, cancellationToken);

        if (pullResponse.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[SYNC WORKER] Pull rejected: Active licenses required on server.");
            Console.ResetColor();
            return;
        }

        if (pullResponse.IsSuccessStatusCode)
        {
            var pullData = await pullResponse.Content.ReadFromJsonAsync<SyncPullResponseDto>(cancellationToken: cancellationToken);
            if (pullData != null)
            {
                var now = DateTimeOffset.UtcNow;
                await ApplyPullDataAsync(db, pullData, now, cancellationToken);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[SYNC WORKER] Full PULL completed. All references & prices synchronized at {DateTime.Now:HH:mm:ss}.");
                Console.ResetColor();
            }
        }
    }

    private static async Task ApplyPullDataAsync(MermerDbContext db, SyncPullResponseDto pullData, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (pullData.Offices != null && pullData.Offices.Any())
        {
            foreach (var dto in pullData.Offices)
            {
                var entity = await db.Offices.FirstOrDefaultAsync(x => x.Id == dto.Id, cancellationToken);
                if (entity == null)
                    db.Offices.Add(new Data.Postgres.Entities.OfficeEntity { Id = dto.Id, Name = dto.Name, Region = dto.Region, Description = dto.Description, Tags = dto.Tags, IsDisabled = dto.IsDisabled, CreatedAt = now, UpdatedAt = now });
                else
                { entity.Name = dto.Name; entity.Region = dto.Region; entity.Description = dto.Description; entity.Tags = dto.Tags; entity.IsDisabled = dto.IsDisabled; entity.UpdatedAt = now; }
            }
            await db.SaveChangesAsync(cancellationToken);
        }

        if (pullData.Warehouses != null && pullData.Warehouses.Any())
        {
            foreach (var dto in pullData.Warehouses)
            {
                var entity = await db.Warehouses.FirstOrDefaultAsync(x => x.Id == dto.Id, cancellationToken);
                if (entity == null)
                    db.Warehouses.Add(new Data.Postgres.Entities.WarehouseEntity { Id = dto.Id, OfficeId = dto.OfficeId, Name = dto.Name, Description = dto.Description, Tags = dto.Tags, IsDisabled = dto.IsDisabled, CreatedAt = now, UpdatedAt = now });
                else
                { entity.OfficeId = dto.OfficeId; entity.Name = dto.Name; entity.Description = dto.Description; entity.Tags = dto.Tags; entity.IsDisabled = dto.IsDisabled; entity.UpdatedAt = now; }
            }
            await db.SaveChangesAsync(cancellationToken);
        }

        if (pullData.Depositories != null && pullData.Depositories.Any())
        {
            foreach (var dto in pullData.Depositories)
            {
                var entity = await db.Depositories.FirstOrDefaultAsync(x => x.Id == dto.Id, cancellationToken);
                if (entity == null)
                    db.Depositories.Add(new Data.Postgres.Entities.DepositoryEntity { Id = dto.Id, OfficeId = dto.OfficeId, Name = dto.Name, Description = dto.Description, Tags = dto.Tags, IsDisabled = dto.IsDisabled, CreatedAt = now, UpdatedAt = now });
                else
                { entity.OfficeId = dto.OfficeId; entity.Name = dto.Name; entity.Description = dto.Description; entity.Tags = dto.Tags; entity.IsDisabled = dto.IsDisabled; entity.UpdatedAt = now; }
            }
            await db.SaveChangesAsync(cancellationToken);
        }

        if (pullData.Stocks != null && pullData.Stocks.Any())
        {
            foreach (var dto in pullData.Stocks)
            {
                var entity = await db.Stocks.FirstOrDefaultAsync(x => x.Id == dto.Id, cancellationToken);
                if (entity == null)
                    db.Stocks.Add(new Data.Postgres.Entities.StockEntity { Id = dto.Id, Code = dto.Code, Name = dto.Name, ShortName = dto.ShortName, Type = dto.Type ?? "Stock", Group = dto.Group, Tags = dto.Tags, Barcodes = dto.Barcodes, LimitMin = dto.LimitMin, LimitMax = dto.LimitMax, Description = dto.Description, IsDisabled = dto.IsDisabled, CreatedAt = now, UpdatedAt = now });
                else
                { entity.Code = dto.Code; entity.Name = dto.Name; entity.ShortName = dto.ShortName; entity.Type = dto.Type; entity.Group = dto.Group; entity.Tags = dto.Tags; entity.Barcodes = dto.Barcodes; entity.LimitMin = dto.LimitMin; entity.LimitMax = dto.LimitMax; entity.Description = dto.Description; entity.IsDisabled = dto.IsDisabled; entity.UpdatedAt = now; }
            }
            await db.SaveChangesAsync(cancellationToken);
        }

        if (pullData.StockBalances != null && pullData.StockBalances.Any())
        {
            foreach (var dto in pullData.StockBalances)
            {
                var entity = await db.StockBalances.FirstOrDefaultAsync(x => x.WarehouseId == dto.WarehouseId && x.StockId == dto.StockId, cancellationToken);
                if (entity == null)
                    db.StockBalances.Add(new Data.Postgres.Entities.StockBalanceEntity { WarehouseId = dto.WarehouseId, StockId = dto.StockId, Income = dto.Income, Expense = dto.Expense, UpdatedAt = now });
                else
                { entity.Income = dto.Income; entity.Expense = dto.Expense; entity.UpdatedAt = now; }
            }
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
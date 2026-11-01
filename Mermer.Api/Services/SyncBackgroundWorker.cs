using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Mermer.Api.DTOs;
using Mermer.Data.Postgres;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mermer.Api.Services;

public class SyncBackgroundWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SyncOptions _syncOptions;

    public SyncBackgroundWorker(
        IServiceProvider serviceProvider,
        IHttpClientFactory httpClientFactory,
        IOptions<SyncOptions> syncOptions)
    {
        _serviceProvider = serviceProvider ?? throw new Exception("ServiceProvider cannot be null.");
        _httpClientFactory = httpClientFactory ?? throw new Exception("HttpClientFactory cannot be null.");
        _syncOptions = syncOptions?.Value ?? new SyncOptions();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_syncOptions.Enabled)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("[SYNC WORKER] Offline mode active (Sync:Enabled = false). Background sync is disabled.");
            Console.ResetColor();
            return;
        }

        if (string.IsNullOrWhiteSpace(_syncOptions.ServerUrl))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[SYNC WORKER] ServerUrl is empty. Background sync halted.");
            Console.ResetColor();
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(_syncOptions.SyncIntervalSeconds, 10));

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(string.Format("[SYNC WORKER] Started. Target VDS: {0}, Interval: {1}s", _syncOptions.ServerUrl, interval.TotalSeconds));
        Console.ResetColor();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DoSyncCycleAsync(stoppingToken);
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

    private async Task DoSyncCycleAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();

        // 1. Идентификация клиента
        var currentClientId = !string.IsNullOrWhiteSpace(LicenseManager.CurrentClientId) && LicenseManager.CurrentClientId != "default"
            ? LicenseManager.CurrentClientId
            : "default";

        MermerDbContext.CurrentClientId = currentClientId;

        // 2. Проверка лицензии
        var licenseValidator = scope.ServiceProvider.GetRequiredService<IServerLicenseValidationService>();
        bool isLicenseValid = await licenseValidator.IsServerLicenseValidAsync();

        if (!isLicenseValid)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[SYNC WORKER] Skip VDS push: Active server license not found or expired.");
            Console.ResetColor();
            return;
        }

        var db = scope.ServiceProvider.GetRequiredService<MermerDbContext>();
        var client = _httpClientFactory.CreateClient("VdsSyncClient");
        var baseUrl = _syncOptions.ServerUrl.TrimEnd('/');

        // ====================================================================
        // 1. PUSH: Отправка накладных и складских актов
        // ====================================================================
        var unsyncedInvoices = await db.Invoices
            .AsSplitQuery()
            .Include(i => i.Lines)
            .Include(i => i.Payments)
            .Include(i => i.Discounts)
            .Where(i => !i.IsSynced)
            .OrderBy(i => i.CreatedAt)
            .Take(100)
            .ToListAsync(cancellationToken);

        var unsyncedSlips = await db.StockSlips
            .AsSplitQuery()
            .Include(s => s.Lines)
            .Where(s => !s.IsSynced)
            .OrderBy(s => s.CreatedAt)
            .Take(100)
            .ToListAsync(cancellationToken);

        if (unsyncedInvoices.Any() || unsyncedSlips.Any())
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
                    Lines = i.Lines
                        .Where(l => l.StockId.HasValue && l.StockId.Value != Guid.Empty)
                        .Select(l => new InvoiceLineSyncDto
                        {
                            Id = l.Id,
                            ProductId = l.StockId!.Value,
                            Quantity = l.Quantity,
                            UnitPrice = l.Price,
                            TotalPrice = l.Quantity * l.Price
                        }).ToList(),
                    Payments = i.Payments.Select(p => new InvoicePaymentSyncDto
                    {
                        Id = p.Id,
                        PaymentType = p.PaymentType ?? "Payment",
                        Amount = p.Amount,
                        CurrencyId = p.CurrencyId,
                        SortOrder = p.SortOrder
                    }).ToList(),
                    Discounts = i.Discounts.Select(d => new InvoiceDiscountSyncDto
                    {
                        Id = d.Id,
                        DiscountType = d.DiscountType ?? "Flat",
                        Amount = d.Amount,
                        Description = d.Description,
                        SortOrder = d.SortOrder
                    }).ToList()
                }).ToList(),
                StockSlips = unsyncedSlips.Select(s => new StockSlipSyncDto
                {
                    Id = s.Id,
                    SlipNumber = s.Code ?? string.Empty,
                    SlipType = s.SlipType ?? "Incoming",
                    Date = s.Date.UtcDateTime,
                    StockId = s.WarehouseId ?? Guid.Empty,
                    Description = s.Description,
                    Lines = s.Lines
                        .Where(l => l.StockId.HasValue && l.StockId.Value != Guid.Empty)
                        .Select(l => new StockSlipLineSyncDto
                        {
                            Id = l.Id,
                            ProductId = l.StockId!.Value,
                            Quantity = l.Quantity
                        }).ToList()
                }).ToList()
            };

            var pushResponse = await client.PostAsJsonAsync(string.Format("{0}/api/sync/push", baseUrl), pushDto, cancellationToken);

            if (pushResponse.IsSuccessStatusCode)
            {
                var result = await pushResponse.Content.ReadFromJsonAsync<SyncPushResponseDto>(cancellationToken: cancellationToken);
                if (result != null && result.Success)
                {
                    foreach (var inv in unsyncedInvoices)
                    {
                        inv.IsSynced = true;
                    }

                    foreach (var slip in unsyncedSlips)
                    {
                        slip.IsSynced = true;
                    }

                    await db.SaveChangesAsync(cancellationToken);

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine(string.Format("[SYNC WORKER] Pushed {0} invoices and {1} stock slips to VDS.", unsyncedInvoices.Count, unsyncedSlips.Count));
                    Console.ResetColor();
                }
            }
        }

        // ====================================================================
        // 2. PULL: Скачивание актуальных справочников с VDS
        // ====================================================================
        var pullUrl = string.Format("{0}/api/sync/pull?clientId={1}", baseUrl, Uri.EscapeDataString(currentClientId));
        var pullResponse = await client.GetAsync(pullUrl, cancellationToken);

        if (pullResponse.IsSuccessStatusCode)
        {
            var pullData = await pullResponse.Content.ReadFromJsonAsync<SyncPullResponseDto>(cancellationToken: cancellationToken);
            if (pullData != null)
            {
                var now = DateTimeOffset.UtcNow;

                // --- 1. Торговые точки (Offices) ---
                if (pullData.Offices != null && pullData.Offices.Any())
                {
                    foreach (var dto in pullData.Offices)
                    {
                        var entity = await db.Offices.FirstOrDefaultAsync(x => x.Id == dto.Id, cancellationToken);
                        if (entity == null)
                        {
                            db.Offices.Add(new Data.Postgres.Entities.OfficeEntity
                            {
                                Id = dto.Id,
                                Name = dto.Name,
                                Region = dto.Region,
                                Description = dto.Description,
                                Tags = dto.Tags,
                                IsDisabled = dto.IsDisabled,
                                CreatedAt = now,
                                UpdatedAt = now
                            });
                        }
                        else
                        {
                            entity.Name = dto.Name;
                            entity.Region = dto.Region;
                            entity.Description = dto.Description;
                            entity.Tags = dto.Tags;
                            entity.IsDisabled = dto.IsDisabled;
                            entity.UpdatedAt = now;
                        }
                    }
                    await db.SaveChangesAsync(cancellationToken);
                }

                // --- 2. Валюты (Currencies) и Курсы (CurrencyRates) ---
                if (pullData.Currencies != null && pullData.Currencies.Any())
                {
                    foreach (var dto in pullData.Currencies)
                    {
                        var entity = await db.Currencies.FirstOrDefaultAsync(x => x.Id == dto.Id, cancellationToken);
                        if (entity == null)
                        {
                            db.Currencies.Add(new Data.Postgres.Entities.CurrencyEntity
                            {
                                Id = dto.Id,
                                Name = dto.Name,
                                Decimals = dto.Decimals,
                                IsDefault = dto.IsDefault,
                                Description = dto.Description,
                                IsDisabled = dto.IsDisabled,
                                CreatedAt = now,
                                UpdatedAt = now
                            });
                        }
                        else
                        {
                            entity.Name = dto.Name;
                            entity.Decimals = dto.Decimals;
                            entity.IsDefault = dto.IsDefault;
                            entity.Description = dto.Description;
                            entity.IsDisabled = dto.IsDisabled;
                            entity.UpdatedAt = now;
                        }
                    }
                    await db.SaveChangesAsync(cancellationToken);
                }

                if (pullData.CurrencyRates != null && pullData.CurrencyRates.Any())
                {
                    foreach (var dto in pullData.CurrencyRates)
                    {
                        var entity = await db.CurrencyRates.FirstOrDefaultAsync(x => x.Id == dto.Id, cancellationToken);
                        if (entity == null)
                        {
                            db.CurrencyRates.Add(new Data.Postgres.Entities.CurrencyRateEntity
                            {
                                Id = dto.Id,
                                CurrencyId = dto.CurrencyId,
                                ValidFrom = dto.ValidFrom,
                                Multiplier = dto.Multiplier,
                                Divider = dto.Divider,
                                CreatedAt = now
                            });
                        }
                        else
                        {
                            entity.Multiplier = dto.Multiplier;
                            entity.Divider = dto.Divider;
                            entity.ValidFrom = dto.ValidFrom;
                        }
                    }
                    await db.SaveChangesAsync(cancellationToken);
                }

                // --- 3. Склады (Warehouses) и Кассы (Depositories) ---
                if (pullData.Warehouses != null && pullData.Warehouses.Any())
                {
                    foreach (var dto in pullData.Warehouses)
                    {
                        var entity = await db.Warehouses.FirstOrDefaultAsync(x => x.Id == dto.Id, cancellationToken);
                        if (entity == null)
                        {
                            db.Warehouses.Add(new Data.Postgres.Entities.WarehouseEntity
                            {
                                Id = dto.Id,
                                OfficeId = dto.OfficeId,
                                Name = dto.Name,
                                Description = dto.Description,
                                Tags = dto.Tags,
                                IsDisabled = dto.IsDisabled,
                                CreatedAt = now,
                                UpdatedAt = now
                            });
                        }
                        else
                        {
                            entity.OfficeId = dto.OfficeId;
                            entity.Name = dto.Name;
                            entity.Description = dto.Description;
                            entity.Tags = dto.Tags;
                            entity.IsDisabled = dto.IsDisabled;
                            entity.UpdatedAt = now;
                        }
                    }
                    await db.SaveChangesAsync(cancellationToken);
                }

                if (pullData.Depositories != null && pullData.Depositories.Any())
                {
                    foreach (var dto in pullData.Depositories)
                    {
                        var entity = await db.Depositories.FirstOrDefaultAsync(x => x.Id == dto.Id, cancellationToken);
                        if (entity == null)
                        {
                            db.Depositories.Add(new Data.Postgres.Entities.DepositoryEntity
                            {
                                Id = dto.Id,
                                OfficeId = dto.OfficeId,
                                Name = dto.Name,
                                Description = dto.Description,
                                Tags = dto.Tags,
                                IsDisabled = dto.IsDisabled,
                                CreatedAt = now,
                                UpdatedAt = now
                            });
                        }
                        else
                        {
                            entity.OfficeId = dto.OfficeId;
                            entity.Name = dto.Name;
                            entity.Description = dto.Description;
                            entity.Tags = dto.Tags;
                            entity.IsDisabled = dto.IsDisabled;
                            entity.UpdatedAt = now;
                        }
                    }
                    await db.SaveChangesAsync(cancellationToken);
                }

                // --- 4. Контрагенты (Partners) ---
                if (pullData.Partners != null && pullData.Partners.Any())
                {
                    foreach (var dto in pullData.Partners)
                    {
                        var entity = await db.Partners.FirstOrDefaultAsync(x => x.Id == dto.Id, cancellationToken);
                        if (entity == null)
                        {
                            db.Partners.Add(new Data.Postgres.Entities.PartnerEntity
                            {
                                Id = dto.Id,
                                Code = dto.Code,
                                Name = dto.Name,
                                Phone = dto.Phone,
                                Address = dto.Address,
                                Group = dto.Group,
                                CreditLimit = dto.CreditLimit,
                                Tags = dto.Tags,
                                Description = dto.Description,
                                Rating = dto.Rating,
                                CurrencyId = dto.CurrencyId,
                                IsDisabled = dto.IsDisabled,
                                CreatedAt = now,
                                UpdatedAt = now
                            });
                        }
                        else
                        {
                            entity.Code = dto.Code;
                            entity.Name = dto.Name;
                            entity.Phone = dto.Phone;
                            entity.Address = dto.Address;
                            entity.Group = dto.Group;
                            entity.CreditLimit = dto.CreditLimit;
                            entity.Tags = dto.Tags;
                            entity.Description = dto.Description;
                            entity.Rating = dto.Rating;
                            entity.CurrencyId = dto.CurrencyId;
                            entity.IsDisabled = dto.IsDisabled;
                            entity.UpdatedAt = now;
                        }
                    }
                    await db.SaveChangesAsync(cancellationToken);
                }

                // --- 5. Статьи расходов (Expenses) ---
                if (pullData.Expenses != null && pullData.Expenses.Any())
                {
                    foreach (var dto in pullData.Expenses)
                    {
                        var entity = await db.Expenses.FirstOrDefaultAsync(x => x.Id == dto.Id, cancellationToken);
                        if (entity == null)
                        {
                            db.Expenses.Add(new Data.Postgres.Entities.ExpenseEntity
                            {
                                Id = dto.Id,
                                Name = dto.Name,
                                Description = dto.Description,
                                Group = dto.Group,
                                Tags = dto.Tags,
                                IsDisabled = dto.IsDisabled,
                                CreatedAt = now.UtcDateTime,
                                UpdatedAt = now.UtcDateTime
                            });
                        }
                        else
                        {
                            entity.Name = dto.Name;
                            entity.Description = dto.Description;
                            entity.Group = dto.Group;
                            entity.Tags = dto.Tags;
                            entity.IsDisabled = dto.IsDisabled;
                            entity.UpdatedAt = now.UtcDateTime;
                        }
                    }
                    await db.SaveChangesAsync(cancellationToken);
                }

                // --- 6. Роли (Roles) и Пользователи (Users) ---
                if (pullData.Roles != null && pullData.Roles.Any())
                {
                    foreach (var dto in pullData.Roles)
                    {
                        var entity = await db.Roles.FirstOrDefaultAsync(x => x.Id == dto.Id, cancellationToken);
                        if (entity == null)
                        {
                            db.Roles.Add(new Data.Postgres.Entities.RoleEntity
                            {
                                Id = dto.Id,
                                Name = dto.Name,
                                Description = dto.Description,
                                AuthorizationsJson = dto.AuthorizationsJson,
                                AccountPrivilegesJson = dto.AccountPrivilegesJson,
                                IsDisabled = dto.IsDisabled,
                                CreatedAt = now,
                                UpdatedAt = now
                            });
                        }
                        else
                        {
                            entity.Name = dto.Name;
                            entity.Description = dto.Description;
                            entity.AuthorizationsJson = dto.AuthorizationsJson;
                            entity.AccountPrivilegesJson = dto.AccountPrivilegesJson;
                            entity.IsDisabled = dto.IsDisabled;
                            entity.UpdatedAt = now;
                        }
                    }
                    await db.SaveChangesAsync(cancellationToken);
                }

                if (pullData.Users != null && pullData.Users.Any())
                {
                    foreach (var dto in pullData.Users)
                    {
                        var entity = await db.Users.FirstOrDefaultAsync(x => x.Id == dto.Id, cancellationToken);
                        if (entity == null)
                        {
                            db.Users.Add(new Data.Postgres.Entities.UserEntity
                            {
                                Id = dto.Id,
                                Username = dto.Username,
                                Password = dto.Password,
                                IsAdmin = dto.IsAdmin,
                                IsDisabled = dto.IsDisabled,
                                Description = dto.Description,
                                Roles = dto.Roles ?? new List<string>(),
                                CreatedAt = now,
                                UpdatedAt = now
                            });
                        }
                        else
                        {
                            entity.Username = dto.Username;
                            if (!string.IsNullOrEmpty(dto.Password)) entity.Password = dto.Password;
                            entity.IsAdmin = dto.IsAdmin;
                            entity.IsDisabled = dto.IsDisabled;
                            entity.Description = dto.Description;
                            entity.Roles = dto.Roles ?? entity.Roles;
                            entity.UpdatedAt = now;
                        }
                    }
                    await db.SaveChangesAsync(cancellationToken);
                }

                // --- 7. Номенклатура (Stocks) ---
                if (pullData.Stocks != null && pullData.Stocks.Any())
                {
                    foreach (var dto in pullData.Stocks)
                    {
                        var entity = await db.Stocks.FirstOrDefaultAsync(x => x.Id == dto.Id, cancellationToken);
                        if (entity == null)
                        {
                            db.Stocks.Add(new Data.Postgres.Entities.StockEntity
                            {
                                Id = dto.Id,
                                Code = dto.Code,
                                Name = dto.Name,
                                ShortName = dto.ShortName,
                                Type = dto.Type ?? "Stock",
                                Group = dto.Group,
                                Tags = dto.Tags,
                                Barcodes = dto.Barcodes,
                                LimitMin = dto.LimitMin,
                                LimitMax = dto.LimitMax,
                                Description = dto.Description,
                                IsDisabled = dto.IsDisabled,
                                CreatedAt = now,
                                UpdatedAt = now
                            });
                        }
                        else
                        {
                            entity.Code = dto.Code;
                            entity.Name = dto.Name;
                            entity.ShortName = dto.ShortName;
                            entity.Type = dto.Type;
                            entity.Group = dto.Group;
                            entity.Tags = dto.Tags;
                            entity.Barcodes = dto.Barcodes;
                            entity.LimitMin = dto.LimitMin;
                            entity.LimitMax = dto.LimitMax;
                            entity.Description = dto.Description;
                            entity.IsDisabled = dto.IsDisabled;
                            entity.UpdatedAt = now;
                        }
                    }
                    await db.SaveChangesAsync(cancellationToken);
                }

                // --- 8. Единицы измерения (StockUnits) ---
                if (pullData.StockUnits != null && pullData.StockUnits.Any())
                {
                    foreach (var dto in pullData.StockUnits)
                    {
                        var entity = await db.StockUnits.FirstOrDefaultAsync(x => x.Id == dto.Id, cancellationToken);
                        if (entity == null)
                        {
                            db.StockUnits.Add(new Data.Postgres.Entities.StockUnitEntity
                            {
                                Id = dto.Id,
                                StockId = dto.StockId,
                                Name = dto.Name,
                                Multiplier = dto.Multiplier,
                                Divider = dto.Divider,
                                IsDefault = dto.IsDefault
                            });
                        }
                        else
                        {
                            entity.Name = dto.Name;
                            entity.Multiplier = dto.Multiplier;
                            entity.Divider = dto.Divider;
                            entity.IsDefault = dto.IsDefault;
                        }
                    }
                    await db.SaveChangesAsync(cancellationToken);
                }

                // --- 9. Прайс-листы и цены (StockPrices) ---
                if (pullData.StockPrices != null && pullData.StockPrices.Any())
                {
                    foreach (var dto in pullData.StockPrices)
                    {
                        var entity = await db.StockPrices.FirstOrDefaultAsync(x => x.Id == dto.Id, cancellationToken);
                        if (entity == null)
                        {
                            db.StockPrices.Add(new Data.Postgres.Entities.StockPriceEntity
                            {
                                Id = dto.Id,
                                StockId = dto.StockId,
                                CurrencyId = dto.CurrencyId,
                                Price = dto.Price,
                                PriceGroup = dto.PriceGroup,
                                ValidFrom = dto.ValidFrom,
                                CreatedAt = now
                            });
                        }
                        else
                        {
                            entity.Price = dto.Price;
                            entity.CurrencyId = dto.CurrencyId;
                            entity.PriceGroup = dto.PriceGroup;
                            entity.ValidFrom = dto.ValidFrom;
                        }
                    }
                    await db.SaveChangesAsync(cancellationToken);
                }

                // --- 10. Дополнительные цены (StockAdditionalPrices) ---
                if (pullData.StockAdditionalPrices != null && pullData.StockAdditionalPrices.Any())
                {
                    foreach (var dto in pullData.StockAdditionalPrices)
                    {
                        var entity = await db.StockAdditionalPrices.FirstOrDefaultAsync(x => x.Id == dto.Id, cancellationToken);
                        if (entity == null)
                        {
                            db.StockAdditionalPrices.Add(new Data.Postgres.Entities.StockAdditionalPriceEntity
                            {
                                Id = dto.Id,
                                StockId = dto.StockId,
                                CurrencyId = dto.CurrencyId,
                                Price = dto.Price,
                                PriceGroup = dto.PriceGroup,
                                ValidFrom = dto.ValidFrom
                            });
                        }
                        else
                        {
                            entity.Price = dto.Price;
                            entity.CurrencyId = dto.CurrencyId;
                            entity.PriceGroup = dto.PriceGroup;
                            entity.ValidFrom = dto.ValidFrom;
                        }
                    }
                    await db.SaveChangesAsync(cancellationToken);
                }

                // --- 11. Остатки на складах (StockBalances) ---
                if (pullData.StockBalances != null && pullData.StockBalances.Any())
                {
                    foreach (var dto in pullData.StockBalances)
                    {
                        var entity = await db.StockBalances.FirstOrDefaultAsync(
                            x => x.WarehouseId == dto.WarehouseId && x.StockId == dto.StockId,
                            cancellationToken);

                        if (entity == null)
                        {
                            db.StockBalances.Add(new Data.Postgres.Entities.StockBalanceEntity
                            {
                                WarehouseId = dto.WarehouseId,
                                StockId = dto.StockId,
                                Income = dto.Income,
                                Expense = dto.Expense,
                                UpdatedAt = now
                            });
                        }
                        else
                        {
                            entity.Income = dto.Income;
                            entity.Expense = dto.Expense;
                            entity.UpdatedAt = now;
                        }
                    }
                    await db.SaveChangesAsync(cancellationToken);
                }

                // --- 12. Группы аналогов (StockAlternatives) ---
                if (pullData.StockAlternatives != null && pullData.StockAlternatives.Any())
                {
                    foreach (var dto in pullData.StockAlternatives)
                    {
                        var entity = await db.StockAlternatives.FirstOrDefaultAsync(x => x.Id == dto.Id, cancellationToken);
                        if (entity == null)
                        {
                            db.StockAlternatives.Add(new Data.Postgres.Entities.StockAlternativeEntity
                            {
                                Id = dto.Id,
                                Name = dto.Name,
                                Description = dto.Description,
                                IsDisabled = dto.IsDisabled,
                                CreatedAt = now,
                                UpdatedAt = now
                            });
                        }
                        else
                        {
                            entity.Name = dto.Name;
                            entity.Description = dto.Description;
                            entity.IsDisabled = dto.IsDisabled;
                            entity.UpdatedAt = now;
                        }
                    }
                    await db.SaveChangesAsync(cancellationToken);
                }

                // --- 13. Строки аналогов (StockAlternativeLines) ---
                if (pullData.StockAlternativeLines != null && pullData.StockAlternativeLines.Any())
                {
                    foreach (var dto in pullData.StockAlternativeLines)
                    {
                        var entity = await db.StockAlternativeLines.FirstOrDefaultAsync(x => x.Id == dto.Id, cancellationToken);
                        if (entity == null)
                        {
                            db.StockAlternativeLines.Add(new Data.Postgres.Entities.StockAlternativeLineEntity
                            {
                                Id = dto.Id,
                                StockAlternativeId = dto.StockAlternativeId,
                                StockId = dto.StockId
                            });
                        }
                        else
                        {
                            entity.StockAlternativeId = dto.StockAlternativeId;
                            entity.StockId = dto.StockId;
                        }
                    }
                    await db.SaveChangesAsync(cancellationToken);
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(string.Format("[SYNC WORKER] Full PULL completed. All references & prices synchronized at {0:HH:mm:ss}.", DateTime.Now));
                Console.ResetColor();
            }
        }
    }
}
using Microsoft.EntityFrameworkCore;
using Mermer.Api.DTOs;
using Mermer.Data.Postgres;
using Mermer.Data.Postgres.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mermer.Api.Services;

public class SyncService : ISyncService
{
    private readonly MermerDbContext _dbContext;
    private readonly IStockBalanceCalculator _balanceCalculator;

    public SyncService(
        MermerDbContext dbContext,
        IStockBalanceCalculator balanceCalculator)
    {
        _dbContext = dbContext ?? throw new Exception("Database context cannot be null.");
        _balanceCalculator = balanceCalculator ?? throw new Exception("Stock balance calculator cannot be null.");
    }

    public async Task<SyncPullResponseDto> ProcessPullAsync(string? clientId = null, CancellationToken cancellationToken = default)
    {
        var targetClientId = !string.IsNullOrWhiteSpace(clientId)
            ? clientId
            : (!string.IsNullOrWhiteSpace(LicenseManager.CurrentClientId) ? LicenseManager.CurrentClientId : "default");

        return new SyncPullResponseDto
        {
            ServerTime = DateTimeOffset.UtcNow,

            // 1. Организационная структура и валюты
            Offices = await _dbContext.Offices.AsNoTracking()
                .Where(x => EF.Property<string>(x, "ClientId") == targetClientId || EF.Property<string>(x, "ClientId") == "default")
                .Select(x => new OfficeSyncDto
                {
                    Id = x.Id,
                    Name = x.Name,
                    Region = x.Region,
                    Description = x.Description,
                    Tags = x.Tags,
                    IsDisabled = x.IsDisabled
                }).ToListAsync(cancellationToken),

            Warehouses = await _dbContext.Warehouses.AsNoTracking()
                .Where(x => EF.Property<string>(x, "ClientId") == targetClientId || EF.Property<string>(x, "ClientId") == "default")
                .Select(x => new WarehouseSyncDto
                {
                    Id = x.Id,
                    OfficeId = x.OfficeId,
                    Name = x.Name,
                    Description = x.Description,
                    Tags = x.Tags,
                    IsDisabled = x.IsDisabled
                }).ToListAsync(cancellationToken),

            Depositories = await _dbContext.Depositories.AsNoTracking()
                .Where(x => EF.Property<string>(x, "ClientId") == targetClientId || EF.Property<string>(x, "ClientId") == "default")
                .Select(x => new DepositorySyncDto
                {
                    Id = x.Id,
                    OfficeId = x.OfficeId,
                    Name = x.Name,
                    Description = x.Description,
                    Tags = x.Tags,
                    IsDisabled = x.IsDisabled
                }).ToListAsync(cancellationToken),

            Currencies = await _dbContext.Currencies.AsNoTracking()
                .Where(x => EF.Property<string>(x, "ClientId") == targetClientId || EF.Property<string>(x, "ClientId") == "default")
                .Select(x => new CurrencySyncDto
                {
                    Id = x.Id,
                    Name = x.Name,
                    Decimals = x.Decimals,
                    IsDefault = x.IsDefault,
                    Description = x.Description,
                    IsDisabled = x.IsDisabled
                }).ToListAsync(cancellationToken),

            CurrencyRates = await _dbContext.CurrencyRates.AsNoTracking()
                .Where(x => EF.Property<string>(x, "ClientId") == targetClientId || EF.Property<string>(x, "ClientId") == "default")
                .Select(x => new CurrencyRateSyncDto
                {
                    Id = x.Id,
                    CurrencyId = x.CurrencyId,
                    ValidFrom = x.ValidFrom,
                    Multiplier = x.Multiplier,
                    Divider = x.Divider
                }).ToListAsync(cancellationToken),

            Partners = await _dbContext.Partners.AsNoTracking()
                .Where(x => EF.Property<string>(x, "ClientId") == targetClientId || EF.Property<string>(x, "ClientId") == "default")
                .Select(x => new PartnerSyncDto
                {
                    Id = x.Id,
                    Code = x.Code,
                    Name = x.Name,
                    Phone = x.Phone,
                    Address = x.Address,
                    Group = x.Group,
                    CreditLimit = x.CreditLimit,
                    Tags = x.Tags,
                    Description = x.Description,
                    Rating = x.Rating,
                    CurrencyId = x.CurrencyId,
                    IsDisabled = x.IsDisabled
                }).ToListAsync(cancellationToken),

            // 2. Статьи затрат
            Expenses = await _dbContext.Expenses.AsNoTracking()
                .Where(x => EF.Property<string>(x, "ClientId") == targetClientId || EF.Property<string>(x, "ClientId") == "default")
                .Select(x => new ExpenseSyncDto
                {
                    Id = x.Id,
                    Name = x.Name,
                    Description = x.Description,
                    Group = x.Group,
                    Tags = x.Tags,
                    IsDisabled = x.IsDisabled
                }).ToListAsync(cancellationToken),

            // 3. Персонал и безопасность
            Roles = await _dbContext.Roles.AsNoTracking()
                .Where(x => EF.Property<string>(x, "ClientId") == targetClientId || EF.Property<string>(x, "ClientId") == "default")
                .Select(x => new RoleSyncDto
                {
                    Id = x.Id,
                    Name = x.Name,
                    Description = x.Description,
                    AuthorizationsJson = x.AuthorizationsJson,
                    AccountPrivilegesJson = x.AccountPrivilegesJson,
                    IsDisabled = x.IsDisabled
                }).ToListAsync(cancellationToken),

            Users = await _dbContext.Users.AsNoTracking()
                .Where(x => EF.Property<string>(x, "ClientId") == targetClientId || EF.Property<string>(x, "ClientId") == "default")
                .Select(x => new UserSyncDto
                {
                    Id = x.Id,
                    Username = x.Username,
                    Password = x.Password,
                    IsAdmin = x.IsAdmin,
                    IsDisabled = x.IsDisabled,
                    Description = x.Description,
                    Roles = x.Roles
                }).ToListAsync(cancellationToken),

            // 4. Каталог товаров, единицы и цены
            Stocks = await _dbContext.Stocks.AsNoTracking()
                .Where(x => EF.Property<string>(x, "ClientId") == targetClientId || EF.Property<string>(x, "ClientId") == "default")
                .Select(x => new StockSyncDto
                {
                    Id = x.Id,
                    Code = x.Code,
                    Name = x.Name,
                    ShortName = x.ShortName,
                    Type = x.Type,
                    Group = x.Group,
                    Tags = x.Tags,
                    Barcodes = x.Barcodes,
                    LimitMin = x.LimitMin,
                    LimitMax = x.LimitMax,
                    Description = x.Description,
                    IsDisabled = x.IsDisabled
                }).ToListAsync(cancellationToken),

            StockUnits = await _dbContext.StockUnits.AsNoTracking()
                .Where(x => EF.Property<string>(x, "ClientId") == targetClientId || EF.Property<string>(x, "ClientId") == "default")
                .Select(x => new StockUnitSyncDto
                {
                    Id = x.Id,
                    StockId = x.StockId,
                    Name = x.Name,
                    Multiplier = x.Multiplier,
                    Divider = x.Divider,
                    IsDefault = x.IsDefault
                }).ToListAsync(cancellationToken),

            StockPrices = await _dbContext.StockPrices.AsNoTracking()
                .Where(x => EF.Property<string>(x, "ClientId") == targetClientId || EF.Property<string>(x, "ClientId") == "default")
                .Select(x => new StockPriceSyncDto
                {
                    Id = x.Id,
                    StockId = x.StockId,
                    CurrencyId = x.CurrencyId,
                    Price = x.Price,
                    PriceGroup = x.PriceGroup,
                    ValidFrom = x.ValidFrom
                }).ToListAsync(cancellationToken),

            StockAdditionalPrices = await _dbContext.StockAdditionalPrices.AsNoTracking()
                .Where(x => EF.Property<string>(x, "ClientId") == targetClientId || EF.Property<string>(x, "ClientId") == "default")
                .Select(x => new StockAdditionalPriceSyncDto
                {
                    Id = x.Id,
                    StockId = x.StockId,
                    CurrencyId = x.CurrencyId,
                    Price = x.Price,
                    PriceGroup = x.PriceGroup,
                    ValidFrom = x.ValidFrom
                }).ToListAsync(cancellationToken),

            StockBalances = await _dbContext.StockBalances.AsNoTracking()
                .Where(x => EF.Property<string>(x, "ClientId") == targetClientId || EF.Property<string>(x, "ClientId") == "default")
                .Select(x => new StockBalanceSyncDto
                {
                    WarehouseId = x.WarehouseId,
                    StockId = x.StockId,
                    Income = x.Income,
                    Expense = x.Expense
                }).ToListAsync(cancellationToken),

            // 5. Аналоги и заменители товаров
            StockAlternatives = await _dbContext.StockAlternatives.AsNoTracking()
                .Where(x => EF.Property<string>(x, "ClientId") == targetClientId || EF.Property<string>(x, "ClientId") == "default")
                .Select(x => new StockAlternativeSyncDto
                {
                    Id = x.Id,
                    Name = x.Name,
                    Description = x.Description,
                    IsDisabled = x.IsDisabled
                }).ToListAsync(cancellationToken),

            StockAlternativeLines = await _dbContext.StockAlternativeLines.AsNoTracking()
                .Where(x => EF.Property<string>(x, "ClientId") == targetClientId || EF.Property<string>(x, "ClientId") == "default")
                .Select(x => new StockAlternativeLineSyncDto
                {
                    Id = x.Id,
                    StockAlternativeId = x.StockAlternativeId,
                    StockId = x.StockId ?? Guid.Empty
                }).ToListAsync(cancellationToken)
        };
    }

    public async Task<SyncPushResponseDto> ProcessPushAsync(SyncPushRequestDto request, CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new Exception("Sync push request payload cannot be empty.");
        }

        var targetClientId = !string.IsNullOrWhiteSpace(request.ClientId)
            ? request.ClientId
            : (!string.IsNullOrWhiteSpace(LicenseManager.CurrentClientId) ? LicenseManager.CurrentClientId : "default");

        if (string.IsNullOrWhiteSpace(targetClientId))
        {
            throw new Exception("Client identification failed. ClientId cannot be empty.");
        }

        MermerDbContext.CurrentClientId = targetClientId;

        var response = new SyncPushResponseDto();
        var strategy = _dbContext.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var now = DateTimeOffset.UtcNow;

                // 0. Защита внешних ключей: проверяем существование товаров и складов
                var requestedProductIds = (request.Invoices?.SelectMany(i => i.Lines?.Select(l => l.ProductId) ?? Enumerable.Empty<Guid>()) ?? Enumerable.Empty<Guid>())
                    .Concat(request.StockSlips?.SelectMany(s => s.Lines?.Select(l => l.ProductId) ?? Enumerable.Empty<Guid>()) ?? Enumerable.Empty<Guid>())
                    .Where(id => id != Guid.Empty)
                    .Distinct()
                    .ToList();

                var existingProductIds = (await _dbContext.Stocks
                    .Where(s => requestedProductIds.Contains(s.Id))
                    .Select(s => s.Id)
                    .ToListAsync(cancellationToken))
                    .ToHashSet();

                var requestedWarehouseIds = (request.Invoices?.Where(i => i.WarehouseId.HasValue && i.WarehouseId.Value != Guid.Empty).Select(i => i.WarehouseId!.Value) ?? Enumerable.Empty<Guid>())
                    .Concat(request.StockSlips?.Where(s => s.StockId != Guid.Empty).Select(s => s.StockId) ?? Enumerable.Empty<Guid>())
                    .Distinct()
                    .ToList();

                var existingWarehouseIds = (await _dbContext.Warehouses
                    .Where(w => requestedWarehouseIds.Contains(w.Id))
                    .Select(w => w.Id)
                    .ToListAsync(cancellationToken))
                    .ToHashSet();

                // ====================================================================
                // 1. НАКЛАДНЫЕ (INVOICES): СТРОКИ, ОПЛАТЫ И СКИДКИ
                // ====================================================================
                if (request.Invoices != null)
                {
                    foreach (var invDto in request.Invoices)
                    {
                        Guid? resolvedUserId = invDto.ClientId;
                        if (!resolvedUserId.HasValue && Guid.TryParse(targetClientId, out var parsedGuid))
                        {
                            resolvedUserId = parsedGuid;
                        }

                        Guid? validWarehouseId = invDto.WarehouseId.HasValue && existingWarehouseIds.Contains(invDto.WarehouseId.Value)
                            ? invDto.WarehouseId
                            : null;

                        var existingInvoice = await _dbContext.Invoices
                            .FirstOrDefaultAsync(i => i.Id == invDto.Id, cancellationToken);

                        Guid currentInvoiceId;

                        if (existingInvoice != null)
                        {
                            currentInvoiceId = existingInvoice.Id;

                            existingInvoice.Code = invDto.InvoiceNumber;
                            existingInvoice.InvoiceType = invDto.InvoiceType;
                            existingInvoice.Date = invDto.Date;
                            existingInvoice.UserId = resolvedUserId;
                            existingInvoice.WarehouseId = validWarehouseId;
                            existingInvoice.Description = invDto.Description;
                            existingInvoice.UpdatedAt = now;

                            // Прямое атомарное удаление строк в БД
                            await _dbContext.InvoiceLines.Where(l => l.InvoiceId == currentInvoiceId).ExecuteDeleteAsync(cancellationToken);
                            await _dbContext.InvoicePayments.Where(p => p.InvoiceId == currentInvoiceId).ExecuteDeleteAsync(cancellationToken);
                            await _dbContext.InvoiceDiscounts.Where(d => d.InvoiceId == currentInvoiceId).ExecuteDeleteAsync(cancellationToken);
                        }
                        else
                        {
                            currentInvoiceId = invDto.Id;

                            var invoice = new InvoiceEntity
                            {
                                Id = currentInvoiceId,
                                Code = invDto.InvoiceNumber,
                                InvoiceType = invDto.InvoiceType,
                                Date = invDto.Date,
                                UserId = resolvedUserId,
                                WarehouseId = validWarehouseId,
                                Description = invDto.Description,
                                IsCompleted = true,
                                IsDisabled = false,
                                CreatedAt = now,
                                UpdatedAt = now
                            };

                            _dbContext.Invoices.Add(invoice);
                        }

                        // Строки товаров (только валидные существующие товары)
                        if (invDto.Lines != null && invDto.Lines.Any())
                        {
                            var validLines = invDto.Lines
                                .Where(l => l.ProductId != Guid.Empty && existingProductIds.Contains(l.ProductId))
                                .Select((lineDto, idx) => new InvoiceLineEntity
                                {
                                    Id = lineDto.Id == Guid.Empty ? Guid.NewGuid() : lineDto.Id,
                                    InvoiceId = currentInvoiceId,
                                    StockId = lineDto.ProductId,
                                    Quantity = lineDto.Quantity,
                                    Price = lineDto.UnitPrice,
                                    SortOrder = idx
                                });
                            await _dbContext.InvoiceLines.AddRangeAsync(validLines, cancellationToken);
                        }

                        // Строки оплат
                        if (invDto.Payments != null && invDto.Payments.Any())
                        {
                            var newPayments = invDto.Payments.Select(pDto => new InvoicePaymentEntity
                            {
                                Id = pDto.Id == Guid.Empty ? Guid.NewGuid() : pDto.Id,
                                InvoiceId = currentInvoiceId,
                                PaymentType = pDto.PaymentType ?? "Payment",
                                CurrencyId = pDto.CurrencyId,
                                Amount = pDto.Amount,
                                SortOrder = pDto.SortOrder
                            });
                            await _dbContext.InvoicePayments.AddRangeAsync(newPayments, cancellationToken);
                        }

                        // Строки скидок
                        if (invDto.Discounts != null && invDto.Discounts.Any())
                        {
                            var newDiscounts = invDto.Discounts.Select(dDto => new InvoiceDiscountEntity
                            {
                                Id = dDto.Id == Guid.Empty ? Guid.NewGuid() : dDto.Id,
                                InvoiceId = currentInvoiceId,
                                DiscountType = dDto.DiscountType ?? "Flat",
                                Amount = dDto.Amount,
                                Description = dDto.Description,
                                SortOrder = dDto.SortOrder
                            });
                            await _dbContext.InvoiceDiscounts.AddRangeAsync(newDiscounts, cancellationToken);
                        }

                        response.ProcessedInvoicesCount++;
                    }
                }

                // ====================================================================
                // 2. СКЛАДСКИЕ АКТЫ (STOCK SLIPS)
                // ====================================================================
                if (request.StockSlips != null)
                {
                    foreach (var slipDto in request.StockSlips)
                    {
                        Guid? validSlipWarehouseId = slipDto.StockId != Guid.Empty && existingWarehouseIds.Contains(slipDto.StockId)
                            ? slipDto.StockId
                            : null;

                        var existingSlip = await _dbContext.StockSlips
                            .FirstOrDefaultAsync(s => s.Id == slipDto.Id, cancellationToken);

                        Guid currentSlipId;

                        if (existingSlip != null)
                        {
                            currentSlipId = existingSlip.Id;

                            existingSlip.Code = slipDto.SlipNumber;
                            existingSlip.SlipType = slipDto.SlipType;
                            existingSlip.Date = slipDto.Date;
                            existingSlip.WarehouseId = validSlipWarehouseId;
                            existingSlip.Description = slipDto.Description;
                            existingSlip.IsStockIncome = slipDto.SlipType.Equals("Incoming", StringComparison.OrdinalIgnoreCase);
                            existingSlip.UpdatedAt = now;

                            await _dbContext.StockSlipLines.Where(l => l.StockSlipId == currentSlipId).ExecuteDeleteAsync(cancellationToken);
                        }
                        else
                        {
                            currentSlipId = slipDto.Id;

                            var slip = new StockSlipEntity
                            {
                                Id = currentSlipId,
                                Code = slipDto.SlipNumber,
                                SlipType = slipDto.SlipType,
                                Date = slipDto.Date,
                                WarehouseId = validSlipWarehouseId,
                                Description = slipDto.Description,
                                IsCompleted = true,
                                IsStockIncome = slipDto.SlipType.Equals("Incoming", StringComparison.OrdinalIgnoreCase),
                                CreatedAt = now,
                                UpdatedAt = now
                            };

                            _dbContext.StockSlips.Add(slip);
                        }

                        // Строки складского акта (только валидные существующие товары)
                        if (slipDto.Lines != null && slipDto.Lines.Any())
                        {
                            var validSlipLines = slipDto.Lines
                                .Where(l => l.ProductId != Guid.Empty && existingProductIds.Contains(l.ProductId))
                                .Select((lineDto, idx) => new StockSlipLineEntity
                                {
                                    Id = lineDto.Id == Guid.Empty ? Guid.NewGuid() : lineDto.Id,
                                    StockSlipId = currentSlipId,
                                    StockId = lineDto.ProductId,
                                    Quantity = lineDto.Quantity,
                                    SortOrder = idx
                                });
                            await _dbContext.StockSlipLines.AddRangeAsync(validSlipLines, cancellationToken);
                        }

                        response.ProcessedStockSlipsCount++;
                    }
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                var detail = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                throw new Exception(string.Format("Failed to persist documents for ClientId '{0}'. Details: {1}", targetClientId, detail), ex);
            }

            // Пересчет остатков
            try
            {
                await _balanceCalculator.RecalculateAllBalancesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                var detail = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                throw new Exception(string.Format("Documents saved, but stock balance recalculation failed for ClientId '{0}'. Details: {1}", targetClientId, detail), ex);
            }

            response.Success = true;
            return response;
        });
    }
}
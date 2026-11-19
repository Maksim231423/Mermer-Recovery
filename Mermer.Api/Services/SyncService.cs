using Microsoft.EntityFrameworkCore;
using Mermer.Api.DTOs;
using Mermer.Data.Postgres;
using Mermer.Data.Postgres.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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

    private static string? ExtractClientIdFromLicenseKey(string? key)
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

    private async Task<string> ResolveEffectiveClientIdAsync(string? requestedClientId, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(requestedClientId) &&
            !string.Equals(requestedClientId, "default", StringComparison.OrdinalIgnoreCase))
        {
            return requestedClientId.Trim();
        }

        var now = DateTimeOffset.UtcNow;
        var activeLicenses = await _dbContext.Licenses
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
            return LicenseManager.CurrentClientId.Trim();
        }

        return "default";
    }

    public async Task<SyncPullResponseDto> ProcessPullAsync(string? clientId = null, CancellationToken cancellationToken = default)
    {
        var targetClientId = await ResolveEffectiveClientIdAsync(clientId, cancellationToken);
        MermerDbContext.CurrentClientId = targetClientId;

        return new SyncPullResponseDto
        {
            ServerTime = DateTimeOffset.UtcNow,

            Offices = await _dbContext.Offices.AsNoTracking()
                .Where(x => EF.Property<string>(x, "ClientId") == targetClientId || EF.Property<string>(x, "ClientId") == "default")
                .Select(x => new OfficeSyncDto { Id = x.Id, Name = x.Name, Region = x.Region, Description = x.Description, Tags = x.Tags, IsDisabled = x.IsDisabled }).ToListAsync(cancellationToken),

            Warehouses = await _dbContext.Warehouses.AsNoTracking()
                .Where(x => EF.Property<string>(x, "ClientId") == targetClientId || EF.Property<string>(x, "ClientId") == "default")
                .Select(x => new WarehouseSyncDto { Id = x.Id, OfficeId = x.OfficeId, Name = x.Name, Description = x.Description, Tags = x.Tags, IsDisabled = x.IsDisabled }).ToListAsync(cancellationToken),

            Depositories = await _dbContext.Depositories.AsNoTracking()
                .Where(x => EF.Property<string>(x, "ClientId") == targetClientId || EF.Property<string>(x, "ClientId") == "default")
                .Select(x => new DepositorySyncDto { Id = x.Id, OfficeId = x.OfficeId, Name = x.Name, Description = x.Description, Tags = x.Tags, IsDisabled = x.IsDisabled }).ToListAsync(cancellationToken),

            Currencies = await _dbContext.Currencies.AsNoTracking()
                .Where(x => EF.Property<string>(x, "ClientId") == targetClientId || EF.Property<string>(x, "ClientId") == "default")
                .Select(x => new CurrencySyncDto { Id = x.Id, Name = x.Name, Decimals = x.Decimals, IsDefault = x.IsDefault, Description = x.Description, IsDisabled = x.IsDisabled }).ToListAsync(cancellationToken),

            CurrencyRates = await _dbContext.CurrencyRates.AsNoTracking()
                .Where(x => EF.Property<string>(x, "ClientId") == targetClientId || EF.Property<string>(x, "ClientId") == "default")
                .Select(x => new CurrencyRateSyncDto { Id = x.Id, CurrencyId = x.CurrencyId, ValidFrom = x.ValidFrom, Multiplier = x.Multiplier, Divider = x.Divider }).ToListAsync(cancellationToken),

            Partners = await _dbContext.Partners.AsNoTracking()
                .Where(x => EF.Property<string>(x, "ClientId") == targetClientId || EF.Property<string>(x, "ClientId") == "default")
                .Select(x => new PartnerSyncDto { Id = x.Id, Code = x.Code, Name = x.Name, Phone = x.Phone, Address = x.Address, Group = x.Group, CreditLimit = x.CreditLimit, Tags = x.Tags, Description = x.Description, Rating = x.Rating, CurrencyId = x.CurrencyId, IsDisabled = x.IsDisabled }).ToListAsync(cancellationToken),

            Expenses = await _dbContext.Expenses.AsNoTracking()
                .Where(x => EF.Property<string>(x, "ClientId") == targetClientId || EF.Property<string>(x, "ClientId") == "default")
                .Select(x => new ExpenseSyncDto { Id = x.Id, Name = x.Name, Description = x.Description, Group = x.Group, Tags = x.Tags, IsDisabled = x.IsDisabled }).ToListAsync(cancellationToken),

            Roles = await _dbContext.Roles.AsNoTracking()
                .Where(x => EF.Property<string>(x, "ClientId") == targetClientId || EF.Property<string>(x, "ClientId") == "default")
                .Select(x => new RoleSyncDto { Id = x.Id, Name = x.Name, Description = x.Description, AuthorizationsJson = x.AuthorizationsJson, AccountPrivilegesJson = x.AccountPrivilegesJson, IsDisabled = x.IsDisabled }).ToListAsync(cancellationToken),

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
                    Roles = x.Roles ?? new List<string>(),
                    AccountPrivileges = x.AccountPrivileges
                }).ToListAsync(cancellationToken),

            Stocks = await _dbContext.Stocks.AsNoTracking()
                .Where(x => EF.Property<string>(x, "ClientId") == targetClientId || EF.Property<string>(x, "ClientId") == "default")
                .Select(x => new StockSyncDto { Id = x.Id, Code = x.Code, Name = x.Name, ShortName = x.ShortName, Type = x.Type, Group = x.Group, Tags = x.Tags, Barcodes = x.Barcodes, LimitMin = x.LimitMin, LimitMax = x.LimitMax, Description = x.Description, IsDisabled = x.IsDisabled }).ToListAsync(cancellationToken),

            StockUnits = await _dbContext.StockUnits.AsNoTracking()
                .Where(x => EF.Property<string>(x, "ClientId") == targetClientId || EF.Property<string>(x, "ClientId") == "default")
                .Select(x => new StockUnitSyncDto { Id = x.Id, StockId = x.StockId, Name = x.Name, Multiplier = x.Multiplier, Divider = x.Divider, IsDefault = x.IsDefault }).ToListAsync(cancellationToken),

            StockPrices = await _dbContext.StockPrices.AsNoTracking()
                .Where(x => EF.Property<string>(x, "ClientId") == targetClientId || EF.Property<string>(x, "ClientId") == "default")
                .Select(x => new StockPriceSyncDto { Id = x.Id, StockId = x.StockId, CurrencyId = x.CurrencyId, Price = x.Price, PriceGroup = x.PriceGroup, ValidFrom = x.ValidFrom }).ToListAsync(cancellationToken),

            StockAdditionalPrices = await _dbContext.StockAdditionalPrices.AsNoTracking()
                .Where(x => EF.Property<string>(x, "ClientId") == targetClientId || EF.Property<string>(x, "ClientId") == "default")
                .Select(x => new StockAdditionalPriceSyncDto { Id = x.Id, StockId = x.StockId, CurrencyId = x.CurrencyId, Price = x.Price, PriceGroup = x.PriceGroup, ValidFrom = x.ValidFrom }).ToListAsync(cancellationToken),

            StockBalances = await _dbContext.StockBalances.AsNoTracking()
                .Where(x => EF.Property<string>(x, "ClientId") == targetClientId || EF.Property<string>(x, "ClientId") == "default")
                .Select(x => new StockBalanceSyncDto { WarehouseId = x.WarehouseId, StockId = x.StockId, Income = x.Income, Expense = x.Expense }).ToListAsync(cancellationToken),

            StockAlternatives = await _dbContext.StockAlternatives.AsNoTracking()
                .Where(x => EF.Property<string>(x, "ClientId") == targetClientId || EF.Property<string>(x, "ClientId") == "default")
                .Select(x => new StockAlternativeSyncDto { Id = x.Id, Name = x.Name, Description = x.Description, IsDisabled = x.IsDisabled }).ToListAsync(cancellationToken),

            StockAlternativeLines = await _dbContext.StockAlternativeLines.AsNoTracking()
                .Where(x => EF.Property<string>(x, "ClientId") == targetClientId || EF.Property<string>(x, "ClientId") == "default")
                .Select(x => new StockAlternativeLineSyncDto { Id = x.Id, StockAlternativeId = x.StockAlternativeId, StockId = x.StockId ?? Guid.Empty }).ToListAsync(cancellationToken)
        };
    }

    public async Task<SyncPushResponseDto> ProcessPushAsync(SyncPushRequestDto request, CancellationToken cancellationToken = default)
    {
        if (request == null) throw new Exception("Sync push request payload cannot be empty.");

        var targetClientId = await ResolveEffectiveClientIdAsync(request.ClientId, cancellationToken);
        MermerDbContext.CurrentClientId = targetClientId;

        var response = new SyncPushResponseDto();
        var strategy = _dbContext.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var now = DateTimeOffset.UtcNow;

                // 1. НАКЛАДНЫЕ (INVOICES)
                if (request.Invoices != null && request.Invoices.Any())
                {
                    foreach (var invDto in request.Invoices)
                    {
                        var inv = await _dbContext.Invoices.FirstOrDefaultAsync(i => i.Id == invDto.Id, cancellationToken);
                        if (inv == null)
                        {
                            inv = new InvoiceEntity { Id = invDto.Id, Code = invDto.InvoiceNumber, InvoiceType = invDto.InvoiceType, Date = invDto.Date, UserId = invDto.ClientId, WarehouseId = invDto.WarehouseId, Description = invDto.Description, IsCompleted = true, IsDisabled = false, CreatedAt = now, UpdatedAt = now };
                            _dbContext.Invoices.Add(inv);
                        }
                        else
                        {
                            inv.Code = invDto.InvoiceNumber; inv.InvoiceType = invDto.InvoiceType; inv.Date = invDto.Date; inv.UserId = invDto.ClientId; inv.WarehouseId = invDto.WarehouseId; inv.Description = invDto.Description; inv.UpdatedAt = now;
                            await _dbContext.InvoiceLines.Where(l => l.InvoiceId == inv.Id).ExecuteDeleteAsync(cancellationToken);
                            await _dbContext.InvoicePayments.Where(p => p.InvoiceId == inv.Id).ExecuteDeleteAsync(cancellationToken);
                            await _dbContext.InvoiceDiscounts.Where(d => d.InvoiceId == inv.Id).ExecuteDeleteAsync(cancellationToken);
                        }

                        _dbContext.Entry(inv).Property("ClientId").CurrentValue = targetClientId;
                        _dbContext.Entry(inv).Property("IsSynced").CurrentValue = true;

                        if (invDto.Lines != null)
                        {
                            var lines = invDto.Lines.Select((l, idx) => new InvoiceLineEntity { Id = l.Id == Guid.Empty ? Guid.NewGuid() : l.Id, InvoiceId = inv.Id, StockId = l.ProductId, Quantity = l.Quantity, Price = l.UnitPrice, SortOrder = idx }).ToList();
                            await _dbContext.InvoiceLines.AddRangeAsync(lines, cancellationToken);
                            foreach (var line in lines) _dbContext.Entry(line).Property("ClientId").CurrentValue = targetClientId;
                        }

                        if (invDto.Payments != null)
                        {
                            var payments = invDto.Payments.Select((p, idx) => new InvoicePaymentEntity { Id = p.Id == Guid.Empty ? Guid.NewGuid() : p.Id, InvoiceId = inv.Id, PaymentType = p.PaymentType ?? "Payment", CurrencyId = p.CurrencyId, Amount = p.Amount, SortOrder = idx }).ToList();
                            await _dbContext.InvoicePayments.AddRangeAsync(payments, cancellationToken);
                            foreach (var pay in payments) _dbContext.Entry(pay).Property("ClientId").CurrentValue = targetClientId;
                        }

                        if (invDto.Discounts != null)
                        {
                            var discounts = invDto.Discounts.Select((d, idx) => new InvoiceDiscountEntity { Id = d.Id == Guid.Empty ? Guid.NewGuid() : d.Id, InvoiceId = inv.Id, DiscountType = d.DiscountType ?? "Flat", Amount = d.Amount, Description = d.Description, SortOrder = idx }).ToList();
                            await _dbContext.InvoiceDiscounts.AddRangeAsync(discounts, cancellationToken);
                            foreach (var disc in discounts) _dbContext.Entry(disc).Property("ClientId").CurrentValue = targetClientId;
                        }

                        response.ProcessedInvoicesCount++;
                    }
                }

                // 2. СКЛАДСКИЕ АКТЫ (STOCK SLIPS)
                if (request.StockSlips != null && request.StockSlips.Any())
                {
                    foreach (var sDto in request.StockSlips)
                    {
                        var slip = await _dbContext.StockSlips.FirstOrDefaultAsync(s => s.Id == sDto.Id, cancellationToken);
                        if (slip == null)
                        {
                            slip = new StockSlipEntity { Id = sDto.Id, Code = sDto.SlipNumber, SlipType = sDto.SlipType, Date = sDto.Date, WarehouseId = sDto.StockId, Description = sDto.Description, IsCompleted = true, IsStockIncome = sDto.SlipType.Equals("Incoming", StringComparison.OrdinalIgnoreCase), CreatedAt = now, UpdatedAt = now };
                            _dbContext.StockSlips.Add(slip);
                        }
                        else
                        {
                            slip.Code = sDto.SlipNumber; slip.SlipType = sDto.SlipType; slip.Date = sDto.Date; slip.WarehouseId = sDto.StockId; slip.Description = sDto.Description; slip.UpdatedAt = now;
                            await _dbContext.StockSlipLines.Where(l => l.StockSlipId == slip.Id).ExecuteDeleteAsync(cancellationToken);
                        }

                        _dbContext.Entry(slip).Property("ClientId").CurrentValue = targetClientId;
                        _dbContext.Entry(slip).Property("IsSynced").CurrentValue = true;

                        if (sDto.Lines != null)
                        {
                            var lines = sDto.Lines.Select((l, idx) => new StockSlipLineEntity { Id = l.Id == Guid.Empty ? Guid.NewGuid() : l.Id, StockSlipId = slip.Id, StockId = l.ProductId, Quantity = l.Quantity, SortOrder = idx }).ToList();
                            await _dbContext.StockSlipLines.AddRangeAsync(lines, cancellationToken);
                            foreach (var line in lines) _dbContext.Entry(line).Property("ClientId").CurrentValue = targetClientId;
                        }

                        response.ProcessedStockSlipsCount++;
                    }
                }

                // 3. КАССОВЫЕ ОРДЕРА (FUNDS SLIPS)
                if (request.FundsSlips != null && request.FundsSlips.Any())
                {
                    foreach (var fDto in request.FundsSlips)
                    {
                        var fund = await _dbContext.FundsSlips.FirstOrDefaultAsync(f => f.Id == fDto.Id, cancellationToken);
                        if (fund == null)
                        {
                            fund = new FundsSlipEntity { Id = fDto.Id, Code = fDto.SlipNumber, FundsSlipType = fDto.SlipType, Date = fDto.Date, DepositoryId = fDto.DepositoryId, PartnerId = fDto.PartnerId, OfficeId = fDto.OfficeId, DisplayCurrencyId = fDto.DisplayCurrencyId, Description = fDto.Description, IsCompleted = true, IsDisabled = false, CreatedAt = now.UtcDateTime, UpdatedAt = now.UtcDateTime };
                            _dbContext.FundsSlips.Add(fund);
                        }
                        else
                        {
                            fund.Code = fDto.SlipNumber; fund.FundsSlipType = fDto.SlipType; fund.Date = fDto.Date; fund.DepositoryId = fDto.DepositoryId; fund.PartnerId = fDto.PartnerId; fund.OfficeId = fDto.OfficeId; fund.DisplayCurrencyId = fDto.DisplayCurrencyId; fund.Description = fDto.Description; fund.UpdatedAt = now.UtcDateTime;
                            await _dbContext.FundsSlipLines.Where(l => l.FundsSlipId == fund.Id).ExecuteDeleteAsync(cancellationToken);
                        }

                        _dbContext.Entry(fund).Property("ClientId").CurrentValue = targetClientId;
                        _dbContext.Entry(fund).Property("IsSynced").CurrentValue = true;

                        if (fDto.Lines != null)
                        {
                            var lines = fDto.Lines.Select((l, idx) => new FundsSlipLineEntity { Id = l.Id == Guid.Empty ? Guid.NewGuid() : l.Id, FundsSlipId = fund.Id, Amount = l.Amount, CurrencyId = l.CurrencyId, SortOrder = idx }).ToList();
                            await _dbContext.FundsSlipLines.AddRangeAsync(lines, cancellationToken);
                            foreach (var line in lines) _dbContext.Entry(line).Property("ClientId").CurrentValue = targetClientId;
                        }

                        response.ProcessedFundsSlipsCount++;
                    }
                }

                // 4. ПЕРЕМЕЩЕНИЯ ДЕНЕГ (FUNDS TRANSFERS)
                if (request.FundsTransfers != null && request.FundsTransfers.Any())
                {
                    foreach (var ftDto in request.FundsTransfers)
                    {
                        var ft = await _dbContext.FundsTransfers.FirstOrDefaultAsync(f => f.Id == ftDto.Id, cancellationToken);
                        if (ft == null)
                        {
                            ft = new FundsTransferEntity { Id = ftDto.Id, Code = ftDto.SlipNumber, Date = ftDto.Date, FromDepositoryId = ftDto.FromDepositoryId, ToDepositoryId = ftDto.ToDepositoryId, DisplayCurrencyId = ftDto.DisplayCurrencyId, Description = ftDto.Description, IsCompleted = true, IsDisabled = false, CreatedAt = now.UtcDateTime, UpdatedAt = now.UtcDateTime };
                            _dbContext.FundsTransfers.Add(ft);
                        }
                        else
                        {
                            ft.Code = ftDto.SlipNumber; ft.Date = ftDto.Date; ft.FromDepositoryId = ftDto.FromDepositoryId; ft.ToDepositoryId = ftDto.ToDepositoryId; ft.DisplayCurrencyId = ftDto.DisplayCurrencyId; ft.Description = ftDto.Description; ft.UpdatedAt = now.UtcDateTime;
                            await _dbContext.FundsTransferLines.Where(l => l.FundsTransferId == ft.Id).ExecuteDeleteAsync(cancellationToken);
                        }

                        _dbContext.Entry(ft).Property("ClientId").CurrentValue = targetClientId;
                        _dbContext.Entry(ft).Property("IsSynced").CurrentValue = true;

                        if (ftDto.Lines != null)
                        {
                            var lines = ftDto.Lines.Select((l, idx) => new FundsTransferLineEntity { Id = l.Id == Guid.Empty ? Guid.NewGuid() : l.Id, FundsTransferId = ft.Id, Amount = l.Amount, ReceivedAmount = l.ReceivedAmount, CurrencyId = l.CurrencyId, SortOrder = idx }).ToList();
                            await _dbContext.FundsTransferLines.AddRangeAsync(lines, cancellationToken);
                            foreach (var line in lines) _dbContext.Entry(line).Property("ClientId").CurrentValue = targetClientId;
                        }

                        response.ProcessedFundsTransfersCount++;
                    }
                }

                // 5. РАСХОДЫ (EXPENSE SLIPS)
                if (request.ExpenseSlips != null && request.ExpenseSlips.Any())
                {
                    foreach (var esDto in request.ExpenseSlips)
                    {
                        var es = await _dbContext.ExpenseSlips.FirstOrDefaultAsync(e => e.Id == esDto.Id, cancellationToken);
                        if (es == null)
                        {
                            es = new ExpenseSlipEntity { Id = esDto.Id, Code = esDto.SlipNumber, Date = esDto.Date, OfficeId = esDto.OfficeId, DepositoryId = esDto.DepositoryId, DisplayCurrencyId = esDto.DisplayCurrencyId, Description = esDto.Description, IsCompleted = true, IsDisabled = false, CreatedAt = now.UtcDateTime, UpdatedAt = now.UtcDateTime };
                            _dbContext.ExpenseSlips.Add(es);
                        }
                        else
                        {
                            es.Code = esDto.SlipNumber; es.Date = esDto.Date; es.OfficeId = esDto.OfficeId; es.DepositoryId = esDto.DepositoryId; es.DisplayCurrencyId = esDto.DisplayCurrencyId; es.Description = esDto.Description; es.UpdatedAt = now.UtcDateTime;
                            await _dbContext.ExpenseSlipLines.Where(l => l.ExpenseSlipId == es.Id).ExecuteDeleteAsync(cancellationToken);
                        }

                        _dbContext.Entry(es).Property("ClientId").CurrentValue = targetClientId;
                        _dbContext.Entry(es).Property("IsSynced").CurrentValue = true;

                        if (esDto.Lines != null)
                        {
                            var lines = esDto.Lines.Select((l, idx) => new ExpenseSlipLineEntity { Id = l.Id == Guid.Empty ? Guid.NewGuid() : l.Id, ExpenseSlipId = es.Id, ExpenseId = l.ExpenseId, Amount = l.Amount, CurrencyId = l.CurrencyId, SortOrder = idx }).ToList();
                            await _dbContext.ExpenseSlipLines.AddRangeAsync(lines, cancellationToken);
                            foreach (var line in lines) _dbContext.Entry(line).Property("ClientId").CurrentValue = targetClientId;
                        }

                        response.ProcessedExpenseSlipsCount++;
                    }
                }

                // 6. СКЛАДСКИЕ ПЕРЕМЕЩЕНИЯ (STOCK TRANSFERS)
                if (request.StockTransfers != null && request.StockTransfers.Any())
                {
                    foreach (var stDto in request.StockTransfers)
                    {
                        var st = await _dbContext.StockTransfers.FirstOrDefaultAsync(s => s.Id == stDto.Id, cancellationToken);
                        if (st == null)
                        {
                            st = new StockTransferEntity { Id = stDto.Id, Code = stDto.SlipNumber, Date = stDto.Date, WarehouseId = stDto.WarehouseId, DestinationWarehouseId = stDto.DestinationWarehouseId, DisplayCurrencyId = stDto.DisplayCurrencyId, Description = stDto.Description, ActionTotal = stDto.ActionTotal, ActionReceivedTotal = stDto.ActionReceivedTotal, IsCompleted = true, IsDisabled = false, CreatedAt = now, UpdatedAt = now };
                            _dbContext.StockTransfers.Add(st);
                        }
                        else
                        {
                            st.Code = stDto.SlipNumber; st.Date = stDto.Date; st.WarehouseId = stDto.WarehouseId; st.DestinationWarehouseId = stDto.DestinationWarehouseId; st.DisplayCurrencyId = stDto.DisplayCurrencyId; st.Description = stDto.Description; st.ActionTotal = stDto.ActionTotal; st.ActionReceivedTotal = stDto.ActionReceivedTotal; st.UpdatedAt = now;
                            await _dbContext.StockTransferLines.Where(l => l.StockTransferId == st.Id).ExecuteDeleteAsync(cancellationToken);
                        }

                        _dbContext.Entry(st).Property("ClientId").CurrentValue = targetClientId;
                        _dbContext.Entry(st).Property("IsSynced").CurrentValue = true;

                        if (stDto.Lines != null)
                        {
                            var lines = stDto.Lines.Select((l, idx) => new StockTransferLineEntity { Id = l.Id == Guid.Empty ? Guid.NewGuid() : l.Id, StockTransferId = st.Id, StockId = l.ProductId, UnitId = l.UnitId, ReceivedUnitId = l.ReceivedUnitId, Quantity = l.Quantity, ReceivedQuantity = l.ReceivedQuantity, Price = l.UnitPrice, ActionTotal = l.ActionTotal, ActionReceivedTotal = l.ActionReceivedTotal, SortOrder = idx }).ToList();
                            await _dbContext.StockTransferLines.AddRangeAsync(lines, cancellationToken);
                            foreach (var line in lines) _dbContext.Entry(line).Property("ClientId").CurrentValue = targetClientId;
                        }

                        response.ProcessedStockTransfersCount++;
                    }
                }

                // 7. РЕВИЗИИ (STOCK REVISIONS)
                if (request.StockRevisions != null && request.StockRevisions.Any())
                {
                    foreach (var srDto in request.StockRevisions)
                    {
                        var sr = await _dbContext.StockRevisions.FirstOrDefaultAsync(s => s.Id == srDto.Id, cancellationToken);
                        if (sr == null)
                        {
                            sr = new StockRevisionEntity { Id = srDto.Id, Code = srDto.SlipNumber, Date = srDto.Date, FinishDate = srDto.FinishDate, WarehouseId = srDto.WarehouseId, ExceedSlipId = srDto.ExceedSlipId, DeficitSlipId = srDto.DeficitSlipId, UserId = srDto.UserId, UserName = srDto.UserName, Description = srDto.Description, IsCompleted = true, IsDisabled = false, CreatedAt = now, UpdatedAt = now };
                            _dbContext.StockRevisions.Add(sr);
                        }
                        else
                        {
                            sr.Code = srDto.SlipNumber; sr.Date = srDto.Date; sr.FinishDate = srDto.FinishDate; sr.WarehouseId = srDto.WarehouseId; sr.Description = srDto.Description; sr.UpdatedAt = now;
                            await _dbContext.StockRevisionLines.Where(l => l.StockRevisionId == sr.Id).ExecuteDeleteAsync(cancellationToken);
                        }

                        _dbContext.Entry(sr).Property("ClientId").CurrentValue = targetClientId;
                        _dbContext.Entry(sr).Property("IsSynced").CurrentValue = true;

                        if (srDto.Lines != null)
                        {
                            var lines = srDto.Lines.Select(l => new StockRevisionLineEntity { Id = l.Id == Guid.Empty ? Guid.NewGuid() : l.Id, StockRevisionId = sr.Id, StockId = l.ProductId, Date = l.Date, Quantity = l.Quantity, UnitId = l.UnitId, Price = l.Price, CurrencyId = l.CurrencyId }).ToList();
                            await _dbContext.StockRevisionLines.AddRangeAsync(lines, cancellationToken);
                            foreach (var line in lines) _dbContext.Entry(line).Property("ClientId").CurrentValue = targetClientId;
                        }

                        response.ProcessedStockRevisionsCount++;
                    }
                }

                // 8. ЗАКАЗЫ (STOCK ORDERS)
                if (request.StockOrders != null && request.StockOrders.Any())
                {
                    foreach (var soDto in request.StockOrders)
                    {
                        var so = await _dbContext.StockOrders.FirstOrDefaultAsync(s => s.Id == soDto.Id, cancellationToken);
                        if (so == null)
                        {
                            so = new StockOrderEntity { Id = soDto.Id, Code = soDto.SlipNumber, Date = soDto.Date, WarehouseId = soDto.WarehouseId, PartnerId = soDto.PartnerId, UserId = soDto.UserId, UserName = soDto.UserName, Description = soDto.Description, IsCompleted = true, IsDisabled = false, CreatedAt = now, UpdatedAt = now };
                            _dbContext.StockOrders.Add(so);
                        }
                        else
                        {
                            so.Code = soDto.SlipNumber; so.Date = soDto.Date; so.WarehouseId = soDto.WarehouseId; so.PartnerId = soDto.PartnerId; so.Description = soDto.Description; so.UpdatedAt = now;
                            await _dbContext.StockOrderLines.Where(l => l.StockOrderId == so.Id).ExecuteDeleteAsync(cancellationToken);
                        }

                        _dbContext.Entry(so).Property("ClientId").CurrentValue = targetClientId;
                        _dbContext.Entry(so).Property("IsSynced").CurrentValue = true;

                        if (soDto.Lines != null)
                        {
                            var lines = soDto.Lines.Select(l => new StockOrderLineEntity { Id = l.Id == Guid.Empty ? Guid.NewGuid() : l.Id, StockOrderId = so.Id, StockId = l.ProductId, Quantity = l.Quantity, UnitId = l.UnitId }).ToList();
                            await _dbContext.StockOrderLines.AddRangeAsync(lines, cancellationToken);
                            foreach (var line in lines) _dbContext.Entry(line).Property("ClientId").CurrentValue = targetClientId;
                        }

                        response.ProcessedStockOrdersCount++;
                    }
                }

                // 9. АГРЕГИРОВАННЫЕ ЗАКАЗЫ (AGGREGATED STOCK ORDERS)
                if (request.AggregatedStockOrders != null && request.AggregatedStockOrders.Any())
                {
                    foreach (var aoDto in request.AggregatedStockOrders)
                    {
                        var ao = await _dbContext.AggregatedStockOrders.FirstOrDefaultAsync(a => a.Id == aoDto.Id, cancellationToken);
                        if (ao == null)
                        {
                            ao = new AggregatedStockOrderEntity { Id = aoDto.Id, Code = aoDto.SlipNumber, Date = aoDto.Date, WarehouseId = aoDto.WarehouseId, PartnerId = aoDto.PartnerId, UserId = aoDto.UserId, UserName = aoDto.UserName, Description = aoDto.Description, IsCompleted = true, IsDisabled = false, CreatedAt = now, UpdatedAt = now };
                            _dbContext.AggregatedStockOrders.Add(ao);
                        }
                        else
                        {
                            ao.Code = aoDto.SlipNumber; ao.Date = aoDto.Date; ao.WarehouseId = aoDto.WarehouseId; ao.PartnerId = aoDto.PartnerId; ao.Description = aoDto.Description; ao.UpdatedAt = now;
                            await _dbContext.AggregatedStockOrderLines.Where(l => l.AggregatedStockOrderId == ao.Id).ExecuteDeleteAsync(cancellationToken);
                        }

                        _dbContext.Entry(ao).Property("ClientId").CurrentValue = targetClientId;
                        _dbContext.Entry(ao).Property("IsSynced").CurrentValue = true;

                        if (aoDto.Lines != null)
                        {
                            var lines = aoDto.Lines.Select(l => new AggregatedStockOrderLineEntity { Id = l.Id == Guid.Empty ? Guid.NewGuid() : l.Id, AggregatedStockOrderId = ao.Id, StockId = l.ProductId, UnitId = l.UnitId, OrdersJson = l.OrdersJson }).ToList();
                            await _dbContext.AggregatedStockOrderLines.AddRangeAsync(lines, cancellationToken);
                            foreach (var line in lines) _dbContext.Entry(line).Property("ClientId").CurrentValue = targetClientId;
                        }

                        response.ProcessedAggregatedStockOrdersCount++;
                    }
                }

                // 10. ЕЖЕДНЕВНЫЕ РЕГИСТРЫ КАССЫ (DAILY FUNDS REGISTRIES)
                if (request.DailyFundsRegisteries != null && request.DailyFundsRegisteries.Any())
                {
                    foreach (var drDto in request.DailyFundsRegisteries)
                    {
                        var dr = await _dbContext.DailyFundsRegisteries.FirstOrDefaultAsync(d => d.Id == drDto.Id, cancellationToken);
                        if (dr == null)
                        {
                            dr = new DailyFundsRegisteryEntity { Id = drDto.Id, Code = drDto.SlipNumber, Date = drDto.Date, UserId = drDto.UserId, UserName = drDto.UserName, DepositoryId = drDto.DepositoryId, DisplayCurrencyId = drDto.DisplayCurrencyId, Description = drDto.Description, IsCompleted = true, IsDisabled = false, CreatedAt = now.UtcDateTime, UpdatedAt = now.UtcDateTime };
                            _dbContext.DailyFundsRegisteries.Add(dr);
                        }
                        else
                        {
                            dr.Code = drDto.SlipNumber; dr.Date = drDto.Date; dr.DepositoryId = drDto.DepositoryId; dr.DisplayCurrencyId = drDto.DisplayCurrencyId; dr.Description = drDto.Description; dr.UpdatedAt = now.UtcDateTime;
                            await _dbContext.DailyFundsRegisteryLines.Where(l => l.RegisteryId == dr.Id).ExecuteDeleteAsync(cancellationToken);
                        }

                        _dbContext.Entry(dr).Property("ClientId").CurrentValue = targetClientId;
                        _dbContext.Entry(dr).Property("IsSynced").CurrentValue = true;

                        if (drDto.Lines != null)
                        {
                            var lines = drDto.Lines.Select((l, idx) => new DailyFundsRegisteryLineEntity { Id = l.Id == Guid.Empty ? Guid.NewGuid() : l.Id, RegisteryId = dr.Id, Amount = l.Amount, CurrencyId = l.CurrencyId, SortOrder = idx }).ToList();
                            await _dbContext.DailyFundsRegisteryLines.AddRangeAsync(lines, cancellationToken);
                            foreach (var line in lines) _dbContext.Entry(line).Property("ClientId").CurrentValue = targetClientId;
                        }

                        response.ProcessedDailyFundsRegisteriesCount++;
                    }
                }

                // 11. АКТЫ ВЗАИМОРАСЧЕТОВ (PARTNER SLIPS)
                if (request.PartnerSlips != null && request.PartnerSlips.Any())
                {
                    foreach (var psDto in request.PartnerSlips)
                    {
                        var ps = await _dbContext.PartnerSlips.FirstOrDefaultAsync(p => p.Id == psDto.Id, cancellationToken);
                        if (ps == null)
                        {
                            ps = new PartnerSlipEntity { Id = psDto.Id, Code = psDto.SlipNumber, SlipType = psDto.SlipType, Date = psDto.Date, OfficeId = psDto.OfficeId, UserId = psDto.UserId, UserName = psDto.UserName, Description = psDto.Description, IsCompleted = true, IsDisabled = false, CreatedAt = now.UtcDateTime, UpdatedAt = now.UtcDateTime };
                            _dbContext.PartnerSlips.Add(ps);
                        }
                        else
                        {
                            ps.Code = psDto.SlipNumber; ps.SlipType = psDto.SlipType; ps.Date = psDto.Date; ps.OfficeId = psDto.OfficeId; ps.Description = psDto.Description; ps.UpdatedAt = now.UtcDateTime;
                            await _dbContext.PartnerSlipLines.Where(l => l.PartnerSlipId == ps.Id).ExecuteDeleteAsync(cancellationToken);
                        }

                        _dbContext.Entry(ps).Property("ClientId").CurrentValue = targetClientId;
                        _dbContext.Entry(ps).Property("IsSynced").CurrentValue = true;

                        if (psDto.Lines != null)
                        {
                            var lines = psDto.Lines.Select(l => new PartnerSlipLineEntity { Id = l.Id == Guid.Empty ? Guid.NewGuid() : l.Id, PartnerSlipId = ps.Id, PartnerId = l.PartnerId, DebitAmount = l.DebitAmount, DebitCurrencyId = l.DebitCurrencyId, CreditAmount = l.CreditAmount, CreditCurrencyId = l.CreditCurrencyId }).ToList();
                            await _dbContext.PartnerSlipLines.AddRangeAsync(lines, cancellationToken);
                            foreach (var line in lines) _dbContext.Entry(line).Property("ClientId").CurrentValue = targetClientId;
                        }

                        response.ProcessedPartnerSlipsCount++;
                    }
                }

                // 12. ВЗАИМОЗАЧЕТЫ МЕЖДУ ПАРТНЕРАМИ (PARTNER TRANSFERS)
                if (request.PartnerTransfers != null && request.PartnerTransfers.Any())
                {
                    foreach (var ptDto in request.PartnerTransfers)
                    {
                        var pt = await _dbContext.PartnerTransfers.FirstOrDefaultAsync(p => p.Id == ptDto.Id, cancellationToken);
                        if (pt == null)
                        {
                            pt = new PartnerTransferEntity { Id = ptDto.Id, Code = ptDto.SlipNumber, Date = ptDto.Date, UserId = ptDto.UserId, UserName = ptDto.UserName, Description = ptDto.Description, IsCompleted = true, IsDisabled = false, CreatedAt = now.UtcDateTime, UpdatedAt = now.UtcDateTime };
                            _dbContext.PartnerTransfers.Add(pt);
                        }
                        else
                        {
                            pt.Code = ptDto.SlipNumber; pt.Date = ptDto.Date; pt.Description = ptDto.Description; pt.UpdatedAt = now.UtcDateTime;
                            await _dbContext.PartnerTransferLines.Where(l => l.PartnerTransferId == pt.Id).ExecuteDeleteAsync(cancellationToken);
                        }

                        _dbContext.Entry(pt).Property("ClientId").CurrentValue = targetClientId;
                        _dbContext.Entry(pt).Property("IsSynced").CurrentValue = true;

                        if (ptDto.Lines != null)
                        {
                            var lines = ptDto.Lines.Select(l => new PartnerTransferLineEntity { Id = l.Id == Guid.Empty ? Guid.NewGuid() : l.Id, PartnerTransferId = pt.Id, OfficeId = l.OfficeId, PartnerId = l.PartnerId, DebitAmount = l.DebitAmount, DebitCurrencyId = l.DebitCurrencyId, CreditAmount = l.CreditAmount, CreditCurrencyId = l.CreditCurrencyId }).ToList();
                            await _dbContext.PartnerTransferLines.AddRangeAsync(lines, cancellationToken);
                            foreach (var line in lines) _dbContext.Entry(line).Property("ClientId").CurrentValue = targetClientId;
                        }

                        response.ProcessedPartnerTransfersCount++;
                    }
                }

                // 13. НОВЫЕ КОНТРАГЕНТЫ (PARTNERS)
                if (request.Partners != null && request.Partners.Any())
                {
                    foreach (var pDto in request.Partners)
                    {
                        var partner = await _dbContext.Partners.FirstOrDefaultAsync(p => p.Id == pDto.Id, cancellationToken);
                        if (partner == null)
                        {
                            partner = new PartnerEntity { Id = pDto.Id, Code = pDto.Code, Name = pDto.Name, Phone = pDto.Phone, Address = pDto.Address, Group = pDto.Group, CreditLimit = pDto.CreditLimit, Tags = pDto.Tags, Description = pDto.Description, Rating = pDto.Rating, CurrencyId = pDto.CurrencyId, IsDisabled = pDto.IsDisabled, CreatedAt = now, UpdatedAt = now };
                            _dbContext.Partners.Add(partner);
                        }
                        else
                        {
                            partner.Code = pDto.Code; partner.Name = pDto.Name; partner.Phone = pDto.Phone; partner.Address = pDto.Address; partner.Group = pDto.Group; partner.CreditLimit = pDto.CreditLimit; partner.Tags = pDto.Tags; partner.Description = pDto.Description; partner.Rating = pDto.Rating; partner.CurrencyId = pDto.CurrencyId; partner.IsDisabled = pDto.IsDisabled; partner.UpdatedAt = now;
                        }

                        _dbContext.Entry(partner).Property("ClientId").CurrentValue = targetClientId;
                        _dbContext.Entry(partner).Property("IsSynced").CurrentValue = true;

                        response.ProcessedPartnersCount++;
                    }
                }

                // 14. ПОЛЬЗОВАТЕЛИ (USERS)
                if (request.Users != null && request.Users.Any())
                {
                    foreach (var uDto in request.Users)
                    {
                        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == uDto.Id, cancellationToken);
                        if (user == null)
                        {
                            user = new UserEntity
                            {
                                Id = uDto.Id,
                                Username = uDto.Username,
                                Password = uDto.Password ?? string.Empty,
                                IsAdmin = uDto.IsAdmin,
                                IsDisabled = uDto.IsDisabled,
                                Description = uDto.Description ?? string.Empty,
                                Roles = uDto.Roles ?? new List<string>(),
                                AccountPrivileges = uDto.AccountPrivileges ?? new Dictionary<string, int>(),
                                CreatedAt = now,
                                UpdatedAt = now
                            };
                            _dbContext.Users.Add(user);
                        }
                        else
                        {
                            user.Username = uDto.Username;
                            if (!string.IsNullOrEmpty(uDto.Password)) user.Password = uDto.Password;
                            user.IsAdmin = uDto.IsAdmin;
                            user.IsDisabled = uDto.IsDisabled;
                            user.Description = uDto.Description ?? string.Empty;
                            user.Roles = uDto.Roles ?? new List<string>();
                            if (uDto.AccountPrivileges != null) user.AccountPrivileges = uDto.AccountPrivileges;
                            user.UpdatedAt = now;
                        }

                        _dbContext.Entry(user).Property("ClientId").CurrentValue = targetClientId;
                        _dbContext.Entry(user).Property("IsSynced").CurrentValue = true;
                        response.ProcessedUsersCount++;
                    }
                }

                // 15. РОЛИ (ROLES)
                if (request.Roles != null && request.Roles.Any())
                {
                    foreach (var rDto in request.Roles)
                    {
                        var role = await _dbContext.Roles.FirstOrDefaultAsync(r => r.Id == rDto.Id, cancellationToken);
                        if (role == null)
                        {
                            role = new RoleEntity
                            {
                                Id = rDto.Id,
                                Name = rDto.Name,
                                Description = rDto.Description ?? string.Empty,
                                AuthorizationsJson = rDto.AuthorizationsJson,
                                AccountPrivilegesJson = rDto.AccountPrivilegesJson,
                                IsDisabled = rDto.IsDisabled,
                                CreatedAt = now,
                                UpdatedAt = now
                            };
                            _dbContext.Roles.Add(role);
                        }
                        else
                        {
                            role.Name = rDto.Name;
                            role.Description = rDto.Description ?? string.Empty;
                            role.AuthorizationsJson = rDto.AuthorizationsJson;
                            role.AccountPrivilegesJson = rDto.AccountPrivilegesJson;
                            role.IsDisabled = rDto.IsDisabled;
                            role.UpdatedAt = now;
                        }

                        _dbContext.Entry(role).Property("ClientId").CurrentValue = targetClientId;
                        _dbContext.Entry(role).Property("IsSynced").CurrentValue = true;
                        response.ProcessedRolesCount++;
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